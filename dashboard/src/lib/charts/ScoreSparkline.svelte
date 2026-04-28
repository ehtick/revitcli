<script lang="ts">
  import { onMount } from 'svelte';
  import { Line } from 'svelte-chartjs';
  import { registerCharts } from './registerCharts';

  /**
   * Score sparkline for the Overview page. Default window is "last 7
   * captures"; can be re-used for the History page's full series by
   * lengthening the input.
   *
   * Display choice — line over bars: a sparkline conveys trend
   * direction more effectively than discrete bars at this size, and
   * Chart.js's line chart hover tooltip is more informative than the
   * bar variant when point density is high.
   */
  export let points: { date: string; score: number }[] = [];

  /** Optional override for the y-axis range. Defaults to 0-100 (the score range). */
  export let yMin: number = 0;
  export let yMax: number = 100;

  /** Hide axis ticks / grid for a cleaner sparkline look. The History page passes false. */
  export let minimal: boolean = true;

  onMount(() => registerCharts());

  $: data = {
    labels: points.map((p) => p.date),
    datasets: [
      {
        label: 'Score',
        data: points.map((p) => p.score),
        borderColor: 'rgba(56, 189, 248, 1)', // rc-accent
        backgroundColor: 'rgba(56, 189, 248, 0.18)',
        pointBackgroundColor: 'rgba(56, 189, 248, 1)',
        pointBorderColor: 'rgba(15, 23, 42, 1)', // rc-bg
        pointBorderWidth: 1.5,
        pointRadius: 3,
        tension: 0.3, // gentle smoothing without distorting trend
        fill: true,
      },
    ],
  };

  $: options = {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: { display: false },
      tooltip: {
        callbacks: {
          label: (ctx: { parsed: { y: number } }) => `score: ${ctx.parsed.y}`,
        },
      },
    },
    scales: {
      x: {
        display: !minimal,
        ticks: { color: 'rgb(148, 163, 184)' },
        grid: { display: false },
      },
      y: {
        display: !minimal,
        min: yMin,
        max: yMax,
        ticks: { color: 'rgb(148, 163, 184)' },
        grid: { color: 'rgba(148, 163, 184, 0.15)' },
      },
    },
  };
</script>

{#if points.length === 0}
  <p class="mt-3 text-rc-muted">No history available.</p>
{:else}
  <div
    class="mt-3"
    style="height: {minimal ? '90px' : '260px'}"
    data-test-id="score-sparkline-chart"
  >
    <Line {data} options={options} />
  </div>
{/if}
