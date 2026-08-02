import { z } from "zod";

/**
 * 서버 ↔ 클라이언트 경계.
 *
 * 이 패키지는 규칙을 모른다 — 규칙은 sim에만 있다(ARCH-02). 여기 있는 것은
 * "무엇을 주고받는가"뿐이며, 스키마 단일 정의에서 TS 타입이 나오고 나중에 C# 클래스가 나온다.
 *
 * 스냅샷에 **원자료를 넣지 않는다**는 규칙이 중요하다. 특히 RNG 상태와 시드는 절대 내보내지 않는다 —
 * 나가면 클라이언트가 기온 롤과 돌발을 미리 계산할 수 있고, 그 순간 서버 권위가 무의미해진다.
 */

export const PROTOCOL_VERSION = 1;

/* ------------------------------------------------------------------ 공용 */

export const roleSchema = z.enum(["rifle", "comms", "medic", "admin"]);
export const rankSchema = z.enum(["private", "pfc", "corporal", "sergeant"]);
export const phaseIdSchema = z.enum([
  "reveille",
  "morning",
  "lunch",
  "afternoon",
  "personal",
  "rollcall",
]);
export const tempBandSchema = z.enum([
  "extremeCold",
  "cold",
  "normal",
  "warm",
  "hot",
  "extremeHot",
]);
/**
 * 4.3 구역 — **방 하나가 곧 구역이다.**
 *
 * 정의는 `packages/sim/data/zones.json`이 소유한다. 예전에는 8개뿐이라
 * 생활관·복도·세면장·행정반이 전부 `barracks` 하나였고, 그래서 세면장 일과를
 * 생활관에 선 채로 끝낼 수 있었다.
 */
export const zoneSchema = z.enum([
  "Z01",
  "Z01b",
  "Z01c",
  "Z02",
  "Z03",
  "Z13",
  "Z16",
  "Z17",
  "Z09",
  "Z20",
  "Z07",
  "Z07b",
  "Z21",
  "Z05",
  "Z06",
  "Z04",
  "Z22",
  "Z08",
  "Z10",
  "Z11",
  "Z12",
  "Z12b",
  "Z18",
  "Z19",
  "Z14",
  "Z50",
  "TR01",
  "TR02",
  "TR03",
  "TR04",
  "TR05",
  "TR06",
  "TR07",
  "TR08",
  "TR09",
  "TR10",
]);
/**
 * 8.0 무전 상태 3단계.
 *
 * **분대 공통 값이라 서버가 소유한다.** 클라이언트가 통신병의 상태를 보고 스스로
 * 유도하면 사람마다 다른 화면을 보게 되고, "무전이 끊겼으니 모이자"는 합의 자체가
 * 성립하지 않는다. 2D 탑다운에서는 이게 유일한 정보 차단 장치다(SAD-ART-001 §1.3).
 */
export const radioStateSchema = z.enum(["ok", "weak", "down"]);

export const questKindSchema = z.enum([
  "role",
  "chore",
  "joint",
  "surprise",
  "hidden",
  // 회복 행동 — 밥·물·세면. 판정에 들어가지 않는다(sim types.ts QuestKind)
  "care",
]);
export const questStatusSchema = z.enum(["pending", "active", "done", "failed", "locked"]);
export const presenceSchema = z.enum([
  "player",
  "npcEvac",
  "npcLeave",
  "npcVacant",
  "evacuated",
]);

/**
 * 6.2 하달 거부 사유 9종 (`packages/sim/src/delegation.ts` `DelegationRefusal`).
 *
 * **예전에는 이 값이 서버에서 아예 버려졌다**(`services/gameserver/src/snapshot.ts`
 * `projectEffect` — "클라이언트가 알 필요 없는 신호는 흘리지 않는다"로 묶여 있었다).
 * 하달 버튼을 눌러도 왜 안 됐는지 화면이 말하지 않는 원인이었다 — 감사 보고서
 * 침묵 판정 1건(WORKORDER.md E단계). 이제는 흘려보내고 하달 창이 사유를 문구로 보여준다.
 */
export const delegationRefusalSchema = z.enum([
  "locked",
  "notDelegationWindow",
  "notChore",
  "notOwner",
  "rankTooLow",
  "giverLimit",
  "receiverLimit",
  "alreadyDelegated",
  "unknownMember",
]);

/**
 * 6.2 컨디션 붕괴 임계 3종 (`packages/sim/src/condition.ts` `raiseCriticals`).
 *
 * **예전에는 이 값도 서버에서 아예 버려졌다**(`services/gameserver/src/snapshot.ts`
 * `projectEffect` — "이 발주 범위 밖" 주석으로 묶여 있었다). 스태미나 0·탈수 2단계·
 * 강제취침 임계는 후송·강제취침 직전 신호인데, 화면은 아무 예고 없이 결과만
 * 보여줬다 — 감사 보고서 침묵 판정 1건(WORKORDER.md E단계). 이제는 흘려서
 * 결과가 벌어지기 전에 화면이 먼저 말한다.
 */
export const conditionCriticalStatSchema = z.enum(["stamina", "hydration", "fatigue"]);

/**
 * B-2 위기 스탯 — `conditionCriticalStatSchema` 3종 중 실제로 쓰러지는 둘뿐이다
 * (`packages/sim/src/crisis.ts` `CrisisStat`). 피로 100은 강제취침이지 위기가
 * 아니다(evacuation.ts).
 */
export const crisisStatSchema = z.enum(["stamina", "hydration"]);

/** 8.0 퀵 커맨드 8슬롯 — 타임 프레셔 구간의 유일한 채널이므로 프로토콜에 고정한다 */
export const quickCommandSchema = z.enum([
  "assemble",
  "wait",
  "allClear",
  "needHelp",
  "done",
  "cannot",
  "overHere",
  "hurry",
]);
export type QuickCommand = z.infer<typeof quickCommandSchema>;

/**
 * 일과 수행 등급.
 *
 * **통과 여부가 아니라 여유다.** 목표를 못 채우면 완료가 아니라 재시도이므로
 * 등급에 "실패"가 없다 — 통과한 판을 얼마나 깨끗하게 했는가(남은 시간 · 실수
 * 횟수)만 가른다. 복무 점수 차등에만 쓰이고 판정(조건 A)은 바꾸지 않는다.
 *
 * 양쪽에 다 나온다. 클라가 `questCleared`로 올리고, 스냅샷이 그대로 되비춘다.
 */
export const gradeSchema = z.enum(["A", "B", "C"]);
export type Grade = z.infer<typeof gradeSchema>;

/* ------------------------------------------------------- 클라이언트 → 서버 */

/**
 * 클라이언트는 **의도만** 보낸다. 판정도, 진척 계산도 서버가 한다.
 * 예컨대 `interact`는 "이 퀘스트를 붙잡고 있다"는 신고이며, 얼마나 진행됐는지는 서버가 센다.
 */
export const intentSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("ready"), value: z.boolean() }),
  z.object({
    type: z.literal("move"),
    to: zoneSchema,
    /**
     * 걸어서 경계를 넘었는가.
     *
     * 2D 맵에서는 플레이어가 실제로 걸어서 구역을 옮기므로(SAD-ART-002) 이동
     * 소요를 서버가 또 세면 이중 계산이 된다. 다만 서버는 **인접한 구역인지**
     * 확인한다 — 안 그러면 순간이동으로 동선 비용을 통째로 건너뛸 수 있다.
     */
    onFoot: z.boolean().optional(),
  }),
  z.object({ type: z.literal("interact"), questId: z.string(), active: z.boolean() }),
  /**
   * 미니게임 판을 통과했다.
   *
   * **여기 하나만 예외다.** 나머지 의도는 전부 "하고 있다"는 신고이고 완료는
   * 서버가 세지만, 판이 붙은 일과는 완료 시점을 클라만 안다 — 서버는 커버리지도
   * 오답 횟수도 볼 수 없다.
   *
   * 그래서 **거절할 수 있는 것은 그대로 거절한다.** 구역·시간대·소유자·이동
   * 중·합동 인원은 `interact`와 똑같이 검증하고, 여기에 최소 시간 게이트를
   * 하나 더 건다(`step.ts`) — 판이 소요의 절반도 안 지나 통과를 외치면 무시한다.
   * 조작된 클라가 하루를 즉시 끝내는 것만 막고, 정상 플레이는 걸리지 않는다.
   *
   * 실패는 보내지 않는다. 판을 다시 여는 것은 순전히 클라 동작이고, 계속
   * 실패하면 시간대 종료로 잠긴다 — 잃은 시간이 곧 페널티다.
   */
  z.object({
    type: z.literal("questCleared"),
    questId: z.string(),
    grade: gradeSchema,
  }),
  /**
   * QST-01 합동 — 판의 조각 하나를 채웠다.
   *
   * 합동은 혼자 통과하는 판이 아니라 **인원이 나눠 채우는 판**이다. 요구 인원이
   * 그 자리에 모이지 않으면 서버가 받지 않는다 — 한 명이 잘해서 캐리하는 구조를
   * 물리적으로 막는 것이 이 퀘스트의 목적이다.
   */
  z.object({ type: z.literal("jointStep"), questId: z.string() }),
  /**
   * 지금 서 있는 자리.
   *
   * **판정에 쓰이지 않는다.** 서버는 이 값을 검증도 저장도 하지 않고 스냅샷에
   * 그대로 되비추기만 한다 — 규칙이 보는 것은 여전히 `zone`뿐이다(`canWork`).
   * 벽을 통과해 걸어도 서버는 모르지만, 엉뚱한 구역에서는 일과가 안 되므로
   * 얻을 것이 없다.
   *
   * 이게 없으면 같은 방 안에서 남이 어디 있는지 알 수가 없다. 구역이 바뀔
   * 때만 위치가 갱신되어, 방 안에서는 아무도 움직이지 않는 것으로 보인다.
   */
  z.object({ type: z.literal("position"), x: z.number(), y: z.number() }),
  z.object({ type: z.literal("quickCommand"), command: quickCommandSchema }),
  z.object({ type: z.literal("chat"), text: z.string().max(200) }),
  z.object({ type: z.literal("voteSkip"), value: z.boolean() }),
  z.object({
    type: z.literal("delegateChore"),
    toId: z.string(),
    questId: z.string(),
  }),
  z.object({ type: z.literal("vetoChore"), questId: z.string() }),
  /**
   * 하달 창 조기 종료 신고 — "이 창에서 더 볼 것이 없다."
   *
   * 하달 확정이든 그냥 넘김이든 클라가 창을 닫는 그 순간 보낸다. 사람 참석자
   * (`presence === "player"`) 전원이 보내야 창이 닫힌다 — sim이 판정한다
   * (`packages/sim/src/delegation.ts` `markDelegationDone`).
   */
  z.object({ type: z.literal("delegationDone") }),
  z.object({
    type: z.literal("leaderReassign"),
    questId: z.string(),
    toId: z.string(),
  }),
  z.object({ type: z.literal("voteLeader"), candidateId: z.string() }),
  /** 11.0 청구서 작성 — 서버가 행정병인지 확인한다 */
  z.object({ type: z.literal("fileClaim"), items: z.array(z.string()).max(12) }),
  /**
   * B-2 위기 구조 — 쓰러진 동료 곁에서 E 홀드.
   *
   * `interact`와 같은 모양이지만 대상이 퀘스트가 아니라 사람이다. 새 미니게임을
   * 만들지 않는다는 발주 경계 때문에 별도 채널로 둔다 — 판정은 전부 서버
   * (`packages/sim/src/crisis.ts`)가 하고, 클라는 "지금 이 사람을 붙잡고 있다"는
   * 신고만 보낸다.
   */
  z.object({ type: z.literal("rescue"), targetId: z.string(), active: z.boolean() }),
]);
export type Intent = z.infer<typeof intentSchema>;

/* ------------------------------------------------------- 서버 → 클라이언트 */

export const statsSchema = z.object({
  stamina: z.number(),
  hydration: z.number(),
  fatigue: z.number(),
  mental: z.number(),
  hygiene: z.number(),
  satiety: z.number(),
});

export const memberViewSchema = z.object({
  id: z.string(),
  name: z.string(),
  role: roleSchema,
  rank: rankSchema,
  presence: presenceSchema,
  zone: zoneSchema,
  /** 이동 중이면 도착까지 남은 ms */
  travelRemainingMs: z.number(),
  stats: statsSchema,
  serviceScore: z.number(),
  /** 11.0 소지 장비 */
  inventory: z.array(z.string()),
  /** 오늘 밴드에서 모자란 필수 장비 — 조건 D가 깨질 신호다 */
  missingGear: z.array(z.string()),
  choresReceived: z.number(),
  vetoUsedToday: z.boolean(),
  onGuardTonight: z.boolean(),
  /**
   * 5.0 보온 게이지 — 열원 접촉까지 남은 ms. 극혹한 밴드에서만 흐른다.
   *
   * 남은 시간만 주고 전체 길이는 주지 않는다. 클라가 비율을 그리려면 밴드 규칙을
   * 알아야 하는데, 그건 서버 데이터 테이블에 있다(ARCH-02).
   */
  warmthRemainingMs: z.number(),
  /** 동상 디버프 — 이동 −30%, 작업 −20%. 의무병만 해제할 수 있다 */
  frostbitten: z.boolean(),
  /**
   * B-2 위기 — null이면 위기가 아니다. 값이 있으면 쓰러져 구조를 기다리는 중이고,
   * 미니맵·HUD가 이 값으로 마커/프롬프트를 낼 수 있다.
   */
  crisisStat: crisisStatSchema.nullable(),
  /** 위기 남은 ms — 0에 닿으면 기존 후송 규칙대로 실려 나간다 */
  crisisMsLeft: z.number(),
  /** 구조 진행 0~1 — 동료가 곁에서 E를 홀드한 누적 비율. 의무병이면 더 빨리 찬다 */
  rescueProgress: z.number(),
  /**
   * 월드 좌표 — **표시 전용이다.**
   *
   * 규칙은 이 값을 보지 않는다. sim에 두지 않은 이유가 그것이다 — 규칙 엔진이
   * 화면 데이터를 들기 시작하면 헤드리스 시뮬이 좌표를 만들어내야 하고,
   * 퀘스트 픽스처마다 의미 없는 x·y가 붙는다.
   *
   * 아직 한 번도 안 보낸 사람은 0이다. 클라는 그때 구역의 기본 자리에 세운다.
   */
  x: z.number(),
  y: z.number(),
});

/* ---------------------------------------------------------------- 미니게임 */

/**
 * 일과 미니게임 원형 14종 (+ 랜덤 소환).
 *
 * 퀘스트마다 새 게임을 만들지 않는다 — **원형을 파라미터로 변주한다.** 같은
 * `SCRUB`이라도 대상(타일·총열·차체)과 브러시 크기·오염 패턴이 다르면 다른 일로
 * 읽히므로, 예산은 스킨과 파라미터에 쓰고 로직은 하나만 유지한다.
 *
 * `REACT`는 원래 단독으로 쓰지 않는다 — 다른 원형 위에 끼워 넣는 인터럽트
 * 모디파이어(`interrupt`)다. 예외는 `간부 순찰` 하나뿐이다.
 *
 * `RANDOM`은 다른 구역의 미니게임 하나를 무작위로 불러온다 (`타 분대 지원 요청`).
 */
export const minigameTypeSchema = z.enum([
  "SCRUB",
  "PLACE",
  "AUDIT",
  "MASH",
  "BALANCE",
  "HOLD",
  "TRACE",
  "SORT",
  "TIMING",
  "SEQ",
  "RHYTHM",
  "TRACK",
  "SEARCH",
  "REACT",
  "RANDOM",
]);
export type MinigameType = z.infer<typeof minigameTypeSchema>;

/**
 * 원형별 파라미터.
 *
 * **평평하다.** 스펙 초안은 `params: {...}`로 한 겹 감쌌지만, C# 생성기가
 * 판별 union을 필드 합집합으로 펴면서 중첩 객체는 첫 variant의 모양만 가져간다
 * (`tools/csharpgen/src/emit.ts` `buildUnion`). 감싸는 순간 Unity가 SCRUB 말고는
 * 파라미터를 못 읽는다.
 *
 * 제한 시간은 여기 없다 — `workSeconds`가 곧 제한 시간이다. 두 곳에 두면
 * 반드시 어긋난다.
 */
const board = <T extends string, P extends z.ZodRawShape>(type: T, params: P) =>
  z.object({
    type: z.literal(type),
    /** 같은 원형을 다른 일로 읽히게 하는 스킨 이름 */
    variant: z.string(),
    /** 1~3. 야간·혹한기·피로도에서 런타임으로 +1 할 수 있다 */
    difficulty: z.number().int().min(1).max(3),
    /** 20초 이상 퀘스트의 2페이즈 */
    phase2: minigameTypeSchema.optional(),
    /** 진행 중인 판을 끊고 들어오는 모디파이어 */
    interrupt: z.literal("REACT").optional(),
    ...params,
  });

export const minigameSchema = z.discriminatedUnion("type", [
  /** 오염 커버리지를 채운다. `supply`가 있으면 문지를 수 있는 총량이 제한된다 */
  board("SCRUB", {
    stains: z.number(),
    brush: z.number(),
    coverage: z.number(),
    supply: z.number().optional(),
  }),
  /** 격자·실루엣에 맞춘다. `snapDeg`가 0이면 각도는 보지 않는다 */
  board("PLACE", {
    pieces: z.number(),
    snapDeg: z.number(),
    tolerance: z.number(),
  }),
  /** 목록과 실물을 대조해 불일치를 적발한다 */
  board("AUDIT", {
    entries: z.number(),
    mismatches: z.number(),
    strikes: z.number(),
  }),
  /** 연타로 게이지를 채운다. `stamina`가 1 미만이면 같은 일이 더 무겁다 */
  board("MASH", { taps: z.number(), drain: z.number(), stamina: z.number() }),
  /** 흔들림을 보정하며 구간을 완주한다 */
  board("BALANCE", {
    sway: z.number(),
    segments: z.number(),
    tolerance: z.number(),
  }),
  /** 바늘을 안전 범위에 붙잡는다. `slack`은 허용 이탈 누적(초) */
  board("HOLD", {
    bandMin: z.number(),
    bandMax: z.number(),
    drift: z.number(),
    slack: z.number(),
  }),
  /** 노드를 돌려 시작에서 끝까지 잇는다 */
  board("TRACE", {
    nodes: z.number(),
    branches: z.number(),
    rotations: z.number(),
  }),
  /** 좌/우/폐기로 가른다. `ambiguous`는 판단이 갈리는 품목 수 */
  board("SORT", {
    items: z.number(),
    bins: z.number(),
    ambiguous: z.number(),
    strikes: z.number(),
  }),
  /** 움직이는 바를 판정 구간에 세운다 */
  board("TIMING", { hits: z.number(), window: z.number(), speed: z.number() }),
  /** 제시된 순서를 재현한다. `steps`가 2 이상이면 자릿수가 점증한다 */
  board("SEQ", { length: z.number(), steps: z.number(), show: z.number() }),
  /** 박자에 맞춰 입력하고 콤보를 유지한다 */
  board("RHYTHM", { beats: z.number(), bpm: z.number(), combo: z.number() }),
  /** 움직이는 표적 위에 커서를 유지한다 */
  board("TRACK", {
    targets: z.number(),
    speed: z.number(),
    holdRatio: z.number(),
  }),
  /** 화면을 훑어 은닉 대상을 찾는다. `signal`은 근접 신호 반경 */
  board("SEARCH", {
    hidden: z.number(),
    cells: z.number(),
    signal: z.number(),
  }),
  /** 단발 QTE. 단독으로 쓰는 것은 `간부 순찰` 하나뿐이다 */
  board("REACT", { window: z.number(), count: z.number() }),
  /** 다른 구역의 미니게임 하나를 무작위 소환한다 */
  board("RANDOM", {}),
]);
export type Minigame = z.infer<typeof minigameSchema>;

export const questViewSchema = z.object({
  id: z.string(),
  kind: questKindSchema,
  label: z.string(),
  ownerId: z.string().nullable(),
  required: z.boolean(),
  phase: phaseIdSchema,
  zone: zoneSchema,
  /**
   * 그 일이 벌어지는 물건 이름.
   *
   * 클라는 이 이름의 소품을 그 구역에서 찾아 표식을 세운다. 예전에는 일과
   * **이름**을 보고 방 안의 아무 물건을 골랐고, 그래서 관물대 정돈과 복도
   * 정돈이 같은 자리에서 벌어졌다 — 같은 사실은 한 곳에만 있어야 한다.
   */
  spot: z.string().nullable(),
  /** 0~1. 남은 ms가 아니라 비율만 준다 — 클라가 완료 시점을 스스로 정하지 못하게 */
  progress: z.number(),
  /**
   * 총 소요(초).
   *
   * 클라가 **판의 크기**를 정하는 데 쓴다 — 수거 일과에 물건을 몇 개 흩을지,
   * 조작 판을 몇 번 맞춰야 하는지. 비율(`progress`)만으로는 20초짜리와 60초짜리가
   * 화면에서 같은 분량이 되어, 무거운 일과가 무겁게 느껴지지 않는다.
   *
   * 완료 판정에는 쓰이지 않는다. 판을 다 채워도 완료를 선언하는 것은 서버다.
   */
  workSeconds: z.number(),
  status: questStatusSchema,
  minActors: z.number(),
  delegatedFrom: z.string().nullable(),
  training: z.string().nullable(),
  /**
   * 이 일과를 무엇으로 하는가.
   *
   * null이면 판이 없다 — 회복 행동·합동·훈련 체크포인트가 그렇고, 이쪽은
   * 예전처럼 붙잡고 있는 시간만으로 완료된다. 미구현 원형이 진행을 막지 않게
   * 하는 안전장치이기도 하다.
   */
  minigame: minigameSchema.nullable(),
  /** 완료된 일과의 수행 등급. 아직 안 끝났으면 null */
  grade: gradeSchema.nullable(),
  /** 합동 판의 목표 조각 수. 합동이 아니면 0 */
  jointTotal: z.number(),
  /** 분대가 지금까지 채운 조각 — 남이 채운 것이 내 화면에도 보여야 협동이다 */
  jointDone: z.number(),
});

export const snapshotSchema = z.object({
  type: z.literal("snapshot"),
  /** 스냅샷 순번. 늦게 도착한 스냅샷을 버리는 데 쓴다 */
  seq: z.number(),
  runId: z.string(),
  status: z.enum(["running", "cleared", "discharged", "disbanded"]),
  day: z.number(),
  totalDays: z.number(),
  phase: z.object({
    id: phaseIdSchema,
    index: z.number(),
    label: z.string(),
    clock: z.string(),
    elapsedMs: z.number(),
    durationMs: z.number(),
    /** 0보다 크면 하달 창이 열려 있고 시간대 타이머가 멈춰 있다 */
    delegationWindowMsLeft: z.number(),
  }),
  weather: z.object({
    band: tempBandSchema,
    label: z.string(),
    feelsLike: z.number(),
    /** 14.0 D-10 폭우. 밴드로는 표현되지 않는 악천후라 따로 보낸다 */
    rain: z.boolean(),
  }),
  discipline: z.object({ value: z.number(), band: z.string() }),
  /**
   * 12.0 간부 신뢰도 3트랙 (DISC-02).
   *
   * **예전엔 스냅샷에 아예 없었다** — 승급 점수의 절반(`trustBonus`)이 네트워크에
   * 실리지 않아 `bestOfficer`/`strictestOfficer`(sim `discipline.ts`)가 죽은
   * 코드였다(WORKORDER.md E단계 감사 목록 3). 값 범위는 sim `discipline.json`
   * `officers.max`(0~100)를 따른다.
   */
  trust: z.object({
    platoonLeader: z.number(),
    assistant: z.number(),
    sergeantMajor: z.number(),
  }),
  /** 8.0 무전 상태 — 미니맵 마커·핑 게이팅의 근거 (§7.1.3) */
  radio: radioStateSchema,
  supply: z.object({
    points: z.number(),
    isSupplyDay: z.boolean(),
    /** 행정병이 제출해둔 청구서 */
    pendingClaim: z.array(z.string()),
  }),
  reliefsRemaining: z.number(),
  leaderId: z.string().nullable(),
  members: z.array(memberViewSchema),
  quests: z.array(questViewSchema),
  lastJudgement: z
    .object({
      day: z.number(),
      passed: z.boolean(),
      failedAt: z.enum(["A", "B", "C", "D"]).nullable(),
      requiredDone: z.number(),
      requiredTotal: z.number(),
    })
    .nullable(),
});
export type Snapshot = z.infer<typeof snapshotSchema>;

/** 서버가 흘려보내는 사건. sim의 Effect를 그대로 태우지 않고 표시용으로 좁힌다. */
export const serverEventSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("phaseStarted"), phase: phaseIdSchema, day: z.number() }),
  z.object({ type: z.literal("phaseEnded"), phase: phaseIdSchema, lockedCount: z.number() }),
  z.object({ type: z.literal("weatherRolled"), band: tempBandSchema, label: z.string() }),
  z.object({ type: z.literal("surpriseRaised"), questId: z.string(), label: z.string() }),
  z.object({
    type: z.literal("dayJudged"),
    day: z.number(),
    passed: z.boolean(),
    failedAt: z.enum(["A", "B", "C", "D"]).nullable(),
    /** 이 판정에서 구제권을 몇 건 썼는가 — 0이면 구제 없이 통과했다는 뜻이다 */
    reliefsUsed: z.number(),
    /** 판정 **후** 남은 구제권. 0이면 다음 미달은 곧바로 런 종료다 */
    reliefsRemaining: z.number(),
  }),
  z.object({ type: z.literal("disciplineChanged"), to: z.number(), band: z.string() }),
  z.object({ type: z.literal("memberEvacuated"), memberId: z.string(), absorbed: z.boolean() }),
  z.object({ type: z.literal("memberReturned"), memberId: z.string(), asRecruit: z.boolean() }),
  z.object({ type: z.literal("memberLeft"), memberId: z.string() }),
  z.object({ type: z.literal("frostbitten"), memberId: z.string() }),
  z.object({
    type: z.literal("frostbiteRelieved"),
    memberId: z.string(),
    /** 해제는 의무병만 할 수 있다 — 누가 풀었는지가 곧 근거다 (5.0) */
    byId: z.string(),
  }),
  z.object({ type: z.literal("radioChanged"), radioState: radioStateSchema }),
  z.object({ type: z.literal("forcedSleep"), memberId: z.string() }),
  z.object({
    type: z.literal("supplyClaimed"),
    day: z.number(),
    items: z.array(z.string()),
    pointsLeft: z.number(),
  }),
  z.object({
    type: z.literal("rankReviewed"),
    day: z.number(),
    isRetry: z.boolean(),
    require: z.number(),
    /** 점수 내역은 전원에게 공개된다 — 무임승차가 드러나는 것이 압력 장치다 (13.1) */
    outcomes: z.array(
      z.object({
        memberId: z.string(),
        promoted: z.boolean(),
        from: rankSchema,
        to: rankSchema,
        score: z.number(),
        require: z.number(),
        /**
         * 저녁 개인정비를 대화에 쓴 선택이 심사에 반영된 몫 (`score`에 이미 합산됨).
         *
         * 계산식(`ranks.ts` trustBonus = min(trustMax, round(신뢰도 합 / trustDivisor)))은
         * 서버 소관이라 클라에서 재계산하지 않는다 — 심사 시점 값을 그대로 실어
         * `score = 복무점수 + trustBonus`로 승급 화면이 분해해 보여준다(WORKORDER.md E-2).
         */
        trustBonus: z.number(),
      }),
    ),
  }),
  z.object({ type: z.literal("sleepSettled"), guardIds: z.array(z.string()) }),
  z.object({ type: z.literal("choreDelegated"), fromId: z.string(), toId: z.string(), questId: z.string() }),
  z.object({ type: z.literal("choreVetoed"), memberId: z.string(), questId: z.string() }),
  z.object({ type: z.literal("choreReassigned"), toId: z.string(), questId: z.string() }),
  /** 하달 거부 — 되살린 침묵 판정. reason은 `delegationRefusalSchema` 9종 */
  z.object({
    type: z.literal("delegationRefused"),
    reason: delegationRefusalSchema,
    questId: z.string(),
  }),
  /** 컨디션 붕괴 임계 — 되살린 침묵 판정. stat은 `conditionCriticalStatSchema` 3종 */
  z.object({
    type: z.literal("conditionCritical"),
    memberId: z.string(),
    stat: conditionCriticalStatSchema,
  }),
  z.object({ type: z.literal("hiddenUnlocked"), id: z.string(), label: z.string() }),
  z.object({ type: z.literal("runEnded"), status: z.string() }),
  /**
   * B-2 위기 시작 — 화면이 결과보다 먼저 말한다: "OOO 쓰러졌다 — N초 안에 가라."
   * 미니맵 마커는 스냅샷의 `crisisStat`/`crisisMsLeft`가 대신하고, 이 이벤트는
   * 토스트 전용이다.
   */
  z.object({
    type: z.literal("crisisStarted"),
    memberId: z.string(),
    stat: crisisStatSchema,
    crisisMs: z.number(),
  }),
  /**
   * B-2 구조 성공. 실패(시간 만료)는 새 이벤트를 만들지 않는다 — 기존
   * `memberEvacuated`가 그 자리를 그대로 대신한다(긴장을 물타기하지 않는다).
   */
  z.object({
    type: z.literal("crisisRescued"),
    memberId: z.string(),
    rescuerId: z.string(),
    stat: crisisStatSchema,
  }),
  z.object({
    type: z.literal("quickCommand"),
    memberId: z.string(),
    command: quickCommandSchema,
    zone: zoneSchema,
  }),
  z.object({ type: z.literal("chat"), memberId: z.string(), text: z.string(), viaRadio: z.boolean() }),
  z.object({ type: z.literal("log"), message: z.string() }),
]);
export type ServerEvent = z.infer<typeof serverEventSchema>;

export const lobbyStateSchema = z.object({
  type: z.literal("lobby"),
  code: z.string(),
  started: z.boolean(),
  hostId: z.string(),
  seats: z.array(
    z.object({
      role: roleSchema,
      memberId: z.string().nullable(),
      name: z.string().nullable(),
      ready: z.boolean(),
    }),
  ),
});
export type LobbyState = z.infer<typeof lobbyStateSchema>;

export const serverMessageSchema = z.discriminatedUnion("type", [
  z.object({
    type: z.literal("welcome"),
    protocolVersion: z.number(),
    memberId: z.string(),
    code: z.string(),
  }),
  lobbyStateSchema,
  snapshotSchema,
  z.object({ type: z.literal("events"), items: z.array(serverEventSchema) }),
  z.object({ type: z.literal("error"), code: z.string(), message: z.string() }),
]);
export type ServerMessage = z.infer<typeof serverMessageSchema>;

/* -------------------------------------------------------------- HTTP API */

export const createRoomRequestSchema = z.object({
  name: z.string().min(1).max(12),
  role: roleSchema,
  difficulty: z.enum(["regular", "relaxed"]).default("regular"),
  season: z.enum(["cold", "hot", "random"]).default("random"),
});
export type CreateRoomRequest = z.infer<typeof createRoomRequestSchema>;

export const joinRoomRequestSchema = z.object({
  name: z.string().min(1).max(12),
  role: roleSchema,
});
export type JoinRoomRequest = z.infer<typeof joinRoomRequestSchema>;

export const sessionSchema = z.object({
  code: z.string(),
  memberId: z.string(),
  /** WS 접속에 쓰는 단기 토큰. 로비에서 발급하고 서버가 검증한다 (ARCH-02 핸드오프) */
  token: z.string(),
});
export type Session = z.infer<typeof sessionSchema>;

export function parseIntent(raw: unknown): Intent | null {
  const result = intentSchema.safeParse(raw);
  return result.success ? result.data : null;
}

export function parseServerMessage(raw: unknown): ServerMessage | null {
  const result = serverMessageSchema.safeParse(raw);
  return result.success ? result.data : null;
}
