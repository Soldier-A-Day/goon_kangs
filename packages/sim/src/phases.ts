import phaseTable from "../data/phases.json";
import { disciplineBand } from "./discipline.js";
import { travelSecondsToZone } from "./training.js";
import type { PhaseId, RunState } from "./types.js";

export interface PhaseDef {
  readonly id: PhaseId;
  readonly clock: string;
  readonly label: string;
  readonly baseSeconds: number;
  /** 6.2 하달 창 — 이 동안 시간대 타이머는 정지한다 */
  readonly delegationWindowSeconds?: number;
}

export const PHASES = phaseTable.phases as readonly PhaseDef[];

export const PHASE_COUNT = PHASES.length;

export function phaseAt(index: number): PhaseDef {
  const phase = PHASES[index];
  if (!phase) {
    throw new Error(`시간대 인덱스 범위 초과: ${index}`);
  }
  return phase;
}

export function currentPhase(state: RunState): PhaseDef {
  return phaseAt(state.phaseIndex);
}

/**
 * 시간대 길이 = 기본값 + 훈련장 이동 시간 + 이월분.
 *
 * 스킵 투표로 아낀 시간은 사라지지 않고 다음 칸의 여유가 된다(TIME-01) —
 * 빨리 끝내는 것이 손해가 되면 아무도 서두르지 않는다.
 *
 * **`startPhase`에서만 불린다** — 매 틱이 아니다. 그래서 아래에서 퀘스트
 * 배열을 훑어도 헤드리스 배치 시뮬이 느려지지 않는다(같은 이유로 매 틱
 * 순회를 피한 선례: `refreshRadio`).
 */
export function phaseDurationMsFor(state: RunState, index: number): number {
  const phase = phaseAt(index);
  let seconds = phase.baseSeconds;

  // 12.0 우수분대(80+)는 개인정비 시간을 20초 더 받는다
  if (phase.id === "personal") {
    seconds += disciplineBand(state.discipline).personalTimeBonusSeconds ?? 0;
  }

  seconds += trainingTravelSeconds(state, phase.id);

  return seconds * 1000 + state.carryoverMs;
}

/**
 * 그 시간대에 훈련장 일과가 있으면, 거기까지 걸어가는 **편도** 시간을 더한다.
 *
 * ── 왜 필요한가 ────────────────────────────────────────────────────────
 * 훈련장은 전부 부대 밖이고(§9.0 "정문 위병소를 지나야 나갈 수 있다"),
 * 시간대는 60초 고정이었다. 실측하면 생활관에서 사격장까지 **왕복 61초**,
 * 혹서기 급수 라인은 **왕복 71초**다 — 걷기만 해도 시간대가 끝난다.
 * 훈련 체크포인트는 필수(TRN-02)이므로 이건 "빡빡한 난이도"가 아니라
 * **필수를 물리적으로 수행할 수 없는 상태**였다. 사용자가 플레이 중 신고했다.
 *
 * 편도만 주는 이유는 체크포인트가 오전(2건)·오후(나머지)로 갈려 있어서다 —
 * 오전에 나가고 오후에 돌아오므로 두 시간대가 편도 하나씩을 쓴다.
 * 구보(1.4배)로 가면 그 차이가 통째로 여유가 된다.
 *
 * 목적지는 퀘스트가 이미 들고 있는 `zone`에서 읽는다. `trainingPlace()`를
 * 다시 부르지 않는 이유: 그 함수는 그날 기온 밴드로 계절 훈련의 갈래를
 * 정하는데, 퀘스트를 만들 때 이미 그 판정을 거쳐 `zone`이 박혀 있다.
 * 여기서 다시 재면 그 사이 밴드가 바뀌었을 때 **일과가 가리키는 곳과
 * 시간을 준 곳이 달라진다.**
 */
function trainingTravelSeconds(state: RunState, phaseId: PhaseId): number {
  let longest = 0;
  for (const quest of state.quests) {
    if (quest.training === null || quest.phase !== phaseId) continue;
    // 한 시간대에 훈련장이 둘일 일은 없지만, 있다면 먼 쪽을 기준으로 준다 —
    // 가까운 쪽에 맞추면 먼 쪽이 다시 도달 불가가 된다
    const seconds = travelSecondsToZone(quest.zone);
    if (seconds > longest) longest = seconds;
  }
  return longest;
}
