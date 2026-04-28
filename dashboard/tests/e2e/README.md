# Dashboard e2e suite (Playwright)

Smoke coverage for the v2.0 dashboard's three routes
(Overview / History / Multi-project) per
`docs/roadmap-2026q2-q3.md` §7 implementation step 10.

## Run

From `dashboard/`:

```bash
npm install                  # picks up @playwright/test
npm run test:e2e:install     # one-time: download Chromium for Playwright
npm run test:e2e             # runs the suite against `npm run dev`
```

The Playwright config (`../../playwright.config.ts`) launches `npm run
dev` automatically as a `webServer`, runs the specs against
`http://127.0.0.1:5173`, and tears the server down on completion.

## Strategy

The suite is **stub-data driven**: no `data/history.json` or
`data/projects.json` is staged, so the dashboard's loader fallbacks
(`STUB_HISTORY` / `STUB_PROJECTS`) supply enough shape for every chart
to render. CI runs from a fresh checkout with no operator data —
nothing else to set up.

Assertions target `data-test-id="*"` hooks rather than display copy
so a future copy or styling change doesn't break the suite. Pin
_shape_ (count of cards, presence of canvas, headings) rather than
_content_ (specific score values) to stay resilient against stub
data evolution.

## Files

- `overview.spec.ts` — Overview route: header, demo badge, both
  Chart.js mounts (bar + sparkline), nav surface
- `history.spec.ts` — History route: heading, time-series chart,
  delta table row count
- `projects.spec.ts` — Multi-project route: card count, sort order,
  per-card sparkline, label triplet
