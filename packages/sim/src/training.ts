import trainingTable from "../data/training.json";
import type { TempBand, Zone } from "./types.js";

/**
 * 훈련장까지 걸어가는 **편도** 시간(초). 시간대 길이에 더해진다
 * (`phases.ts` `phaseDurationMsFor`). 근거·측정법은 `data/training.json` 주석에.
 */
const TRAVEL_SECONDS = trainingTable.travelSeconds as Record<string, number>;

/** 그 훈련장까지의 편도 도보 시간(초). 모르는 구역이면 0 — 시간을 더 주지 않는다 */
export function travelSecondsToZone(zone: string): number {
  return TRAVEL_SECONDS[zone] ?? 0;
}

/**
 * 9.0 훈련 — 어디서 하는가.
 *
 * 커리큘럼(`curriculum.json`)은 그날의 훈련 **종류**만 정한다. 그 종류가 어느
 * 장소에서 벌어지는지는 여기가 정하고, 아트(§6.4 TR01~TR10)가 그 장소를 그린다.
 *
 * **훈련장은 부대 밖이다.** 정문 위병소(Z18)를 지나야 나갈 수 있고, 그래서
 * 훈련일은 아침에 부대를 비운다 — 그 사이 부대 안 공통 일과가 쌓이는 것이
 * 훈련일이 빡빡한 이유다(§6.1 동선 비용).
 *
 * 이름은 표시가 아니라 **판정에 쓰인다.** 사격장에 서 있지 않으면 사격 훈련
 * 체크포인트가 안 잡힌다 — 방 하나가 곧 구역이라는 규칙이 훈련장에도 그대로 산다.
 */

export type Training =
  | "marksmanship"
  | "cbrn"
  | "march"
  | "bivouac"
  | "seasonal"
  | "commando"
  | "externalSupport"
  | "combinedTactics";

export interface TrainingPlace {
  readonly zone: Zone;
  readonly name: string;
  /** §9.0 재설계 — 사이드뷰 횡스크롤 코스인가 (행군 · 유격) */
  readonly sideView: boolean;
}

const PLACES: Record<string, TrainingPlace> = {
  marksmanship: { zone: "TR01", name: "사격장", sideView: false },
  cbrn: { zone: "TR02", name: "화생방 제독소", sideView: false },
  march: { zone: "TR03", name: "행군로", sideView: true },
  bivouac: { zone: "TR04", name: "숙영지", sideView: false },
  externalSupport: { zone: "TR09", name: "대민지원", sideView: false },
  combinedTactics: { zone: "TR10", name: "합동 전술훈련장", sideView: false },
};

/**
 * 그날의 훈련장.
 *
 * 갈래가 있는 둘은 여기서 갈린다.
 *
 *   **seasonal** (D-15) — 14.0이 "혹한기 훈련 **또는** 혹서기 대비 훈련"이라
 *     적었다. 그날 기온이 어느 쪽인지가 훈련을 정하므로 밴드를 본다
 *   **commando** (D-12·13) — 기초와 종합이 다른 코스다. 이틀에 걸쳐 같은
 *     장소를 쓰면 D-13의 "고난도"가 화면에서 드러나지 않는다
 */
export function trainingPlace(
  training: string | null,
  day: number,
  band: TempBand,
): TrainingPlace | null {
  if (training === null) return null;

  if (training === "seasonal") {
    const cold = band === "extremeCold" || band === "cold";
    return cold
      ? { zone: "TR05", name: "혹한기 훈련장", sideView: false }
      : { zone: "TR06", name: "혹서기 급수 라인", sideView: false };
  }

  if (training === "commando") {
    return day <= 12
      ? { zone: "TR07", name: "유격장 (기초)", sideView: true }
      : { zone: "TR08", name: "유격장 (종합)", sideView: true };
  }

  return PLACES[training] ?? null;
}

/** 훈련 종류의 한글 이름. 퀘스트 라벨이 쓴다 */
export function trainingName(training: string | null): string {
  const table: Record<string, string> = {
    marksmanship: "사격",
    cbrn: "화생방",
    march: "행군",
    bivouac: "숙영",
    seasonal: "계절 훈련",
    commando: "유격",
    externalSupport: "대민지원",
    combinedTactics: "합동 전술",
  };
  return training === null ? "훈련" : (table[training] ?? training);
}
