import {
  bandRule,
  disciplineBand,
  isSupplyDay,
  missingGear,
  phaseAt,
  type Effect,
  type RunState,
} from "@sad/sim";
import type { ServerEvent, Snapshot } from "@sad/protocol";

/**
 * RunState → 스냅샷 투영.
 *
 * 여기가 "클라이언트에게 무엇을 보여줄지"를 정하는 유일한 곳이다.
 * 시드와 RNG 상태는 절대 나가지 않는다 — 나가면 기온 롤과 돌발을 미리 계산할 수 있다.
 * 퀘스트도 남은 ms가 아니라 진척 비율만 준다.
 */
export function projectSnapshot(state: RunState, seq: number): Snapshot {
  const phase = phaseAt(state.phaseIndex);
  const last = state.judgements[state.judgements.length - 1];

  return {
    type: "snapshot",
    seq,
    runId: state.runId,
    status: state.status,
    day: state.day,
    totalDays: state.config.totalDays,
    phase: {
      id: phase.id,
      index: state.phaseIndex,
      label: phase.label,
      clock: phase.clock,
      elapsedMs: Math.round(state.phaseElapsedMs),
      durationMs: Math.round(state.phaseDurationMs),
      delegationWindowMsLeft: Math.round(state.delegationWindowMsLeft),
    },
    weather: {
      band: state.weather.band,
      label: bandRule(state.weather.band).label,
      feelsLike: state.weather.feelsLike,
    },
    discipline: {
      value: Math.round(state.discipline),
      band: disciplineBand(state.discipline).id,
    },
    supply: {
      points: state.supplyPoints,
      isSupplyDay: isSupplyDay(state.day),
      pendingClaim: [...state.pendingClaim],
    },
    reliefsRemaining: state.reliefsRemaining,
    leaderId: state.leaderId,
    members: state.members.map((member) => ({
      id: member.id,
      name: member.name,
      role: member.role,
      rank: member.rank,
      presence: member.presence,
      zone: member.zone,
      travelRemainingMs: Math.round(member.travelRemainingMs),
      stats: {
        stamina: Math.round(member.stats.stamina),
        hydration: Math.round(member.stats.hydration),
        fatigue: Math.round(member.stats.fatigue),
        mental: Math.round(member.stats.mental),
        hygiene: Math.round(member.stats.hygiene),
        satiety: Math.round(member.stats.satiety),
      },
      serviceScore: member.serviceScore,
      inventory: [...member.inventory],
      missingGear: missingGear(member, state.weather.band),
      choresReceived: member.choresReceived,
      vetoUsedToday: member.vetoUsedToday,
      onGuardTonight: state.nightGuardIds.includes(member.id),
    })),
    quests: state.quests.map((quest) => ({
      id: quest.id,
      kind: quest.kind,
      label: quest.label,
      ownerId: quest.ownerId,
      required: quest.required,
      phase: quest.phase,
      zone: quest.zone,
      progress: quest.workMs === 0 ? 1 : Math.min(1, quest.workedMs / quest.workMs),
      status: quest.status,
      minActors: quest.minActors,
      delegatedFrom: quest.delegatedFrom,
      training: quest.training,
    })),
    lastJudgement: last
      ? {
          day: last.day,
          passed: last.passed,
          failedAt: last.failedAt,
          requiredDone: last.requiredDone,
          requiredTotal: last.requiredTotal,
        }
      : null,
  };
}

/** sim의 Effect를 표시용 이벤트로 좁힌다. 내부 수치를 그대로 태우지 않는다. */
export function projectEffect(effect: Effect): ServerEvent | null {
  switch (effect.type) {
    case "phaseStarted":
      return { type: "phaseStarted", phase: effect.phase, day: effect.day };
    case "phaseEnded":
      return {
        type: "phaseEnded",
        phase: effect.phase,
        lockedCount: effect.lockedQuestIds.length,
      };
    case "weatherRolled":
      return {
        type: "weatherRolled",
        band: effect.weather.band,
        label: bandRule(effect.weather.band).label,
      };
    case "surpriseRaised":
      return {
        type: "surpriseRaised",
        questId: effect.quest.id,
        label: effect.quest.label,
      };
    case "dayJudged":
      return {
        type: "dayJudged",
        day: effect.judgement.day,
        passed: effect.judgement.passed,
        failedAt: effect.judgement.failedAt,
      };
    case "disciplineChanged":
      return { type: "disciplineChanged", to: effect.to, band: effect.band };
    case "memberEvacuated":
      return {
        type: "memberEvacuated",
        memberId: effect.memberId,
        absorbed: effect.absorbed,
      };
    case "memberReturned":
      return {
        type: "memberReturned",
        memberId: effect.memberId,
        asRecruit: effect.asRecruit,
      };
    case "memberLeft":
      return { type: "memberLeft", memberId: effect.memberId };
    case "forcedSleep":
      return { type: "forcedSleep", memberId: effect.memberId };
    case "supplyClaimed":
      return {
        type: "supplyClaimed",
        day: effect.day,
        items: [...effect.items],
        pointsLeft: effect.pointsLeft,
      };
    case "rankReviewed":
      return {
        type: "rankReviewed",
        day: effect.day,
        isRetry: effect.isRetry,
        require: effect.require,
        outcomes: effect.outcomes.map((outcome) => ({
          memberId: outcome.memberId,
          promoted: outcome.promoted,
          from: outcome.from,
          to: outcome.to,
          score: outcome.score,
          require: outcome.require,
        })),
      };
    case "sleepSettled":
      return { type: "sleepSettled", guardIds: [...effect.guardIds] };
    case "choreDelegated":
      return {
        type: "choreDelegated",
        fromId: effect.fromId,
        toId: effect.toId,
        questId: effect.questId,
      };
    case "choreVetoed":
      return { type: "choreVetoed", memberId: effect.memberId, questId: effect.questId };
    case "choreReassigned":
      return { type: "choreReassigned", toId: effect.toId, questId: effect.questId };
    case "hiddenUnlocked":
      return { type: "hiddenUnlocked", id: effect.id, label: effect.label };
    case "runEnded":
      return { type: "runEnded", status: effect.status };
    case "log":
      return { type: "log", message: effect.message };
    // 클라이언트가 알 필요 없는 신호는 흘리지 않는다
    case "conditionCritical":
    case "delegationRefused":
      return null;
    default:
      return null;
  }
}
