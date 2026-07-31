import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve } from "node:path";

/**
 * M0 WebGL 빌드 정적 서버.
 *
 * `python -m http.server`로는 안 된다. Unity WebGL은 Brotli로 압축된 `.br` 파일을
 * 그대로 내려주면서 `Content-Encoding: br`을 붙여야 브라우저가 풀어서 읽는다 —
 * 헤더가 없으면 "Unable to parse Build/xxx.framework.js.br" 로 죽는다.
 *
 * SharedArrayBuffer용 COOP/COEP도 함께 붙인다. 지금 씬은 쓰지 않지만
 * 나중에 스레드를 켜면 필요하고, 미리 맞춰두면 그때 원인을 찾느라 헤매지 않는다.
 */
const ROOT = resolve(process.argv[2] ?? "unity/Build/M0");
const PORT = Number(process.argv[3] ?? 8090);

const MIME = {
  ".html": "text/html; charset=utf-8",
  ".js": "application/javascript",
  ".wasm": "application/wasm",
  ".data": "application/octet-stream",
  ".json": "application/json",
  ".css": "text/css",
  ".png": "image/png",
  ".symbols": "application/octet-stream",
};

/** Unity는 압축 방식을 확장자로만 알린다 */
const ENCODING = { ".br": "br", ".gz": "gzip" };

createServer((req, res) => {
  const url = new URL(req.url ?? "/", `http://${req.headers.host}`);
  const requested = url.pathname === "/" ? "/index.html" : url.pathname;
  const path = join(ROOT, normalize(requested).replace(/^(\.\.[/\\])+/, ""));

  if (!existsSync(path) || statSync(path).isDirectory()) {
    res.statusCode = 404;
    res.end("not found");
    return;
  }

  const ext = extname(path);
  const encoding = ENCODING[ext];

  if (encoding) {
    // .framework.js.br → 안쪽 확장자로 실제 타입을 정한다
    const inner = extname(path.slice(0, -ext.length));
    res.setHeader("Content-Type", MIME[inner] ?? "application/octet-stream");
    res.setHeader("Content-Encoding", encoding);
  } else {
    res.setHeader("Content-Type", MIME[ext] ?? "application/octet-stream");
  }

  res.setHeader("Cross-Origin-Opener-Policy", "same-origin");
  res.setHeader("Cross-Origin-Embedder-Policy", "require-corp");
  res.setHeader("Cache-Control", "no-store");

  createReadStream(path).pipe(res);
}).listen(PORT, () => {
  console.log(`[m0serve] ${ROOT} → http://localhost:${PORT}`);
});
