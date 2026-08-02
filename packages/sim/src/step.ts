import { applyPhaseCondition, applySleep } from "./condition.js";
import { refreshRadio } from "./radio.js";
import { clearWarmth, tickWarmth, travelMultiplier, workMultiplier } from "./warmth.js";
import {
  awardProxyScore,
  delegateChore,
  leaderReassign,
  markDelegationDone,
  openDelegationWindow,
  vetoChore,
} from "./delegation.js";
import { applyDailyDiscipline } from "./discipline.js";
import { applyDailyServiceScore, runReview } from "./ranks.js";
import { checkHiddenQuests } from "./hidden.js";
import { planFor } from "./curriculum.js";
import { accrueSupplyPoints, fileClaim, runSupplyDay } from "./supply.js";
import {
  applyForcedSleep,
  checkCollapses,
  leaveRun,
  rejoinRun,
  returnEvacuees,
  tickRehab,
} from "./evacuation.js";
import { applyJudgement, checkDisband } from "./judge.js";
import { PHASE_COUNT, phaseAt, phaseDurationMsFor } from "./phases.js";
import { careRecovery, generateDayQuests, rollSurprise } from "./quests.js";
import { useOfficerRelief, useRelief } from "./relief.js";
import { weatherFor } from "./weather.js";
import { isAdjacent, travelMs } from "./zones.js";
import type {
  Effect,
  Grade,
  Member,
  PhaseId,
  Quest,
  RunState,
  SimEvent,
  StepResult,
  Zone,
} from "./types.js";

/**
 * 규칙 엔진의 유일한 진입점.
 *
 * sim은 시계를 갖지 않는다 — 시간은 서버가 `tick` 이벤트로 주입한다(17.0 서버 권위).
 * 그래야 헤드리스 시뮬레이터가 18일을 수 밀리초에 돌릴 수 있고, 재접속·스냅샷 복구에서
 * 시간이 어긋나지 않는다.
 */
export function step(state: RunState, event: SimEvent): StepResult {
  const next = cloneState(state);
  const effects: Effect[] = [];

  if (next.status !== "running") {
    return { state: next, effects };
  }

  switch (event.type) {
    case "beginDay":
      beginDay(next, effects);
      break;
    case "tick":
      applyTick(next, event.elapsedMs, effects);
      break;
    case "skipPhase":
      skipPhase(next, effects);
      break;
    case "move":
      startMove(next, event.memberId, event.to, event.onFoot === true);
      break;
    case "work":
      applyWork(next, event.memberId, event.questId, event.deltaMs);
      break;
    case "jointStep":
      addJointStep(next, event.memberId, event.questId);
      break;
    case "questCleared":
      clearQuest(next, event.memberId, event.questId, event.grade);
      break;
    case "delegateChore":
      delegateChore(next, event.fromId, event.toId, event.questId, effects);
      break;
    case "vetoChore":
      vetoChore(next, event.memberId, event.questId, effects);
      break;
    case "delegationDone":
      markDelegationDone(next, event.memberId, effects);
      break;
    case "leaderReassign":
      leaderReassign(next, event.leaderId, event.questId, event.toId, effects);
      break;
    case "leaveRun":
      leaveRun(next, event.memberId, effects);
      checkDisband(next, effects);
      break;
    case "rejoinRun":
      rejoinRun(next, event.memberId, effects);
      break;
    case "fileClaim":
      fileClaim(next, event.memberId, event.items);
      break;
    case "useRelief":
      useRelief(next, event.leaderId, event.questId, effects);
      break;
    case "useOfficerRelief":
      useOfficerRelief(next, event.memberId, effects);
      break;
  }

  return { state: next, effects };
}

/* ------------------------------------------------------------ 시간대 엔진 */

function beginDay(state: RunState, effects: Effect[]): void {
  state.phaseIndex = 0;
  state.carryoverMs = 0;

  // 기온은 하루의 시작에 딱 한 번 뽑힌다. 밴드가 그날의 일과표 자체를 바꾸므로(1.0),
  // 퀘스트 배정보다 먼저 확정되어야 한다.
  state.weather = weatherFor(state.seed, state.day, state.season);
  effects.push({ type: "weatherRolled", day: state.day, weather: state.weather });
  // 밴드가 극혹한을 벗어났으면 보온 게이지와 동상을 여기서 한 번에 치운다
  clearWarmth(state);

  // 보급은 아침에 도착한다. 판정 뒤로 미루면 보급일 당일의 조건 D를 막지 못해
  // "그날 극단 밴드가 뽑히면 손쓸 방법이 없는" 확정 사망이 생긴다 (SUP-01)
  runSupplyDay(state, effects);

  const [quests, afterQuests] = generateDayQuests(state);
  state.quests = quests;
  state.rngState = afterQuests;
  // 경고로 얹힌 필수는 하루만 유효하다
  state.nextDayExtraRequired = 0;

  startPhase(state, effects);
}

function startPhase(state: RunState, effects: Effect[]): void {
  state.phaseElapsedMs = 0;
  state.phaseDurationMs = phaseDurationMsFor(state, state.phaseIndex);
  state.carryoverMs = 0;

  // 오전·오후 일과는 20초 하달 창으로 시작한다. 이 동안 시간대 타이머는 정지한다 —
  // 눈치싸움에 실제 일과 시간을 쓰게 하면 짜증이 된다 (QST-04)
  openDelegationWindow(state);

  const phase = phaseAt(state.phaseIndex);

  // NPC 대리를 합동 퀘스트 장소에 세워둔다.
  // 대리는 필수를 완수하지만 합동은 사람이 시작해야 한다(1.0 "혼자서는 하루를 못 끝낸다").
  // 다만 머릿수는 채워줘야 한다 — JDG-03이 "NPC는 암구호를 읽어주지 못한다"고만 적은 것은
  // 참여 자체는 한다는 전제이며, 그렇지 않으면 1~3인 방은 합동이 있는 날마다 조건 B로 죽는다.
  stationProxies(state);

  const [surprise, rng] = rollSurprise(state, phase.id);
  state.rngState = rng;
  if (surprise) {
    state.quests.push(surprise);
    effects.push({ type: "surpriseRaised", quest: surprise });
  }

  effects.push({ type: "phaseStarted", phase: phase.id, day: state.day });
}

function applyTick(state: RunState, elapsedMs: number, effects: Effect[]): void {
  if (elapsedMs <= 0) return;

  // F-1 — 해금이 물어보는 "실시간"의 유일한 소스. `skipPhase`는 이 값을 절대
  // 건드리지 않으므로, 스킵을 몇 번을 눌러도 여기는 안 뛴다.
  state.elapsedRealMs += elapsedMs;

  for (const member of state.members) {
    if (member.travelRemainingMs > 0) {
      member.travelRemainingMs = Math.max(0, member.travelRemainingMs - elapsedMs);
    }
  }

  // 5.0 보온 게이지는 **실시간 90초**라 시간대 정산에 얹을 수 없다.
  // 여기서 ms 단위로 흘려야 한다
  tickWarmth(state, elapsedMs, effects);

  // 합동에서 대리가 채우는 제 몫 (ROLE-03)
  tickJointProxies(state, elapsedMs);

  let remaining = elapsedMs;

  // 하달 창이 열려 있는 동안은 시간대 타이머가 멈춰 있다
  if (state.delegationWindowMsLeft > 0) {
    const consumed = Math.min(state.delegationWindowMsLeft, remaining);
    state.delegationWindowMsLeft -= consumed;
    remaining -= consumed;
  }

  while (remaining > 0 && state.status === "running") {
    const left = state.phaseDurationMs - state.phaseElapsedMs;
    if (remaining < left) {
      state.phaseElapsedMs += remaining;
      return;
    }
    state.phaseElapsedMs = state.phaseDurationMs;
    remaining -= left;
    endPhase(state, effects);
  }
}

/**
 * 시간대 종료. 그 칸에 배정된 미완료 퀘스트는 여기서 잠긴다 —
 * 시간대를 넘겨서 만회하는 방법은 없다(4.0). 이게 압박의 원천이다.
 */
function endPhase(state: RunState, effects: Effect[]): void {
  const phase = phaseAt(state.phaseIndex);
  const locked: string[] = [];

  // 잠그기 전에 NPC 대리가 제 몫의 필수를 끝낸다 (ROLE-03 · 2.0).
  // 대리는 필수만 완수하고 선택·돌발·히든은 일절 수행하지 않는다 —
  // 생존은 시켜주지만 성장은 시켜주지 않는다는 JDG-03의 원칙이 여기서도 같다.
  completeProxyWork(state, phase.id);

  for (const quest of state.quests) {
    if (quest.phase !== phase.id) continue;
    if (quest.status === "done" || quest.status === "locked") continue;
    quest.status = "locked";
    locked.push(quest.id);
  }

  // 8.0 무전 — 통신병의 참여 상태와 유지 일과는 시간대 경계에서만 바뀐다.
  // `applyTick`에서 매 틱 세면 배치 시뮬이 퀘스트 배열을 수백만 번 훑는다
  const radio = refreshRadio(state);
  if (radio) effects.push({ type: "radioChanged", to: radio });

  // 컨디션은 시간대 단위로 정산된다 — 밴드 드레인이 몸으로 체감되는 지점이다 (7.0)
  applyPhaseCondition(state, phase.id, effects);
  applyForcedSleep(state, phase.id, effects);
  // 쓰러짐은 점호를 기다리지 않는다 (JDG-03)
  checkCollapses(state, state.phaseDurationMs, effects);

  effects.push({ type: "phaseEnded", phase: phase.id, lockedQuestIds: locked });

  if (state.phaseIndex >= PHASE_COUNT - 1) {
    endDay(state, effects);
    return;
  }

  state.phaseIndex += 1;
  startPhase(state, effects);
}

/**
 * 대리를 그날 합동 장소로 옮긴다. 이동에 시간이 걸리지 않는 것은
 * 대리가 "이미 거기서 일하고 있는 사람"으로 취급되기 때문이다.
 */
function stationProxies(state: RunState): void {
  const joint = state.quests.find(
    (q) => q.kind === "joint" && q.status !== "done" && q.status !== "locked",
  );
  if (!joint) return;

  for (const member of state.members) {
    if (member.presence !== "npcVacant" && member.presence !== "npcLeave") continue;
    member.zone = joint.zone;
    member.travelRemainingMs = 0;
  }
}

/**
 * NPC 대리의 일과 처리.
 *
 * 처음부터 비어 있던 자리(npcVacant)와 이탈로 생긴 자리(npcLeave) 둘 다 해당한다.
 * 이게 없으면 4인이 못 모인 방은 D-01 점호에서 조건 A가 무조건 깨져
 * ROLE-03이 말하는 1~3인 방이 아예 성립하지 않는다.
 */
function completeProxyWork(state: RunState, phaseId: PhaseId): void {
  for (const member of state.members) {
    if (member.presence !== "npcVacant" && member.presence !== "npcLeave") continue;

    for (const quest of state.quests) {
      if (quest.ownerId !== member.id || quest.phase !== phaseId) continue;
      if (!quest.required) continue;
      if (quest.status === "done" || quest.status === "locked") continue;
      quest.workedMs = quest.workMs;
      quest.status = "done";
    }
  }
}

/**
 * 스킵 투표 성립 시 호출된다. 투표 정족수(생존 인원 3/4 — 4인이면 3명)는 서버가 검증하고,
 * sim은 성립한 결과만 받는다.
 */
function skipPhase(state: RunState, effects: Effect[]): void {
  const left = Math.max(0, state.phaseDurationMs - state.phaseElapsedMs);
  state.carryoverMs += left;
  state.phaseElapsedMs = state.phaseDurationMs;
  endPhase(state, effects);
}

/**
 * 하루 마감. 점호 판정(JDG-01)이 여기서 내려지고, 통과해야만 다음 날이 온다 —
 * 세이브·리트라이의 단위는 하루다(1.0).
 */
function endDay(state: RunState, effects: Effect[]): void {
  // 그날의 성과를 군기와 복무 점수에 먼저 반영한 뒤 판정한다 (DISC-01 · 표 13-1)
  applyDailyDiscipline(state, effects);
  applyDailyServiceScore(state);
  applyJudgement(state, effects);
  if (state.status !== "running") return;

  checkDisband(state, effects);
  if (state.status !== "running") return;

  // 히든은 페널티가 없고 그날의 판정을 바꾸지 않는다 — 통과한 뒤에 확인한다 (표 6-1)
  checkHiddenQuests(state, effects);

  // 포인트는 그날의 성과에서 나온다 — 청구는 다음 보급일 아침에 이뤄진다
  accrueSupplyPoints(state);

  // 점호 판정 통과 → 심사 화면 → 승급 발표 (RANK-01)
  runReview(state, effects);

  // 점호 통과 → 야간 경계 배정 → 취침 정산 (COND-02)
  applySleep(state, effects);

  state.day += 1;
  for (const member of state.members) {
    member.vetoUsedToday = false;
    member.choresReceived = 0;
  }
  // 간부 구제 발동은 "오늘"에만 유효하다 — 판정이 이미 반영했으니 내일을 위해 비운다
  state.officerReliefArmedToday = false;
  // 후송자는 다음 날 아침 "복귀 신병"으로 돌아온다 — 몸은 돌려주고 기록은 지운다
  returnEvacuees(state, effects);
  tickRehab(state);
  state.quests = [];
  beginDay(state, effects);
}

/* ---------------------------------------------------------------- 행동 */

/**
 * 구역 이동. 도착 전까지는 상호작용이 불가능하므로, 목적지 zone을 즉시 반영하되
 * `travelRemainingMs`가 남아 있는 동안은 "이동 중"으로 취급한다.
 */
/**
 * 구역 이동.
 *
 * 두 갈래다.
 *
 *   **걸어서** — 플레이어가 맵에서 실제로 경계를 넘었다. 시간은 이미 걷는 데
 *   썼으므로 소요를 또 붙이지 않는다. 다만 **인접 구역인지 검증한다** — 안 그러면
 *   순간이동으로 §6.1의 동선 비용을 통째로 건너뛸 수 있다.
 *
 *   **그 외** — NPC 대리와 헤드리스 시뮬(simrunner)에는 맵이 없다. 이쪽은 격자
 *   거리로 계산한 소요를 그대로 문다.
 */
function startMove(state: RunState, memberId: string, to: Zone, onFoot: boolean): void {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.zone === to) return;

  if (onFoot && isAdjacent(member.zone, to)) {
    member.travelRemainingMs = 0;
    member.zone = to;
    return;
  }

  // 5.0 동상 — 이동 속도 −30%. 같은 거리를 더 오래 걷는다
  member.travelRemainingMs = travelMs(member.zone, to) * travelMultiplier(state, member);
  member.zone = to;
}

function applyWork(
  state: RunState,
  memberId: string,
  questId: string,
  deltaMs: number,
): void {
  const member = state.members.find((m) => m.id === memberId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!member || !quest) return;
  if (quest.status === "done" || quest.status === "locked") return;
  if (!canWork(state, member, quest)) return;

  // 합동 퀘스트는 요구 인원이 모이지 않으면 진행 게이지가 차오르지 않는다 (QST-01)
  if (quest.minActors > 1 && actorsAt(state, quest) < quest.minActors) return;

  quest.status = "active";
  // 5.0 동상 — 상호작용 실패율 +20%를 진척 감소로 옮긴다
  quest.workedMs = Math.min(quest.workMs, quest.workedMs + deltaMs * workMultiplier(state, member));

  // **판이 붙은 일과는 시간으로 끝나지 않는다.** 통과해야 끝나고(`clearQuest`),
  // 못 통과한 채로 소요가 다 흘렀으면 그 자리에서 다시 시도한다. 계속 실패하면
  // 시간대 종료로 잠긴다 — 잃은 시간이 곧 페널티다.
  if (quest.minigame !== null) return;

  if (quest.workedMs >= quest.workMs) complete(state, member, quest, null);
}

/**
 * 미니게임 판 통과.
 *
 * **완료 시점을 클라만 아는 유일한 경로다.** 서버는 커버리지도 오답 횟수도 볼 수
 * 없으므로 통과 자체는 받아들이되, `interact`가 통과하는 관문은 전부 그대로
 * 통과시킨다 — 엉뚱한 구역, 남의 일과, 지난 시간대, 이동 중이면 여기서도 막힌다.
 *
 * 여기에 관문을 하나 더 건다: **붙잡고 있던 시간이 소요의 절반에 못 미치면
 * 무시한다.** 원형은 깨끗이 통과해도 소요의 70~100%를 쓰도록 맞추므로 정상
 * 플레이는 걸리지 않고, 조작된 클라가 하루를 즉시 끝내는 것만 걸린다.
 */
function clearQuest(
  state: RunState,
  memberId: string,
  questId: string,
  grade: Grade,
): void {
  const member = state.members.find((m) => m.id === memberId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!member || !quest) return;
  if (quest.status === "done" || quest.status === "locked") return;
  // 판이 없는 일과는 통과를 선언할 것이 없다 — 시간이 끝낸다
  if (quest.minigame === null) return;
  if (!canWork(state, member, quest)) return;
  if (quest.minActors > 1 && actorsAt(state, quest) < quest.minActors) return;
  if (quest.workedMs < quest.workMs * CLEAR_MIN_RATIO) return;

  quest.workedMs = quest.workMs;
  complete(state, member, quest, grade);
}

/** 통과를 인정하는 최소 진척. 값 하나가 조작 여지의 크기를 정한다 — 밸런싱 대상이다 */
const CLEAR_MIN_RATIO = 0.5;

/**
 * QST-01 합동 — 조각 하나를 채웠다.
 *
 * **한 명이 잘해서 캐리하는 구조를 물리적으로 막는다.** 요구 인원이 그 자리에
 * 모이지 않으면 아무리 눌러도 조각이 올라가지 않는다 — 게이지가 안 차는 것이
 * 곧 경고이고, 화면은 그것을 읽어 준다.
 *
 * 서버는 원형 로직을 모른다. 한 조각이 무엇인지(불일치 1건 · 물건 1개 · 구간
 * 하나)는 판이 알고, 여기로는 "하나 채웠다"만 올라온다.
 */
function addJointStep(state: RunState, memberId: string, questId: string): void {
  const member = state.members.find((m) => m.id === memberId);
  const quest = state.quests.find((q) => q.id === questId);
  if (!member || !quest || quest.kind !== "joint") return;
  if (quest.status === "done" || quest.status === "locked") return;
  if (quest.jointTotal <= 0) return;
  if (!canWork(state, member, quest)) return;
  // 요구 인원 미달이면 받지 않는다 (QST-01 "참여 인원이 요구치 미달이면
  // 진행 게이지가 차오르지 않는다")
  if (actorsAt(state, quest) < quest.minActors) return;

  quest.status = "active";
  quest.jointDone = Math.min(quest.jointTotal, quest.jointDone + 1);
  if (quest.jointDone >= quest.jointTotal) {
    quest.workedMs = quest.workMs;
    // 합동에는 등급이 없다 — 분대가 같이 채운 것을 개인 등급으로 가를 수 없다
    complete(state, member, quest, null);
  }
}

/**
 * 대리가 채우는 제 몫.
 *
 * ROLE-03이 1~3인 방을 성립시키려면 대리도 합동에 실제로 기여해야 한다.
 * 다만 **사람보다 느리다** — 소요 시간을 꽉 채워야 한 사람 몫이고, 사람은
 * 판을 돌려 그보다 빨리 채운다. "대리는 생존은 시켜주지만 성장은 시켜주지
 * 않는다"가 여기서도 같다.
 */
function tickJointProxies(state: RunState, elapsedMs: number): void {
  for (const quest of state.quests) {
    if (quest.kind !== "joint" || quest.jointTotal <= 0) continue;
    if (quest.status === "done" || quest.status === "locked") continue;
    if (quest.phase !== phaseAt(state.phaseIndex).id) continue;
    if (actorsAt(state, quest) < quest.minActors) continue;

    const proxies = state.members.filter(
      (m) =>
        (m.presence === "npcVacant" || m.presence === "npcLeave") &&
        m.zone === quest.zone &&
        m.travelRemainingMs === 0,
    ).length;
    if (proxies === 0) continue;

    // 한 사람 몫 = 전체 조각 / 요구 인원. 그것을 소요 시간에 걸쳐 채운다
    const share = quest.jointTotal / Math.max(1, quest.minActors);
    state.jointProxyMs += elapsedMs * proxies;
    const earned = Math.floor((state.jointProxyMs / quest.workMs) * share);
    if (earned <= 0) continue;

    state.jointProxyMs -= (earned / share) * quest.workMs;
    quest.status = "active";
    quest.jointDone = Math.min(quest.jointTotal, quest.jointDone + earned);
    if (quest.jointDone >= quest.jointTotal) {
      quest.workedMs = quest.workMs;
      quest.status = "done";
    }
  }
}

function complete(
  state: RunState,
  member: Member,
  quest: Quest,
  grade: Grade | null,
): void {
  quest.status = "done";
  quest.grade = grade;
  // 대행 점수는 하달자가 아니라 수행자에게 간다 (6.2 역전 경로 1)
  awardProxyScore(state, quest);
  // 회복 행동은 **그 자리에서** 몸으로 돌아온다. 시간대 종료를 기다리게 하면
  // 밥을 먹은 것과 안 먹은 것이 그 칸 안에서 구분되지 않는다
  if (quest.kind === "care") applyCare(state, member, quest.id);
}

function applyCare(state: RunState, member: Member, questId: string): void {
  const recovery = careRecovery(questId, planFor(state.day).training === "bivouac");
  for (const [key, amount] of Object.entries(recovery)) {
    const stat = key as keyof Member["stats"];
    member.stats[stat] = Math.min(100, Math.max(0, member.stats[stat] + amount));
  }
}

function canWork(state: RunState, member: Member, quest: Quest): boolean {
  if (member.presence === "evacuated") return false;
  if (member.travelRemainingMs > 0) return false;
  if (member.zone !== quest.zone) return false;
  if (quest.ownerId !== null && quest.ownerId !== member.id) return false;

  // **그 시간대에만 할 수 있다** (4.0).
  //
  // 이게 없어서 오후 일과를 오전에 끝낼 수 있었다. 그러면 하루가 시간대로
  // 나뉜 의미가 사라진다 — 아침에 전부 해치우고 나머지를 흘려보내면 되고,
  // 시간대 종료로 잠기는 규칙(`endPhase`)도 걸릴 일이 없다.
  //
  // 돌발은 예외다. 뜬 그 자리에서 처리하는 것이고, 시간대를 기다리라고 하면
  // 돌발이 아니게 된다.
  if (quest.kind !== "surprise" && quest.phase !== phaseAt(state.phaseIndex).id) {
    return false;
  }

  return true;
}

function actorsAt(state: RunState, quest: Quest): number {
  return state.members.filter(
    (m) =>
      m.presence !== "evacuated" &&
      m.travelRemainingMs === 0 &&
      m.zone === quest.zone,
  ).length;
}

/* ---------------------------------------------------------------- 유틸 */

export function cloneState(state: RunState): RunState {
  return {
    ...state,
    rngState: { ...state.rngState },
    trust: { ...state.trust },
    members: state.members.map((m) => ({
      ...m,
      stats: { ...m.stats },
      inventory: [...m.inventory],
    })),
    quests: state.quests.map((q) => ({ ...q })),
    judgements: [...state.judgements],
    ledger: [...state.ledger],
    nightGuardIds: [...state.nightGuardIds],
    pendingClaim: [...state.pendingClaim],
    hiddenUnlocked: [...state.hiddenUnlocked],
  };
}
