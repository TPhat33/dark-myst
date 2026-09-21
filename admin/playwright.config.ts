import { defineConfig, devices } from '@playwright/test'

// The Chromium build under PLAYWRIGHT_BROWSERS_PATH is pre-installed and may not match whatever
// @playwright/test's own bundled revision expects — launch it explicitly rather than let
// Playwright resolve (and potentially try to download) its own copy.
const CHROMIUM_PATH = '/opt/pw-browsers/chromium'

export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [['list']],
  use: {
    baseURL: 'http://127.0.0.1:5173',
    trace: 'retain-on-failure'
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        launchOptions: { executablePath: CHROMIUM_PATH }
      }
    }
  ],
  // Two real, live processes for the whole test run — a real DarkMyst.Api (against a scratch
  // Postgres database and an isolated copy of content/, see e2e/run-api.sh) and a real Vite dev
  // server serving the actual admin app. This is what lets docs/11-admin-spec.md's publish/
  // rollback loop be proven end to end rather than mocked.
  webServer: [
    {
      command: 'bash e2e/run-api.sh',
      url: 'http://127.0.0.1:5099/health',
      timeout: 120_000,
      reuseExistingServer: false,
      stdout: 'pipe',
      stderr: 'pipe'
    },
    {
      command: 'npm run dev -- --port 5173 --strictPort',
      url: 'http://127.0.0.1:5173',
      timeout: 60_000,
      reuseExistingServer: false,
      env: { VITE_API_BASE_URL: 'http://127.0.0.1:5099' }
    }
  ]
})
