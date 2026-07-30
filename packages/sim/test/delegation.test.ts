import { describe, expect, it } from "vitest";
import {
  RECEIVE_LIMIT,
  canDelegate,
  consecutiveDelegationDays,
  delegationAllowance,
  delegationLedgerSummary,
  isDelegationWindow,
  mentalRecoveryMultiplier,
  step,
  type Quest,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, fullSquad } from "./helpers.js";

/** 오전 일과(하달 창이 열리는 칸)까지 진행시킨다 */
function atMorning(): RunState {
  let state = beginDay(fullSquad());
  state = step(state, { type: "skipPhase" }).state;
  return state;
}

function chore(state: RunState, ownerId: string, id = "c1"): Quest {
  const quest: Quest = {
    id,
    kind: "chore",
    label: "생활관 바닥 청소",
    training: null,
    ownerId,
    required: true,
    phase: "morning",
    zone: "barracks",
    workMs: 15 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
  };
  state.quests.push(quest);
  return quest;
}

function promote(state: RunState, id: string, rank: RunState["members"][number]["rank"]): void {
  const member = state.members.find((m) => m.id === id);
  if (!member) throw new Error(`분대원 없음: ${id}`);
  member.rank = rank;
}

describe("QST-04 하달 창", () => {
  it("오전·오후 일과에만 열린다", () => {
    const reveille = beginDay(fullSquad());
    expect(isDelegationWindow(reveille)).toBe(false);

    expect(isDelegationWindow(atMorning())).toBe(true);
  });

  it("창이 열려 있는 동안 시간대 타이머가 멈춘다", () => {
    let state = atMorning();
    expect(state.phaseElapsedMs).toBe(0);

    state = step(state, { type: "tick", elapsedMs: 10 * SECOND }).state;
    expect(state.phaseElapsedMs).toBe(0);
    expect(state.delegationWindowMsLeft).toBe(10 * SECOND);

    state = step(state, { type: "tick", elapsedMs: 15 * SECOND }).state;
    // 20초를 다 쓰고 남은 5초부터 일과 시간이 흐른다
    expect(state.delegationWindowMsLeft).toBe(0);
    expect(state.phaseElapsedMs).toBe(5 * SECOND);
  });
});

describe("QST-04 하달 규칙", () => {
  it("이병은 하달할 수 없다 — 전원 이병인 D-01~03은 시스템이 잠겨 있다", () => {
    const state = atMorning();
    chore(state, "p1");
    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "rankTooLow",
    });
  });

  it("계급차 1단계는 1건, 2단계 이상은 2건", () => {
    const state = fullSquad();
    promote(state, "p1", "sergeant");
    promote(state, "p2", "corporal");
    const [sergeant, corporal, privateMember] = [
      state.members.find((m) => m.id === "p1"),
      state.members.find((m) => m.id === "p2"),
      state.members.find((m) => m.id === "p3"),
    ];
    if (!sergeant || !corporal || !privateMember) throw new Error("분대원 없음");

    expect(delegationAllowance(corporal, privateMember)).toBe(2);
    expect(delegationAllowance(sergeant, corporal)).toBe(1);
    expect(delegationAllowance(privateMember, sergeant)).toBe(0);
  });

  it("공통 일과만 넘길 수 있다 — 보직 전용은 넘겨도 수행이 불가능하다", () => {
    const state = atMorning();
    promote(state, "p1", "corporal");
    const roleQuest = state.quests.find((q) => q.kind === "role" && q.ownerId === "p1");
    if (!roleQuest) throw new Error("보직 퀘스트 없음");

    expect(canDelegate(state, "p1", "p2", roleQuest.id)).toEqual({
      ok: false,
      reason: "notChore",
    });
  });

  it("하달하면 소유자가 바뀌고 넘긴 사람이 기록된다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    const quest = state.quests.find((q) => q.id === "c1");
    expect(quest?.ownerId).toBe("p2");
    expect(quest?.delegatedFrom).toBe("p1");
    expect(state.members.find((m) => m.id === "p2")?.choresReceived).toBe(1);
  });

  it("하달자는 군기 −2를 문다 — 남용하면 스스로 조건 C에 접근한다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");
    const before = state.discipline;

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    expect(state.discipline).toBe(before - 2);
  });

  it("재하달은 금지된다 — 연쇄 폭탄을 원천 차단한다", () => {
    let state = atMorning();
    promote(state, "p1", "sergeant");
    promote(state, "p2", "corporal");
    chore(state, "p1");

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    expect(canDelegate(state, "p2", "p3", "c1")).toEqual({
      ok: false,
      reason: "alreadyDelegated",
    });
  });

  it("받는 쪽 보유 상한은 2건 — 4인 편성에서 결정적인 안전장치다", () => {
    let state = atMorning();
    promote(state, "p1", "sergeant");
    chore(state, "p1", "c1");
    chore(state, "p1", "c2");
    chore(state, "p1", "c3");

    const receiver = state.members.find((m) => m.id === "p2");
    if (!receiver) throw new Error("분대원 없음");
    receiver.choresReceived = RECEIVE_LIMIT;

    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "receiverLimit",
    });
  });

  it("계급차 상한을 넘겨 하달하면 거절된다", () => {
    let state = atMorning();
    promote(state, "p1", "pfc"); // 이병과 1단계 차 → 1건
    chore(state, "p1", "c1");
    chore(state, "p1", "c2");

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    const result = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c2",
    });
    expect(result.effects.some((e) => e.type === "delegationRefused")).toBe(true);
    expect(result.state.quests.find((q) => q.id === "c2")?.ownerId).toBe("p1");
  });

  it("하달 창 밖에서는 넘길 수 없다", () => {
    const state = beginDay(fullSquad());
    promote(state, "p1", "corporal");
    chore(state, "p1");
    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "notDelegationWindow",
    });
  });
});

describe("6.2 비용 구조", () => {
  it("대행 점수는 수행자에게 간다 — 하달자는 시간을 사고 승급 점수를 판다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;
    state = step(state, { type: "tick", elapsedMs: 20 * SECOND }).state;
    state = step(state, {
      type: "work",
      memberId: "p2",
      questId: "c1",
      deltaMs: 15 * SECOND,
    }).state;

    expect(state.quests.find((q) => q.id === "c1")?.status).toBe("done");
    expect(state.members.find((m) => m.id === "p2")?.serviceScore).toBe(2);
    expect(state.members.find((m) => m.id === "p1")?.serviceScore).toBe(0);
  });
});

describe("QST-05 거부권과 분대장 개입", () => {
  it("거부하면 일과가 하달자에게 돌아가고 양쪽이 대가를 치른다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");
    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    const trustBefore = state.trust.assistant;
    state = step(state, { type: "vetoChore", memberId: "p2", questId: "c1" }).state;

    expect(state.quests.find((q) => q.id === "c1")?.ownerId).toBe("p1");
    // 거부 −3 에 상병 배율 1.4가 곱해진다 (표 13-2 감점 배율)
    expect(state.members.find((m) => m.id === "p1")?.serviceScore).toBe(-4);
    expect(state.trust.assistant).toBe(trustBefore - 5);
    expect(state.members.find((m) => m.id === "p2")?.vetoUsedToday).toBe(true);
  });

  it("거부권은 하루 한 번뿐이다", () => {
    let state = atMorning();
    promote(state, "p1", "sergeant");
    chore(state, "p1", "c1");
    chore(state, "p1", "c2");

    for (const id of ["c1", "c2"]) {
      state = step(state, {
        type: "delegateChore",
        fromId: "p1",
        toId: "p2",
        questId: id,
      }).state;
    }

    state = step(state, { type: "vetoChore", memberId: "p2", questId: "c1" }).state;
    state = step(state, { type: "vetoChore", memberId: "p2", questId: "c2" }).state;

    expect(state.quests.find((q) => q.id === "c2")?.ownerId).toBe("p2");
  });

  it("분대장은 계급과 무관하게 배정을 되돌린다 — 이병 분대장도 가능하다", () => {
    let state = atMorning();
    state.leaderId = "p4"; // 이병 분대장
    promote(state, "p1", "sergeant");
    chore(state, "p1");

    state = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    }).state;

    state = step(state, {
      type: "leaderReassign",
      leaderId: "p4",
      questId: "c1",
      toId: "p3",
    }).state;

    expect(state.quests.find((q) => q.id === "c1")?.ownerId).toBe("p3");
    expect(state.leaderOverridePhase).toBe(state.phaseIndex);
  });

  it("분대장 개입은 시간대당 1회다", () => {
    let state = atMorning();
    state.leaderId = "p4";
    chore(state, "p1", "c1");
    chore(state, "p1", "c2");

    state = step(state, {
      type: "leaderReassign",
      leaderId: "p4",
      questId: "c1",
      toId: "p2",
    }).state;
    state = step(state, {
      type: "leaderReassign",
      leaderId: "p4",
      questId: "c2",
      toId: "p2",
    }).state;

    expect(state.quests.find((q) => q.id === "c2")?.ownerId).toBe("p1");
  });
});

describe("남용 억제와 장부", () => {
  it("3일 연속 하달을 받으면 정신력 회복이 −30% 된다", () => {
    const state = fullSquad();
    state.day = 3;
    const member = state.members.find((m) => m.id === "p2");
    if (!member) throw new Error("분대원 없음");

    expect(mentalRecoveryMultiplier(state, member)).toBe(1);

    for (const day of [1, 2, 3]) {
      state.ledger.push({
        day,
        phaseIndex: 1,
        fromId: "p1",
        toId: "p2",
        questId: `c${day}`,
        outcome: "accepted",
      });
    }

    expect(consecutiveDelegationDays(state, "p2")).toBe(3);
    expect(mentalRecoveryMultiplier(state, member)).toBeCloseTo(0.7, 5);
  });

  it("하루라도 끊기면 연속이 아니다", () => {
    const state = fullSquad();
    state.day = 3;
    state.ledger.push(
      { day: 1, phaseIndex: 1, fromId: "p1", toId: "p2", questId: "a", outcome: "accepted" },
      { day: 3, phaseIndex: 1, fromId: "p1", toId: "p2", questId: "b", outcome: "accepted" },
    );
    expect(consecutiveDelegationDays(state, "p2")).toBe(1);
  });

  it("장부에 누가 몇 건을 넘겼고 받았는지 남는다", () => {
    let state = atMorning();
    promote(state, "p1", "sergeant");
    chore(state, "p1", "c1");
    chore(state, "p1", "c2");

    for (const id of ["c1", "c2"]) {
      state = step(state, {
        type: "delegateChore",
        fromId: "p1",
        toId: "p2",
        questId: id,
      }).state;
    }

    const summary = delegationLedgerSummary(state);
    expect(summary.find((s) => s.memberId === "p1")?.given).toBe(2);
    expect(summary.find((s) => s.memberId === "p2")?.received).toBe(2);
  });
});
