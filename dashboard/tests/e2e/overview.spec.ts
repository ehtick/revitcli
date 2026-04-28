import { test, expect } from "@playwright/test";

/**
 * Overview route — landing page.
 *
 * Stub-data driven: no `data/history.json` is staged, so
 * `loadHistory()` falls back to STUB_HISTORY (7 synthetic captures
 * with an upward score trend). That's enough to exercise the data
 * path, the Chart.js mount points, and the "Demo data" badge.
 *
 * We assert on data-test-id hooks rather than text labels so a
 * future copy change in the page doesn't break the suite.
 */
test.describe("Overview", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
  });

  test("renders header and demo-data badge from STUB_HISTORY", async ({
    page,
  }) => {
    await expect(
      page.getByRole("heading", { level: 1, name: /overview/i }),
    ).toBeVisible();
    // The stub fallback path emits the badge so reviewers know this is
    // not real model data.
    await expect(page.getByText("Demo data")).toBeVisible();
  });

  test("mounts the elements-by-category bar chart", async ({ page }) => {
    // Chart.js renders into a <canvas>. The container we wrap it in
    // carries data-test-id="element-counts-chart"; presence of that
    // node + a child canvas confirms the chart booted (Chart.register
    // ran, data shape matched, no SSR window-touching crash).
    const chart = page.locator('[data-test-id="element-counts-chart"]');
    await expect(chart).toBeVisible();
    await expect(chart.locator("canvas")).toBeVisible();
  });

  test("mounts the score sparkline", async ({ page }) => {
    const sparkline = page.locator('[data-test-id="score-sparkline-chart"]');
    await expect(sparkline).toBeVisible();
    await expect(sparkline.locator("canvas")).toBeVisible();
  });

  test("shows the current score from the latest stub entry", async ({
    page,
  }) => {
    // The stub trends upward, so SOME numeric score must show in the
    // "Current score" tile. We don't pin the value — the stub may
    // shift across releases — but we pin the shape (a number, not "—").
    const tile = page.locator("text=Current score").locator("..");
    await expect(tile).toContainText(/\d+/);
  });

  test("navigation surface includes all three live routes", async ({
    page,
  }) => {
    // Phase 2 + this PR's predecessor enabled History and Multi-project.
    // Pinning the nav state catches a regression where one is
    // accidentally re-disabled.
    await expect(page.getByRole("link", { name: "Overview" })).toBeVisible();
    await expect(page.getByRole("link", { name: "History" })).toBeVisible();
    await expect(
      page.getByRole("link", { name: "Multi-project" }),
    ).toBeVisible();
  });
});
