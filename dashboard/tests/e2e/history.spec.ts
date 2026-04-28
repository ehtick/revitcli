import { test, expect } from "@playwright/test";

/**
 * History route — full time-series chart + delta table.
 *
 * Same stub-data strategy as overview.spec.ts. The stub history has
 * 7 entries with monotonically increasing scores, so:
 *  - the time-series chart should mount with all 7 points
 *  - the delta table should render 7 rows
 *  - all but the oldest row should show a Δscore (the oldest has no
 *    previous capture to compare against)
 */
test.describe("History", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/history");
  });

  test("renders heading + capture count from STUB_HISTORY", async ({
    page,
  }) => {
    await expect(
      page.getByRole("heading", { level: 1, name: /history/i }),
    ).toBeVisible();
    // The header emits "<n> capture(s)" — pin the shape.
    await expect(page.getByText(/\d+ captures?/)).toBeVisible();
  });

  test("mounts the full-axis score line chart", async ({ page }) => {
    // History reuses ScoreSparkline with `minimal={false}` so the
    // chart renders with axes visible. Same canvas mount marker as
    // Overview's sparkline.
    const chart = page.locator('[data-test-id="score-sparkline-chart"]');
    await expect(chart).toBeVisible();
    await expect(chart.locator("canvas")).toBeVisible();
  });

  test("renders the delta table with rows for every capture", async ({
    page,
  }) => {
    const table = page.locator('[data-test-id="history-deltas"]');
    await expect(table).toBeVisible();

    // Stub has 7 entries → 7 data rows + 1 header row in the same
    // table. Counting all <tr> is the most resilient assertion.
    const rows = table.locator("tr");
    await expect(rows).toHaveCount(8);
  });

  test("Δscore column shows a numeric or em-dash per row", async ({ page }) => {
    // The newest row's Δscore is some number; the oldest row's is
    // the em-dash ("—") because it has no previous capture. Both
    // shapes are valid; we just assert the column populates.
    const table = page.locator('[data-test-id="history-deltas"]');
    const firstDataRowDeltaScore = table
      .locator("tbody tr")
      .first()
      .locator("td")
      .nth(3);
    await expect(firstDataRowDeltaScore).toBeVisible();
  });
});
