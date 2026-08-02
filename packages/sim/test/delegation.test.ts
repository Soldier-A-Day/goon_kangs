import { describe, expect, it } from "vitest";
import {
  RECEIVE_LIMIT,
  canDelegate,
  consecutiveDelegationDays,
  delegationAllowance,
  delegationLedgerSummary,
  isDelegationWindow,
  openDelegationWindow,
  mentalRecoveryMultiplier,
  step,
  type Quest,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, fullSquad } from "./helpers.js";

/**
 * 오전 일과(하달 창이 열리는 칸)까지 진행시킨다.
 *
 * 6.2가 "전원이 이병이면 시스템이 잠긴다"고 못박아서 **계급차가 없으면 창
 * 자체가 안 열린다.** 하달 테스트는 거의 다 창이 필요하므로 한 명을 상병으로
 * 올린 채로 시작하고, 잠금을 확인하는 테스트만 `promoted: false`를 준다.
 */
function atMorning(promoted = true): RunState {
  // **D-3부터 열린다** (표 14-1 `unlocks`). 예전에는 이 표가 문서였고 배정기가
  // 첫날부터 전부 냈는데, 지금은 규칙이라 D-1에서는 `locked`로 거절된다
  const squad = fullSquad();
  squad.day = 3;
  // 이 파일은 하달 창·거부권·재배정 로직을 시험한다 — F-1의 실시간 하한이
  // 우연히 끼어들지 않도록 충분히 큰 값으로 채워 둔다. 하한 자체는 `unlock.test.ts`가 잰다.
  squad.elapsedRealMs = Number.MAX_SAFE_INTEGER;
  let state = beginDay(squad);
  if (promoted) {
    const first = state.members[0];
    if (first) first.rank = "corporal";
  }
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
    zone: "Z01",
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
    workMs: 15 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: null,
    grade: null,
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
  it("이병은 하달할 수 없다", () => {
    // 창이 열려 있어도(누군가는 계급이 높다) 이병끼리는 못 넘긴다
    const state = atMorning();
    chore(state, "p2");
    expect(canDelegate(state, "p2", "p3", "c1")).toEqual({
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
    // F-2 — 받는 쪽이 이병이면 초행자 보호로 거절된다(receiverRookie). 이
    // 시험은 그 규칙이 아니라 하달 성사 자체를 보는 것이라 받는 쪽도 올려 둔다
    promote(state, "p2", "pfc");
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
    promote(state, "p2", "pfc"); // F-2 — 이병 수신 거절을 피해 하달 자체를 성사시킨다
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
    // F-2 — receiverLimit을 보는 시험이다. p2가 이병이면 receiverRookie가
    // 먼저 걸려 다른 사유로 거절되므로, 상한 자체를 보려면 이병을 벗어나야 한다
    promote(state, "p2", "pfc");
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
    // F-2 — 1단계 차(→1건) 자체는 유지하되, 받는 쪽을 이병에서 한 단계
    // 올려 둔다 — 안 그러면 receiverRookie가 먼저 걸려 giverLimit을 못 본다
    promote(state, "p1", "corporal");
    promote(state, "p2", "pfc"); // corporal↔pfc = 1단계 차 → 1건
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
    const squad = fullSquad();
    squad.day = 3;
    // 이 시험은 시간대(창 안/밖)를 잰다 — F-1의 실시간 하한이 아니라. 하한 자체는
    // `unlock.test.ts`가 잰다
    squad.elapsedRealMs = Number.MAX_SAFE_INTEGER;
    const state = beginDay(squad);
    promote(state, "p1", "corporal");
    chore(state, "p1");
    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "notDelegationWindow",
    });
  });
});

describe("F-2(WORKORDER) — 초행자(이병) 하달 수신 제한", () => {
  it("받는 쪽이 이병이면 receiverRookie로 거절된다 — 다른 사유보다 앞서 걸린다", () => {
    const state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1"); // p2는 기본값(이병) 그대로 둔다

    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "receiverRookie",
    });
  });

  it("거절이어도 침묵하지 않는다 — delegationRefused 이펙트가 그대로 뜬다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");

    const result = step(state, {
      type: "delegateChore",
      fromId: "p1",
      toId: "p2",
      questId: "c1",
    });

    expect(
      result.effects.some(
        (e) => e.type === "delegationRefused" && e.reason === "receiverRookie",
      ),
    ).toBe(true);
    // 실제로 소유자도 안 바뀌었어야 한다 — 거절은 부수효과가 없다
    expect(result.state.quests.find((q) => q.id === "c1")?.ownerId).toBe("p1");
  });

  it("계급이 올라 이병을 벗어나면 그때부터는 받을 수 있다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    chore(state, "p1");

    expect(canDelegate(state, "p1", "p2", "c1").ok).toBe(false);

    promote(state, "p2", "pfc");
    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({ ok: true });
  });

  it("계급차 자체가 없으면 receiverRookie가 아니라 rankTooLow가 먼저 걸린다", () => {
    // 6.2 규칙: 계급차가 0 이하면 allowance가 0이라 rankTooLow로 걸린다.
    // receiverRookie는 그 다음 단계(받는 쪽 자격) 검사라 여기까지 안 온다.
    // (실전에서는 전원 동급이면 하달 창 자체가 안 열리지만 — "하달 창 개폐"
    // describe 참고 — canDelegate는 그 관문을 우회해 단위로 시험할 수 있다)
    const state = atMorning(false);
    for (const member of state.members) member.rank = "private";
    state.delegationWindowMsLeft = 20 * SECOND;
    chore(state, "p1");

    expect(canDelegate(state, "p1", "p2", "c1")).toEqual({
      ok: false,
      reason: "rankTooLow",
    });
  });
});

describe("6.2 비용 구조", () => {
  it("대행 점수는 수행자에게 간다 — 하달자는 시간을 사고 승급 점수를 판다", () => {
    let state = atMorning();
    promote(state, "p1", "corporal");
    promote(state, "p2", "pfc"); // F-2 — 이병 수신 거절을 피한다
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
    promote(state, "p2", "pfc"); // F-2 — 이병 수신 거절을 피한다
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
    promote(state, "p2", "pfc"); // F-2 — 이병 수신 거절을 피한다
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
    promote(state, "p2", "pfc"); // F-2 — 사전 하달(delegateChore)이 실제로 성사돼야 이 시험의 전제(재배정 대상이 이미 하달받은 상태)가 성립한다
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
    promote(state, "p2", "pfc"); // F-2 — 이병 수신 거절을 피한다
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

describe("하달 창 개폐", () => {
  it("전원이 같은 계급이면 창이 열리지 않는다 — 6.2 D-01~03 잠금", () => {
    // 6.2가 "전원이 이병인 D-01~03 구간에는 시스템이 잠겨 있다"고 못박았다.
    // 열어봐야 하달할 수 있는 사람이 없고, 시간대 타이머만 20초 멈춘다 —
    // 하루 두 번이면 아무 선택지 없는 화면이 40초를 먹는다.
    const state = atMorning(false);
    for (const member of state.members) member.rank = "private";

    openDelegationWindow(state);
    expect(isDelegationWindow(state)).toBe(false);
  });

  it("한 명이라도 계급이 높으면 열린다", () => {
    const state = atMorning(false);
    for (const member of state.members) member.rank = "private";
    const first = state.members[0];
    if (first) first.rank = "corporal";

    openDelegationWindow(state);
    expect(isDelegationWindow(state)).toBe(true);
  });
});

describe("하달 창 조기 종료 — 전원 확정 시 delegationWindowMsLeft = 0", () => {
  it("1인만 확정해서는 안 닫힌다", () => {
    let state = atMorning();
    state = step(state, { type: "delegationDone", memberId: "p1" }).state;

    expect(state.delegationWindowMsLeft).toBe(20 * SECOND);
    expect(isDelegationWindow(state)).toBe(true);
  });

  it("사람 참석자 전원이 확정하면 즉시 닫히고 일과 시간이 다시 흐른다", () => {
    let state = atMorning();

    // 마지막 한 명이 남을 때까지는 계속 열려 있다
    for (const id of ["p1", "p2", "p3"]) {
      state = step(state, { type: "delegationDone", memberId: id }).state;
      expect(state.delegationWindowMsLeft).toBe(20 * SECOND);
    }

    state = step(state, { type: "delegationDone", memberId: "p4" }).state;
    expect(state.delegationWindowMsLeft).toBe(0);

    // 창이 닫혔으니 이제부터는 틱이 곧바로 일과 시간을 깎는다 — 남은 창
    // 시간을 기다리지 않는다(조기 종료 전이라면 여기서 phaseElapsedMs가
    // 여전히 0이어야 한다)
    state = step(state, { type: "tick", elapsedMs: 5 * SECOND }).state;
    expect(state.phaseElapsedMs).toBe(5 * SECOND);
  });

  it("같은 사람이 두 번 신고해도 중복으로 세지 않는다", () => {
    let state = atMorning();
    for (const id of ["p1", "p1", "p2", "p3"]) {
      state = step(state, { type: "delegationDone", memberId: id }).state;
    }
    expect(state.delegationWindowMsLeft).toBe(20 * SECOND); // p4가 아직

    state = step(state, { type: "delegationDone", memberId: "p4" }).state;
    expect(state.delegationWindowMsLeft).toBe(0);
  });

  it("NPC 대리 · 후송자는 세지 않는다 — 남은 사람 전원만 확정하면 닫힌다", () => {
    let state = atMorning();
    const evacuee = state.members.find((m) => m.id === "p3");
    const proxy = state.members.find((m) => m.id === "p4");
    if (!evacuee || !proxy) throw new Error("분대원 없음");
    // p3는 후송자, p4는 이탈 대리로 바꿔둔다 — 둘 다 "사람 참석자"가 아니다
    evacuee.presence = "evacuated";
    proxy.presence = "npcLeave";

    // 남은 사람은 p1 · p2뿐이다. 둘만 확정해도 닫혀야 한다
    state = step(state, { type: "delegationDone", memberId: "p1" }).state;
    expect(state.delegationWindowMsLeft).toBe(20 * SECOND);

    state = step(state, { type: "delegationDone", memberId: "p2" }).state;
    expect(state.delegationWindowMsLeft).toBe(0);
  });

  it("이미 닫힌 창에 신고가 와도 아무 일도 없다", () => {
    let state = atMorning();
    state = step(state, { type: "tick", elapsedMs: 20 * SECOND }).state;
    expect(state.delegationWindowMsLeft).toBe(0);

    const before = state.phaseElapsedMs;
    state = step(state, { type: "delegationDone", memberId: "p1" }).state;
    expect(state.phaseElapsedMs).toBe(before);
    expect(state.delegationWindowMsLeft).toBe(0);
  });
});
