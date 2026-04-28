# Deploying the RevitCli Dashboard to GitHub Pages

The dashboard is a static SPA — no server, no build step at runtime. That
means GitHub Pages is the lowest-friction host for it. This doc covers
the canonical path: a workflow that re-builds and re-deploys on every
push to `main`.

## Privacy first

The dashboard renders model element counts, scores, and capture
timestamps loaded from `data/history.json` (and optionally
`data/projects.json`). **A public GitHub Pages deploy makes this data
publicly fetchable.** Two layers of protection ship by default:

1. The dashboard's `app.html` includes `<meta name="robots"
content="noindex">`, so search engines won't index the deployed site.
2. The deploy workflow runs only on the default branch (typically
   `main`), so an opened PR cannot publish a draft.

**Neither layer hides the data from a determined visitor with the URL.**
For sensitive history, deploy from a private repo (Pages built from
private repos requires GitHub Pro / Enterprise) or host the static
output behind your own auth proxy.

## Quickstart

1. Copy the bundled template into your repo:

   ```bash
   mkdir -p .github/workflows
   cp \
     ../revitcli/docs/ci/dashboard-deploy-template.yml \
     .github/workflows/dashboard-deploy.yml
   ```

2. Edit `--history-dir` (and optionally one or more `--project`) to
   match where your repo keeps its history store(s).

3. Repo Settings → **Pages** → set "Source" to **GitHub Actions**.
   The workflow's `actions/configure-pages` + `actions/deploy-pages` do
   the rest; no manual `gh-pages` branch is required.

4. Push to `main`. The workflow runs, the deploy URL appears in the
   workflow summary.

## Repository layout shapes the URL

| Repo               | Pages URL                         | `BASE_PATH`           |
| ------------------ | --------------------------------- | --------------------- |
| `<user>.github.io` | `https://<user>.github.io/`       | empty (drop the line) |
| `<org>/<repo>`     | `https://<org>.github.io/<repo>/` | `/<repo>` (default)   |
| custom domain      | `https://your.domain.tld/`        | empty                 |

The template defaults to **project pages** (`<org>/<repo>`) by setting
`BASE_PATH=/${GITHUB_REPOSITORY##*/}` before `npm run build`. Drop the
prefix on the build line for user/org root pages or a custom domain.

## How it bundles your data

`revitcli dashboard build` runs after `npm run build` and:

1. Copies the prebuilt SvelteKit static bundle from `dashboard/build/`
   into `./public/`.
2. Inlines `index.json` from `--history-dir` as `public/data/history.json`
   for the Overview / History routes.
3. For each `--project NAME:DIR`, inlines that project's `index.json` as
   one entry inside `public/data/projects.json` (used by the Multi-project
   route).

A missing or malformed `index.json` becomes an empty placeholder so the
build never blocks on one bad input — the dashboard renders a "0 captures"
state.

## Disabling indexing on subdirectories

If your deployed site lives under a path you'd like search engines to
ignore even more emphatically (e.g. you want a site-wide
`/robots.txt`), drop a `dashboard/static/robots.txt`:

```
User-agent: *
Disallow: /
```

SvelteKit copies `static/*` verbatim into the build output, so this lands
at the deploy root.

## SHA-pinning third-party actions

Every `uses:` line in the template is SHA-pinned with a comment label
naming the version. When updating:

```bash
gh api repos/<owner>/<repo>/git/refs/tags/<tag> --jq '.object.sha'
# Update both the SHA and the comment label in the same commit.
```

Never trust a floating `@v4` tag — it can be re-pointed at compromised
code at any time.
