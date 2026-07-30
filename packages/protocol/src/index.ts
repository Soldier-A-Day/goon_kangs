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
export const zoneSchema = z.enum([
  "barracks",
  "drillGround",
  "storage",
  "messHall",
  "guardPost",
  "trainingField",
  "infirmary",
  "boilerRoom",
]);
export const questKindSchema = z.enum(["role", "chore", "joint", "surprise", "hidden"]);
export const questStatusSchema = z.enum(["pending", "active", "done", "failed", "locked"]);
export const presenceSchema = z.enum([
  "player",
  "npcEvac",
  "npcLeave",
  "npcVacant",
  "evacuated",
]);

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

/* ------------------------------------------------------- 클라이언트 → 서버 */

/**
 * 클라이언트는 **의도만** 보낸다. 판정도, 진척 계산도 서버가 한다.
 * 예컨대 `interact`는 "이 퀘스트를 붙잡고 있다"는 신고이며, 얼마나 진행됐는지는 서버가 센다.
 */
export const intentSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("ready"), value: z.boolean() }),
  z.object({ type: z.literal("move"), to: zoneSchema }),
  z.object({ type: z.literal("interact"), questId: z.string(), active: z.boolean() }),
  z.object({ type: z.literal("quickCommand"), command: quickCommandSchema }),
  z.object({ type: z.literal("chat"), text: z.string().max(200) }),
  z.object({ type: z.literal("voteSkip"), value: z.boolean() }),
  z.object({
    type: z.literal("delegateChore"),
    toId: z.string(),
    questId: z.string(),
  }),
  z.object({ type: z.literal("vetoChore"), questId: z.string() }),
  z.object({
    type: z.literal("leaderReassign"),
    questId: z.string(),
    toId: z.string(),
  }),
  z.object({ type: z.literal("voteLeader"), candidateId: z.string() }),
  /** 11.0 청구서 작성 — 서버가 행정병인지 확인한다 */
  z.object({ type: z.literal("fileClaim"), items: z.array(z.string()).max(12) }),
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
});

export const questViewSchema = z.object({
  id: z.string(),
  kind: questKindSchema,
  label: z.string(),
  ownerId: z.string().nullable(),
  required: z.boolean(),
  phase: phaseIdSchema,
  zone: zoneSchema,
  /** 0~1. 남은 ms가 아니라 비율만 준다 — 클라가 완료 시점을 스스로 정하지 못하게 */
  progress: z.number(),
  status: questStatusSchema,
  minActors: z.number(),
  delegatedFrom: z.string().nullable(),
  training: z.string().nullable(),
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
  }),
  discipline: z.object({ value: z.number(), band: z.string() }),
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
  }),
  z.object({ type: z.literal("disciplineChanged"), to: z.number(), band: z.string() }),
  z.object({ type: z.literal("memberEvacuated"), memberId: z.string(), absorbed: z.boolean() }),
  z.object({ type: z.literal("memberReturned"), memberId: z.string(), asRecruit: z.boolean() }),
  z.object({ type: z.literal("memberLeft"), memberId: z.string() }),
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
      }),
    ),
  }),
  z.object({ type: z.literal("sleepSettled"), guardIds: z.array(z.string()) }),
  z.object({ type: z.literal("choreDelegated"), fromId: z.string(), toId: z.string(), questId: z.string() }),
  z.object({ type: z.literal("choreVetoed"), memberId: z.string(), questId: z.string() }),
  z.object({ type: z.literal("choreReassigned"), toId: z.string(), questId: z.string() }),
  z.object({ type: z.literal("hiddenUnlocked"), id: z.string(), label: z.string() }),
  z.object({ type: z.literal("runEnded"), status: z.string() }),
  z.object({
    type: z.literal("quickCommand"),
    memberId: z.string(),
    command: quickCommandSchema,
    zone: zoneSchema,
  }),
  z.object({ type: z.literal("chat"), memberId: z.string(), text: z.string(), radio: z.boolean() }),
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
