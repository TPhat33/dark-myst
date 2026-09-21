import { test, expect } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// Written by e2e/run-api.sh once it knows the scratch content/ directory this test run's API is
// actually serving from — see that script and playwright.config.ts's webServer entry for it.
const CONTENT_DIR_MARKER = path.resolve(__dirname, '.content-dir')

function readContentDir(): string {
  return fs.readFileSync(CONTENT_DIR_MARKER, 'utf-8').trim()
}

function readManifestVersion(contentDir: string): string {
  const manifest = JSON.parse(fs.readFileSync(path.join(contentDir, 'manifest.json'), 'utf-8'))
  return manifest.contentVersion as string
}

function readAshenKnightAttack(contentDir: string): number {
  const characters = JSON.parse(fs.readFileSync(path.join(contentDir, 'characters.json'), 'utf-8'))
  const knight = characters.characters.find((c: { id: string }) => c.id === 'chr_ashen_knight_i')
  return knight.baseStats.attack as number
}

test.describe.configure({ mode: 'serial' })

test('edit → validate → publish → roll back changes content/ on disk and changes it back', async ({ page }) => {
  const contentDir = readContentDir()
  const originalVersion = readManifestVersion(contentDir)
  const originalAttack = readAshenKnightAttack(contentDir)
  const editedAttack = originalAttack + 77

  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await expect(page.getByTestId('current-version')).toHaveText(originalVersion)

  await page.getByTestId(`select-chr_ashen_knight_i`).click()
  await page.getByTestId('field-base-attack').fill(String(editedAttack))
  await expect(page.getByTestId('pending-badge')).toBeVisible()

  await page.getByTestId('validate-button').click()
  await expect(page.getByTestId('validate-ok')).toBeVisible()

  await page.getByTestId('publish-notes').fill('playwright e2e: bump ashen knight attack')
  await page.getByTestId('publish-button').click()
  await expect(page.getByTestId('publish-message')).toBeVisible()

  const publishedVersion = await page.getByTestId('current-version').innerText()
  expect(publishedVersion).not.toBe(originalVersion)

  // --- Proof: content/ itself changed on disk, not just the page's own state. ---
  await expect.poll(() => readManifestVersion(contentDir)).toBe(publishedVersion)
  expect(readAshenKnightAttack(contentDir)).toBe(editedAttack)

  // Roll back to the version we started from.
  await page.getByTestId(`rollback-${originalVersion}`).click()
  await expect(page.getByTestId('publish-message')).toContainText(originalVersion)
  await expect(page.getByTestId('current-version')).toHaveText(originalVersion)

  // --- Proof: content/ reverted on disk too. ---
  await expect.poll(() => readManifestVersion(contentDir)).toBe(originalVersion)
  expect(readAshenKnightAttack(contentDir)).toBe(originalAttack)
})

test('an invalid edit is refused at publish, and a player bearer token cannot reach the admin API', async ({
  page,
  request
}) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await expect(page.getByTestId('current-version')).toBeVisible()

  await page.getByTestId('select-chr_ashen_knight_i').click()
  await page.getByTestId('field-skill-ids').fill('')

  await page.getByTestId('validate-button').click()
  await expect(page.getByTestId('validate-errors')).toBeVisible()
  await expect(page.getByTestId('validate-errors')).toContainText('has no skills')

  await page.getByTestId('publish-button').click()
  await expect(page.getByTestId('publish-problems')).toBeVisible()
  await expect(page.getByTestId('publish-problems')).toContainText('has no skills')

  // A player bearer token structurally cannot reach the admin surface (different header, different
  // table — server/DarkMyst.Api/Auth/AdminAuth.cs).
  const guest = await request.post('http://127.0.0.1:5099/accounts/guest')
  expect(guest.ok()).toBeTruthy()
  const { accessToken } = await guest.json()

  const withoutAdminHeader = await request.get('http://127.0.0.1:5099/admin/content/current', {
    headers: { Authorization: `Bearer ${accessToken}` }
  })
  expect(withoutAdminHeader.status()).toBe(401)

  const playerTokenAsAdminHeader = await request.get('http://127.0.0.1:5099/admin/content/current', {
    headers: { 'X-Admin-Token': accessToken }
  })
  expect(playerTokenAsAdminHeader.status()).toBe(401)
})
