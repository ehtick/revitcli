/**
 * Chart.js global registration.
 *
 * Chart.js 4 ships as a tree-shakable library — no controller / scale /
 * element is registered by default. Calling registerables once at app
 * boot pulls everything in; that's the right trade-off for our small
 * dashboard (bundle saving from selective imports is < 30KB and not
 * worth the maintenance burden of a "did you import the right
 * controller for this chart" footgun).
 *
 * This module is idempotent: calling `registerCharts()` more than once
 * is a no-op (Chart.js's internal registry deduplicates).
 */

import { Chart, registerables } from "chart.js";

let registered = false;
export function registerCharts(): void {
  if (registered) return;
  Chart.register(...registerables);
  registered = true;
}
