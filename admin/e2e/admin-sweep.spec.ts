import { test, expect } from '@playwright/test'

test.describe.configure({ mode: 'serial' })

test('sweep: runs against the real API and returns real win-rate and survival numbers', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-sweep').click()

  // The panel pre-fills a roster from the pack's own characters and an encounter from its own
  // encounters — nothing hand-typed needs to happen for a first, minimal sweep.
  await expect(page.getByTestId('sweep-field-roster')).not.toHaveValue('')
  await page.getByTestId('sweep-field-repeat').fill('30')

  await page.getByTestId('sweep-run-button').click()
  await expect(page.getByTestId('sweep-result')).toBeVisible()
  await expect(page.getByTestId('sweep-result-summary')).toContainText('Win rate')
  await expect(page.getByTestId('sweep-result-summary')).toContainText('avg')

  const survivorRows = page.getByTestId('sweep-survivors').locator('li')
  expect(await survivorRows.count()).toBeGreaterThan(0)
})

test('sweep: a repeat above the server cap is refused, and the field is clamped to the real max', async ({ page }) => {
  await page.goto('/')
  await page.getByTestId('bootstrap-button').click()
  await page.getByTestId('tab-sweep').click()
  await expect(page.getByTestId('sweep-field-roster')).not.toHaveValue('')

  await page.getByTestId('sweep-field-repeat').fill('5000')
  await page.getByTestId('sweep-run-button').click()

  const error = page.getByTestId('sweep-error')
  await expect(error).toBeVisible()
  await expect(error).toHaveAttribute('data-error-kind', 'sweep_repeat_too_large')

  // The panel clamps the input to the server's real max (Admin:MaxSweepRepeat) rather than
  // leaving 5000 sitting there to be resubmitted and refused again.
  const clamped = await page.getByTestId('sweep-field-repeat').inputValue()
  expect(Number(clamped)).toBeLessThan(5000)
  expect(Number(clamped)).toBeGreaterThan(0)

  // Proves the bound actually held: running at the clamped value now succeeds.
  await page.getByTestId('sweep-run-button').click()
  await expect(page.getByTestId('sweep-result')).toBeVisible()
})

// A "click it twice" concurrency test belongs at the API level, not here: over a real browser and
// a real HTTP round trip there is no way to guarantee the second request actually lands while the
// first is still running without an artificial delay, and a timing-dependent assertion here would
// trade a real proof for a flaky one. That guarantee is proven deterministically instead in
// tests/DarkMyst.Api.Tests/AdminSweepTests.cs (A_second_concurrent_sweep_is_refused_while_one_is_running),
// which calls AdminSweepService directly and never depends on wall-clock timing.
