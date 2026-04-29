import { test, expect } from "@playwright/test";

test.describe("BASE_PATH static deploy", () => {
  test("keeps navigation and data fetches under the configured base path", async ({
    page,
  }, testInfo) => {
    test.skip(testInfo.project.name !== "chromium-pages-base");

    const requestedPaths: string[] = [];
    page.on("request", (request) => {
      requestedPaths.push(new URL(request.url()).pathname);
    });

    await page.goto(".");
    await page.getByRole("link", { name: "History" }).click();

    await expect(
      page.getByRole("heading", { level: 1, name: /history/i }),
    ).toBeVisible();
    await expect(page).toHaveURL(/\/revitcli\/history$/);
    expect(requestedPaths.some((path) => path.startsWith("/data/"))).toBe(
      false,
    );
  });
});
