import type { DelegationRecord } from "./delegation.js";
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
 * 4.3 구역 — **방 하나가 곧 구역이다.**
 *
 * 정의는 `data/zones.json`에 있고 이 유니온은 그것과 일치해야 한다
 * (`test/zones.test.ts`가 검사한다). 예전에는 8개뿐이라 생활관·복도·세면장·
 * 행정반이 전부 `barracks` 하나였고, 그래서 세면장 일과를 생활관에 선 채로
 * 끝낼 수 있었다.
 */
export type Zone =
  | "Z01"
  | "Z01b"
  | "Z01c"
  | "Z02"
  | "Z03"
  | "Z13"
  | "Z16"
  | "Z17"
  | "Z09"
  | "Z20"
  | "Z07"
  | "Z07b"
  | "Z21"
  | "Z05"
  | "Z06"
  | "Z04"
  | "Z22"
  | "Z08"
  | "Z10"
  | "Z11"
  | "Z12"
  | "Z12b"
  | "Z18"
  | "Z19"
  | "Z14"
  | "Z50"
  | "TR01"
  | "TR02"
  | "TR03"
  | "TR04"
  | "TR05"
  | "TR06"
  | "TR07"
  | "TR08"
  | "TR09"
  | "TR10";

/**
 * 8.0 무전 상태 3단계.
 *
 * 분대 공통 값이다 — 사람마다 다르게 보이면 "모여야 정보가 전달된다"는 합의가
 * 불가능해진다. 그래서 개인이 아니라 런이 들고 있다.
 */
export type RadioState = "ok" | "weak" | "down";

/** 6.0 퀘스트 5종 */
/**
 * 일과의 종류.
 *
 * `care`만 성격이 다르다 — **판정 대상이 아니다.** 밥·물·세면처럼 몸을 되돌리는
 * 행동이고, 안 했다고 조건 A가 깨지거나 군기가 깎이지 않는다. 대신 안 하면
 * 포만감·수분·청결로 갚는다(7.0). 그래서 조건 A·군기·복무 점수를 세는 자리마다
 * 이 종류를 빼야 한다.
 */
export type QuestKind = "role" | "chore" | "joint" | "surprise" | "hidden" | "care";

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
  /** 이번 하달 창에서 넘긴 건수 — 계급차 상한과 비교한다 */
  delegatedThisWindow: number;
  /**
   * 이번 하달 창을 내가 끝냈다고 신고했는가 (조기 종료용).
   *
   * 하달했든 넘겼든(그냥 대기만 했든) 상관없다 — 창을 더 볼 것이 없다는 신고일 뿐이다.
   * 창이 열릴 때마다 초기화되고, 사람 참석자(`presence === "player"`) 전원이 이걸 세우면
   * `delegationWindowMsLeft`가 즉시 0이 된다.
   */
  delegationDone: boolean;
  /** 열사병 2단계 진입 후 경과 — 60초를 버티면 쓰러진다 (5.0) */
  collapseTimerMs: number;
  /** 후송 횟수. 1회라도 있으면 모범 전역이 불가능해진다 (JDG-03) */
  evacuations: number;
  /** 복귀 후 재활 일수 — 이 동안 본인 필수가 +1 된다 */
  rehabDaysLeft: number;
  /** 11.0 소지 장비. 표 5-1 필수 장비 판정과 피복 보온치 계산에 쓰인다 */
  inventory: string[];
  /**
   * 5.0 보온 게이지 — 열원 접촉까지 남은 ms. 극혹한 밴드에서만 흐른다.
   * 0이 되면 동상이 붙고, 그때부터 이동과 작업이 실제로 느려진다.
   */
  warmthRemainingMs: number;
  /** 동상 디버프. 열원에 다시 가도 안 풀리고 **의무병만** 해제할 수 있다 (5.0) */
  frostbitten: boolean;
}

/* ----------------------------------------------------------------- 퀘스트 */

/**
 * 일과 미니게임 원형 14종 (+ 랜덤 소환).
 *
 * 정의는 `packages/sim/data/quests.json`이 소유하고 프로토콜이 되비춘다 —
 * 이쪽이 원본인 이유는 제한 시간이 `workSeconds`이기 때문이다. 미니게임을
 * 별도 파일로 빼면 소요와 제한 시간이 반드시 어긋난다.
 */
export type MinigameType =
  | "SCRUB"
  | "PLACE"
  | "AUDIT"
  | "MASH"
  | "BALANCE"
  | "HOLD"
  | "TRACE"
  | "SORT"
  | "TIMING"
  | "SEQ"
  | "RHYTHM"
  | "TRACK"
  | "SEARCH"
  | "REACT"
  | "RANDOM";

/**
 * 일과 하나의 판.
 *
 * **sim은 파라미터를 읽지 않는다.** 판을 도는 것은 클라이언트이고, 여기서는
 * 그대로 실어 나르기만 한다 — 그래서 원형별 모양을 union으로 가르지 않고
 * 색인 서명으로 열어 둔다. 값의 모양을 검증하는 것은 프로토콜의 몫이다.
 */
export interface Minigame {
  readonly type: MinigameType;
  /** 같은 원형을 다른 일로 읽히게 하는 스킨 이름 */
  readonly variant: string;
  /** 1~3 */
  readonly difficulty: number;
  /** 20초 이상 퀘스트의 2페이즈 */
  readonly phase2?: MinigameType;
  /** 진행 중인 판을 끊고 들어오는 모디파이어 */
  readonly interrupt?: "REACT";
  readonly [param: string]: string | number | undefined;
}

/**
 * 수행 등급 — 통과한 판을 얼마나 깨끗하게 했는가.
 *
 * 못 채우면 완료가 아니라 재시도이므로 "실패" 등급이 없다. 복무 점수 차등에만
 * 쓰이고 하루 판정(조건 A)은 바꾸지 않는다 — C등급도 완료는 완료다.
 */
export type Grade = "A" | "B" | "C";

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
  /**
   * 그 일이 벌어지는 물건 (`files-6` 목업의 수행 지점).
   *
   * "관물대 정돈"은 관물대 앞에서 하는 일이다. 예전에는 클라가 일과 **이름**을
   * 보고 방 안의 아무 물건을 골랐고, 그래서 관물대 정돈과 복도 정돈이 같은
   * 자리에서 벌어졌다. 판이 없는 일과(회복 · 합동 · 훈련)는 null이다.
   */
  readonly spot: string | null;
  /** 상호작용 소요 (이동 시간은 별도) */
  readonly workMs: number;
  workedMs: number;
  /** 합동 퀘스트가 요구하는 동시 참여 인원 (기본 2, 최대 3 — QST-01) */
  readonly minActors: number;
  status: QuestStatus;
  /** 하달로 넘겨받은 것이면 넘긴 사람 id (수첩에 이름이 붙는다 — 15.0) */
  delegatedFrom: string | null;
  /**
   * 이 일과를 무엇으로 하는가. null이면 판이 없다.
   *
   * 판이 없는 일과는 붙잡고 있는 시간만으로 완료된다 — 회복 행동·합동·훈련
   * 체크포인트가 그렇고, 아직 안 만든 원형도 여기로 떨어져 진행을 막지 않는다.
   */
  readonly minigame: Minigame | null;
  /** 완료된 일과의 수행 등급. 판이 없거나 아직 안 끝났으면 null */
  grade: Grade | null;
  /**
   * 합동 판의 목표 조각 수. 합동이 아니면 0.
   *
   * **판 하나를 인원이 나눠 채운다.** 서버는 원형 로직을 모르고 조각 수만
   * 센다 — 한 조각을 채우는 것이 무엇인지(불일치 1건 · 물건 1개 · 구간 1개)는
   * 원형이 알고, 채웠다는 신고만 올라온다.
   */
  readonly jointTotal: number;
  /** 지금까지 분대가 채운 조각 */
  jointDone: number;
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
  /**
   * 폭우 — 14.0 D-10 "기상 악화(폭우 · 한파 · 폭염)"의 첫 갈래.
   *
   * 온도 밴드와 **따로 논다.** 폭우는 추워서 오는 것이 아니라 그날의 사건이고,
   * 밴드로는 표현할 수 없다 — 15℃에 쏟아지는 비는 평시 밴드다.
   */
  readonly rain: boolean;
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
  /**
   * 런 시작부터 실제로 흐른 실시간(ms) 누적. `tick` 이벤트로만 늘어난다 (F-1).
   *
   * `day`·`phaseIndex`는 스킵 투표로 실제 시간 없이도 앞으로 간다 — 그래서 해금을
   * 일차만으로 게이트하면 스킵을 반복해 몇 초 만에 여러 날을 밀어버릴 수 있다.
   * 이 값은 `skipPhase`가 절대 건드리지 않으므로, 해금이 "진짜 몇 분이 지났는가"를
   * 물을 수 있는 유일한 자리다.
   */
  elapsedRealMs: number;

  weather: WeatherState;
  /** 8.0 무전 상태. 통신병의 참여 상태와 유지 일과가 정한다 (`radio.ts`) */
  radio: RadioState;
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
  /** 하달 창 잔여 시간. 0보다 크면 시간대 타이머가 멈춰 있다 (QST-04) */
  delegationWindowMsLeft: number;
  /** 분대장이 개입한 시간대 인덱스 — 1회/시간대 */
  leaderOverridePhase: number;
  /** 런 종료 시 공개되는 하달 장부 */
  ledger: DelegationRecord[];
  /** 11.0 분대 보급 포인트 — 항상 부족하도록 잡혀 있다 */
  supplyPoints: number;
  /** 행정병이 작성한 청구서. 비어 있으면 표준 청구로 채운다 */
  pendingClaim: string[];
  /** 달성한 히든 퀘스트 — 4개를 모으면 분대 기록 엔딩이 열린다 (META-02) */
  hiddenUnlocked: string[];
  /**
   * 합동에서 대리가 흘린 시간.
   *
   * 조각은 정수라 한 틱에 0.3개씩 채울 수가 없다. 떨어질 만큼 모일 때까지
   * 여기 쌓아 둔다 — 이게 없으면 틱이 잘게 오는 서버에서 대리가 영영 기여를
   * 못 한다.
   */
  jointProxyMs: number;

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
  /**
   * 합동 판의 조각 하나를 채웠다.
   *
   * 요구 인원이 그 구역에 모이지 않으면 받지 않는다 — 그게 강제 협동이다.
   * 혼자 아무리 눌러도 조각이 올라가지 않는다.
   */
  | {
      readonly type: "jointStep";
      readonly memberId: string;
      readonly questId: string;
    }
  /**
   * 미니게임 판을 통과했다 — 판이 붙은 일과는 이것으로만 완료된다.
   *
   * 서버가 자격을 검증한 뒤에만 들어온다. sim은 여기서 한 번 더 본다:
   * 붙잡고 있던 시간이 소요의 절반에 못 미치면 무시한다.
   */
  | {
      readonly type: "questCleared";
      readonly memberId: string;
      readonly questId: string;
      readonly grade: Grade;
    }
  /** 구역 이동 시작 */
  | {
      readonly type: "move";
      readonly memberId: string;
      readonly to: Zone;
      /** 걸어서 경계를 넘었는가. 인접 구역이면 이동 소요가 붙지 않는다 (`zones.ts`) */
      readonly onFoot?: boolean;
    }
  /** QST-04 하달 — 공통 일과를 하급자에게 넘긴다 */
  | {
      readonly type: "delegateChore";
      readonly fromId: string;
      readonly toId: string;
      readonly questId: string;
    }
  /** QST-05 거부권 — 1일 1회 */
  | { readonly type: "vetoChore"; readonly memberId: string; readonly questId: string }
  /**
   * 하달 창 조기 종료 신고 — "이 창에서 더 볼 것이 없다."
   *
   * 하달을 했든, 넘겼든(대기만 했든) 상관없이 보낸다. 사람 참석자 전원이 이걸
   * 보내면 남은 창 시간을 기다리지 않고 시간대 타이머가 다시 흐른다.
   */
  | { readonly type: "delegationDone"; readonly memberId: string }
  /** 분대장 개입 — 1회/시간대 */
  | {
      readonly type: "leaderReassign";
      readonly leaderId: string;
      readonly questId: string;
      readonly toId: string;
    }
  /** 중도 이탈 — 장기 대리로 전환된다 */
  | { readonly type: "leaveRun"; readonly memberId: string }
  /** 재접속 */
  | { readonly type: "rejoinRun"; readonly memberId: string }
  /** 11.0 청구서 작성 — 행정병만 */
  | {
      readonly type: "fileClaim";
      readonly memberId: string;
      readonly items: readonly string[];
    };

export type Effect =
  | { readonly type: "weatherRolled"; readonly day: number; readonly weather: WeatherState }
  | { readonly type: "phaseStarted"; readonly phase: PhaseId; readonly day: number }
  | { readonly type: "surpriseRaised"; readonly quest: Quest }
  | { readonly type: "phaseEnded"; readonly phase: PhaseId; readonly lockedQuestIds: string[] }
  | {
      readonly type: "dayJudged";
      readonly judgement: Judgement;
      /** 이 판정 반영 **후** 런에 남은 구제권. 차감 순서를 따로 타지 않도록 여기서 확정해 실어 보낸다 */
      readonly reliefsRemaining: number;
    }
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
  | {
      readonly type: "choreDelegated";
      readonly fromId: string;
      readonly toId: string;
      readonly questId: string;
    }
  | {
      readonly type: "delegationRefused";
      readonly reason: string;
      readonly questId: string;
    }
  | { readonly type: "choreVetoed"; readonly memberId: string; readonly questId: string }
  | {
      readonly type: "choreReassigned";
      readonly leaderId: string;
      readonly toId: string;
      readonly questId: string;
    }
  | {
      readonly type: "memberEvacuated";
      readonly memberId: string;
      /** 대리가 잔여 필수를 인수했는가 — 못 하면 조건 A가 깨진다 */
      readonly absorbed: boolean;
    }
  | { readonly type: "memberLeft"; readonly memberId: string }
  | { readonly type: "frostbitten"; readonly memberId: string }
  | {
      readonly type: "frostbiteRelieved";
      readonly memberId: string;
      /** 5.0 — 해제는 의무병만 할 수 있다. 누가 풀었는지가 곧 근거다 */
      readonly byId: string;
    }
  | { readonly type: "radioChanged"; readonly to: RadioState }
  | {
      readonly type: "memberReturned";
      readonly memberId: string;
      /** 후송 복귀는 축적을 전부 잃은 "복귀 신병"이다 */
      readonly asRecruit: boolean;
    }
  | { readonly type: "forcedSleep"; readonly memberId: string }
  | {
      readonly type: "supplyClaimed";
      readonly day: number;
      readonly items: readonly string[];
      readonly pointsLeft: number;
    }
  | {
      readonly type: "rankReviewed";
      readonly day: number;
      readonly isRetry: boolean;
      readonly require: number;
      /** 점수 내역은 전원에게 공개된다 — 무임승차가 드러나는 것이 사회적 압력 장치다 */
      readonly outcomes: readonly {
        readonly memberId: string;
        readonly promoted: boolean;
        readonly from: Rank;
        readonly to: Rank;
        readonly score: number;
        readonly require: number;
        readonly trustBonus: number;
      }[];
    }
  | { readonly type: "hiddenUnlocked"; readonly id: string; readonly label: string }
  | { readonly type: "runEnded"; readonly status: RunStatus }
  | { readonly type: "log"; readonly message: string };

export interface StepResult {
  readonly state: RunState;
  readonly effects: readonly Effect[];
}
