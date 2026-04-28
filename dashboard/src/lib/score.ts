/**
 * Score extraction helpers for client-side rendering.
 *
 * This file mirrors the *read-only* surface of the C# `MetricExtractor`
 * (src/RevitCli/History/MetricExtractor.cs) and `ScoreCommand.SnapshotScore`
 * for the metrics the Overview page currently renders. The intent is parity
 * for `score`, `sheets`, `schedules`, and `elements.<category>`.
 *
 * IMPORTANT: when the C# extractor grows new metric forms, mirror them here
 * (or import them from a shared definitions file generated at build time).
 * Keeping this in sync is a v2.0 follow-up: see roadmap §7.
 */

export interface ModelSnapshot {
  schemaVersion?: number;
  capturedAt?: string;
  documentPath?: string;
  documentTitle?: string;
  contentHash?: string;
  metaHash?: string;
  summary?: SnapshotSummary;
  categories?: Record<string, { count?: number; elements?: unknown[] } | null>;
  sheets?: unknown[];
  schedules?: unknown[];
  // Embedded score field — populated by `revitcli score --history` exports.
  score?: number;
}

export interface SnapshotSummary {
  sheetCount?: number;
  scheduleCount?: number;
  elementCounts?: Record<string, number>;
}

/**
 * Extract a numeric value for the given metric. Returns `null` when the
 * metric is unknown or missing on the snapshot. Behavioural parity with
 * the C# `MetricExtractor.Extract`:
 *
 *  - `score`            → snapshot.score (no live recompute in the browser).
 *  - `sheets`           → summary.sheetCount, falls back to sheets.length.
 *  - `schedules`        → summary.scheduleCount, falls back to schedules.length.
 *  - `elements.<cat>`   → categories[cat].count, falls back to elementCounts[cat].
 *  - `count.<key>`      → summary.elementCounts[key], falls back to categories[key].count.
 */
export function extract(snapshot: ModelSnapshot | null | undefined, metric: string): number | null {
  if (!snapshot || typeof metric !== 'string') return null;
  const trimmed = metric.trim();
  if (!trimmed) return null;

  const lower = trimmed.toLowerCase();

  if (lower === 'score') {
    return typeof snapshot.score === 'number' && Number.isFinite(snapshot.score)
      ? snapshot.score
      : null;
  }

  if (lower === 'sheets') {
    const summaryCount = snapshot.summary?.sheetCount;
    if (typeof summaryCount === 'number' && summaryCount > 0) return summaryCount;
    return Array.isArray(snapshot.sheets) ? snapshot.sheets.length : 0;
  }

  if (lower === 'schedules') {
    const summaryCount = snapshot.summary?.scheduleCount;
    if (typeof summaryCount === 'number' && summaryCount > 0) return summaryCount;
    return Array.isArray(snapshot.schedules) ? snapshot.schedules.length : 0;
  }

  const elementsPrefix = 'elements.';
  if (lower.startsWith(elementsPrefix) && lower.length > elementsPrefix.length) {
    const cat = trimmed.substring(elementsPrefix.length);
    return categoryCount(snapshot, cat) ?? summaryCount(snapshot, cat);
  }

  const countPrefix = 'count.';
  if (lower.startsWith(countPrefix) && lower.length > countPrefix.length) {
    const key = trimmed.substring(countPrefix.length);
    return summaryCount(snapshot, key) ?? categoryCount(snapshot, key);
  }

  return null;
}

function categoryCount(snapshot: ModelSnapshot, name: string): number | null {
  const categories = snapshot.categories;
  if (!categories) return null;
  for (const key of Object.keys(categories)) {
    if (key.toLowerCase() === name.toLowerCase()) {
      const cat = categories[key];
      if (!cat) return 0;
      return typeof cat.count === 'number' ? cat.count : Array.isArray(cat.elements) ? cat.elements.length : 0;
    }
  }
  return null;
}

function summaryCount(snapshot: ModelSnapshot, name: string): number | null {
  const counts = snapshot.summary?.elementCounts;
  if (!counts) return null;
  for (const key of Object.keys(counts)) {
    if (key.toLowerCase() === name.toLowerCase()) {
      const v = counts[key];
      return typeof v === 'number' ? v : null;
    }
  }
  return null;
}

/**
 * Convenience: build the per-category bar chart input from a snapshot.
 * Returns `[{ category, count }]` sorted descending by count, capped to
 * `limit` rows. The Overview page uses this for its element-count bar.
 */
export function elementCountsByCategory(
  snapshot: ModelSnapshot | null | undefined,
  limit = 12
): Array<{ category: string; count: number }> {
  if (!snapshot) return [];
  const out = new Map<string, number>();

  if (snapshot.summary?.elementCounts) {
    for (const [k, v] of Object.entries(snapshot.summary.elementCounts)) {
      if (typeof v === 'number') out.set(k, v);
    }
  }

  if (snapshot.categories) {
    for (const [k, cat] of Object.entries(snapshot.categories)) {
      if (out.has(k)) continue;
      if (!cat) continue;
      const c =
        typeof cat.count === 'number'
          ? cat.count
          : Array.isArray(cat.elements)
          ? cat.elements.length
          : 0;
      out.set(k, c);
    }
  }

  return Array.from(out.entries())
    .map(([category, count]) => ({ category, count }))
    .sort((a, b) => b.count - a.count)
    .slice(0, limit);
}
