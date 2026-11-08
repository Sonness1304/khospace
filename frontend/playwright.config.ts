import { defineConfig } from '@playwright/test';

// Isolated headless QA fixture only; never reuses a user's Chrome profile.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  outputDir: '../.qa/wp01/browser',
  reporter: [['list'], ['json', { outputFile: '../.qa/wp01/browser-results.json' }]],
  use: {
    baseURL: process.env.WAREHOUSERENT_BROWSER_URL ?? 'http://127.0.0.1:5080',
    channel: process.env.CI ? undefined : 'chrome',
    headless: true,
    trace: 'retain-on-failure',
  },
});
