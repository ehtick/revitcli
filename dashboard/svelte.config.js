import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/**
 * SvelteKit configuration for the RevitCli dashboard.
 *
 * We commit to a fully static build (`adapter-static`) with no SSR. The C#
 * `revitcli dashboard serve` command serves the prebuilt files via HttpListener
 * (cross-platform, BCL only), and `revitcli dashboard build` copies these files
 * to a user-specified output directory and injects the user's history.json.
 *
 * `prerender: true` + `fallback: 'index.html'` produces a single-page app shell
 * that resolves all client-side routes through `+page.svelte` modules. We avoid
 * `trailingSlash: 'always'` so URLs match the way HttpListener resolves paths.
 */
/** @type {import('@sveltejs/kit').Config} */
const config = {
  preprocess: vitePreprocess(),
  kit: {
    adapter: adapter({
      pages: 'build',
      assets: 'build',
      fallback: 'index.html',
      precompress: false,
      strict: true
    }),
    paths: {
      // Serve from root by default. Static deploys under a sub-path
      // (e.g. GitHub Pages) can override via `BASE_PATH` env var.
      base: process.env.BASE_PATH ?? ''
    },
    prerender: {
      handleHttpError: ({ path, message }) => {
        // Routes referenced only in the data layer (e.g. /data/history.json)
        // are loaded at runtime — silence prerender warnings for them.
        if (path.startsWith('/data/')) return;
        throw new Error(message);
      }
    }
  }
};

export default config;
