import { pickLeader } from "./leader.js";
import { rollWeeklyModifier } from "./modifier.js";
import { evaluateRadio } from "./radio.js";
import { LEADER_RELIEF_LIMIT, OFFICER_RELIEF_LIMIT } from "./relief.js";
import { createRngState, roll } from "./rng.js";
import { STARTING_KIT, SUPPLY_START } from "./supply.js";
import { ROLES, type Member, type Role, type RunConfig, type RunState, type Stats } from "./types.js";

export const DEFAULT_CONFIG: RunConfig = {
  difficulty: "regular",
  season: "random",
  choreDelegation: "free",
  totalDays: 18,
};

/** 7.0 표 7-1 초기값. fatigue만 0에서 시작한다. */
export function initialStats(): Stats {
  return {
    stamina: 100,
    hydration: 100,
    fatigue: 0,
    mental: 100,
    hygiene: 100,
    satiety: 100,
  };
}

export interface MemberSeed {
  readonly id: string;
  readonly name: string;
  readonly role: Role;
}

export interface CreateRunOptions {
  readonly runId: string;
  readonly seed: number;
  readonly members: readonly MemberSeed[];
  readonly config?: Partial<RunConfig>;
}

/**
 * 런 생성. 보직은 4개뿐이고 중복이 없으므로(3.0), 비어 있는 보직은 NPC로 채운다.
 *
 * 처음부터 비어 있던 자리는 `npcVacant`로 표시된다 — ROLE-03에 따라 이 자리에는
 * 군기 −3/일을 부과하지 않는다. 사고로 생긴 공백과 애초에 인원이 부족한 것은 다른 사건이다.
 */
export function createRun(options: CreateRunOptions): RunState {
  const taken = new Set<Role>();
  const members: Member[] = [];

  for (const seed of options.members) {
    if (taken.has(seed.role)) {
      throw new Error(`보직 중복: ${seed.role} — 4인 편성은 보직당 1명이다`);
    }
    taken.add(seed.role);
    members.push(makeMember(seed.id, seed.name, seed.role, "player"));
  }

  for (const role of ROLES) {
    if (!taken.has(role)) {
      members.push(makeMember(`npc-${role}`, npcName(role), role, "npcVacant"));
    }
  }

  members.sort((a, b) => ROLES.indexOf(a.role) - ROLES.indexOf(b.role));

  const config = { ...DEFAULT_CONFIG, ...options.config };
  // 계절은 런 시작 시 한 번 확정한다 (5.0). 랜덤이면 여기서 뽑고 이후로는 바뀌지 않는다.
  const [isCold, rngAfterSeason] = roll(createRngState(options.seed), 0.5);
  const season = config.season === "random" ? (isCold ? "cold" : "hot") : config.season;
  // C-3 주간 변조 — 계절 롤과 별도 스트림에서 시드로 확정한다(modifier.ts).
  // 여기서 메인 RNG를 소비하면 계절 확률이 밀리므로 rngAfterSeason에는 손대지 않는다.
  const weeklyModifier = rollWeeklyModifier(options.seed).id;

  const state: RunState = {
    runId: options.runId,
    seed: options.seed,
    rngState: rngAfterSeason,
    config,
    season,
    weeklyModifier,
    status: "running",

    day: 1,
    phaseIndex: 0,
    phaseElapsedMs: 0,
    phaseDurationMs: 0,
    carryoverMs: 0,
    elapsedRealMs: 0,

    weather: {
      band: "normal",
      feelsLike: 12,
      airTemp: 12,
      humidity: 50,
      windSpeed: 0,
      rain: false,
    },
    // 편성을 보고 정한다 — 아래에서 `evaluateRadio`로 덮는다.
    // (여기 리터럴은 형태를 맞추기 위한 자리이며 실제 값이 아니다)
    radio: "ok",
    members,
    quests: [],

    discipline: 60,
    trust: { platoonLeader: 50, assistant: 50, sergeantMajor: 50 },

    // G1 — 예전엔 여기가 `null`로 끝까지 남아 구제권(`relief.ts` canUseRelief)이
    // 영원히 `notLeader`로 막혔다. `members`는 위에서 이미 ROLES 순서로 정렬됐으므로
    // (line 64) `pickLeader`가 그 순서에서 첫 사람 참석자를 결정적으로 뽑는다.
    leaderId: pickLeader(members),
    leaderVotes: {},
    // 10.0 구제 총량 3회 = 분대장 몫(LEADER_RELIEF_LIMIT) + 간부 몫(OFFICER_RELIEF_LIMIT)
    reliefsRemaining: LEADER_RELIEF_LIMIT + OFFICER_RELIEF_LIMIT,
    leaderReliefsRemaining: LEADER_RELIEF_LIMIT,
    officerReliefsRemaining: OFFICER_RELIEF_LIMIT,
    officerReliefArmedToday: false,
    warnings: 0,
    nextDayExtraRequired: 0,
    personalTimeRevoked: false,
    nextDayExtraOptional: 0,
    startedHumans: options.members.length,

    nightGuardIds: [],
    delegationWindowMsLeft: 0,
    dayEndWindowMsLeft: 0,
    leaderOverridePhase: -1,
    ledger: [],
    supplyPoints: SUPPLY_START,
    pendingClaim: [],
    hiddenUnlocked: [],
    jointProxyMs: 0,
    judgements: [],
    firstConditionBreach: {},
  };

  // **시작 무전 상태는 편성에서 나온다.**
  // 예전에는 `"ok"`로 시작해 놓고 "첫 tick에서 갱신된다"고 적어 뒀는데, 실제로
  // `refreshRadio`는 `endPhase`(시간대 경계)에서만 돈다 — 매 틱 퀘스트 배열을
  // 훑으면 배치 시뮬이 수백만 번 순회하기 때문이다. 그래서 **첫 시간대(기상·점호)
  // 내내 무전이 살아 있는 것처럼 보이다가 오전 일과가 시작하는 순간 두절로 뒤집혔다.**
  // 통신병이 NPC인 편성에서 사용자가 실제로 그 증상을 보고했다.
  // 시작할 때 한 번 제대로 재면 그 거짓말이 사라진다(비용은 런당 1회).
  state.radio = evaluateRadio(state);
  return state;
}

function makeMember(
  id: string,
  name: string,
  role: Role,
  presence: Member["presence"],
): Member {
  return {
    id,
    name,
    role,
    rank: "private",
    presence,
    zone: "Z01",   // 아침에 눈을 뜨는 곳은 생활관이다
    travelRemainingMs: 0,
    stats: initialStats(),
    serviceScore: 0,
    choresReceived: 0,
    vetoUsedToday: false,
    delegatedThisWindow: 0,
    delegationDone: false,
    dayEndAcked: false,
    collapseTimerMs: 0,
    warmthRemainingMs: 0,
    frostbitten: false,
    evacuations: 0,
    rehabDaysLeft: 0,
    inventory: [...STARTING_KIT],
    crisisStat: null,
    crisisMsLeft: 0,
    rescueMs: 0,
  };
}

function npcName(role: Role): string {
  const names: Record<Role, string> = {
    rifle: "대리 소총수",
    comms: "대리 통신병",
    medic: "대리 의무병",
    admin: "대리 행정병",
  };
  return names[role];
}

/** 실제 플레이어 수. NPC 대리는 세지 않는다 (JDG-03 런 종료 조건). */
export function humanCount(state: RunState): number {
  return state.members.filter((m) => m.presence === "player").length;
}

export function findMember(state: RunState, memberId: string): Member | undefined {
  return state.members.find((m) => m.id === memberId);
}
