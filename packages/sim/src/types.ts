import type { RngState } from "./rng.js";

/* ---------------------------------------------------------------- 기본 열거 */

/** 3.0 보직 — 4인 편성에서 보직당 정확히 1명, 중복 없음 */
export type Role = "rifle" | "comms" | "medic" | "admin";

export const ROLES: readonly Role[] = ["rifle", "comms", "medic", "admin"];

/** 13.1 계급 — 능력치가 아니라 권한만 준다 */
export type Rank = "private" | "pfc" | "corporal" | "sergeant";

export const RANK_ORDER: readonly Rank[] = [
  "private",
  "pfc",
  "corporal",
  "sergeant",
];

/** 4.0 시간대 6칸 */
export type PhaseId =
  | "reveille"
  | "morning"
  | "lunch"
  | "afternoon"
  | "personal"
  | "rollcall";

/** 5.0 온도 밴드 6종 */
export type TempBand =
  | "extremeCold"
  | "cold"
  | "normal"
  | "warm"
  | "hot"
  | "extremeHot";

/**
 * 4.3 구역 그래프 — 3D가 없어도 "동선이 멀다"(6.1)를 표현하기 위한 논리 위치.
 * Unity가 붙으면 좌표를 zone으로 매핑할 뿐 규칙은 바뀌지 않는다.
 */
export type Zone =
  | "barracks"
  | "drillGround"
  | "storage"
  | "messHall"
  | "guardPost"
  | "trainingField"
  | "infirmary"
  | "boilerRoom";

/** 6.0 퀘스트 5종 */
export type QuestKind = "role" | "chore" | "joint" | "surprise" | "hidden";

export type QuestStatus =
  | "pending"
  | "active"
  | "done"
  | "failed"
  /** 시간대 종료로 잠김 — 만회 경로는 없다 (4.0) */
  | "locked";

/** 분대원의 참여 상태 */
export type Presence =
  /** 사람이 조작 중 */
  | "player"
  /** 후송 대리 — 단기, 인수 한도 2건 (JDG-03) */
  | "npcEvac"
  /** 이탈 대리 — 장기, 한도 없음 (2.0 중도 이탈) */
  | "npcLeave"
  /** 처음부터 비어 있던 자리 — 군기 −3/일 면제 (ROLE-03) */
  | "npcVacant"
  /** 후송되어 당일 밤 조작 불가 */
  | "evacuated";

export type RunStatus = "running" | "cleared" | "discharged" | "disbanded";

/* -------------------------------------------------------------------- 스탯 */

/** 7.0 컨디션 6스탯. fatigue만 0에서 시작해 100으로 오른다. */
export interface Stats {
  /** 체력 */
  stamina: number;
  /** 수분 */
  hydration: number;
  /** 피로 (0 → 100) */
  fatigue: number;
  /** 정신력 */
  mental: number;
  /** 청결 */
  hygiene: number;
  /** 포만감 */
  satiety: number;
}

/* ------------------------------------------------------------------ 분대원 */

export interface Member {
  readonly id: string;
  readonly name: string;
  readonly role: Role;
  rank: Rank;
  presence: Presence;
  zone: Zone;
  /** 이동 중이면 도착까지 남은 ms. 0이면 도착 상태 */
  travelRemainingMs: number;
  stats: Stats;
  /** 13.1 복무 점수 — 승급 심사의 유일한 기준 */
  serviceScore: number;
  /** 하달받아 보유 중인 공통 일과 수 (상한 2, QST-04) */
  choresReceived: number;
  /** 오늘 하달 거부권을 썼는가 (1일 1회, QST-05) */
  vetoUsedToday: boolean;
}

/* ----------------------------------------------------------------- 퀘스트 */

export interface Quest {
  readonly id: string;
  readonly kind: QuestKind;
  readonly label: string;
  /** 훈련 구간 체크포인트면 훈련 id. 일반 퀘스트는 null (TRN-02) */
  readonly training: string | null;
  /** 개인 판정 대상이면 담당자 id, 합동이면 null */
  ownerId: string | null;
  /** 필수 여부 — 필수 미완료는 조건 A를 깨고 분대 전원 게임오버 (JDG-01) */
  readonly required: boolean;
  readonly phase: PhaseId;
  readonly zone: Zone;
  /** 상호작용 소요 (이동 시간은 별도) */
  readonly workMs: number;
  workedMs: number;
  /** 합동 퀘스트가 요구하는 동시 참여 인원 (기본 2, 최대 3 — QST-01) */
  readonly minActors: number;
  status: QuestStatus;
  /** 하달로 넘겨받은 것이면 넘긴 사람 id (수첩에 이름이 붙는다 — 15.0) */
  delegatedFrom: string | null;
}

/* ------------------------------------------------------------------ 판정 */

export type JudgementCondition = "A" | "B" | "C" | "D";

export interface Judgement {
  readonly day: number;
  readonly passed: boolean;
  /** 깨진 첫 조건 — 실패 원인을 한 줄로 지목한다 (JDG-02) */
  readonly failedAt: JudgementCondition | null;
  readonly requiredTotal: number;
  readonly requiredDone: number;
  readonly jointPassed: boolean;
  readonly discipline: number;
  /** 이 판정에서 구제권을 몇 건 썼는가 */
  readonly reliefsUsed: number;
}

/* -------------------------------------------------------------------- 런 */

export type Difficulty = "regular" | "relaxed";
export type SeasonChoice = "hot" | "cold" | "random";

export interface RunConfig {
  /** 2.0 난이도 옵션 — 정규는 1회 미달 즉시 종료 */
  readonly difficulty: Difficulty;
  readonly season: SeasonChoice;
  /** 6.2 하달 정책 — 공개 매칭에서는 분대장 승인제 (20.0 결정 항목 6) */
  readonly choreDelegation: "free" | "leaderApproval";
  readonly totalDays: number;
}

export interface WeatherState {
  readonly band: TempBand;
  /** 체감온도 (섭씨) */
  readonly feelsLike: number;
  readonly airTemp: number;
  readonly humidity: number;
  readonly windSpeed: number;
}

export interface RunState {
  readonly runId: string;
  readonly seed: number;
  rngState: RngState;
  readonly config: RunConfig;
  /** 런 시작 시 확정된 계절. 같은 18일이 두 가지 커리큘럼으로 갈린다 (5.0) */
  readonly season: "cold" | "hot";
  status: RunStatus;

  /** 1부터 시작하는 일차 */
  day: number;
  phaseIndex: number;
  phaseElapsedMs: number;
  phaseDurationMs: number;
  /** 스킵 투표로 아낀 시간 — 다음 시간대로 이월된다 (TIME-01) */
  carryoverMs: number;

  weather: WeatherState;
  members: Member[];
  quests: Quest[];

  /** 12.0 분대 군기 게이지 (0~100, 시작 60) */
  discipline: number;
  /** 12.0 간부 신뢰도 3트랙 */
  trust: { platoonLeader: number; assistant: number; sergeantMajor: number };

  /** 분대장 id (3.0 ROLE-02) */
  leaderId: string | null;
  /** 10.0 구제 총량 3회 = 분대장 우선순위 지정 2 + 간부 구제권 1 */
  reliefsRemaining: number;
  /** 완화 난이도의 누적 경고 횟수 */
  warnings: number;
  /** 완화 난이도 1차 경고의 대가 — 다음 날 필수 퀘스트에 얹힌다 */
  nextDayExtraRequired: number;
  /** 완화 난이도 2차 근신 — 다음 날 개인정비 시간대 박탈 */
  personalTimeRevoked: boolean;
  /** 12.0 이완 구간(20~39)의 대가 — 다음 날 선택 퀘스트가 늘어난다 */
  nextDayExtraOptional: number;
  /** 런 시작 시점의 실제 플레이어 수. 분대 해체 판정의 기준이 된다 */
  readonly startedHumans: number;
  /** 오늘 밤 경계 근무자 — 회복량 50%를 감수한 사람들 (COND-02) */
  nightGuardIds: string[];

  judgements: Judgement[];
}

/* ---------------------------------------------------------- 이벤트 / 결과 */

export type SimEvent =
  /** 서버가 주입하는 시간. sim은 시계를 갖지 않는다. */
  | { readonly type: "tick"; readonly elapsedMs: number }
  /** 하루 시작 — 기온 롤과 일과표 생성 */
  | { readonly type: "beginDay" }
  /** 남은 시간을 버리고 다음 시간대로 (스킵 투표 성립 시) */
  | { readonly type: "skipPhase" }
  /** 퀘스트 수행 진척 */
  | {
      readonly type: "work";
      readonly memberId: string;
      readonly questId: string;
      readonly deltaMs: number;
    }
  /** 구역 이동 시작 */
  | { readonly type: "move"; readonly memberId: string; readonly to: Zone };

export type Effect =
  | { readonly type: "weatherRolled"; readonly day: number; readonly weather: WeatherState }
  | { readonly type: "phaseStarted"; readonly phase: PhaseId; readonly day: number }
  | { readonly type: "surpriseRaised"; readonly quest: Quest }
  | { readonly type: "phaseEnded"; readonly phase: PhaseId; readonly lockedQuestIds: string[] }
  | { readonly type: "dayJudged"; readonly judgement: Judgement }
  | { readonly type: "sleepSettled"; readonly guardIds: readonly string[] }
  | {
      readonly type: "disciplineChanged";
      readonly from: number;
      readonly to: number;
      readonly band: string;
    }
  | {
      readonly type: "conditionCritical";
      readonly memberId: string;
      readonly stat: keyof Stats;
    }
  | { readonly type: "runEnded"; readonly status: RunStatus }
  | { readonly type: "log"; readonly message: string };

export interface StepResult {
  readonly state: RunState;
  readonly effects: readonly Effect[];
}
