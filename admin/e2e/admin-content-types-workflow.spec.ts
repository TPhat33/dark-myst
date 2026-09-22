import { test, expect } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// Same marker admin-content-workflow.spec.ts reads — written by e2e/run-api.sh once it knows the
// scratch content/ directory this test run's API is actually serving from.
const CONTENT_DIR_MARKER = path.resolve(__dirname, '.content-dir')

function readContentDir(): string {
  return fs.readFileSync(CONTENT_DIR_MARKER, 'utf-8').trim()
}

function readManifestVersion(contentDir: string): string {
  const manifest = JSON.parse(fs.readFileSync(path.join(contentDir, 'manifest.json'), 'utf-8'))
  return manifest.contentVersion as string
}

function readJson(contentDir: string, file: string): any {
  return JSON.parse(fs.readFileSync(path.join(contentDir, file), 'utf-8'))
}

test.describe.configure({ mode: 'serial' })

test('skill: edit -> validate -> publish -> roll back changes content/ on disk and changes it back', async ({ page }) => {
  const contentDir = readContentDir()
  const originalVersion = readManifestVersion(contentDir)
  const originalName = readJson(contentDir, 'skills.json').skills.find((s: any) => s.id === 'skl_basic_strike').name
  const editedName = originalName + ' (e2e)'

  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-skills').click()
  await expect(page.getByTestId('skill-current-version')).toHaveText(originalVersion)

  await page.getByTestId('skill-select-skl_basic_strike').click()
  await page.getByTestId('skill-field-name').fill(editedName)
  await expect(page.getByTestId('skill-pending-badge')).toBeVisible()

  await page.getByTestId('skill-validate-button').click()
  await expect(page.getByTestId('skill-validate-ok')).toBeVisible()

  await page.getByTestId('skill-publish-notes').fill('playwright e2e: rename basic strike')
  await page.getByTestId('skill-publish-button').click()
  await expect(page.getByTestId('skill-publish-message')).toBeVisible()

  const publishedVersion = await page.getByTestId('skill-current-version').innerText()
  expect(publishedVersion).not.toBe(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(publishedVersion)
  expect(readJson(contentDir, 'skills.json').skills.find((s: any) => s.id === 'skl_basic_strike').name).toBe(editedName)

  await page.getByTestId(`skill-rollback-${originalVersion}`).click()
  await expect(page.getByTestId('skill-publish-message')).toContainText(originalVersion)
  await expect(page.getByTestId('skill-current-version')).toHaveText(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(originalVersion)
  expect(readJson(contentDir, 'skills.json').skills.find((s: any) => s.id === 'skl_basic_strike').name).toBe(originalName)
})

test('skill: an edit that breaks a character\'s guaranteed action is refused at publish', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-skills').click()
  await expect(page.getByTestId('skill-current-version')).toBeVisible()

  await page.getByTestId('skill-select-skl_basic_strike').click()
  // chr_ashen_knight_i's only guaranteed OnAction skill is skl_basic_strike (its other OnAction
  // skill, skl_vanguard_cleave, has a < 100% activation chance and a cooldown) — dropping this
  // below 100% leaves that character with nothing guaranteed to do on its turn.
  await page.getByTestId('skill-field-activation').fill('500')

  await page.getByTestId('skill-validate-button').click()
  await expect(page.getByTestId('skill-validate-errors')).toBeVisible()
  await expect(page.getByTestId('skill-validate-errors')).toContainText('no guaranteed OnAction skill')

  await page.getByTestId('skill-publish-button').click()
  await expect(page.getByTestId('skill-publish-problems')).toBeVisible()
  await expect(page.getByTestId('skill-publish-problems')).toContainText('no guaranteed OnAction skill')
})

test('enemy: edit -> validate -> publish -> roll back changes content/ on disk and changes it back', async ({ page }) => {
  const contentDir = readContentDir()
  const originalVersion = readManifestVersion(contentDir)
  const originalAttack = readJson(contentDir, 'enemies.json').enemies.find((e: any) => e.id === 'enm_gloom_hound').stats.attack
  const editedAttack = originalAttack + 33

  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-enemies').click()
  await expect(page.getByTestId('enemy-current-version')).toHaveText(originalVersion)

  await page.getByTestId('enemy-select-enm_gloom_hound').click()
  await page.getByTestId('enemy-field-stat-attack').fill(String(editedAttack))
  await expect(page.getByTestId('enemy-pending-badge')).toBeVisible()

  await page.getByTestId('enemy-validate-button').click()
  await expect(page.getByTestId('enemy-validate-ok')).toBeVisible()

  await page.getByTestId('enemy-publish-notes').fill('playwright e2e: buff gloom hound')
  await page.getByTestId('enemy-publish-button').click()
  await expect(page.getByTestId('enemy-publish-message')).toBeVisible()

  const publishedVersion = await page.getByTestId('enemy-current-version').innerText()
  expect(publishedVersion).not.toBe(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(publishedVersion)
  expect(readJson(contentDir, 'enemies.json').enemies.find((e: any) => e.id === 'enm_gloom_hound').stats.attack).toBe(
    editedAttack
  )

  await page.getByTestId(`enemy-rollback-${originalVersion}`).click()
  await expect(page.getByTestId('enemy-publish-message')).toContainText(originalVersion)
  await expect(page.getByTestId('enemy-current-version')).toHaveText(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(originalVersion)
  expect(readJson(contentDir, 'enemies.json').enemies.find((e: any) => e.id === 'enm_gloom_hound').stats.attack).toBe(
    originalAttack
  )
})

test('enemy: clearing an enemy\'s skills is refused at publish', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-enemies').click()
  await expect(page.getByTestId('enemy-current-version')).toBeVisible()

  await page.getByTestId('enemy-select-enm_gloom_hound').click()
  await page.getByTestId('enemy-field-skill-ids').fill('')

  await page.getByTestId('enemy-validate-button').click()
  await expect(page.getByTestId('enemy-validate-errors')).toBeVisible()
  await expect(page.getByTestId('enemy-validate-errors')).toContainText('no guaranteed OnAction skill')

  await page.getByTestId('enemy-publish-button').click()
  await expect(page.getByTestId('enemy-publish-problems')).toBeVisible()
})

test('encounter: edit -> validate -> publish -> roll back changes content/ on disk and changes it back', async ({ page }) => {
  const contentDir = readContentDir()
  const originalVersion = readManifestVersion(contentDir)
  const originalName = readJson(contentDir, 'encounters.json').encounters.find(
    (e: any) => e.id === 'enc_tutorial_hounds'
  ).name
  const editedName = originalName + ' (e2e)'

  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-encounters').click()
  await expect(page.getByTestId('encounter-current-version')).toHaveText(originalVersion)

  await page.getByTestId('encounter-select-enc_tutorial_hounds').click()
  await page.getByTestId('encounter-field-name').fill(editedName)
  await expect(page.getByTestId('encounter-pending-badge')).toBeVisible()

  await page.getByTestId('encounter-validate-button').click()
  await expect(page.getByTestId('encounter-validate-ok')).toBeVisible()

  await page.getByTestId('encounter-publish-notes').fill('playwright e2e: rename encounter')
  await page.getByTestId('encounter-publish-button').click()
  await expect(page.getByTestId('encounter-publish-message')).toBeVisible()

  const publishedVersion = await page.getByTestId('encounter-current-version').innerText()
  expect(publishedVersion).not.toBe(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(publishedVersion)
  expect(
    readJson(contentDir, 'encounters.json').encounters.find((e: any) => e.id === 'enc_tutorial_hounds').name
  ).toBe(editedName)

  await page.getByTestId(`encounter-rollback-${originalVersion}`).click()
  await expect(page.getByTestId('encounter-publish-message')).toContainText(originalVersion)
  await expect(page.getByTestId('encounter-current-version')).toHaveText(originalVersion)

  await expect.poll(() => readManifestVersion(contentDir)).toBe(originalVersion)
  expect(
    readJson(contentDir, 'encounters.json').encounters.find((e: any) => e.id === 'enc_tutorial_hounds').name
  ).toBe(originalName)
})

test('encounter: removing every unit is refused at publish', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-encounters').click()
  await expect(page.getByTestId('encounter-current-version')).toBeVisible()

  await page.getByTestId('encounter-select-enc_tutorial_hounds').click()
  // enc_tutorial_hounds has 3 units (see content/encounters.json) — remove index 0 repeatedly.
  await page.getByTestId('encounter-unit-0-remove').click()
  await page.getByTestId('encounter-unit-0-remove').click()
  await page.getByTestId('encounter-unit-0-remove').click()

  await page.getByTestId('encounter-validate-button').click()
  await expect(page.getByTestId('encounter-validate-errors')).toBeVisible()
  await expect(page.getByTestId('encounter-validate-errors')).toContainText('has no units')

  await page.getByTestId('encounter-publish-button').click()
  await expect(page.getByTestId('encounter-publish-problems')).toBeVisible()
})
