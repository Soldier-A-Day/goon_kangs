import { describe, expect, it } from "vitest";
import {
  PROTOCOL_VERSION,
  intentSchema,
  parseIntent,
  parseServerMessage,
  snapshotSchema,
} from "../src/index.js";

describe("의도 스키마", () => {
  it("알려진 의도만 통과시킨다", () => {
    expect(parseIntent({ type: "move", to: "storage" })).toEqual({
      type: "move",
      to: "storage",
    });
    expect(parseIntent({ type: "move", to: "moon" })).toBeNull();
    expect(parseIntent({ type: "granadeAttack" })).toBeNull();
  });

  it("채팅 길이를 제한한다 — 서버가 아니라 스키마에서 먼저 막는다", () => {
    expect(parseIntent({ type: "chat", text: "집합" })).not.toBeNull();
    expect(parseIntent({ type: "chat", text: "가".repeat(201) })).toBeNull();
  });

  it("클라이언트는 진척도나 판정을 보낼 수 없다", () => {
    const keys = intentSchema.options.flatMap((option) =>
      Object.keys(option.shape as Record<string, unknown>),
    );
    expect(keys).not.toContain("progress");
    expect(keys).not.toContain("passed");
    expect(keys).not.toContain("workedMs");
  });
});

describe("스냅샷 스키마", () => {
  const base = {
    type: "snapshot" as const,
    seq: 1,
    runId: "r1",
    status: "running" as const,
    day: 1,
    totalDays: 18,
    phase: {
      id: "morning" as const,
      index: 1,
      label: "오전 일과",
      clock: "08:00",
      elapsedMs: 0,
      durationMs: 60000,
      delegationWindowMsLeft: 20000,
    },
    weather: { band: "normal" as const, label: "평시", feelsLike: 12 },
    discipline: { value: 60, band: "normal" },
    supply: { points: 12, isSupplyDay: false, pendingClaim: [] },
    reliefsRemaining: 3,
    leaderId: null,
    members: [],
    quests: [],
    lastJudgement: null,
  };

  it("정상 스냅샷을 통과시킨다", () => {
    expect(snapshotSchema.safeParse(base).success).toBe(true);
  });

  it("시드와 RNG 상태는 스냅샷에 존재하지 않는다 — 나가면 롤을 예측당한다", () => {
    const shape = Object.keys(snapshotSchema.shape);
    expect(shape).not.toContain("seed");
    expect(shape).not.toContain("rngState");

    const parsed = snapshotSchema.parse({ ...base, seed: 1234, rngState: { s: 1 } });
    expect(parsed).not.toHaveProperty("seed");
    expect(parsed).not.toHaveProperty("rngState");
  });

  it("퀘스트는 남은 시간이 아니라 진척 비율만 노출한다", () => {
    const questShape = Object.keys(snapshotSchema.shape.quests.element.shape);
    expect(questShape).toContain("progress");
    expect(questShape).not.toContain("workMs");
    expect(questShape).not.toContain("workedMs");
  });
});

describe("서버 메시지", () => {
  it("프로토콜 버전을 담은 welcome을 파싱한다", () => {
    const message = parseServerMessage({
      type: "welcome",
      protocolVersion: PROTOCOL_VERSION,
      memberId: "m1",
      code: "ABC123",
    });
    expect(message?.type).toBe("welcome");
  });

  it("모르는 메시지는 거른다", () => {
    expect(parseServerMessage({ type: "hack", payload: 1 })).toBeNull();
  });
});
