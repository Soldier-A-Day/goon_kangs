import { cpSync, existsSync, rmSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

/**
 * Unity WebGL 산출물을 웹 앱의 `public/game/`으로 나른다.
 *
 *     node tools/webgl-sync.mjs
 *
 * 저장소에 번들을 넣지 않는 이유는 하나다 — 14MB짜리 바이너리가 커밋마다
 * 통째로 갈리면 이력이 못 쓰게 된다. 빌드는 만들어 쓰는 것이고,
 * `.gitignore`가 `apps/web/public/game/`을 막고 있다.
 */
const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const from = resolve(root, "unity/Build/web");
const to = resolve(root, "apps/web/public/game");

if (!existsSync(from)) {
  console.error(`[webgl] 빌드가 없다: ${from}\n` +
    "  Unity -batchmode -quit -projectPath unity " +
    "-executeMethod SoldierADay.EditorTools.BuildPlayer.Web -out Build/web");
  process.exit(1);
}

rmSync(to, { recursive: true, force: true });
cpSync(from, to, { recursive: true });
console.log(`[webgl] ${from} → ${to}`);
