import { describe, expect, it } from "vitest";
import { judgeDay, step, type Quest, type RunState } from "../src/index.js";
import { FULL_DAY, SECOND, beginDay, fullSquad, playDays, withQuests } from "./helpers.js";

function quest(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q",
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId: "p1",
    required: true,
    phase: "morning",
    zone: "barracks",
    workMs: 10 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    ...overrides,
  };
}

/**
 * 하루를 통째로 흘려보내 점호까지 도달시킨다.
 * 배정기가 만든 일과는 지우고 테스트가 준 퀘스트만 남긴다 — 판정 규칙만 보기 위해서다.
 */
function playDay(state: RunState, quests: readonly Quest[] = []): RunState {
  const started = withQuests(beginDay(state), quests);
  return step(started, { type: "tick", elapsedMs: FULL_DAY }).state;
}

describe("JDG-01 판정 조건", () => {
  it("필수를 전부 끝내면 통과한다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a", status: "done" }), quest({ id: "b", status: "done" })];
    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(true);
    expect(judgement.requiredDone).toBe(2);
    expect(judgement.failedAt).toBeNull();
  });

  it("선택 퀘스트 미완은 판정에 영향을 주지 않는다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a", status: "done" }), quest({ id: "b", required: false })];
    expect(judgeDay(state).passed).toBe(true);
  });

  it("조건 B — 합동 퀘스트는 70% 부분 성공까지 인정한다", () => {
    const state = fullSquad();
    state.quests = [
      quest({ id: "j", kind: "joint", ownerId: null, required: false, workedMs: 7 * SECOND }),
    ];
    expect(judgeDay(state).passed).toBe(true);

    state.quests = [
      quest({ id: "j", kind: "joint", ownerId: null, required: false, workedMs: 6.9 * SECOND }),
    ];
    expect(judgeDay(state).failedAt).toBe("B");
  });

  it("조건 C — 군기 40 미만이면 실패한다", () => {
    const state = fullSquad();
    state.discipline = 39;
    expect(judgeDay(state).failedAt).toBe("C");
    state.discipline = 40;
    expect(judgeDay(state).passed).toBe(true);
  });

  it("조건 D — 청결 20 미만이면 실패한다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.stats.hygiene = 19;
    expect(judgeDay(state).failedAt).toBe("D");
  });

  it("NPC 대리의 청결은 조건 D 대상이 아니다", () => {
    const state = fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] });
    for (const member of state.members) {
      if (member.presence === "npcVacant") member.stats.hygiene = 0;
    }
    expect(judgeDay(state).passed).toBe(true);
  });

  it("깨진 첫 조건만 지목한다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a" })];
    state.discipline = 0;
    state.reliefsRemaining = 0;
    expect(judgeDay(state).failedAt).toBe("A");
  });
});

describe("구제권 (총량 3회)", () => {
  it("미완료 필수 1건은 구제로 메워지고 구제권이 하나 줄어든다", () => {
    let state = fullSquad();
    state = playDay(state, [quest({ id: "a", status: "done" }), quest({ id: "b" })]);

    expect(state.status).toBe("running");
    expect(state.day).toBe(2);
    expect(state.reliefsRemaining).toBe(2);
    expect(state.judgements[0]?.reliefsUsed).toBe(1);
  });

  it("구제권이 없으면 필수 1건 미달로 런이 끝난다", () => {
    let state = fullSquad();
    state.reliefsRemaining = 0;
    state = playDay(state, [quest({ id: "a" })]);

    expect(state.status).toBe("discharged");
    expect(state.judgements[0]?.failedAt).toBe("A");
  });

  it("구제로도 못 메우면 구제권을 소모하지 않는다", () => {
    const state = fullSquad();
    state.quests = [quest({ id: "a" }), quest({ id: "b" }), quest({ id: "c" }), quest({ id: "d" })];
    const judgement = judgeDay(state);
    expect(judgement.passed).toBe(false);
    expect(judgement.reliefsUsed).toBe(0);
  });

  it("구제는 조건 A에만 쓰이고 군기는 구제하지 못한다", () => {
    const state = fullSquad();
    state.discipline = 10;
    expect(judgeDay(state).failedAt).toBe("C");
    expect(judgeDay(state).reliefsUsed).toBe(0);
  });
});

describe("난이도", () => {
  it("정규 — 1회 미달로 즉시 종료된다", () => {
    let state = fullSquad({ config: { difficulty: "regular" } });
    state.reliefsRemaining = 0;
    state = playDay(state, [quest({ id: "a" })]);
    expect(state.status).toBe("discharged");
  });

  it("완화 — 1차는 경고, 3차에 종료된다", () => {
    let state = fullSquad({ config: { difficulty: "relaxed" } });
    state.reliefsRemaining = 0;

    state = playDay(state, [quest({ id: "a" })]);
    expect(state.status).toBe("running");
    expect(state.warnings).toBe(1);

    state = playDay(state, [quest({ id: "b" })]);
    expect(state.status).toBe("running");
    expect(state.personalTimeRevoked).toBe(true);

    state = playDay(state, [quest({ id: "c" })]);
    expect(state.status).toBe("discharged");
    expect(state.warnings).toBe(3);
  });
});

describe("런 종료", () => {
  it("완화 난이도라도 마지막 날 판정을 놓치면 전역하지 못한다", () => {
    let state = fullSquad({ config: { difficulty: "relaxed" } });
    state.reliefsRemaining = 0;
    state.day = 18;
    state = playDay(state, [quest({ id: "a" })]);

    expect(state.status).toBe("discharged");
    expect(state.day).toBe(18);
    expect(state.warnings).toBe(1);
  });

  it("18일차 판정을 통과하면 전역한다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
    expect(state.judgements).toHaveLength(18);
    expect(state.judgements.every((j) => j.passed)).toBe(true);
  });

  it("실제 플레이어가 2명 미만이 되면 분대가 해체된다", () => {
    let state = fullSquad();
    for (const member of state.members.slice(1)) {
      member.presence = "npcLeave";
    }
    state = playDays(state, 1);
    expect(state.status).toBe("disbanded");
  });

  it("처음부터 1인으로 시작한 방은 해체 대상이 아니다", () => {
    let state = fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] });
    state = playDays(state, 1);
    expect(state.status).toBe("running");
  });

  it("런이 끝난 뒤의 이벤트는 상태를 바꾸지 않는다", () => {
    let state = fullSquad();
    state.reliefsRemaining = 0;
    state = playDay(state, [quest({ id: "a" })]);

    const after = step(state, { type: "tick", elapsedMs: FULL_DAY }).state;
    expect(after.day).toBe(state.day);
    expect(after.status).toBe("discharged");
  });
});
