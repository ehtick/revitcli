/**
 * Multi-project loader for the /projects route.
 *
 * Source priority (mirrors loadHistory.ts):
 *   1. `?projects=<url>` query param — for dev / one-off comparisons
 *   2. `/data/projects.json` — the conventional path injected by
 *      `revitcli dashboard build --project NAME:DIR ...`
 *   3. STUB_PROJECTS fallback so the route always renders something
 *      meaningful for screenshots and offline contributors
 *
 * Shape mirrors what `DashboardCommand.InjectProjectsAsync` writes:
 * `{ version: 1, projects: [{ name, historyDir, history: HistoryDocument }] }`.
 * The `history` field is exactly the same shape the Overview / History
 * routes already consume, so chart components can be reused as-is.
 */

import type { HistoryDocument } from "./loadHistory";
import type { ModelSnapshot } from "./score";

export interface ProjectEntry {
  name: string;
  /**
   * Server-side resolved absolute path to the source `.revitcli/history/`
   * directory. Display-only — the dashboard never reads from it directly.
   */
  historyDir?: string;
  history: HistoryDocument;
}

export interface MultiProjectDocument {
  version: number;
  projects: ProjectEntry[];
  /** True when the data came from STUB_PROJECTS rather than a real file. */
  isStub?: boolean;
}

/**
 * Stub used when no projects.json can be found. Two synthetic projects
 * with a small score difference so the comparative card layout has
 * something to render in screenshots.
 */
export const STUB_PROJECTS: MultiProjectDocument = {
  version: 1,
  isStub: true,
  projects: [
    {
      name: "Demo / Office Tower",
      history: {
        version: 1,
        entries: stubEntries(72, 5),
      },
    },
    {
      name: "Demo / Residential",
      history: {
        version: 1,
        entries: stubEntries(85, 7),
      },
    },
  ],
};

function stubEntries(startingScore: number, count: number) {
  const now = Date.now();
  const day = 86_400_000;
  const out = [];
  for (let i = 0; i < count; i++) {
    const ts = new Date(now - (count - 1 - i) * day);
    out.push({
      id: ts.toISOString().replace(/[-:T]/g, "").slice(0, 14),
      capturedAt: ts.toISOString(),
      source: "manual" as const,
      score: Math.min(100, startingScore + i * 2),
      elementCount: 1200 + i * 40,
    });
  }
  return out;
}

export async function loadProjects(): Promise<MultiProjectDocument> {
  const url = resolveProjectsUrl();
  try {
    const resp = await fetch(url, { headers: { Accept: "application/json" } });
    if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
    const json = (await resp.json()) as unknown;
    return parseProjects(json);
  } catch {
    // Stub fallback — log to console so devs see why, but never throw
    // out of the loader (the page should always render).
    if (typeof console !== "undefined") {
      console.info(
        "[loadProjects] no projects.json found at",
        url,
        "— using stub.",
      );
    }
    return STUB_PROJECTS;
  }
}

function resolveProjectsUrl(): string {
  if (typeof window !== "undefined") {
    const param = new URLSearchParams(window.location.search).get("projects");
    if (param) return param;
  }
  return "/data/projects.json";
}

function parseProjects(raw: unknown): MultiProjectDocument {
  if (!raw || typeof raw !== "object") return STUB_PROJECTS;
  // Allow either the documented shape or a bare array of projects
  // (some hand-curated mirrors of projects.json drop the wrapper).
  if (Array.isArray(raw)) {
    return { version: 1, projects: raw as ProjectEntry[] };
  }
  const obj = raw as { version?: number; projects?: unknown };
  if (!Array.isArray(obj.projects)) return STUB_PROJECTS;
  return {
    version: typeof obj.version === "number" ? obj.version : 1,
    projects: obj.projects as ProjectEntry[],
  };
}

/**
 * Helpful read-only projection: pluck the latest entry per project,
 * with a uniform fallback when an entry is missing fields. The
 * /projects card layout uses this directly.
 */
export function latestSummary(p: ProjectEntry) {
  const entries = p.history?.entries ?? [];
  const latest = entries[entries.length - 1] ?? null;
  return {
    score: latest?.score ?? null,
    elementCount: latest?.elementCount ?? null,
    capturedAt: latest?.capturedAt ?? null,
    captureCount: entries.length,
    snapshot: (latest?.snapshot ?? null) as ModelSnapshot | null,
  };
}
