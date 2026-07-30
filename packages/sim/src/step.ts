import { applyPhaseCondition, applySleep } from "./condition.js";
import {
  awardProxyScore,
  delegateChore,
  leaderReassign,
  openDelegationWindow,
  vetoChore,
} from "./delegation.js";
import { applyDailyDiscipline } from "./discipline.js";
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
import { generateDayQuests, rollSurprise } from "./quests.js";
import { rollWeather } from "./weather.js";
import { travelMs } from "./zones.js";
import type {
  Effect,
  Member,
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
      startMove(next, event.memberId, event.to);
      break;
    case "work":
      applyWork(next, event.memberId, event.questId, event.deltaMs);
      break;
    case "delegateChore":
      delegateChore(next, event.fromId, event.toId, event.questId, effects);
      break;
    case "vetoChore":
      vetoChore(next, event.memberId, event.questId, effects);
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
  }

  return { state: next, effects };
}

/* ------------------------------------------------------------ 시간대 엔진 */

function beginDay(state: RunState, effects: Effect[]): void {
  state.phaseIndex = 0;
  state.carryoverMs = 0;

  // 기온은 하루의 시작에 딱 한 번 뽑힌다. 밴드가 그날의 일과표 자체를 바꾸므로(1.0),
  // 퀘스트 배정보다 먼저 확정되어야 한다.
  const [weather, rng] = rollWeather(state.rngState, state.day, state.season);
  state.weather = weather;
  state.rngState = rng;
  effects.push({ type: "weatherRolled", day: state.day, weather });

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

  for (const member of state.members) {
    if (member.travelRemainingMs > 0) {
      member.travelRemainingMs = Math.max(0, member.travelRemainingMs - elapsedMs);
    }
  }

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

  for (const quest of state.quests) {
    if (quest.phase !== phase.id) continue;
    if (quest.status === "done" || quest.status === "locked") continue;
    quest.status = "locked";
    locked.push(quest.id);
  }

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
  // 그날의 성과를 군기에 먼저 반영한 뒤 판정한다 (DISC-01)
  applyDailyDiscipline(state, effects);
  applyJudgement(state, effects);
  if (state.status !== "running") return;

  checkDisband(state, effects);
  if (state.status !== "running") return;

  // 점호 통과 → 야간 경계 배정 → 취침 정산 (COND-02)
  applySleep(state, effects);

  state.day += 1;
  for (const member of state.members) {
    member.vetoUsedToday = false;
    member.choresReceived = 0;
  }
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
function startMove(state: RunState, memberId: string, to: Zone): void {
  const member = state.members.find((m) => m.id === memberId);
  if (!member || member.zone === to) return;
  member.travelRemainingMs = travelMs(member.zone, to);
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
  if (!canWork(member, quest)) return;

  // 합동 퀘스트는 요구 인원이 모이지 않으면 진행 게이지가 차오르지 않는다 (QST-01)
  if (quest.minActors > 1 && actorsAt(state, quest) < quest.minActors) return;

  quest.status = "active";
  quest.workedMs = Math.min(quest.workMs, quest.workedMs + deltaMs);
  if (quest.workedMs >= quest.workMs) {
    quest.status = "done";
    // 대행 점수는 하달자가 아니라 수행자에게 간다 (6.2 역전 경로 1)
    awardProxyScore(state, quest);
  }
}

function canWork(member: Member, quest: Quest): boolean {
  if (member.presence === "evacuated") return false;
  if (member.travelRemainingMs > 0) return false;
  if (member.zone !== quest.zone) return false;
  if (quest.ownerId !== null && quest.ownerId !== member.id) return false;
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
    members: state.members.map((m) => ({ ...m, stats: { ...m.stats } })),
    quests: state.quests.map((q) => ({ ...q })),
    judgements: [...state.judgements],
  };
}
