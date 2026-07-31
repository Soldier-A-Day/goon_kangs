import { mkdirSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { ASSETS, type AssetEntry } from "@sad/assets";
import { assembleParts, totalTriangles, type Part } from "./kit.js";
import { BARRACKS, DRILL_GROUND } from "./maps.js";
import { fitToBudget, toObj } from "./mesh.js";
import { buildRifle } from "./rifle.js";

/**
 * M0 블록아웃 생성기.
 *
 * 카탈로그가 정한 예산에 **넘지 않으면서 가장 가깝게** 맞춘 지오메트리를 뽑는다.
 * 남는 예산은 낭비가 아니라 실제 에셋이 쓸 자리다 — 블록아웃이 예산을 꽉 채우면
 * 실제 모델을 넣을 때마다 예산을 넘긴다.
 *
 * 리그가 필요한 것(캐릭터·피복)은 여기서 만들지 않는다. OBJ가 스키닝을 담지
 * 못하기 때문이며, 그쪽은 Unity 에디터 스크립트가 맡는다.
 */

/**
 * 출력 경로는 **이 파일 기준**으로 잡는다.
 *
 * 상대 경로로 뒀더니 `npm run -w`가 작업 디렉터리를 패키지 안으로 옮겨
 * `tools/blockout/unity/Assets/Art/` 에 썼다. 생성기는 성공했다고 찍었고
 * 예산 검사는 파일이 없다고 했다 — 둘 다 맞는 말이라 원인이 안 보였다.
 */
const OUT = resolve(fileURLToPath(import.meta.url), "../../../..", "unity/Assets/Art");

interface Target {
  readonly id: string;
  readonly build: (detail: number) => Part[];
  /** 모듈 종류 수. 카탈로그의 `modules`와 맞아야 한다 */
  readonly moduleCount?: number;
}

const TARGETS: readonly Target[] = [
  // 소총은 부착물 하나라 조각을 나누지 않는다 — 손에 붙는 단일 오브젝트다
  { id: "equip.rifle", build: (detail) => [{ name: "rifle", mesh: buildRifle(detail) }] },
  {
    id: "base.drillGround",
    build: (detail) => assembleParts(DRILL_GROUND, detail),
    moduleCount: DRILL_GROUND.length,
  },
  {
    id: "base.barracks",
    build: (detail) => assembleParts(BARRACKS, detail),
    moduleCount: BARRACKS.length,
  },
];

function entryFor(id: string): AssetEntry {
  const entry = ASSETS.find((a) => a.id === id);
  if (!entry) throw new Error(`카탈로그에 없는 id: ${id}`);
  return entry;
}

function main(): void {
  let failed = false;

  for (const target of TARGETS) {
    const entry = entryFor(target.id);
    const budget = entry.lod0 * entry.count;

    // 모듈 종류 수는 §2가 정한 값이고, 드로우콜·배칭의 전제다.
    // 여기서 어긋나면 폴리가 맞아도 배칭 특성이 실제와 달라진다.
    if (target.moduleCount !== undefined && target.moduleCount !== entry.modules) {
      console.error(
        `✗ ${entry.id}: 모듈 ${target.moduleCount}종 — 카탈로그는 ${entry.modules}종`,
      );
      failed = true;
      continue;
    }

    const fitted = fitToBudget(budget, target.build, totalTriangles);
    const parts = fitted.value;

    const path = `${OUT}/${entry.category}/${entry.id}/${entry.id.split(".").pop()}.obj`;
    mkdirSync(dirname(path), { recursive: true });
    writeFileSync(
      path,
      toObj(
        parts,
        `${entry.label} — 블록아웃 (예산 ${budget.toLocaleString()} tris · 디테일 ${fitted.detail})`,
      ),
    );

    const fill = (fitted.triangles / budget) * 100;
    const modules = target.moduleCount ? ` · 모듈 ${target.moduleCount}종` : "";
    console.log(
      `✓ ${entry.id.padEnd(18)} ${fitted.triangles.toLocaleString().padStart(7)} tris / ` +
        `${budget.toLocaleString().padStart(7)} (${fill.toFixed(0)}%) · ` +
        `조각 ${parts.length.toString().padStart(3)}${modules}`,
    );
  }

  if (failed) process.exit(1);

  console.log(
    "\n리그가 필요한 캐릭터·피복은 Unity 쪽에서 만든다 — OBJ는 스키닝을 담지 못한다.",
  );
}

main();
