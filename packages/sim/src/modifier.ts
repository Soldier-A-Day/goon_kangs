import { createRngState, pick } from "./rng.js";

/**
 * C-3 주간 변조 — "같은 부대인데 이번 주는 다르다."
 *
 * 런 시드에서 파생되는 4갈래 중 하나를 런 생성 시 확정한다(결정론 — 같은 시드는
 * 언제나 같은 주간). **계절 롤과 다른 스트림을 쓴다** — `run.ts`의 계절 롤은
 * 메인 RNG(`state.rngState`)의 앞부분을 그대로 소비하는데, 여기서 같은 스트림을
 * 또 소비하면 계절 확률이 밀리고 그 뒤에 이어지는 모든 롤(돌발·공통 일과 배정)도
 * 함께 밀린다. `weather.ts`가 일차마다 `seed`를 다시 섞어 독립 스트림을 만드는
 * 것과 같은 이유로, 여기서도 `seed`만 다른 상수로 한 번 더 섞어 완전히 별도의
 * 스트림에서 뽑는다.
 *
 * 강도는 완주율을 흔들지 않는 선(±10% 내 파라미터 보정)으로 잡았다 — 값마다
 * `tools/simrunner`로 변조 없음 대비 완주율 차이를 재서 확인했다(각 함수 주석 참고).
 */
export type WeeklyModifierId = "coldSnap" | "tightSupply" | "inspection" | "trainingPush";

export interface WeeklyModifier {
  readonly id: WeeklyModifierId;
  readonly name: string;
}

/**
 * 4종 원형. 물량이 아니라 조합의 곱에 투자한다는 발주 취지에 따라 축 1~2개(기온·
 * 보급·군기·훈련)만 건드리고, 각 변조는 그중 정확히 하나만 흔든다 — 두 축이
 * 동시에 나쁜 주간은 아직 없다.
 */
const WEEKLY_MODIFIERS: readonly WeeklyModifier[] = [
  { id: "coldSnap", name: "한파 주간" },
  { id: "tightSupply", name: "보급 빠듯한 주간" },
  { id: "inspection", name: "검열 주간" },
  { id: "trainingPush", name: "훈련 강화 주간" },
];

/** 계절 롤·일차별 날씨 스트림과 겹치지 않도록 시드를 가르는 전용 상수 */
const STREAM_SALT = 0x2545f491;

/** 시드에서 이번 런의 주간 변조를 뽑는다. 순수 함수 — `RunState`가 없어도 시드만 있으면 결정된다 */
export function rollWeeklyModifier(seed: number): WeeklyModifier {
  const rng = createRngState((seed ^ STREAM_SALT) | 0);
  const [chosen] = pick(rng, WEEKLY_MODIFIERS);
  return chosen;
}

export function weeklyModifierById(id: WeeklyModifierId): WeeklyModifier {
  const found = WEEKLY_MODIFIERS.find((m) => m.id === id);
  if (!found) throw new Error(`정의되지 않은 주간 변조: ${id}`);
  return found;
}

/* ------------------------------------------------------------- 파라미터 보정 */

/**
 * coldSnap — 그날 기온에 얹는 오프셋(°C).
 *
 * 표 5-1 밴드는 폭이 고르지 않다(온난은 24폭, 혹서는 5폭) — 경계 근처에 몰린
 * 날들을 한 칸 추운 밴드로 밀어내는 것이 "밴드 가중"의 실체다. 계절 기준값
 * (한랭 −10 · 혹서 +30)의 10%인 −1.5도를 계절과 무관하게 얹는다 — 더운 계절
 * 주간이 걸려도 "이번 주는 덜 덥다"로 같은 방향(더 추운 쪽)이 성립해야 한다.
 */
export const COLD_SNAP_TEMP_OFFSET = -1.5;

/** tightSupply — 보급 포인트 획득량 배율. −10% */
export const TIGHT_SUPPLY_MULTIPLIER = 0.9;

/** inspection — 돌발 확률 배율. 12.0 구간표 값(기본 18%)에 곱해진다. +10% */
export const INSPECTION_SURPRISE_MULTIPLIER = 1.1;

/** trainingPush — 훈련 체크포인트 소요시간 배율. +10% */
export const TRAINING_PUSH_WORK_MULTIPLIER = 1.1;

export function weatherTempOffset(id: WeeklyModifierId): number {
  return id === "coldSnap" ? COLD_SNAP_TEMP_OFFSET : 0;
}

export function supplyPointsMultiplier(id: WeeklyModifierId): number {
  return id === "tightSupply" ? TIGHT_SUPPLY_MULTIPLIER : 1;
}

export function surpriseChanceMultiplier(id: WeeklyModifierId): number {
  return id === "inspection" ? INSPECTION_SURPRISE_MULTIPLIER : 1;
}

export function trainingWorkMultiplier(id: WeeklyModifierId): number {
  return id === "trainingPush" ? TRAINING_PUSH_WORK_MULTIPLIER : 1;
}
