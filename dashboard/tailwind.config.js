/** @type {import('tailwindcss').Config} */
export default {
  content: ['./src/**/*.{html,js,svelte,ts}'],
  darkMode: 'class',
  theme: {
    extend: {
      colors: {
        // Match the RevitCli terminal palette: charcoal background, cyan
        // accent for primary metrics, muted slate for secondary text.
        'rc-bg': '#0f172a',
        'rc-surface': '#1e293b',
        'rc-border': '#334155',
        'rc-text': '#e2e8f0',
        'rc-muted': '#94a3b8',
        'rc-accent': '#06b6d4',
        'rc-good': '#22c55e',
        'rc-warn': '#f59e0b',
        'rc-bad': '#ef4444'
      },
      fontFamily: {
        mono: [
          'ui-monospace',
          'SFMono-Regular',
          'Menlo',
          'Consolas',
          'Liberation Mono',
          'monospace'
        ]
      }
    }
  },
  plugins: []
};
