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

  return {
    runId: options.runId,
    seed: options.seed,
    rngState: rngAfterSeason,
    config,
    season,
    status: "running",

    day: 1,
    phaseIndex: 0,
    phaseElapsedMs: 0,
    phaseDurationMs: 0,
    carryoverMs: 0,

    weather: {
      band: "normal",
      feelsLike: 12,
      airTemp: 12,
      humidity: 50,
      windSpeed: 0,
    },
    members,
    quests: [],

    discipline: 60,
    trust: { platoonLeader: 50, assistant: 50, sergeantMajor: 50 },

    leaderId: null,
    reliefsRemaining: 3,
    warnings: 0,
    nextDayExtraRequired: 0,
    personalTimeRevoked: false,
    nextDayExtraOptional: 0,
    startedHumans: options.members.length,

    nightGuardIds: [],
    delegationWindowMsLeft: 0,
    leaderOverridePhase: -1,
    ledger: [],
    supplyPoints: SUPPLY_START,
    pendingClaim: [],
    hiddenUnlocked: [],
    judgements: [],
  };
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
    zone: "barracks",
    travelRemainingMs: 0,
    stats: initialStats(),
    serviceScore: 0,
    choresReceived: 0,
    vetoUsedToday: false,
    delegatedThisWindow: 0,
    collapseTimerMs: 0,
    evacuations: 0,
    rehabDaysLeft: 0,
    inventory: [...STARTING_KIT],
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
