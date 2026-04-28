import { test, expect } from "@playwright/test";

/**
 * Multi-project route — comparative cards.
 *
 * Stub-data path: STUB_PROJECTS in `loadProjects.ts` declares two
 * synthetic projects (Office Tower / Residential) with a small score
 * delta so card sorting has something to differentiate. Pinning that
 * count protects against a regression where one stub project gets
 * accidentally dropped during refactors.
 */
test.describe("Multi-project", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/projects");
  });

  test("renders heading + demo-data badge from STUB_PROJECTS", async ({
    page,
  }) => {
    await expect(
      page.getByRole("heading", { level: 1, name: /multi-project/i }),
    ).toBeVisible();
    await expect(page.getByText("Demo data")).toBeVisible();
  });

  test("renders one card per stub project", async ({ page }) => {
    const grid = page.locator('[data-test-id="project-cards"]');
    await expect(grid).toBeVisible();
    // STUB_PROJECTS ships exactly two projects.
    await expect(grid.locator("article")).toHaveCount(2);
  });

  test("cards are sorted by current score DESC", async ({ page }) => {
    // The stub's "Residential" starts higher than "Office Tower" and
    // ends higher (both trend upward by the same step). Sort order
    // should put Residential first.
    const cardHeadings = page.locator(
      '[data-test-id="project-cards"] article h2',
    );
    const first = await cardHeadings.first().textContent();
    expect(first ?? "").toContain("Residential");
  });

  test("each card mounts a sparkline canvas", async ({ page }) => {
    // Per-card sparkline confirms ScoreSparkline reuse works in the
    // grid context (multiple instances on one page, all reading from
    // the same Chart.js global registry).
    const sparklines = page.locator(
      '[data-test-id="project-cards"] [data-test-id="score-sparkline-chart"] canvas',
    );
    await expect(sparklines).toHaveCount(2);
  });

  test("shows score / elements / captures triplet per card", async ({
    page,
  }) => {
    // The card layout has three labelled tiles. Confirm the labels
    // render so a copy regression (e.g. "Captures" → "Snapshots")
    // surfaces in CI.
    const firstCard = page
      .locator('[data-test-id="project-cards"] article')
      .first();
    await expect(firstCard.getByText("Score")).toBeVisible();
    await expect(firstCard.getByText("Elements")).toBeVisible();
    await expect(firstCard.getByText("Captures")).toBeVisible();
  });
});
