import { mkdirSync, rmSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { ASSETS, type AssetEntry } from "@sad/assets";
import { assembleParts, totalTriangles, type Module } from "./kit.js";
import {
  BARRACKS, BOILER_ROOM, DRILL_GROUND, GUARD_POST, INFIRMARY,
  MESS_HALL, PERIMETER, STORAGE, TRAINING_FIELD,
} from "./maps.js";
import { fitToBudget, toObj, type Mesh } from "./mesh.js";
import { assemblePieces, type Prop } from "./parts.js";
import { LARGE, MEDIUM, SMALL } from "./props.js";
import { EQUIPMENT } from "./equipment.js";
import { buildRifle } from "./rifle.js";
import {
  BIVOUAC, CBRN, COLD_WEATHER, COMBINED, HOT_WEATHER,
  marchSegment, RANGE, RANGER, VILLAGE,
} from "./training.js";

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

function entryFor(id: string): AssetEntry {
  const entry = ASSETS.find((a) => a.id === id);
  if (!entry) throw new Error(`카탈로그에 없는 id: ${id}`);
  return entry;
}

/**
 * 폴더를 비우고 다시 쓴다.
 *
 * 지우지 않으면 이름이 바뀐 옛 파일이 남아 예산 검사에 이중으로 잡힌다 —
 * 생성기를 고칠 때마다 예산이 조금씩 부는 유령이 생긴다.
 */
function write(entry: AssetEntry, file: string, contents: string, wipe = false): void {
  const dir = `${OUT}/${entry.category}/${entry.id}`;
  if (wipe) rmSync(dir, { recursive: true, force: true });
  const path = `${dir}/${file}`;
  mkdirSync(dirname(path), { recursive: true });
  writeFileSync(path, contents);
}

interface Row {
  readonly id: string;
  readonly triangles: number;
  readonly budget: number;
  readonly files: number;
  readonly parts: number;
  readonly note: string;
}

const rows: Row[] = [];
let failed = false;

/** 모듈러 키트 하나를 예산에 맞춰 뽑는다 */
function emitKit(id: string, modules: readonly Module[], checkModuleCount = false): void {
  const entry = entryFor(id);
  const budget = entry.lod0 * entry.count;

  // 모듈 종류 수는 §2가 정한 값이고, 드로우콜·배칭의 전제다.
  // 여기서 어긋나면 폴리가 맞아도 배칭 특성이 실제와 달라진다.
  if (checkModuleCount && modules.length !== entry.modules) {
    console.error(`✗ ${id}: 모듈 ${modules.length}종 — 카탈로그는 ${entry.modules}종`);
    failed = true;
    return;
  }

  const fitted = fitToBudget(budget, (d) => assembleParts(modules, d), totalTriangles);
  const name = entry.id.split(".").pop()!;

  write(
    entry, `${name}.obj`,
    toObj(fitted.value, `${entry.label} — 블록아웃 (예산 ${budget.toLocaleString()} tris)`),
    true,
  );

  rows.push({
    id: entry.id, triangles: fitted.triangles, budget,
    files: 1, parts: fitted.value.length,
    note: checkModuleCount ? `모듈 ${modules.length}종` : `모듈 ${modules.length}`,
  });
}

/**
 * 행군 코스 — 구간마다 따로 뽑는다.
 *
 * §12가 "구간 전환 시 히칭이 우려된다"고 남겨둔 항목이라 로드·언로드를
 * 실제로 재야 한다. 한 덩어리로 만들고 나중에 자르면 **자르는 방식이
 * 측정 결과를 바꾼다.**
 */
function emitMarch(): void {
  const entry = entryFor("train.march");
  const segments = entry.streamingSegments ?? 1;
  const perSegment = Math.floor((entry.lod0 * entry.count) / segments);

  let total = 0;
  let parts = 0;

  for (let i = 0; i < segments; i += 1) {
    const modules = marchSegment(i);
    const fitted = fitToBudget(perSegment, (d) => assembleParts(modules, d), totalTriangles);
    write(
      entry, `march_seg${i}.obj`,
      toObj(fitted.value, `행군 코스 ${i + 1}구간 — 블록아웃 (구간 예산 ${perSegment.toLocaleString()} tris)`),
      i === 0,
    );
    total += fitted.triangles;
    parts += fitted.value.length;
  }

  rows.push({
    id: entry.id, triangles: total, budget: entry.lod0 * entry.count,
    files: segments, parts, note: `${segments}구간 스트리밍`,
  });
}

/** 소품 등급 하나. 종류마다 파일이 하나씩 나온다 */
function emitProps(id: string, props: readonly Prop[]): void {
  const entry = entryFor(id);

  if (props.length !== entry.count) {
    console.error(`✗ ${id}: ${props.length}종 — 카탈로그는 ${entry.count}종`);
    failed = true;
    return;
  }

  let total = 0;
  props.forEach((item, index) => {
    const fitted = fitToBudget(
      entry.lod0,
      (d) => assemblePieces(item.pieces, d),
      (mesh: Mesh) => mesh.triangleCount,
      24,
    );
    write(
      entry, `${item.id}.obj`,
      toObj([{ name: item.id, mesh: fitted.value }], `${item.label} — 블록아웃 (예산 ${entry.lod0} tris)`),
      index === 0,
    );
    total += fitted.triangles;
  });

  rows.push({
    id: entry.id, triangles: total, budget: entry.lod0 * entry.count,
    files: props.length, parts: props.length, note: `${props.length}종`,
  });
}

function main(): void {
  // 소총은 부착물 하나라 조각을 나누지 않는다 — 손에 붙는 단일 오브젝트다
  const rifle = entryFor("equip.rifle");
  const rifleBudget = rifle.lod0 * rifle.count;
  const rifleFit = fitToBudget(rifleBudget, buildRifle, (m: Mesh) => m.triangleCount);
  write(rifle, "rifle.obj", toObj([{ name: "rifle", mesh: rifleFit.value }], "소총 — 블록아웃"), true);
  rows.push({
    id: rifle.id, triangles: rifleFit.triangles, budget: rifleBudget,
    files: 1, parts: 1, note: "",
  });

  emitKit("base.drillGround", DRILL_GROUND, true);
  emitKit("base.barracks", BARRACKS, true);
  emitKit("base.storage", STORAGE, true);
  emitKit("base.messHall", MESS_HALL, true);
  emitKit("base.guardPost", GUARD_POST, true);
  emitKit("base.perimeter", PERIMETER, true);
  emitKit("base.boilerRoom", BOILER_ROOM, true);
  emitKit("base.infirmary", INFIRMARY, true);
  emitKit("base.trainingField", TRAINING_FIELD, true);

  emitKit("train.range", RANGE);
  emitKit("train.cbrn", CBRN);
  emitMarch();
  emitKit("train.bivouac", BIVOUAC);
  emitKit("train.ranger", RANGER);
  emitKit("train.coldWeather", COLD_WEATHER);
  emitKit("train.hotWeather", HOT_WEATHER);
  emitKit("train.village", VILLAGE);
  emitKit("train.combined", COMBINED);

  // 보직 장비 — 종류마다 카탈로그 항목이 따로다(§4.1)
  for (const item of EQUIPMENT) {
    const entry = entryFor(item.assetId);
    const fitted = fitToBudget(
      entry.lod0 * entry.count,
      (d) => assemblePieces(item.pieces, d),
      (mesh: Mesh) => mesh.triangleCount,
      24,
    );
    write(entry, `${item.id}.obj`,
      toObj([{ name: item.id, mesh: fitted.value }], `${item.label} — 블록아웃`), true);
    rows.push({
      id: entry.id, triangles: fitted.triangles, budget: entry.lod0 * entry.count,
      files: 1, parts: 1, note: entry.role ?? "",
    });
  }

  emitProps("prop.small", SMALL);
  emitProps("prop.medium", MEDIUM);
  emitProps("prop.large", LARGE);

  console.log("id\t\t\t   삼각형\t    예산\t충족\t파일\t조각\t비고");
  for (const row of rows) {
    const fill = ((row.triangles / row.budget) * 100).toFixed(0);
    console.log(
      `${row.id.padEnd(20)}\t${row.triangles.toLocaleString().padStart(8)}\t` +
      `${row.budget.toLocaleString().padStart(8)}\t${fill.padStart(3)}%\t` +
      `${row.files}\t${row.parts}\t${row.note}`,
    );
  }

  const total = rows.reduce((sum, r) => sum + r.triangles, 0);
  const budget = rows.reduce((sum, r) => sum + r.budget, 0);
  console.log(`\n합계 ${total.toLocaleString()} / ${budget.toLocaleString()} tris`);

  if (failed) process.exit(1);
  console.log("리그가 필요한 캐릭터·피복은 Unity 쪽에서 만든다 — OBJ는 스키닝을 담지 못한다.");
}

main();
