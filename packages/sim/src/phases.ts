import phaseTable from "../data/phases.json";
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
 * 시간대 길이 = 기본값 + 이월분.
 *
 * 스킵 투표로 아낀 시간은 사라지지 않고 다음 칸의 여유가 된다(TIME-01) —
 * 빨리 끝내는 것이 손해가 되면 아무도 서두르지 않는다.
 */
export function phaseDurationMsFor(state: RunState, index: number): number {
  return phaseAt(index).baseSeconds * 1000 + state.carryoverMs;
}
