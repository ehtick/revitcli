import { createServer } from "node:http";
import { createReadStream, existsSync } from "node:fs";
import { stat } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const base = process.env.BASE_PATH ?? "/revitcli";
const port = Number(process.env.PORT ?? "4174");
const host = process.env.HOST ?? "127.0.0.1";

const build = spawnSync(
  process.platform === "win32" ? "npm.cmd" : "npm",
  ["run", "build"],
  {
    cwd: root,
    env: { ...process.env, BASE_PATH: base },
    shell: process.platform === "win32",
    stdio: "inherit",
  },
);
if (build.status !== 0) process.exit(build.status ?? 1);

const buildDir = path.join(root, "build");
const contentTypes = new Map([
  [".css", "text/css; charset=utf-8"],
  [".html", "text/html; charset=utf-8"],
  [".js", "text/javascript; charset=utf-8"],
  [".json", "application/json; charset=utf-8"],
  [".svg", "image/svg+xml"],
  [".txt", "text/plain; charset=utf-8"],
  [".wasm", "application/wasm"],
]);

function resolveStaticPath(requestUrl) {
  const url = new URL(requestUrl, `http://${host}:${port}`);
  if (url.pathname === base) return path.join(buildDir, "index.html");
  if (!url.pathname.startsWith(`${base}/`)) return null;

  const relative = decodeURIComponent(url.pathname.slice(base.length + 1));
  const candidate = path.resolve(buildDir, relative || "index.html");
  if (!candidate.startsWith(buildDir)) return null;
  if (existsSync(candidate)) return candidate;
  if (!path.extname(candidate)) return path.join(buildDir, "index.html");
  return candidate;
}

const server = createServer(async (req, res) => {
  const filePath = resolveStaticPath(req.url ?? "/");
  if (!filePath) {
    res.writeHead(404).end("outside base path");
    return;
  }

  try {
    const info = await stat(filePath);
    if (!info.isFile()) throw new Error("not a file");
    res.writeHead(200, {
      "content-type": contentTypes.get(path.extname(filePath)) ?? "application/octet-stream",
      "content-length": info.size,
    });
    createReadStream(filePath).pipe(res);
  } catch {
    res.writeHead(404, { "content-type": "text/plain; charset=utf-8" });
    res.end("not found");
  }
});

server.listen(port, host, () => {
  console.log(`Serving dashboard build at http://${host}:${port}${base}/`);
});
