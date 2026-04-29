<script lang="ts">
  import type { TooltipItem } from 'chart.js';
  import { onMount } from 'svelte';
  import { Bar } from 'svelte-chartjs';
  import { registerCharts } from './registerCharts';

  /**
   * Horizontal-style bar chart for "elements per category" on the
   * Overview page. Receives the same projection the placeholder
   * `<ul>` consumed: `{ category, count }[]` already sorted desc.
   *
   * Why horizontal: category names are unbounded ("Specialty
   * Equipment", "Generic Models") and a vertical layout truncates
   * them awkwardly. Chart.js calls horizontal bars `indexAxis: 'y'`.
   */
  export let rows: { category: string; count: number }[] = [];

  // Register Chart.js controllers/scales lazily on mount so SSR (when
  // svelte-kit prerenders) doesn't try to touch `window`.
  onMount(() => registerCharts());

  $: data = {
    labels: rows.map((r) => r.category),
    datasets: [
      {
        label: 'Elements',
        data: rows.map((r) => r.count),
        backgroundColor: 'rgba(56, 189, 248, 0.7)', // rc-accent (sky-400) at 70%
        borderColor: 'rgba(56, 189, 248, 1)',
        borderWidth: 1,
        borderRadius: 4,
      },
    ],
  };

  $: options = {
    indexAxis: 'y' as const,
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          label: (ctx: TooltipItem<'bar'>) =>
            `${(ctx.parsed.x ?? 0).toLocaleString()} elements`,
        },
      },
    },
    scales: {
      x: {
        beginAtZero: true,
        ticks: { color: 'rgb(148, 163, 184)' }, // rc-muted (slate-400)
        grid: { color: 'rgba(148, 163, 184, 0.15)' },
      },
      y: {
        ticks: { color: 'rgb(226, 232, 240)' }, // slate-200
        grid: { display: false },
      },
    },
  };
</script>

{#if rows.length === 0}
  <p class="mt-3 text-rc-muted">No category data available.</p>
{:else}
  <!-- Height scales with row count so dense models still fit each bar. -->
  <div
    class="mt-3"
    style="height: {Math.max(120, rows.length * 28)}px"
    data-test-id="element-counts-chart"
  >
    <Bar {data} options={options} />
  </div>
{/if}
