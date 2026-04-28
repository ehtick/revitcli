<script lang="ts">
  import { onMount } from 'svelte';
  import {
    loadProjects,
    latestSummary,
    type MultiProjectDocument,
  } from '$lib/loadProjects';
  import ScoreSparkline from '$lib/charts/ScoreSparkline.svelte';
  import { extract } from '$lib/score';

  /**
   * Multi-project route — last v2.0 §7 in-scope item. Each project is
   * its own card with: name, latest score, latest element count,
   * capture count, and a 7-capture sparkline reusing the Overview
   * component. Cards lay out in a responsive grid (1 → 2 → 3 columns).
   *
   * Source: `data/projects.json` produced by
   * `revitcli dashboard build --project NAME:DIR ...`. Falls back to a
   * stub for screenshots / offline development.
   */
  let doc: MultiProjectDocument | null = null;
  let loadError = '';

  onMount(async () => {
    try {
      doc = await loadProjects();
    } catch (err) {
      loadError = err instanceof Error ? err.message : String(err);
    }
  });

  // Derived per-project view models so the markup stays declarative.
  $: cards =
    doc?.projects.map((p) => {
      const summary = latestSummary(p);
      const sparklinePoints = (p.history?.entries ?? [])
        .slice(-7)
        .map((e) => ({
          date: e.capturedAt?.slice(0, 10) ?? e.id,
          score:
            e.score ??
            (e.snapshot ? extract(e.snapshot, 'score') : null) ??
            0,
        }));
      return { name: p.name, historyDir: p.historyDir, summary, sparklinePoints };
    }) ?? [];

  // Sort: highest current score first so the "best maintained" project
  // surfaces at the top. Ties broken by name for determinism.
  $: sortedCards = [...cards].sort((a, b) => {
    const sa = a.summary.score ?? -1;
    const sb = b.summary.score ?? -1;
    if (sb !== sa) return sb - sa;
    return a.name.localeCompare(b.name);
  });
</script>

<section class="space-y-6">
  <header class="flex items-end justify-between">
    <div>
      <h1 class="rc-stat-value">Multi-project</h1>
      <p class="rc-stat-label">
        Side-by-side health for every project bundled by
        <code>revitcli dashboard build --project NAME:DIR</code>.
      </p>
    </div>
    {#if doc?.isStub}
      <span
        class="rounded-md border border-rc-warn/40 bg-rc-warn/10 px-3 py-1 text-xs text-rc-warn"
        title="No projects.json found at /data/projects.json or via ?projects= query param. Showing stub data."
      >Demo data</span>
    {/if}
  </header>

  {#if loadError}
    <div class="rc-card border-rc-bad/40 bg-rc-bad/10 text-rc-bad">
      Failed to load projects: {loadError}
    </div>
  {/if}

  {#if cards.length === 0}
    <article class="rc-card">
      <p class="text-rc-muted">
        No projects configured. Bundle one or more histories with
        <code
          >revitcli dashboard build --output ./public --project
          "ProjectA:./projA/.revitcli/history"</code
        >
        and re-deploy.
      </p>
    </article>
  {:else}
    <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3" data-test-id="project-cards">
      {#each sortedCards as card (card.name)}
        <article class="rc-card flex flex-col gap-3">
          <header>
            <h2 class="text-lg font-semibold">{card.name}</h2>
            {#if card.historyDir}
              <p class="rc-stat-label mono truncate" title={card.historyDir}>
                {card.historyDir}
              </p>
            {/if}
          </header>

          <div class="grid grid-cols-3 gap-3 text-center">
            <div>
              <p class="rc-stat-label">Score</p>
              <p class="rc-stat-value">
                {#if card.summary.score != null}
                  {card.summary.score}
                {:else}
                  <span class="text-rc-muted">—</span>
                {/if}
              </p>
            </div>
            <div>
              <p class="rc-stat-label">Elements</p>
              <p class="rc-stat-value text-base">
                {#if card.summary.elementCount != null}
                  {card.summary.elementCount.toLocaleString()}
                {:else}
                  <span class="text-rc-muted">—</span>
                {/if}
              </p>
            </div>
            <div>
              <p class="rc-stat-label">Captures</p>
              <p class="rc-stat-value text-base">{card.summary.captureCount}</p>
            </div>
          </div>

          <div>
            <p class="rc-stat-label">Score, last 7</p>
            <ScoreSparkline points={card.sparklinePoints} />
          </div>

          {#if card.summary.capturedAt}
            <footer class="rc-stat-label mono text-xs">
              latest: {card.summary.capturedAt}
            </footer>
          {/if}
        </article>
      {/each}
    </div>
  {/if}

  <footer class="rc-stat-label">
    Tip: pass <code>?projects=/path/to/projects.json</code> to load a specific
    bundle, or run
    <code
      >revitcli dashboard build --output ./public --project "Name:./.revitcli/history"</code
    >
    to bake your projects into the deployable site.
  </footer>
</section>
