import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

/**
 * Vite config for the RevitCli dashboard.
 *
 * Kept intentionally minimal: SvelteKit's adapter-static handles the build,
 * Tailwind/PostCSS run via postcss.config.js, and we add no SSR or
 * server-side plugins. Dev server is bound to localhost only — the dashboard
 * is local-data tooling and must not be exposed on a LAN by default.
 */
export default defineConfig({
  plugins: [sveltekit()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: false
  },
  preview: {
    host: '127.0.0.1',
    port: 4173,
    strictPort: false
  }
});
