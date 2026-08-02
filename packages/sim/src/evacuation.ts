import { THRESHOLDS } from "./condition.js";
import { penalizeIncident } from "./discipline.js";
import { penalizeEvacuation } from "./ranks.js";
import { LEADER_RELIEF_LIMIT } from "./relief.js";
import type { Effect, Member, RunState } from "./types.js";

/**
 * 대리가 인수할 수 있는 잔여 필수 상한.
 *
 * 대리는 구제 장치이지 면제 장치가 아니다 — 오전에 쓰러지면(잔여 3~4건) 분대가 죽고,
 * 개인정비 시간대에 쓰러지면(잔여 1~2건) 구제된다. "일부러 죽기"가 이를수록 이득이어야 하는데
 * 그쪽이 정확히 막힌다.
 */
export const PROXY_ABSORB_LIMIT = 2;

/** 복귀 후 재활 기간 — 이 동안 본인 필수가 +1 된다 */
export const REHAB_DAYS = 2;

/**
 * 후송자 본인의 간부 신뢰도 손실.
 *
 * 기획서는 "간부 신뢰도 절반"을 개인 손실로 적었지만 이 모델의 신뢰도는 분대 단위 트랙이다.
 * 분대 트랙을 절반으로 깎으면 다른 세 명까지 사실상 런이 끝나므로, 개인 비용은
 * 계급 리셋·복무 점수 0·재활 필수 +1로 물리고 분대 트랙에는 −5만 반영한다.
 */
export const EVAC_TRUST_COST = -5;

/** 대리가 수행하지 않는 퀘스트 종류 — 생존은 시켜주지만 성장은 시켜주지 않는다 */
export function proxyHandles(kind: string): boolean {
  return kind === "role" || kind === "chore";
}

/**
 * JDG-03 후송. 컨디션이 무너진 시점에 즉시 돈다 — 점호까지 기다리지 않는다.
 */
export function evacuate(state: RunState, memberId: string, effects: Effect[]): void {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.presence !== "player") return;

  const remaining = state.quests.filter(
    (q) =>
      q.required &&
      q.ownerId === member.id &&
      (q.status === "pending" || q.status === "active"),
  );

  const absorbed = remaining.length <= PROXY_ABSORB_LIMIT;
  if (absorbed) {
    for (const quest of remaining) {
      if (!proxyHandles(quest.kind)) continue;
      quest.workedMs = quest.workMs;
      quest.status = "done";
    }
  }

  member.presence = "evacuated";
  member.collapseTimerMs = 0;
  member.evacuations += 1;

  // 계급과 무관한 비용이므로 잃을 게 없는 이병이 쓰러져도 분대는 아프다
  penalizeIncident(state);
  // 개인 비용은 복무 점수로 문다 — 계급이 높을수록 크게 (표 13-1)
  penalizeEvacuation(member);

  effects.push({ type: "memberEvacuated", memberId: member.id, absorbed });
}

/**
 * 2.0 중도 이탈. 후송과 같은 파이프라인이지만 인수 한도가 적용되지 않는다 —
 * 이탈은 게임을 못 하게 되는 것 자체가 최대 비용이므로 판정 회피에 악용될 수 없고,
 * 남은 팀의 세션을 지키는 쪽이 우선이다.
 */
export function leaveRun(state: RunState, memberId: string, effects: Effect[]): void {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.presence !== "player") return;

  member.presence = "npcLeave";
  absorbAllRequired(state, member);
  effects.push({ type: "memberLeft", memberId: member.id });
}

/** 재접속 — 대리에서 즉시 복귀한다. 축적은 그대로다(후송과 다르다) */
export function rejoinRun(state: RunState, memberId: string, effects: Effect[]): void {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.presence !== "npcLeave") return;
  member.presence = "player";
  effects.push({ type: "memberReturned", memberId: member.id, asRecruit: false });
}

/** 이탈 대리는 한도 없이 필수를 계속 완수한다 */
export function absorbAllRequired(state: RunState, member: Member): void {
  for (const quest of state.quests) {
    if (!quest.required || quest.ownerId !== member.id) continue;
    if (quest.status !== "pending" && quest.status !== "active") continue;
    if (!proxyHandles(quest.kind)) continue;
    quest.workedMs = quest.workMs;
    quest.status = "done";
  }
}

/**
 * 하루의 시작에 후송자를 복귀시킨다 — 관전도 재투입도 아닌 제3안이다.
 * 몸은 돌려주고 기록은 지운다.
 */
export function returnEvacuees(state: RunState, effects: Effect[]): void {
  for (const member of state.members) {
    if (member.presence !== "evacuated") continue;

    member.presence = "player";
    member.rank = "private";
    member.serviceScore = 0;
    member.rehabDaysLeft = REHAB_DAYS;
    member.stats.stamina = 60;
    member.stats.hydration = 60;
    member.stats.fatigue = 20;

    state.trust.platoonLeader = Math.max(0, state.trust.platoonLeader + EVAC_TRUST_COST);
    state.trust.assistant = Math.max(0, state.trust.assistant + EVAC_TRUST_COST);
    state.trust.sergeantMajor = Math.max(0, state.trust.sergeantMajor + EVAC_TRUST_COST);

    effects.push({ type: "memberReturned", memberId: member.id, asRecruit: true });
  }
}

/** 재활 기간 소진 */
export function tickRehab(state: RunState): void {
  for (const member of state.members) {
    if (member.rehabDaysLeft > 0) member.rehabDaysLeft -= 1;
  }
}

/**
 * 컨디션 붕괴 감지. 시간대 정산 직후에 돈다.
 *
 * 체력이 0이면 즉시, 수분 2단계(열사병)는 60초를 버티면 쓰러진다.
 * 진짜 사고는 대개 하루 후반에 터지므로 인수 한도(2건)에 걸리지 않고 구제된다.
 */
export function checkCollapses(
  state: RunState,
  elapsedMsInPhase: number,
  effects: Effect[],
): void {
  for (const member of state.members) {
    if (member.presence !== "player") continue;
    const stats = member.stats;

    if (stats.stamina <= 0) {
      evacuate(state, member.id, effects);
      continue;
    }

    if (stats.hydration <= THRESHOLDS.dehydrationStage2) {
      member.collapseTimerMs += elapsedMsInPhase;
      if (member.collapseTimerMs >= THRESHOLDS.collapseAfterSeconds * 1000) {
        evacuate(state, member.id, effects);
      }
      continue;
    }

    member.collapseTimerMs = 0;
  }
}

/**
 * 피로 100 — 강제 수면. 후송은 아니지만 그 칸의 퀘스트를 포기하게 된다 (표 7-1).
 */
export function applyForcedSleep(state: RunState, phaseId: string, effects: Effect[]): void {
  for (const member of state.members) {
    if (member.presence !== "player") continue;
    if (member.stats.fatigue < THRESHOLDS.forcedSleepFatigue) continue;

    for (const quest of state.quests) {
      if (quest.ownerId !== member.id || quest.phase !== phaseId) continue;
      if (quest.status !== "pending" && quest.status !== "active") continue;
      quest.status = "locked";
    }
    effects.push({ type: "forcedSleep", memberId: member.id });
  }
}

/** 대리 수 — 군기 상한과 런 종료 판정에 쓰인다 */
export function proxyCount(state: RunState): number {
  return state.members.filter(
    (m) =>
      m.presence === "npcEvac" ||
      m.presence === "npcLeave" ||
      m.presence === "evacuated",
  ).length;
}

/**
 * 모범 전역 조건 — 후송이 한 번이라도 있으면 깨진다.
 *
 * B-4 — `judgement.reliefsUsed`는 이제 간부 몫만 센다. 분대장 몫(우선순위 지정)은
 * 판정 전에 이미 소모돼 판정 기록에 안 남으므로, `leaderReliefsRemaining`이
 * 시작값 그대로인지도 같이 봐야 "구제를 한 번도 안 썼다"는 뜻이 된다
 * (`hidden.ts` resolveEnding과 같은 조건).
 */
export function isFlawless(state: RunState): boolean {
  return (
    state.members.every((m) => m.evacuations === 0) &&
    state.leaderReliefsRemaining === LEADER_RELIEF_LIMIT &&
    state.judgements.every((j) => j.passed && j.reliefsUsed === 0)
  );
}
