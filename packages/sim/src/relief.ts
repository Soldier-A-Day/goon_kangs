import { bestOfficer } from "./discipline.js";
import { phaseAt } from "./phases.js";
import type { Effect, RunState } from "./types.js";

/**
 * B-4 — 구제권을 자동 상쇄에서 "발동하는 행동"으로.
 *
 * 예전 judge.ts는 판정 시각에 `Math.min(shortfall, reliefsRemaining)`을 조용히
 * 깎았다 — 분대장도 간부도 아무 행동을 하지 않았는데 구제가 소모됐다. "썼는지도
 * 몰랐다" 신고의 뿌리는 표시 누락이 아니라 발동이라는 행위 자체가 없던 것이었다
 * (docs/WORKORDER.md B-4). 이 파일이 그 행위를 만든다.
 *
 * 총량 3장은 그대로다. 다만 이제 두 갈래로 나뉜다 —
 *   **분대장 몫(2장)** — 위기에 놓인 필수 퀘스트 1개를 판정 전에 직접 골라 필수→선택으로
 *   강등한다. 강등은 즉시·영구적이다. 판정 시각에 shortfall을 계산할 때는 이미
 *   선택 퀘스트가 되어 있으므로, judge.ts는 이 몫을 전혀 모른다.
 *   **간부 몫(1장)** — 신뢰도 최고 간부가 저녁 개인정비에만 발동할 수 있다. 발동은
 *   "오늘 미달이 생기면 상쇄해 달라"는 예약이라, 판정 시각(judge.ts judgeDay)이
 *   실제로 상쇄가 필요했는지를 확인하고 그때만 총량에서 뗀다 — 필요 없었으면
 *   (그날 필수를 다 채웠으면) 쓴 것으로 치지 않는다.
 */

/** 분대장 몫 — 런당 2회, 리셋 없음(10.0) */
export const LEADER_RELIEF_LIMIT = 2;

/** 간부 몫 — 런당 1회(10.0 · DISC-02) */
export const OFFICER_RELIEF_LIMIT = 1;

/**
 * 간부 구제 발동 문턱.
 *
 * "그날 신뢰도 최고 간부의 신뢰도가 문턱 이상"이라는 기획서 조건을 수치로 고정한다.
 * 최대치가 100이고 시작이 50이므로, 70은 하루이틀 대화로 닿는 값이 아니라 저녁
 * 시간을 반복해서 투자해야 닿는 값이다 — "시간을 쓸 가치가 있는지 판단하게
 * 만든다"(discipline.json DISC-02)는 취지를 지킨다.
 */
export const OFFICER_RELIEF_TRUST_THRESHOLD = 70;

/**
 * 구제 발동 거부 사유.
 *
 * 침묵 판정을 새로 만들지 않는다(E단계 원칙) — 거부에도 반드시 사유가 붙어
 * 이펙트로 흘러간다. 두 발동 경로가 사유 몇 개를 공유한다(`limitReached` 등).
 */
export type ReliefRefusal =
  /** 분대장이 아닌 사람이 우선순위 지정을 시도했다 */
  | "notLeader"
  /** 해당 몫(분대장 2 / 간부 1)이 이미 바닥났다 */
  | "limitReached"
  /** 퀘스트가 없거나, 필수가 아니거나, 이미 완료됐다 — 봐줄 것이 없다 */
  | "notEligibleQuest"
  /** 간부 구제는 저녁 개인정비 시간에만 발동할 수 있다 */
  | "notPersonalPhase"
  /** 오늘 이미 간부 구제를 발동했다 — 하루 1회다 */
  | "alreadyArmed"
  /** 그날 신뢰도 최고 간부의 신뢰도가 문턱에 못 미친다 */
  | "trustTooLow"
  /** 존재하지 않는 분대원 id */
  | "unknownMember";

export function canUseRelief(
  state: RunState,
  leaderId: string,
  questId: string,
): { ok: true } | { ok: false; reason: ReliefRefusal } {
  if (state.leaderId !== leaderId) return { ok: false, reason: "notLeader" };
  if (state.leaderReliefsRemaining <= 0) return { ok: false, reason: "limitReached" };

  const quest = state.quests.find((q) => q.id === questId);
  // 대상은 "지금 필수인데 아직 못 끝낸" 퀘스트뿐이다 — countShortfall(judge.ts)이
  // 세는 것과 정확히 같은 조건이다. 이미 done이면 봐줄 이유가 없고, required가
  // 아니면 애초에 위기가 아니다.
  if (!quest || !quest.required || quest.status === "done") {
    return { ok: false, reason: "notEligibleQuest" };
  }

  return { ok: true };
}

/**
 * 분대장 우선순위 지정 — 발동.
 *
 * 강등은 즉시 총량에서 빠진다. 예전 자동 상쇄처럼 "판정이 실패로 끝나면 쓰지
 * 않은 것으로 친다"는 유예가 없다 — 이건 더 이상 계산의 부산물이 아니라
 * 분대장이 그 순간 내리는 결단이기 때문이다.
 */
export function useRelief(
  state: RunState,
  leaderId: string,
  questId: string,
  effects: Effect[],
): boolean {
  const check = canUseRelief(state, leaderId, questId);
  if (!check.ok) {
    effects.push({ type: "reliefRefused", by: "leader", reason: check.reason, questId });
    return false;
  }

  const quest = state.quests.find((q) => q.id === questId);
  if (!quest) return false;

  quest.required = false;
  state.leaderReliefsRemaining -= 1;
  state.reliefsRemaining = state.leaderReliefsRemaining + state.officerReliefsRemaining;

  effects.push({
    type: "reliefGranted",
    by: "leader",
    questId,
    leaderReliefsRemaining: state.leaderReliefsRemaining,
    officerReliefsRemaining: state.officerReliefsRemaining,
  });
  return true;
}

export function canUseOfficerRelief(
  state: RunState,
  memberId: string,
): { ok: true } | { ok: false; reason: ReliefRefusal } {
  const member = state.members.find((m) => m.id === memberId);
  if (!member) return { ok: false, reason: "unknownMember" };
  if (phaseAt(state.phaseIndex).id !== "personal") {
    return { ok: false, reason: "notPersonalPhase" };
  }
  if (state.officerReliefArmedToday) return { ok: false, reason: "alreadyArmed" };
  if (state.officerReliefsRemaining <= 0) return { ok: false, reason: "limitReached" };

  const officer = bestOfficer(state);
  if (state.trust[officer] < OFFICER_RELIEF_TRUST_THRESHOLD) {
    return { ok: false, reason: "trustTooLow" };
  }

  return { ok: true };
}

/**
 * 간부 구제 — 발동.
 *
 * 발동은 "예약"이다. 총량은 여기서 빠지지 않는다 — judge.ts의 판정이 그날 실제로
 * 미달을 메웠는지 확인한 뒤에야(`applyJudgement`) 진짜로 뗀다. 그래서 이 함수가
 * 성공해도 `officerReliefsRemaining`은 바뀌지 않는다.
 */
export function useOfficerRelief(
  state: RunState,
  memberId: string,
  effects: Effect[],
): boolean {
  const check = canUseOfficerRelief(state, memberId);
  if (!check.ok) {
    effects.push({ type: "reliefRefused", by: "officer", reason: check.reason, questId: null });
    return false;
  }

  state.officerReliefArmedToday = true;

  effects.push({
    type: "reliefGranted",
    by: "officer",
    questId: null,
    leaderReliefsRemaining: state.leaderReliefsRemaining,
    officerReliefsRemaining: state.officerReliefsRemaining,
  });
  return true;
}
