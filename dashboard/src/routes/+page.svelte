<script lang="ts">
  import { onMount } from 'svelte';
  import { loadHistory, type HistoryDocument } from '$lib/loadHistory';
  import { elementCountsByCategory, extract } from '$lib/score';
  import CategoryBarChart from '$lib/charts/CategoryBarChart.svelte';
  import ScoreSparkline from '$lib/charts/ScoreSparkline.svelte';

  // Phase 2: Chart.js charts replace the placeholder DOM that phase 1
  // shipped. The data projections (elementCountsByCategory,
  // sparklinePoints) are unchanged — back-compat with C#-generated
  // history.json is intact.
  let history: HistoryDocument | null = null;
  let loadError = '';

  onMount(async () => {
    try {
      history = await loadHistory();
    } catch (err) {
      loadError = err instanceof Error ? err.message : String(err);
    }
  });

  $: latest = history?.entries?.[history.entries.length - 1] ?? null;
  $: latestSnapshot = latest?.snapshot ?? null;
  $: currentScore =
    latest?.score ?? (latestSnapshot ? extract(latestSnapshot, 'score') : null);
  $: elementsByCategory = elementCountsByCategory(latestSnapshot, 8);
  $: sparklinePoints =
    history?.entries
      ?.slice(-7)
      .map((e) => ({
        date: e.capturedAt?.slice(0, 10) ?? e.id,
        score: e.score ?? (e.snapshot ? extract(e.snapshot, 'score') : null) ?? 0
      })) ?? [];
</script>

<section class="space-y-6">
  <header class="flex items-end justify-between">
    <div>
      <h1 class="rc-stat-value">Overview</h1>
      <p class="rc-stat-label">Snapshot of the most recent capture in the configured history store.</p>
    </div>
    {#if history?.isStub}
      <span
        class="rounded-md border border-rc-warn/40 bg-rc-warn/10 px-3 py-1 text-xs text-rc-warn"
        title="No history.json found at /data/history.json or via ?history= query param. Showing stub data."
      >Demo data</span>
    {/if}
  </header>

  {#if loadError}
    <div class="rc-card border-rc-bad/40 bg-rc-bad/10 text-rc-bad">
      Failed to load history: {loadError}
    </div>
  {/if}

  <div class="grid grid-cols-1 gap-4 md:grid-cols-3">
    <article class="rc-card">
      <p class="rc-stat-label">Current score</p>
      <p class="rc-stat-value">
        {#if currentScore != null}
          {currentScore}
          <span class="text-sm text-rc-muted">/ 100</span>
        {:else}
          <span class="text-rc-muted">—</span>
        {/if}
      </p>
    </article>

    <article class="rc-card">
      <p class="rc-stat-label">Elements (latest)</p>
      <p class="rc-stat-value">
        {#if latest?.elementCount != null}
          {latest.elementCount.toLocaleString()}
        {:else}
          <span class="text-rc-muted">—</span>
        {/if}
      </p>
    </article>

    <article class="rc-card">
      <p class="rc-stat-label">Captured</p>
      <p class="rc-stat-value text-base mono">
        {latest?.capturedAt ?? '—'}
      </p>
      <p class="rc-stat-label mt-1">source: {latest?.source ?? 'n/a'}</p>
    </article>
  </div>

  <article class="rc-card">
    <header class="flex items-baseline justify-between">
      <h2 class="text-lg font-semibold">Element counts by category</h2>
      <span class="rc-stat-label">top {elementsByCategory.length}</span>
    </header>
    <CategoryBarChart rows={elementsByCategory} />
  </article>

  <article class="rc-card">
    <header class="flex items-baseline justify-between">
      <h2 class="text-lg font-semibold">Score, last 7 captures</h2>
      <span class="rc-stat-label">sparkline</span>
    </header>
    <ScoreSparkline points={sparklinePoints} />
  </article>

  <footer class="rc-stat-label">
    Tip: pass <code>?history=/path/to/history.json</code> to load a specific export, or run
    <code>revitcli dashboard build --output ./public</code> to inject your project history into the static site.
  </footer>
</section>
