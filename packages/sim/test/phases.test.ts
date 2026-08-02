import { describe, expect, it } from "vitest";
import { PHASES, phaseAt, step, type Quest, type RunState } from "../src/index.js";
import { SECOND, beginDay as begin, fullSquad, playDays } from "./helpers.js";

function tick(state: RunState, ms: number): RunState {
  return step(state, { type: "tick", elapsedMs: ms }).state;
}

function makeQuest(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q1",
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId: "p1",
    required: true,
    phase: "reveille",
    zone: "Z01",
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    workMs: 10 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: null,
    grade: null,
    ...overrides,
  };
}

describe("TIME-01 시간대 엔진", () => {
  it("하루는 6칸이다", () => {
    expect(PHASES).toHaveLength(6);
    expect(PHASES.map((p) => p.id)).toEqual([
      "reveille",
      "morning",
      "lunch",
      "afternoon",
      "personal",
      "rollcall",
    ]);
  });

  it("칸이 끝나면 다음 칸으로 넘어간다", () => {
    let state = begin(fullSquad());
    expect(state.phaseIndex).toBe(0);
    state = tick(state, 60 * SECOND);
    expect(state.phaseIndex).toBe(1);
    expect(state.phaseElapsedMs).toBe(0);
  });

  it("6칸이 모두 끝나면 다음 날이 온다", () => {
    const state = playDays(fullSquad(), 1);
    expect(state.day).toBe(2);
    expect(state.phaseIndex).toBe(0);
  });

  it("시간대가 끝나면 미완료 퀘스트는 잠긴다 — 만회 경로는 없다", () => {
    let state = begin(fullSquad());
    state.quests = [makeQuest(), makeQuest({ id: "q2", status: "done" })];

    state = tick(state, 60 * SECOND);

    expect(state.quests.find((q) => q.id === "q1")?.status).toBe("locked");
    expect(state.quests.find((q) => q.id === "q2")?.status).toBe("done");
  });

  it("잠금 이벤트에 잠긴 퀘스트 id가 실린다", () => {
    let state = begin(fullSquad());
    state.quests = [makeQuest()];
    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    const ended = result.effects.find((e) => e.type === "phaseEnded");
    expect(ended).toMatchObject({ phase: "reveille", lockedQuestIds: ["q1"] });
  });

  it("스킵 투표로 아낀 시간은 다음 칸으로 이월된다", () => {
    let state = begin(fullSquad());
    state = tick(state, 20 * SECOND);
    state = step(state, { type: "skipPhase" }).state;

    expect(state.phaseIndex).toBe(1);
    // 기본 60초 + 이월 40초
    expect(state.phaseDurationMs).toBe(100 * SECOND);
    expect(state.carryoverMs).toBe(0);
  });

  it("이월분은 한 칸만 늘리고 그 다음 칸으로 넘어가지 않는다", () => {
    let state = begin(fullSquad());
    state = step(state, { type: "skipPhase" }).state;
    // 오전 일과는 60초 이월 + 20초 하달 창(타이머 정지)이므로 140초를 흘려야 넘어간다
    state = tick(state, 140 * SECOND);
    expect(state.phaseIndex).toBe(2);
    expect(state.phaseDurationMs).toBe(60 * SECOND);
  });

  it("한 번의 tick이 여러 칸을 넘어가도 순서대로 처리된다", () => {
    const state = begin(fullSquad());
    const result = step(state, { type: "tick", elapsedMs: 150 * SECOND });
    const started = result.effects
      .filter((e) => e.type === "phaseStarted")
      .map((e) => (e.type === "phaseStarted" ? e.phase : ""));
    expect(started).toEqual(["morning", "lunch"]);
    expect(result.state.phaseIndex).toBe(2);
    expect(result.state.phaseElapsedMs).toBe(30 * SECOND);
  });

  it("18일차가 끝나면 런이 종료된다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
    expect(state.day).toBe(18);
  });
});

describe("이동과 상호작용", () => {
  it("이동 중에는 퀘스트를 진행할 수 없다", () => {
    let state = begin(fullSquad());
    state.quests = [makeQuest({ zone: "Z08" })];

    state = step(state, { type: "move", memberId: "p1", to: "Z08" }).state;
    state = step(state, {
      type: "work",
      memberId: "p1",
      questId: "q1",
      deltaMs: 5 * SECOND,
    }).state;
    expect(state.quests[0]?.workedMs).toBe(0);

    state = tick(state, 30 * SECOND);
    state = step(state, {
      type: "work",
      memberId: "p1",
      questId: "q1",
      deltaMs: 5 * SECOND,
    }).state;
    expect(state.quests[0]?.workedMs).toBe(5 * SECOND);
  });

  it("소요를 채우면 완료된다", () => {
    let state = begin(fullSquad());
    state.quests = [makeQuest()];
    state = step(state, {
      type: "work",
      memberId: "p1",
      questId: "q1",
      deltaMs: 10 * SECOND,
    }).state;
    expect(state.quests[0]?.status).toBe("done");
  });

  it("담당자가 아니면 남의 보직 퀘스트를 진행할 수 없다", () => {
    let state = begin(fullSquad());
    state.quests = [makeQuest()];
    state = step(state, {
      type: "work",
      memberId: "p2",
      questId: "q1",
      deltaMs: 10 * SECOND,
    }).state;
    expect(state.quests[0]?.workedMs).toBe(0);
  });

  it("합동 퀘스트는 인원이 모자라면 게이지가 차오르지 않는다", () => {
    let state = begin(fullSquad());
    state.quests = [
      makeQuest({ kind: "joint", ownerId: null, minActors: 2, zone: "Z08" }),
    ];

    // p1만 창고에 있다
    state = step(state, { type: "move", memberId: "p1", to: "Z08" }).state;
    state = tick(state, 15 * SECOND);
    state = step(state, {
      type: "work",
      memberId: "p1",
      questId: "q1",
      deltaMs: 5 * SECOND,
    }).state;
    expect(state.quests[0]?.workedMs).toBe(0);

    // p2가 합류하면 진행된다
    state = step(state, { type: "move", memberId: "p2", to: "Z08" }).state;
    state = tick(state, 15 * SECOND);
    state = step(state, {
      type: "work",
      memberId: "p1",
      questId: "q1",
      deltaMs: 5 * SECOND,
    }).state;
    expect(state.quests[0]?.workedMs).toBe(5 * SECOND);
  });
});

describe("편성", () => {
  it("빈 보직은 NPC로 채워지고 처음부터 빈 자리로 표시된다", () => {
    const state = fullSquad({
      members: [{ id: "p1", name: "김소총", role: "rifle" }],
    });
    expect(state.members).toHaveLength(4);
    expect(state.members.filter((m) => m.presence === "npcVacant")).toHaveLength(3);
  });

  it("보직 중복은 허용되지 않는다", () => {
    expect(() =>
      fullSquad({
        members: [
          { id: "p1", name: "김소총", role: "rifle" },
          { id: "p2", name: "이소총", role: "rifle" },
        ],
      }),
    ).toThrow();
  });

  it("시간대 정의는 인덱스로 조회된다", () => {
    expect(phaseAt(5).id).toBe("rollcall");
    expect(() => phaseAt(6)).toThrow();
  });
});
