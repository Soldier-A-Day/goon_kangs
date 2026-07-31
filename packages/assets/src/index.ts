import manifestJson from "../data/manifest.json" with { type: "json" };

/**
 * 에셋 카탈로그.
 *
 * `docs/ASSETS.md`의 숫자를 기계가 읽는 형태로 옮긴 것이다. 산문으로만 있으면
 * 사람이 읽고 지켜야 하는데, 예산은 반드시 조용히 넘는다 — 넘은 시점에는
 * 이미 만들어진 에셋이 있어 되돌리기가 비싸다.
 *
 * **총계는 저장하지 않는다.** 저장된 총계는 두 번째 진실이 되어 항목과 갈라진다.
 * 전부 항목에서 유도하고, 문서가 적어둔 값과 맞는지는 테스트가 검산한다.
 */
export type Milestone = "M0" | "M1" | "M2" | "M3" | "M4" | "M5";

export type AssetCategory =
  | "character"
  | "clothing"
  | "baseMap"
  | "trainingMap"
  | "prop"
  | "equipment";

export interface AssetEntry {
  readonly id: string;
  readonly category: AssetCategory;
  readonly label: string;
  /** 이 항목이 몇 벌인지. 피복 슬롯처럼 종류가 여럿이면 그 수다 */
  readonly count: number;
  /** 한 벌당 LOD0 삼각형 */
  readonly lod0: number;
  readonly milestone: Milestone;
  readonly lodLevels?: number;
  readonly rig?: string;
  readonly slot?: string;
  readonly zone?: string;
  readonly modules?: number;
  readonly bundleMb?: number;
  readonly curriculum?: string;
  readonly streamingSegments?: number;
  readonly instanced?: boolean;
  readonly attach?: string;
  readonly role?: string;
  readonly note?: string;
}

export interface Budget {
  readonly value: number;
  /** 표 18-2의 **완화 불가** 항목인가. 미달 시 스코프를 깎지 않고 다시 만든다 */
  readonly hard: boolean;
  readonly note?: string;
}

export const manifest = manifestJson as unknown as {
  readonly budgets: Readonly<Record<string, Budget>>;
  readonly importRules: {
    readonly textureCompression: string;
    readonly maxTextureSize: number;
    readonly meshCompression: string;
    readonly readWriteEnabled: boolean;
    readonly lodRatios: Readonly<Record<string, number>>;
  };
  readonly assets: readonly AssetEntry[];
  readonly concurrent: {
    readonly entries: readonly { id: string; tris: number; basis: string }[];
    readonly documentedTotal: number;
  };
  readonly downloadBundles: {
    readonly initial: readonly { id: string; label: string; mb: number }[];
    readonly documentedInitialTotalMb: number;
  };
  readonly documentedTotals: Readonly<Record<string, number>>;
};

export const ASSETS: readonly AssetEntry[] = manifest.assets;

/** 한 항목이 차지하는 LOD0 폴리 총량 (개당 × 벌 수) */
export function trisOf(entry: AssetEntry): number {
  return entry.lod0 * entry.count;
}

export function byCategory(category: AssetCategory): readonly AssetEntry[] {
  return ASSETS.filter((entry) => entry.category === category);
}

export function categoryTris(category: AssetCategory): number {
  return byCategory(category).reduce((sum, entry) => sum + trisOf(entry), 0);
}

export function categoryCount(category: AssetCategory): number {
  return byCategory(category).reduce((sum, entry) => sum + entry.count, 0);
}

/**
 * 마일스톤까지 만들어져 있어야 하는 에셋. 19.0 로드맵 순서를 그대로 쓴다.
 *
 * M0에서 M5 물량을 만들지 않는 것이 요점이다 — 19.0이 마일스톤을 나눈 이유가
 * 그것이고, 순서를 어기면 성능 게이트가 통과하기 전에 물량이 쌓인다.
 */
const ORDER: readonly Milestone[] = ["M0", "M1", "M2", "M3", "M4", "M5"];

export function requiredBy(milestone: Milestone): readonly AssetEntry[] {
  const limit = ORDER.indexOf(milestone);
  return ASSETS.filter((entry) => ORDER.indexOf(entry.milestone) <= limit);
}

/** 동시 표시 최대 부하. 이게 §0의 화면 예산과 부딪히는 값이다 */
export function concurrentTris(): number {
  return manifest.concurrent.entries.reduce((sum, entry) => sum + entry.tris, 0);
}

export function initialDownloadMb(): number {
  return manifest.downloadBundles.initial.reduce((sum, bundle) => sum + bundle.mb, 0);
}

export interface Finding {
  readonly severity: "error" | "warn";
  readonly message: string;
}

/**
 * 카탈로그 자체의 정합성 검사.
 *
 * 에셋이 아직 하나도 없어도 돌아간다 — 검사 대상은 **예산이지 파일이 아니다.**
 * 실제 파일 검사는 Unity 쪽 `AssetBudgetReport`가 맡고, 이 함수는 그 기준이
 * 스스로 모순되지 않는지 본다. 기준이 틀린 채로 파일을 검사하면 아무 의미가 없다.
 */
export function checkManifest(): readonly Finding[] {
  const findings: Finding[] = [];
  const error = (message: string) => findings.push({ severity: "error", message });
  const warn = (message: string) => findings.push({ severity: "warn", message });

  const seen = new Set<string>();
  for (const entry of ASSETS) {
    if (seen.has(entry.id)) error(`중복 id: ${entry.id}`);
    seen.add(entry.id);

    if (entry.count <= 0) error(`${entry.id}: count가 ${entry.count}`);
    if (entry.lod0 <= 0) error(`${entry.id}: lod0가 ${entry.lod0}`);
    if (!ORDER.includes(entry.milestone)) error(`${entry.id}: 알 수 없는 마일스톤 ${entry.milestone}`);

    // 맵 번들 상한은 16.0이 정한 값이다. 여기서 넘으면 로비 프리페치로도
    // 못 가린다 — 한 번에 받아야 하는 덩어리라서.
    const cap = manifest.budgets.mapBundleMb.value;
    if (entry.bundleMb !== undefined && entry.bundleMb > cap) {
      error(`${entry.id}: 번들 ${entry.bundleMb}MB — 맵당 상한 ${cap}MB 초과`);
    }
  }

  // 문서가 적어둔 총계와 항목에서 유도한 값이 맞는가.
  // 어느 쪽이 틀렸든, 둘이 갈라진 것 자체가 결함이다.
  const documented = manifest.documentedTotals;
  const derived: Record<string, number> = {
    clothingCount: categoryCount("clothing"),
    clothingTris: categoryTris("clothing"),
    baseMapTris: categoryTris("baseMap"),
    baseMapModules: byCategory("baseMap").reduce((sum, e) => sum + (e.modules ?? 0), 0),
    trainingMapTris: categoryTris("trainingMap"),
    trainingMapBundleMb: byCategory("trainingMap").reduce((sum, e) => sum + (e.bundleMb ?? 0), 0),
    propCount: categoryCount("prop"),
    propTris: categoryTris("prop"),
    equipmentTris: categoryTris("equipment"),
    npcTris: byCategory("character")
      .filter((e) => e.id !== "char.base.player")
      .reduce((sum, e) => sum + trisOf(e), 0),
  };

  for (const [key, value] of Object.entries(derived)) {
    if (documented[key] !== value) {
      error(`${key}: 문서 ${documented[key]} ≠ 항목 유도 ${value}`);
    }
  }

  if (concurrentTris() !== manifest.concurrent.documentedTotal) {
    error(
      `동시 표시: 문서 ${manifest.concurrent.documentedTotal} ≠ 항목 합 ${concurrentTris()}`,
    );
  }

  if (initialDownloadMb() !== manifest.downloadBundles.documentedInitialTotalMb) {
    error(
      `초기 번들: 문서 ${manifest.downloadBundles.documentedInitialTotalMb}MB ≠ ` +
        `항목 합 ${initialDownloadMb()}MB`,
    );
  }

  // 예산 대비. 넘지 않았더라도 얼마나 남았는지가 판단 재료다.
  const screen = manifest.budgets.screenTris.value;
  if (concurrentTris() > screen) {
    error(`동시 표시 ${concurrentTris()} tris — 화면 예산 ${screen} 초과`);
  } else if (concurrentTris() > screen * 0.8) {
    warn(`동시 표시가 화면 예산의 ${((concurrentTris() / screen) * 100).toFixed(0)}%`);
  }

  const download = manifest.budgets.initialDownloadMb.value;
  if (initialDownloadMb() > download) {
    error(`초기 번들 ${initialDownloadMb()}MB — 예산 ${download}MB 초과`);
  }

  return findings;
}
