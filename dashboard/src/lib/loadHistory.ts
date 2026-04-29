/**
 * History loader.
 *
 * Two sources, in priority order:
 *  1. `?history=<url-or-path>` query param — typically used in dev when the
 *     user wants to point the dashboard at a specific export. The value is
 *     passed straight to `fetch()`; relative paths resolve against the
 *     current page, absolute URLs work in the browser.
 *  2. `/data/history.json` — the conventional path injected by
 *     `revitcli dashboard build` into the static output. This is what
 *     ships when the dashboard is published to GitHub Pages.
 *
 * If neither source produces a valid payload, the page falls back to the
 * `STUB_HISTORY` shipped here so the dashboard always renders something
 * meaningful for screenshot purposes and offline contributors.
 *
 * The shape mirrors `.revitcli/history/index.json` produced by the C#
 * `HistoryStore`. We only consume a tiny subset of fields here.
 */

import { base } from '$app/paths';
import type { ModelSnapshot } from './score';

export interface HistoryEntry {
  id: string;
  capturedAt: string;
  source: string;
  size?: number;
  elementCount?: number;
  // Optional snapshot payload pre-inlined by `revitcli dashboard build`.
  // When absent, the dashboard renders only the metrics carried by
  // index.json (no element breakdowns) — that is acceptable for v2.0
  // phase 1 and follow-ups can fetch on demand.
  snapshot?: ModelSnapshot;
  score?: number;
}

export interface HistoryDocument {
  version: number;
  entries: HistoryEntry[];
  // Indicates the data was generated rather than loaded from a real file.
  // Used by the Overview page to surface a "demo data" banner.
  isStub?: boolean;
}

/**
 * Synchronous in-memory fallback. 7 entries spanning a week with a small
 * upward score trend, modelled after a typical small architectural project.
 */
export const STUB_HISTORY: HistoryDocument = {
  version: 1,
  isStub: true,
  entries: stubEntries()
};

function stubEntries(): HistoryEntry[] {
  // Generated relative to a fixed anchor so screenshots are reproducible.
  // Using ISO strings avoids Date arithmetic in the loader's hot path.
  const days = [
    { date: '2026-04-21', score: 71, elements: 1240 },
    { date: '2026-04-22', score: 73, elements: 1255 },
    { date: '2026-04-23', score: 72, elements: 1260 },
    { date: '2026-04-24', score: 76, elements: 1272 },
    { date: '2026-04-25', score: 78, elements: 1290 },
    { date: '2026-04-26', score: 81, elements: 1305 },
    { date: '2026-04-27', score: 83, elements: 1311 }
  ];
  return days.map((d, i) => ({
    id: `snapshot-${d.date.replace(/-/g, '')}T120000Z-stub${i.toString().padStart(2, '0')}`,
    capturedAt: `${d.date}T12:00:00Z`,
    source: 'manual',
    size: 80_000 + i * 1500,
    elementCount: d.elements,
    score: d.score,
    snapshot: {
      schemaVersion: 1,
      capturedAt: `${d.date}T12:00:00Z`,
      documentPath: 'demo/sample.rvt',
      score: d.score,
      summary: {
        sheetCount: 24 + i,
        scheduleCount: 8,
        elementCounts: {
          Walls: 220 + i * 3,
          Doors: 88 + i,
          Windows: 144 + i * 2,
          Floors: 56,
          Rooms: 41 + Math.floor(i / 2),
          Sheets: 24 + i,
          Schedules: 8,
          Views: 132 + i * 4
        }
      }
    }
  }));
}

/**
 * Resolve the URL to fetch history.json from. Pure function — no I/O. Tests
 * (when added) can call this directly without a DOM.
 *
 * @param search   The `window.location.search` string, including the leading `?`.
 * @param fallback Default URL when no override is supplied. Use a same-origin
 *                 path (e.g. `/data/history.json`) so it works under both
 *                 `file://` previews and `http://` deployments.
 */
export function resolveHistoryUrl(search: string, fallback = withBasePath('/data/history.json')): string {
  if (typeof search === 'string' && search.length > 1) {
    const params = new URLSearchParams(search);
    const override = params.get('history');
    if (override && override.length > 0) {
      return override;
    }
  }
  return fallback;
}

/**
 * Browser-only loader. Returns the stub document when `fetch` is unavailable
 * (e.g. SSR build inspection) or when the network request fails. Never
 * throws — callers always receive a renderable document.
 */
export async function loadHistory(
  fetchImpl: typeof fetch | null = typeof fetch !== 'undefined' ? fetch.bind(globalThis) : null,
  search = typeof window !== 'undefined' ? window.location.search : ''
): Promise<HistoryDocument> {
  if (!fetchImpl) return STUB_HISTORY;

  const url = resolveHistoryUrl(search);
  try {
    const res = await fetchImpl(url, { headers: { Accept: 'application/json' } });
    if (!res.ok) return STUB_HISTORY;
    const raw = (await res.json()) as unknown;
    return parseHistoryDocument(raw) ?? STUB_HISTORY;
  } catch {
    return STUB_HISTORY;
  }
}

function withBasePath(path: string): string {
  if (!base || !path.startsWith('/')) return path;
  return `${base}${path}`;
}

/**
 * Accept either:
 *  - The `index.json` shape: `{ version, entries: [...] }` (v1.6 default)
 *  - A bare array of entries (legacy / hand-rolled exports)
 *
 * Anything else collapses to the stub document so the page renders.
 */
export function parseHistoryDocument(raw: unknown): HistoryDocument | null {
  const entriesRaw = Array.isArray(raw)
    ? raw
    : isObject(raw) && Array.isArray(raw.entries)
      ? raw.entries
      : null;
  if (!entriesRaw) return null;

  const entries = entriesRaw
    .map(normaliseEntry)
    .filter((entry): entry is HistoryEntry => entry !== null);
  if (entriesRaw.length > 0 && entries.length === 0) return null;

  const version = isObject(raw) && typeof raw.version === 'number'
    ? raw.version
    : 1;
  return { version, entries };
}

function normaliseEntry(raw: unknown): HistoryEntry | null {
  if (!isObject(raw)) return null;
  if (!isNonEmptyString(raw.id) || !isNonEmptyString(raw.capturedAt) || !isNonEmptyString(raw.source)) {
    return null;
  }

  const entry: HistoryEntry = {
    id: raw.id,
    capturedAt: raw.capturedAt,
    source: raw.source
  };
  if (typeof raw.size === 'number' && Number.isFinite(raw.size)) entry.size = raw.size;
  if (typeof raw.elementCount === 'number' && Number.isFinite(raw.elementCount)) {
    entry.elementCount = raw.elementCount;
  }
  if (typeof raw.score === 'number' && Number.isFinite(raw.score)) entry.score = raw.score;
  if (isObject(raw.snapshot)) entry.snapshot = raw.snapshot as unknown as ModelSnapshot;
  return entry;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0;
}
