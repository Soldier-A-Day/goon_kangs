import { adjustDiscipline } from "./discipline.js";
import { UNLOCK, unlocked } from "./curriculum.js";
import { award, penalize } from "./ranks.js";
import { phaseAt } from "./phases.js";
import { RANK_ORDER, type Effect, type Member, type Quest, type RunState } from "./types.js";

/** 받는 쪽 보유 상한. 4인 편성에서는 하달 대상이 최대 3명뿐이라 이 상한이 결정적이다. */
export const RECEIVE_LIMIT = 2;

/** 하달자 군기 비용 — 남용하면 스스로 조건 C에 접근한다 */
export const DELEGATOR_DISCIPLINE_COST = -2;

/** 대행 점수. 점수가 수행자에게 가므로 짬때리기가 계급 역전의 사다리가 된다 */
export const PROXY_SERVICE_SCORE = 2;

/** 거부당한 하달자의 복무 점수 손실 */
export const VETO_DELEGATOR_COST = -3;

/** 거부한 사람의 간부 신뢰도 손실 — 거부는 가능하지만 공짜가 아니다 */
export const VETO_TRUST_COST = -5;

/** 같은 사람에게 연속으로 몰면 정신력 회복이 깎인다 */
export const STRESS_STREAK_DAYS = 3;
export const STRESS_RECOVERY_PENALTY = 0.3;

export interface DelegationRecord {
  readonly day: number;
  readonly phaseIndex: number;
  readonly fromId: string;
  readonly toId: string;
  readonly questId: string;
  readonly outcome: "accepted" | "vetoed" | "reassigned";
}

export type DelegationRefusal =
  /** 표 14-1이 정한 해금 전 — 하달은 D-3부터다 */
  | "locked"
  | "notDelegationWindow"
  | "notChore"
  | "notOwner"
  | "rankTooLow"
  | "giverLimit"
  | "receiverLimit"
  | "alreadyDelegated"
  | "unknownMember";

export function rankIndex(member: Member): number {
  return RANK_ORDER.indexOf(member.rank);
}

/**
 * 계급차 1단계 = 1건, 2단계 이상 = 2건. 이병은 최하위이므로 하달할 수 없다.
 * 전원이 이병인 D-01~03 구간에서는 시스템 자체가 잠겨 있는 셈이다.
 */
export function delegationAllowance(from: Member, to: Member): number {
  const gap = rankIndex(from) - rankIndex(to);
  if (gap <= 0) return 0;
  return gap >= 2 ? 2 : 1;
}

/** 하달 창이 열려 있는가 — 오전·오후 일과 진입 직후 20초 */
export function isDelegationWindow(state: RunState): boolean {
  return state.delegationWindowMsLeft > 0;
}

/**
 * 하달할 수 있는 사람이 하나라도 있는가.
 *
 * 6.2 — "**전원이 이병인 D-01~03 구간에는 시스템이 잠겨 있고**, D-03 1차 승급
 * 심사가 해금 트리거다." 하달은 계급이 1단계 이상 높은 사람만 할 수 있으므로,
 * 계급이 다 같으면 창을 열어도 아무 일도 일어나지 않는다.
 *
 * 그런데도 창이 열리면 시간대 타이머를 20초 세워두고 아무것도 못 하게 만든다 —
 * 하루에 두 번, 아무 선택지도 없는 화면이 40초를 먹는다.
 */
export function anyoneCanDelegate(state: RunState): boolean {
  let lowest = Number.MAX_SAFE_INTEGER;
  let highest = -1;

  for (const member of state.members) {
    if (member.presence === "evacuated") continue;
    const rank = rankIndex(member);
    if (rank < lowest) lowest = rank;
    if (rank > highest) highest = rank;
  }

  return highest - lowest >= 1;
}

export function openDelegationWindow(state: RunState): void {
  // 하달할 수 있는 사람이 없으면 창을 열지 않는다 — 아무 선택지 없는 창에
  // 시간대 타이머를 멈춰 세울 이유가 없다
  if (!anyoneCanDelegate(state)) {
    state.delegationWindowMsLeft = 0;
    state.leaderOverridePhase = -1;
    return;
  }

  const phase = phaseAt(state.phaseIndex);
  state.delegationWindowMsLeft = (phase.delegationWindowSeconds ?? 0) * 1000;
  state.leaderOverridePhase = -1;
  for (const member of state.members) {
    member.delegatedThisWindow = 0;
  }
}

export function canDelegate(
  state: RunState,
  fromId: string,
  toId: string,
  questId: string,
): { ok: true } | { ok: false; reason: DelegationRefusal } {
  // 하달은 D-3부터 열린다 (표 14-1). 계급으로도 막히지만(`rankTooLow`) 그건
  // 우연히 시기가 맞는 것이라, 규칙은 커리큘럼이 소유한다
  if (!unlocked(state.day, UNLOCK.delegation)) return { ok: false, reason: "locked" };
  if (!isDelegationWindow(state)) return { ok: false, reason: "notDelegationWindow" };

  const from = find(state, fromId);
  const to = find(state, toId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!from || !to || !quest) return { ok: false, reason: "unknownMember" };

  // 공통 일과만 넘길 수 있다. 보직 전용은 넘겨도 수행이 불가능하고(행정병만 난로 밸브를 연다),
  // 합동·돌발은 애초에 개인 소유가 아니다.
  if (quest.kind !== "chore") return { ok: false, reason: "notChore" };
  if (quest.ownerId !== fromId) return { ok: false, reason: "notOwner" };

  // 재하달 금지 — 연쇄 폭탄을 원천 차단한다
  if (quest.delegatedFrom !== null) return { ok: false, reason: "alreadyDelegated" };

  const allowance = delegationAllowance(from, to);
  if (allowance === 0) return { ok: false, reason: "rankTooLow" };
  if (from.delegatedThisWindow >= allowance) return { ok: false, reason: "giverLimit" };
  if (to.choresReceived >= RECEIVE_LIMIT) return { ok: false, reason: "receiverLimit" };

  return { ok: true };
}

/**
 * QST-04 하달.
 *
 * 비용 구조가 이 시스템의 전부다. 순수 이득이면 상급자는 항상 쓰고 하급자는 고정 계층이 된다.
 * 그래서 시간을 사는 대가로 승급 점수를 판다 — 복무 점수는 수행자에게 간다.
 */
export function delegateChore(
  state: RunState,
  fromId: string,
  toId: string,
  questId: string,
  effects: Effect[],
): boolean {
  const check = canDelegate(state, fromId, toId, questId);
  if (!check.ok) {
    effects.push({ type: "delegationRefused", reason: check.reason, questId });
    return false;
  }

  const from = find(state, fromId);
  const to = find(state, toId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!from || !to || !quest) return false;

  quest.ownerId = to.id;
  quest.delegatedFrom = from.id;
  from.delegatedThisWindow += 1;
  to.choresReceived += 1;

  adjustDiscipline(state, DELEGATOR_DISCIPLINE_COST);
  state.ledger.push({
    day: state.day,
    phaseIndex: state.phaseIndex,
    fromId: from.id,
    toId: to.id,
    questId: quest.id,
    outcome: "accepted",
  });

  effects.push({ type: "choreDelegated", fromId: from.id, toId: to.id, questId: quest.id });
  return true;
}

/**
 * QST-05 거부권. 1일 1회뿐이므로 언제 쓸지가 판단이 된다.
 */
export function vetoChore(
  state: RunState,
  memberId: string,
  questId: string,
  effects: Effect[],
): boolean {
  const member = find(state, memberId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!member || !quest) return false;
  if (member.vetoUsedToday) return false;
  if (quest.ownerId !== memberId || quest.delegatedFrom === null) return false;

  const delegator = find(state, quest.delegatedFrom);
  quest.ownerId = quest.delegatedFrom;
  quest.delegatedFrom = null;
  member.vetoUsedToday = true;
  member.choresReceived = Math.max(0, member.choresReceived - 1);

  if (delegator) penalize(delegator, VETO_DELEGATOR_COST);
  // 거부한 사람은 간부 신뢰도를 잃는다 — 공짜 카드가 아니다
  state.trust.assistant = Math.max(0, state.trust.assistant + VETO_TRUST_COST);

  state.ledger.push({
    day: state.day,
    phaseIndex: state.phaseIndex,
    fromId: quest.ownerId ?? "",
    toId: memberId,
    questId: quest.id,
    outcome: "vetoed",
  });

  effects.push({ type: "choreVetoed", memberId, questId });
  return true;
}

/**
 * 분대장 개입. 계급과 무관하게 배정을 1회/시간대 강제 변경한다 —
 * 이병 분대장이 병장의 하달을 되돌리는 장면이 여기서 성립한다 (RANK-02).
 */
export function leaderReassign(
  state: RunState,
  leaderId: string,
  questId: string,
  toId: string,
  effects: Effect[],
): boolean {
  if (state.leaderId !== leaderId) return false;
  if (state.leaderOverridePhase === state.phaseIndex) return false;

  const quest = state.quests.find((q) => q.id === questId);
  const to = find(state, toId);
  if (!quest || !to || quest.kind !== "chore") return false;
  if (to.choresReceived >= RECEIVE_LIMIT) return false;

  const previous = quest.ownerId ? find(state, quest.ownerId) : undefined;
  if (previous && quest.delegatedFrom !== null) {
    previous.choresReceived = Math.max(0, previous.choresReceived - 1);
  }

  quest.ownerId = to.id;
  quest.delegatedFrom = previous?.id ?? null;
  to.choresReceived += 1;
  state.leaderOverridePhase = state.phaseIndex;

  state.ledger.push({
    day: state.day,
    phaseIndex: state.phaseIndex,
    fromId: leaderId,
    toId,
    questId,
    outcome: "reassigned",
  });

  effects.push({ type: "choreReassigned", leaderId, toId, questId });
  return true;
}

/**
 * 남용 억제 — 같은 사람에게 3일 연속으로 몰면 그 대상의 정신력 회복이 −30%가 된다.
 * 분대 전체 정신력이 떨어지면 조작 입력이 지연되므로(7.0) 결국 하달자에게 되돌아온다.
 */
export function consecutiveDelegationDays(state: RunState, toId: string): number {
  let streak = 0;
  for (let day = state.day; day >= 1; day -= 1) {
    const received = state.ledger.some(
      (r) => r.day === day && r.toId === toId && r.outcome === "accepted",
    );
    if (!received) break;
    streak += 1;
  }
  return streak;
}

export function mentalRecoveryMultiplier(state: RunState, member: Member): number {
  return consecutiveDelegationDays(state, member.id) >= STRESS_STREAK_DAYS
    ? 1 - STRESS_RECOVERY_PENALTY
    : 1;
}

/** 런 종료 시 공개되는 하달 장부 (QST-05) */
export function delegationLedgerSummary(
  state: RunState,
): { readonly memberId: string; readonly given: number; readonly received: number }[] {
  return state.members.map((member) => ({
    memberId: member.id,
    given: state.ledger.filter((r) => r.fromId === member.id && r.outcome === "accepted").length,
    received: state.ledger.filter((r) => r.toId === member.id && r.outcome === "accepted").length,
  }));
}

/** 하달받은 공통 일과를 완수하면 대행 점수가 수행자에게 간다 */
export function awardProxyScore(state: RunState, quest: Quest): void {
  if (quest.kind !== "chore" || quest.delegatedFrom === null) return;
  const performer = quest.ownerId ? find(state, quest.ownerId) : undefined;
  if (performer) award(performer, PROXY_SERVICE_SCORE);
}

function find(state: RunState, id: string): Member | undefined {
  return state.members.find((m) => m.id === id);
}
