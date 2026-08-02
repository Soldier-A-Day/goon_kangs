import {
  RESCUE_REQUIRED_MS,
  bandRule,
  buildHeadline,
  disciplineBand,
  isSupplyDay,
  jointRoles,
  missingGear,
  phaseAt,
  weeklyModifierById,
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
export function projectSnapshot(
  state: RunState,
  seq: number,
  /** 표시용 좌표 — 방이 들고 있다. 규칙은 이 값을 모른다 */
  positions?: ReadonlyMap<string, { x: number; y: number }>,
): Snapshot {
  const phase = phaseAt(state.phaseIndex);
  const last = state.judgements[state.judgements.length - 1];
  // B-1 정보 비대칭 — 합동판이 SEQ·TRACE가 아니거나 방에 실사람이 2명
  // 미만이면 sim이 빈 맵을 낸다. 숨길 이유가 없는 값이라 전원에게 그대로 보낸다
  const joint = [...jointRoles(state).entries()].map(([memberId, role]) => ({
    memberId,
    role,
  }));

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
      rain: state.weather.rain,
    },
    // C-3 — 화면이 "이번 주 상황"을 알아야 한다. 노출 UI는 후속 발주고,
    // 여기서는 스냅샷에 싣기만 한다.
    weeklyModifier: weeklyModifierById(state.weeklyModifier),
    discipline: {
      value: Math.round(state.discipline),
      band: disciplineBand(state.discipline).id,
    },
    // 12.0 간부 신뢰도 3트랙 — 승급 점수의 절반이라 스냅샷에 실어야 승급 화면이
    // "복무점수+신뢰보너스"로 분해할 수 있다(WORKORDER.md E-2, 감사 목록 3)
    trust: {
      platoonLeader: Math.round(state.trust.platoonLeader),
      assistant: Math.round(state.trust.assistant),
      sergeantMajor: Math.round(state.trust.sergeantMajor),
    },
    // 8.0 — 분대 공통 값이라 멤버가 아니라 스냅샷 최상위에 있다
    radio: state.radio,
    supply: {
      points: state.supplyPoints,
      isSupplyDay: isSupplyDay(state.day),
      pendingClaim: [...state.pendingClaim],
    },
    reliefsRemaining: state.reliefsRemaining,
    // 10.0 B-4 — 발동 자격은 각 몫을 따로 봐야 한다. leaderReliefsRemaining은
    // "분대장에게 우선순위 지정 버튼을 보여줄지", officerReliefsRemaining은
    // "저녁 개인정비에 간부 구제 버튼을 보여줄지"를 클라가 판단하는 근거다.
    leaderReliefsRemaining: state.leaderReliefsRemaining,
    officerReliefsRemaining: state.officerReliefsRemaining,
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
      // 5.0 보온 게이지. 극혹한이 아니면 0이고 클라는 그때 이 UI를 띄우지 않는다
      warmthRemainingMs: Math.round(member.warmthRemainingMs),
      frostbitten: member.frostbitten,
      // B-2 위기 — null이면 위기 아님. 미니맵 마커·HUD 프롬프트가 이 세 값으로 그린다
      crisisStat: member.crisisStat,
      crisisMsLeft: Math.round(member.crisisMsLeft),
      rescueProgress: Math.min(1, member.rescueMs / RESCUE_REQUIRED_MS),
      x: positions?.get(member.id)?.x ?? 0,
      y: positions?.get(member.id)?.y ?? 0,
    })),
    quests: state.quests.map((quest) => ({
      id: quest.id,
      kind: quest.kind,
      label: quest.label,
      ownerId: quest.ownerId,
      required: quest.required,
      phase: quest.phase,
      zone: quest.zone,
      spot: quest.spot,
      progress: quest.workMs === 0 ? 1 : Math.min(1, quest.workedMs / quest.workMs),
      workSeconds: Math.round(quest.workMs / 1000),
      status: quest.status,
      minActors: quest.minActors,
      delegatedFrom: quest.delegatedFrom,
      training: quest.training,
      /**
       * 판 정의는 그대로 내보낸다 — 판을 도는 것이 클라이고, 여기에 판정은 없다.
       *
       * sim의 `Minigame`은 파라미터를 색인 서명으로 열어 둔 느슨한 타입이라
       * 프로토콜의 원형별 union에 그대로 들어가지 않는다. 좁혀 주는 것은 타입이
       * 아니라 데이터 테스트다 — `quests.json`의 69건이 전부 `minigameSchema`를
       * 통과하는지 `test/minigame-data.test.ts`가 매번 확인한다.
       */
      minigame: quest.minigame as Snapshot["quests"][number]["minigame"],
      grade: quest.grade,
      jointTotal: quest.jointTotal,
      jointDone: quest.jointDone,
      // 오늘의 합동은 최대 하나뿐이라(quests.ts) 이 배열은 그 하나에만 채워진다
      jointRoles: quest.kind === "joint" ? joint : [],
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
    // C-1 — 마지막 판정이 실패했으면 그 조건이 "처음" 결정타로 지목된 순간을 함께 싣는다.
    // sim이 조건별로 하나씩만 쥐고 있으므로(`firstConditionBreach`) 여기서는 그대로 흘린다.
    firstFailure:
      last && !last.passed && last.failedAt
        ? (state.firstConditionBreach[last.failedAt] ?? null)
        : null,
    headline: buildHeadline(state),
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
        reliefsUsed: effect.judgement.reliefsUsed,
        reliefsRemaining: effect.reliefsRemaining,
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
    case "frostbitten":
      return { type: "frostbitten", memberId: effect.memberId };
    case "frostbiteRelieved":
      return { type: "frostbiteRelieved", memberId: effect.memberId, byId: effect.byId };
    case "radioChanged":
      return { type: "radioChanged", radioState: effect.to };
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
          // 승급 점수의 절반 — 예전에는 여기서 잘려 화면이 "복무점수"만 보여줬다
          // (WORKORDER.md E-2, 감사 목록 3). sim이 이미 계산해 실어 보내므로 그대로 흘린다.
          trustBonus: outcome.trustBonus,
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
    case "delegationRefused":
      // 예전에는 여기서 버렸다 — 하달 버튼이 아무 신호 없이 씹혔다(WORKORDER.md E단계
      // 침묵 판정). 사유를 흘려서 하달 창이 "왜 안 됐는지"를 말하게 한다.
      //
      // sim의 `Effect["delegationRefused"]`는 `reason`을 `string`으로만 선언한다
      // (packages/sim/src/types.ts) — 실제 값은 언제나 `delegation.ts`의
      // `DelegationRefusal` 9종 중 하나다(canDelegate가 그 타입만 반환한다). sim은
      // 이 발주의 소유가 아니라(ARCH-02) 타입을 여기서 좁혀 받는다.
      return {
        type: "delegationRefused",
        reason: effect.reason as Extract<ServerEvent, { type: "delegationRefused" }>["reason"],
        questId: effect.questId,
      };
    case "crisisStarted":
      // B-2 — 화면이 결과보다 먼저 말한다: "OOO 쓰러졌다 — N초 안에 가라"
      return {
        type: "crisisStarted",
        memberId: effect.memberId,
        stat: effect.stat,
        crisisMs: effect.crisisMs,
      };
    case "crisisRescued":
      // 실패(시간 만료)는 새 이벤트가 아니라 기존 memberEvacuated로 나간다 —
      // 긴장을 물타기하지 않는다
      return {
        type: "crisisRescued",
        memberId: effect.memberId,
        rescuerId: effect.rescuerId,
        stat: effect.stat,
      };
    case "conditionCritical":
      // 예전에는 여기서 버렸다 — 스태미나 0·탈수 2단계·강제취침 임계가 후송/강제취침
      // 직전 신호인데 화면은 결과만 보여주고 예고가 없었다(WORKORDER.md E-2, 감사
      // 목록 1). sim의 `Effect["conditionCritical"]`은 stat을 `keyof Stats`로 넓게
      // 선언하지만(packages/sim/src/types.ts) 실제 값은 `raiseCriticals`
      // (packages/sim/src/condition.ts)가 언제나 stamina·hydration·fatigue 중
      // 하나만 낸다 — sim은 이 발주 소유가 아니라(ARCH-02) 타입을 여기서 좁혀 받는다.
      return {
        type: "conditionCritical",
        memberId: effect.memberId,
        stat: effect.stat as Extract<ServerEvent, { type: "conditionCritical" }>["stat"],
      };
    case "reliefGranted":
      return {
        type: "reliefGranted",
        by: effect.by,
        questId: effect.questId,
        leaderReliefsRemaining: effect.leaderReliefsRemaining,
        officerReliefsRemaining: effect.officerReliefsRemaining,
      };
    case "reliefRefused":
      // B-4 — 구제 발동 거부도 침묵 판정으로 남기지 않는다. sim의
      // `Effect["reliefRefused"]`는 `reason`을 `string`으로만 선언하지만
      // (packages/sim/src/types.ts) 실제 값은 언제나 `relief.ts`의 `ReliefRefusal`
      // 7종 중 하나다 — sim은 이 발주의 소유가 아니라(ARCH-02) 타입을 여기서 좁혀 받는다.
      return {
        type: "reliefRefused",
        by: effect.by,
        reason: effect.reason as Extract<ServerEvent, { type: "reliefRefused" }>["reason"],
        questId: effect.questId,
      };
    default:
      return null;
  }
}
