import { defineConfig, devices } from "@playwright/test";

/**
 * Playwright config for the RevitCli dashboard's e2e suite (v2.0
 * roadmap §7 step 10).
 *
 * Strategy: spin up `npm run dev` for the test run via the
 * `webServer` block, drive a single Chromium project, and rely on
 * the dashboard's stub-data fallback so the suite needs no operator
 * `history.json` on disk. This keeps CI hermetic — the same `npm
 * test:e2e` command works on a fresh checkout.
 *
 * The test files live under `tests/e2e/` (NOT the C# `tests/`
 * directory at the repo root — those are .NET xUnit projects). The
 * `testDir` here is relative to this config.
 */
export default defineConfig({
  testDir: "./tests/e2e",
  // Per-test timeout. Generous because the dev server is cold on
  // first request; subsequent tests reuse the same server.
  timeout: 30_000,
  expect: { timeout: 5_000 },

  // CI defaults: fewer retries, no parallel suites within a project
  // (the dev server is single-threaded). Local devs can run with
  // their own --workers override.
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 1,

  reporter: process.env.CI ? [["github"], ["list"]] : "list",

  use: {
    trace: "on-first-retry",
    screenshot: "only-on-failure",
    video: "retain-on-failure",
  },

  projects: [
    {
      name: "chromium-dev",
      use: {
        ...devices["Desktop Chrome"],
        baseURL: "http://127.0.0.1:5173/",
      },
    },
    {
      name: "chromium-pages-base",
      use: {
        ...devices["Desktop Chrome"],
        baseURL: "http://127.0.0.1:4174/revitcli/",
      },
    },
  ],

  // Reuse an already-running server when present (useful for `npm run
  // dev` in one terminal + `npm run test:e2e` in another). On CI the
  // server is started fresh per run.
  webServer: [
    {
      command: "npm run dev -- --host 127.0.0.1 --port 5173",
      url: "http://127.0.0.1:5173",
      reuseExistingServer: !process.env.CI,
      timeout: 60_000,
    },
    {
      command: "npm run preview:pages",
      url: "http://127.0.0.1:4174/revitcli/",
      reuseExistingServer: !process.env.CI,
      timeout: 90_000,
    },
  ],
});
