import { describe, expect, it } from "vitest";
import {
  PROXY_ABSORB_LIMIT,
  evacuate,
  isFlawless,
  planFor,
  proxyCount,
  step,
  type Effect,
  type Quest,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function required(state: RunState, ownerId: string, id: string): Quest {
  const quest: Quest = {
    id,
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId,
    required: true,
    phase: "afternoon",
    zone: "barracks",
    workMs: 10 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
  };
  state.quests.push(quest);
  return quest;
}

function withOnly(state: RunState, quests: readonly Quest[]): RunState {
  state.quests = [...quests];
  return state;
}

function collapse(state: RunState, memberId: string): { state: RunState; effects: Effect[] } {
  const effects: Effect[] = [];
  evacuate(state, memberId, effects);
  return { state, effects };
}

describe("JDG-03 인수 한도", () => {
  it("잔여 필수가 2건 이하면 대리가 인수한다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    required(state, "p1", "a");
    required(state, "p1", "b");

    const { effects } = collapse(state, "p1");

    expect(effects.some((e) => e.type === "memberEvacuated" && e.absorbed)).toBe(true);
    expect(state.quests.every((q) => q.status === "done")).toBe(true);
  });

  it("3건 이상 남아 있으면 인수하지 못한다 — 대리는 구제 장치이지 면제 장치가 아니다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    required(state, "p1", "a");
    required(state, "p1", "b");
    required(state, "p1", "c");

    const { effects } = collapse(state, "p1");

    expect(effects.some((e) => e.type === "memberEvacuated" && !e.absorbed)).toBe(true);
    expect(state.quests.every((q) => q.status === "pending")).toBe(true);
  });

  it("인수 한도는 2건이다", () => {
    expect(PROXY_ABSORB_LIMIT).toBe(2);
  });

  it("오전에 쓰러지면 분대가 죽고, 늦게 쓰러지면 구제된다", () => {
    const early = withOnly(beginDay(fullSquad()), []);
    for (const id of ["a", "b", "c", "d"]) required(early, "p1", id);
    collapse(early, "p1");
    expect(early.quests.some((q) => q.status === "pending")).toBe(true);

    const late = withOnly(beginDay(fullSquad()), []);
    required(late, "p1", "a");
    collapse(late, "p1");
    expect(late.quests.every((q) => q.status === "done")).toBe(true);
  });

  it("이미 잠긴 필수는 잔여로 세지 않는다 — 그건 이미 잃은 것이다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    const locked = required(state, "p1", "a");
    locked.status = "locked";
    required(state, "p1", "b");
    required(state, "p1", "c");

    const { effects } = collapse(state, "p1");
    expect(effects.some((e) => e.type === "memberEvacuated" && e.absorbed)).toBe(true);
  });
});

describe("후송의 대가", () => {
  it("후송 즉시 군기 −15", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    const before = state.discipline;
    collapse(state, "p1");
    expect(state.discipline).toBe(before - 15);
  });

  it("후송자는 당일 밤 조작 불가 상태가 된다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    collapse(state, "p1");
    expect(state.members.find((m) => m.id === "p1")?.presence).toBe("evacuated");
    expect(proxyCount(state)).toBe(1);
  });

  it("대리가 2명 이상이면 군기 상한이 60으로 떨어진다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    state.discipline = 90;
    collapse(state, "p1");
    collapse(state, "p2");
    expect(state.discipline).toBeLessThanOrEqual(60);
  });

  it("후송 기록이 남아 모범 전역이 불가능해진다", () => {
    const state = withOnly(beginDay(fullSquad()), []);
    expect(isFlawless(state)).toBe(true);
    collapse(state, "p1");
    expect(isFlawless(state)).toBe(false);
  });
});

describe("복귀 신병", () => {
  it("다음 날 아침 복귀하되 축적을 전부 잃는다", () => {
    let state = beginDay(fullSquad());
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.rank = "sergeant";
    member.serviceScore = 120;

    state = completeRequired(state);
    collapse(state, "p1");
    state = completeRequired(state);

    let guard = 0;
    while (state.status === "running" && state.day === 1 && guard++ < 100) {
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    const returned = state.members.find((m) => m.id === "p1");
    expect(returned?.presence).toBe("player");
    expect(returned?.rank).toBe("private");
    expect(returned?.serviceScore).toBe(0);
    expect(returned?.rehabDaysLeft).toBeGreaterThan(0);
  });

  it("재활 기간에는 본인 필수가 +1 된다", () => {
    const state = beginDay(fullSquad());
    state.day = 4;
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.rehabDaysLeft = 2;

    const rebuilt = step(state, { type: "beginDay" }).state;
    const mine = rebuilt.quests.filter((q) => q.required && q.ownerId === "p1");
    const others = rebuilt.quests.filter((q) => q.required && q.ownerId === "p2");

    expect(mine.length).toBe(planFor(4).required.total + 1);
    expect(others.length).toBe(planFor(4).required.total);
  });
});

describe("이탈과 재접속", () => {
  it("이탈 대리는 한도 없이 필수를 인수한다", () => {
    let state = withOnly(beginDay(fullSquad()), []);
    for (const id of ["a", "b", "c", "d", "e"]) required(state, "p1", id);

    state = step(state, { type: "leaveRun", memberId: "p1" }).state;

    expect(state.members.find((m) => m.id === "p1")?.presence).toBe("npcLeave");
    expect(state.quests.every((q) => q.status === "done")).toBe(true);
  });

  it("재접속하면 대리에서 즉시 복귀하고 축적은 그대로다", () => {
    let state = beginDay(fullSquad());
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.serviceScore = 40;

    state = step(state, { type: "leaveRun", memberId: "p1" }).state;
    state = step(state, { type: "rejoinRun", memberId: "p1" }).state;

    const back = state.members.find((m) => m.id === "p1");
    expect(back?.presence).toBe("player");
    expect(back?.serviceScore).toBe(40);
  });

  it("이탈로 실제 인원이 2명 미만이 되면 분대가 해체된다", () => {
    let state = beginDay(fullSquad());
    state = step(state, { type: "leaveRun", memberId: "p1" }).state;
    state = step(state, { type: "leaveRun", memberId: "p2" }).state;
    expect(state.status).toBe("running");

    state = step(state, { type: "leaveRun", memberId: "p3" }).state;
    expect(state.status).toBe("disbanded");
  });
});

describe("쓰러짐 감지", () => {
  it("체력이 0이 되면 그 자리에서 후송된다 — 점호를 기다리지 않는다", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.stamina = 1;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    expect(result.effects.some((e) => e.type === "memberEvacuated")).toBe(true);
  });

  it("열사병 2단계는 한 칸을 버티면 쓰러진다", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.hydration = 5;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    expect(result.effects.some((e) => e.type === "memberEvacuated")).toBe(true);
  });

  it("피로 100은 후송이 아니라 그 칸의 퀘스트 포기다", () => {
    let state = beginDay(fullSquad());
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.fatigue = 100;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    expect(result.effects.some((e) => e.type === "forcedSleep")).toBe(true);
    expect(result.state.members.find((m) => m.id === "p1")?.presence).toBe("player");
  });

  it("이상적인 런에서는 아무도 쓰러지지 않는다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
    expect(state.members.every((m) => m.evacuations === 0)).toBe(true);
  });
});
