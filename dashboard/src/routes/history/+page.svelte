<script lang="ts">
  import { onMount } from 'svelte';
  import { loadHistory, type HistoryDocument, type HistoryEntry } from '$lib/loadHistory';
  import { extract } from '$lib/score';
  import ScoreSparkline from '$lib/charts/ScoreSparkline.svelte';

  /**
   * History page — full time-series chart over every captured snapshot
   * + a delta table that shows what each capture changed vs the
   * previous one. Per roadmap §7 phase 2 "History":
   *
   *   "全时序图 + diff 详情表"
   *
   * The chart reuses the Overview's ScoreSparkline component in
   * `minimal: false` mode (axes + grid visible). The delta table is a
   * lightweight projection — it does NOT call snapshot diff
   * server-side; it just compares cheap metrics carried in the index
   * (score, elementCount). Full snapshot-vs-snapshot diff stays a CLI
   * concern (`revitcli history diff`).
   */
  let history: HistoryDocument | null = null;
  let loadError = '';

  onMount(async () => {
    try {
      history = await loadHistory();
    } catch (err) {
      loadError = err instanceof Error ? err.message : String(err);
    }
  });

  // All entries, oldest -> newest, projected to the sparkline shape.
  $: points =
    history?.entries.map((e) => ({
      date: e.capturedAt?.slice(0, 16).replace('T', ' ') ?? e.id,
      score: e.score ?? (e.snapshot ? extract(e.snapshot, 'score') : null) ?? 0,
    })) ?? [];

  // Newest-first delta rows. Each row compares to the IMMEDIATELY
  // PREVIOUS capture (i+1 in oldest-first order) so the table reads as
  // "what changed since last time".
  $: deltas = buildDeltas(history?.entries ?? []);

  function buildDeltas(entries: HistoryEntry[]) {
    if (entries.length === 0) return [];
    const rows: {
      id: string;
      capturedAt: string;
      source: string;
      score: number | null;
      scoreDelta: number | null;
      elementCount: number | null;
      elementDelta: number | null;
    }[] = [];
    // Walk newest -> oldest so the table reads top-down chronologically.
    for (let i = entries.length - 1; i >= 0; i--) {
      const cur = entries[i];
      const prev = i > 0 ? entries[i - 1] : null;
      const curScore = cur.score ?? (cur.snapshot ? extract(cur.snapshot, 'score') : null);
      const prevScore = prev
        ? prev.score ?? (prev.snapshot ? extract(prev.snapshot, 'score') : null)
        : null;
      const curElems = cur.elementCount ?? null;
      const prevElems = prev?.elementCount ?? null;

      rows.push({
        id: cur.id,
        capturedAt: cur.capturedAt ?? '',
        source: cur.source ?? '',
        score: curScore,
        scoreDelta: curScore != null && prevScore != null ? curScore - prevScore : null,
        elementCount: curElems,
        elementDelta: curElems != null && prevElems != null ? curElems - prevElems : null,
      });
    }
    return rows;
  }

  function formatDelta(d: number | null): string {
    if (d == null) return '—';
    if (d === 0) return '±0';
    return d > 0 ? `+${d}` : `${d}`;
  }

  function deltaTone(d: number | null, higherIsBetter: boolean): string {
    // Score: higher is better; element count: neither is "better" so
    // colour is neutral. Caller decides via higherIsBetter.
    if (d == null || d === 0) return 'text-rc-muted';
    if (!higherIsBetter) return 'text-rc-muted';
    return d > 0 ? 'text-rc-good' : 'text-rc-bad';
  }
</script>

<section class="space-y-6">
  <header>
    <h1 class="rc-stat-value">History</h1>
    <p class="rc-stat-label">
      Every capture in the configured history store, oldest to newest. Hover the
      chart for per-point detail; the table lists deltas vs. the previous capture.
    </p>
  </header>

  {#if loadError}
    <div class="rc-card border-rc-bad/40 bg-rc-bad/10 text-rc-bad">
      Failed to load history: {loadError}
    </div>
  {/if}

  <article class="rc-card">
    <header class="flex items-baseline justify-between">
      <h2 class="text-lg font-semibold">Score over time</h2>
      <span class="rc-stat-label">{points.length} capture{points.length === 1 ? '' : 's'}</span>
    </header>
    <ScoreSparkline {points} minimal={false} />
  </article>

  <article class="rc-card">
    <header class="flex items-baseline justify-between">
      <h2 class="text-lg font-semibold">Capture deltas</h2>
      <span class="rc-stat-label">newest first</span>
    </header>
    {#if deltas.length === 0}
      <p class="mt-3 text-rc-muted">No history available.</p>
    {:else}
      <div class="mt-3 overflow-x-auto">
        <table class="w-full text-sm" data-test-id="history-deltas">
          <thead class="text-rc-muted">
            <tr class="border-b border-rc-border text-left">
              <th class="py-2 pr-3 font-normal">Captured</th>
              <th class="py-2 pr-3 font-normal">Source</th>
              <th class="py-2 pr-3 text-right font-normal">Score</th>
              <th class="py-2 pr-3 text-right font-normal">Δscore</th>
              <th class="py-2 pr-3 text-right font-normal">Elements</th>
              <th class="py-2 text-right font-normal">Δelements</th>
            </tr>
          </thead>
          <tbody class="mono">
            {#each deltas as row}
              <tr class="border-b border-rc-border/40 last:border-b-0">
                <td class="py-2 pr-3">{row.capturedAt || row.id}</td>
                <td class="py-2 pr-3 text-rc-muted">{row.source || '—'}</td>
                <td class="py-2 pr-3 text-right">{row.score ?? '—'}</td>
                <td class="py-2 pr-3 text-right {deltaTone(row.scoreDelta, true)}">
                  {formatDelta(row.scoreDelta)}
                </td>
                <td class="py-2 pr-3 text-right">
                  {row.elementCount?.toLocaleString() ?? '—'}
                </td>
                <td class="py-2 text-right {deltaTone(row.elementDelta, false)}">
                  {formatDelta(row.elementDelta)}
                </td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {/if}
  </article>

  <footer class="rc-stat-label">
    For a full element-by-element diff between two captures, run
    <code>revitcli history diff &lt;fromRef&gt; &lt;toRef&gt;</code>.
  </footer>
</section>
