<script lang="ts">
  import { base } from '$app/paths';
  import '../app.css';

  // Navigation surface for the v2.0 dashboard. All three routes are
  // now live. Phase 1 shipped Overview; phase 2 enabled History; this
  // commit enables Multi-project, the last roadmap §7 in-scope route.
  const navLinks: { href: string; label: string; disabled: boolean }[] = [
    { href: `${base}/`, label: 'Overview', disabled: false },
    { href: `${base}/history`, label: 'History', disabled: false },
    { href: `${base}/projects`, label: 'Multi-project', disabled: false },
  ];
</script>

<div class="flex min-h-screen flex-col">
  <header class="border-b border-rc-border bg-rc-surface">
    <div class="mx-auto flex max-w-6xl items-center justify-between px-4 py-3">
      <div class="flex items-center gap-3">
        <span class="rc-stat-value text-rc-accent">RevitCli</span>
        <span class="rc-stat-label">Dashboard v2.0 (phase 2)</span>
      </div>
      <nav class="flex items-center gap-4">
        {#each navLinks as link}
          {#if link.disabled}
            <span
              class="cursor-not-allowed text-rc-muted"
              title="Coming in a v2.0 follow-up"
              aria-disabled="true"
            >{link.label}</span>
          {:else}
            <a href={link.href}>{link.label}</a>
          {/if}
        {/each}
      </nav>
    </div>
  </header>

  <main class="mx-auto w-full max-w-6xl flex-1 px-4 py-6">
    <slot />
  </main>

  <footer class="border-t border-rc-border bg-rc-surface text-rc-muted">
    <div class="mx-auto max-w-6xl px-4 py-3 text-xs">
      Static data view. All interactivity is client-side. Contribute at
      <a href="https://github.com/xiaodream551-a11y/revitcli">xiaodream551-a11y/revitcli</a>.
    </div>
  </footer>
</div>
