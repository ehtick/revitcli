# RevitCli Dashboard (v2.0 — phase 1 skeleton)

Static SvelteKit SPA that visualises the model-health history captured by
`revitcli history capture`. See `docs/roadmap-2026q2-q3.md` §7 for the full
v2.0 plan.

This phase 1 deliverable contains:

- SvelteKit + `adapter-static` + Tailwind + Chart.js + svelte-chartjs scaffolding
- Overview page with current score / element counts / 7-day sparkline (placeholder DOM; Chart.js wires up in phase 2)
- A loader that accepts `?history=<path>` or falls back to `/data/history.json`
- A pure-TS score extractor mirroring the C# `MetricExtractor`

**Charts are intentionally placeholder DOM in this phase.** The hooks
(`<!-- Chart.js bar chart will mount here in follow-up -->`) are present so
phase 2 can drop in components without restructuring the page.

## Prerequisites

- Node.js >= 20.0.0
- npm >= 10.0.0

## Quickstart (contributors)

```bash
cd dashboard
npm install                # one-time
npm run dev                # localhost dev server (Vite, port 5173)
npm run build              # produce static output in dashboard/build/
npm run preview            # serve the built output locally for QA
```

Once `dashboard/build/` exists, the C# CLI commands take over:

```bash
# From the repo root, after `npm run build`:
revitcli dashboard serve --port 8080
revitcli dashboard build --output ./public --history-dir .revitcli/history
```

`dashboard build` (the C# command, distinct from `npm run build`) copies the
prebuilt static assets to a deploy-ready directory and inlines the user's
`.revitcli/history/index.json` at `data/history.json`.

## Data sources

The Overview page resolves data in this order:

1. `?history=<url-or-path>` query param.
2. `/data/history.json` relative to the static site root.
3. The hard-coded `STUB_HISTORY` constant — used so the page is always
   renderable for screenshots and offline contributors.

## File layout

```
dashboard/
  package.json            pinned versions (no lockfile committed)
  svelte.config.js        adapter-static, output to ./build
  vite.config.ts          dev server bound to 127.0.0.1
  tailwind.config.js      RevitCli dark palette tokens
  postcss.config.js
  tsconfig.json
  src/
    app.html              minimal HTML shell, dark by default
    app.css               Tailwind layers + RevitCli base tokens
    routes/
      +layout.svelte      header / nav / footer
      +page.svelte        Overview
    lib/
      loadHistory.ts      ?history= + /data/history.json + stub fallback
      score.ts            MetricExtractor parity for the metrics rendered today
```

## Design constraints (do not relax without a roadmap update)

- **No SSR**: `adapter-static`, `prerender: true`, `fallback: 'index.html'`.
  The C# server is a dumb byte pipe.
- **No auth**: dashboard is local data tooling. Deployments to a public host
  are the operator's responsibility (the meta tag includes `noindex`).
- **No write APIs**: the dashboard never calls back into Revit. All edits
  flow through the CLI.
- **Localhost only** for both `vite dev` and `revitcli dashboard serve`.

## Tests

Phase 1 ships only the C# integration tests
(`tests/RevitCli.Tests/Commands/DashboardCommandTests.cs`). Front-end unit
tests (Vitest) and Playwright e2e are part of the v2.0 follow-up phase
described in roadmap §7.
