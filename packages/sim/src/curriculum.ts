import curriculumTable from "../data/curriculum.json";

/** 5.0 기온 롤이 어느 범위에서 이뤄지는지 — 14.0의 기후 열 */
export type ClimateRule =
  /** 평시 고정 — 튜토리얼·정비일·심사일 */
  | "fixedNormal"
  /** 일반 롤 */
  | "roll"
  /** 기후 이벤트 확정 (D-10) */
  | "climateEvent"
  /** 평시·온난 한정 (유격) */
  | "normalOrWarm"
  /** 밴드 분기 확정 (D-15) */
  | "bandBranch";

export interface DayPlan {
  readonly day: number;
  readonly climate: ClimateRule;
  readonly focus: string;
  readonly training: string | null;
  readonly joint: string | null;
  /** 합동 퀘스트 요구 인원. 0이면 그날 합동이 없다(정비일 D-06) */
  readonly jointActors: number;
  readonly required: {
    /** 1인당 하루 필수 건수 */
    readonly total: number;
    /** 훈련 구간 체크포인트 — 각각 필수 1건으로 센다 (TRN-02) */
    readonly trainingCheckpoints: number;
    readonly roleQuests: number;
  };
  readonly unlocks: readonly string[];
  readonly maintenanceDay?: boolean;
}

export const CURRICULUM = curriculumTable.days as readonly DayPlan[];

/** 런당 1인 필수 총량. 10.0 완주율 계산의 기준값이다. */
export const TOTAL_REQUIRED_PER_MEMBER = CURRICULUM.reduce(
  (sum, day) => sum + day.required.total,
  0,
);

/**
 * 그 일차에 이 기능이 열려 있는가 (표 14-1 `unlocks`).
 *
 * **누적이다.** D-2에 열린 공통 일과는 D-18까지 열려 있다.
 *
 * 예전에는 이 표가 문서였고 아무도 읽지 않았다 — 커리큘럼은 "공통 일과는
 * D-2부터"라고 적어놨는데 배정기는 D-1부터 냈다. 표에 적힌 난이도 곡선이
 * 실제로는 존재하지 않았다는 뜻이고, 첫날이 3일차와 같은 밀도로 시작했다.
 *
 * **모든 항목이 게이트는 아니다.** `보직 퀘스트`(D-2)처럼 D-1의 필수와
 * 모순되는 것이 있다 — D-1은 보직 2건을 필수로 요구한다. 그런 항목은 화면이
 * 그 기능을 처음 소개하는 시점이지 존재 여부가 아니다. 규칙으로 쓰는 것만
 * 여기로 물어본다.
 */
export function unlocked(day: number, key: string): boolean {
  for (let i = 0; i < day && i < CURRICULUM.length; i += 1) {
    if (CURRICULUM[i]?.unlocks.includes(key)) return true;
  }
  return false;
}

/** 해금 열쇠 — 문자열을 여기저기 흩어 쓰면 오타 하나가 조용히 게이트를 연다 */
export const UNLOCK = {
  chores: "공통 일과",
  surprise: "돌발 퀘스트",
  delegation: "하달",
} as const;

export function planFor(day: number): DayPlan {
  const plan = CURRICULUM[day - 1];
  if (!plan) {
    throw new Error(`커리큘럼에 없는 일차: ${day}`);
  }
  return plan;
}

/**
 * 그날 개인 일과를 돌릴 수 있는 여유 인원 (14.0 "여유").
 *
 * 4인에서 가장 빡빡한 자원은 인원이다. 4인 전원이 합동에 묶이면 여유가 0이 되는데,
 * 그 모순은 훈련 체크포인트가 필수를 대신 채우는 것으로 해소된다 — 훈련을 수행하는 것이
 * 곧 필수를 처리하는 것이다.
 */
export function slackFor(day: number, squadSize = 4): number {
  return Math.max(0, squadSize - planFor(day).jointActors);
}
