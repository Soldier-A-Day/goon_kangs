import { describe, expect, it } from "vitest";
import {
  CRISIS_MS,
  MEDIC_RESCUE_MULTIPLIER,
  PROXY_ABSORB_LIMIT,
  RESCUE_REQUIRED_MS,
  enterCrisis,
  evacuate,
  isFlawless,
  planFor,
  proxyCount,
  step,
  type Effect,
  type Quest,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, completeRequired, fullSquad, playDays, toPhase } from "./helpers.js";

function required(state: RunState, ownerId: string, id: string): Quest {
  const quest: Quest = {
    id,
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId,
    required: true,
    phase: "afternoon",
    zone: "Z01",
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
    workMs: 10 * SECOND,
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

  it("B-4 — 분대장 우선순위 지정을 한 번이라도 쓰면 모범 전역이 불가능해진다", () => {
    // 판정 시각에는 이 사용이 전혀 안 보인다(reliefsUsed는 간부 몫만 센다) —
    // isFlawless가 leaderReliefsRemaining도 같이 보지 않으면 이 런이
    // "구제를 한 번도 안 쓴 런"으로 잘못 인정된다
    const state = withOnly(beginDay(fullSquad()), []);
    expect(isFlawless(state)).toBe(true);
    state.leaderReliefsRemaining -= 1;
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
  it("체력이 0이 되면 그 자리에서 위기에 들어간다 — 점호를 기다리지 않는다 (B-2)", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.stamina = 1;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    // B-2 이전에는 여기서 바로 memberEvacuated였다 — 이제는 위기가 끼어든다
    expect(result.effects.some((e) => e.type === "crisisStarted")).toBe(true);
    expect(result.effects.some((e) => e.type === "memberEvacuated")).toBe(false);
    expect(result.state.members.find((m) => m.id === "p1")?.presence).toBe("player");
  });

  it("열사병 2단계는 한 칸을 버티면 위기에 들어간다 (B-2)", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.hydration = 5;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    expect(result.effects.some((e) => e.type === "crisisStarted")).toBe(true);
    expect(result.effects.some((e) => e.type === "memberEvacuated")).toBe(false);
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

describe("ROLE-03 · 2.0 NPC 대리의 일과", () => {
  it("빈 보직의 필수는 대리가 채운다 — 없으면 1~3인 방이 성립하지 않는다", () => {
    const state = playDays(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
      3,
    );
    expect(state.status).toBe("running");
    expect(state.judgements.every((j) => j.passed)).toBe(true);
  });

  it("대리는 선택·돌발은 손대지 않는다 — 생존은 시켜주되 성장은 시켜주지 않는다", () => {
    let state = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );

    let guard = 0;
    while (state.status === "running" && state.day === 1 && guard++ < 200) {
      state = completeRequired(state);
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    const npcIds = state.members
      .filter((m) => m.presence === "npcVacant")
      .map((m) => m.id);
    const npcOptional = state.quests.filter(
      (q) => npcIds.includes(q.ownerId ?? "") && !q.required,
    );

    expect(npcOptional.length).toBeGreaterThan(0);
    expect(npcOptional.every((q) => q.status !== "done")).toBe(true);
  });

  it("이탈로 생긴 자리도 같은 대리가 이어받는다", () => {
    let state = beginDay(fullSquad());
    state = step(state, { type: "leaveRun", memberId: "p2" }).state;

    // 다음 날로 넘어가도 p2 몫의 필수는 대리가 계속 끝낸다
    let guard = 0;
    while (state.status === "running" && state.day <= 2 && guard++ < 300) {
      state = completeRequired(state);
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    expect(state.status).toBe("running");
    expect(state.judgements.every((j) => j.passed)).toBe(true);
  });
});

describe("대리와 합동 퀘스트", () => {
  it("대리는 합동 장소에 서서 머릿수를 채운다", () => {
    const state = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );
    const joint = state.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");

    const proxies = state.members.filter((m) => m.presence === "npcVacant");
    expect(proxies.length).toBeGreaterThan(0);
    expect(proxies.every((m) => m.zone === joint.zone)).toBe(true);
  });

  it("1인 방도 합동을 완수할 수 있다 — 사람이 조각을 채우면 대리가 나머지를 따라온다", () => {
    let state = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );
    const joint = state.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");

    // **그 일과의 시간대까지 간다.** 일과는 제 칸에서만 할 수 있고(4.0),
    // 이 테스트는 전에 기상·점검 칸에서 오후 합동을 끝내고 있었다
    state = toPhase(state, joint.phase);

    state = step(state, { type: "move", memberId: "p1", to: joint.zone }).state;
    state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;

    // 합동은 판 하나를 인원이 나눠 채운다 (QST-01). 사람이 제 몫을 채우고,
    // 대리는 시간으로 따라온다 — 그래야 1~3인 방이 성립한다 (ROLE-03)
    for (let i = 0; i < joint.jointTotal; i += 1) {
      state = step(state, { type: "jointStep", memberId: "p1", questId: joint.id }).state;
    }

    expect(state.quests.find((q) => q.id === joint.id)?.status).toBe("done");
  });

  it("사람이 붙잡지 않으면 대리만으로는 합동이 진행되지 않는다", () => {
    let state = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );
    const joint = state.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");

    state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    expect(state.quests.find((q) => q.id === joint.id)?.workedMs).toBe(0);
  });
});

describe("B-2 위기와 구조", () => {
  /** p1을 위기에 빠뜨린다 (체력 0 → checkCollapses가 즉시 위기로 잡는다) */
  function crashP1(): RunState {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.stats.stamina = 1;
    return step(state, { type: "tick", elapsedMs: 60 * SECOND }).state;
  }

  it("위기 중에는 이동도 상호작용도 막힌다 — 구조를 기다리는 것만 할 수 있다", () => {
    const state = crashP1();
    const before = state.members.find((m) => m.id === "p1")?.zone;

    const moved = step(state, { type: "move", memberId: "p1", to: "Z02" }).state;
    expect(moved.members.find((m) => m.id === "p1")?.zone).toBe(before);
  });

  it("곁에서 붙잡으면 구조 진척이 오르고, 다 채우면 구조 성공 — 위기가 풀리고 스탯이 일부 회복된다", () => {
    let state = crashP1();
    const target = state.members.find((m) => m.id === "p1");
    if (!target) throw new Error("분대원 없음");
    expect(target.crisisStat).toBe("stamina");

    // p2(통신병 — 의무병이 아니다)를 같은 구역으로 보내 붙잡는다
    state = step(state, { type: "move", memberId: "p2", to: target.zone }).state;

    const result = step(state, {
      type: "rescueWork",
      rescuerId: "p2",
      targetId: "p1",
      deltaMs: RESCUE_REQUIRED_MS,
    });

    expect(result.effects.some((e) => e.type === "crisisRescued")).toBe(true);
    const rescued = result.state.members.find((m) => m.id === "p1");
    expect(rescued?.crisisStat).toBeNull();
    expect(rescued?.presence).toBe("player");
    expect(rescued?.stats.stamina).toBeGreaterThan(0);
  });

  it("의무병은 구조가 더 빠르다 — 같은 시간을 붙잡아도 진척이 더 오른다", () => {
    let state = crashP1();
    const target = state.members.find((m) => m.id === "p1");
    if (!target) throw new Error("분대원 없음");

    const half = RESCUE_REQUIRED_MS / MEDIC_RESCUE_MULTIPLIER / 2;

    let withMedic = step(state, { type: "move", memberId: "p3", to: target.zone }).state;
    withMedic = step(withMedic, {
      type: "rescueWork",
      rescuerId: "p3", // 박의무 — role: medic
      targetId: "p1",
      deltaMs: half,
    }).state;

    let withRifle = step(state, { type: "move", memberId: "p2", to: target.zone }).state;
    withRifle = step(withRifle, {
      type: "rescueWork",
      rescuerId: "p2", // 이통신 — role: comms
      targetId: "p1",
      deltaMs: half,
    }).state;

    const medicProgress = withMedic.members.find((m) => m.id === "p1")?.rescueMs ?? 0;
    const rifleProgress = withRifle.members.find((m) => m.id === "p1")?.rescueMs ?? 0;
    expect(medicProgress).toBeGreaterThan(rifleProgress);
  });

  it("자기 자신은 구조할 수 없고, 다른 구역에서는 진척이 오르지 않는다", () => {
    const state = crashP1();
    const target = state.members.find((m) => m.id === "p1");
    if (!target) throw new Error("분대원 없음");

    const self = step(state, {
      type: "rescueWork",
      rescuerId: "p1",
      targetId: "p1",
      deltaMs: RESCUE_REQUIRED_MS,
    }).state;
    expect(self.members.find((m) => m.id === "p1")?.rescueMs).toBe(0);

    // p2를 다른 구역으로 옮긴다 — 곁이 아니면 진척이 없다
    const remoteState = beginDay(fullSquad());
    const remoteTarget = remoteState.members.find((m) => m.id === "p1");
    if (!remoteTarget) throw new Error("분대원 없음");
    remoteTarget.stats.stamina = 1;
    remoteTarget.crisisStat = "stamina";
    remoteTarget.crisisMsLeft = CRISIS_MS;
    const rescuer = remoteState.members.find((m) => m.id === "p2");
    if (!rescuer) throw new Error("분대원 없음");
    rescuer.zone = rescuer.zone === "Z01" ? "Z11" : "Z01";

    const remote = step(remoteState, {
      type: "rescueWork",
      rescuerId: "p2",
      targetId: "p1",
      deltaMs: RESCUE_REQUIRED_MS,
    }).state;
    expect(remote.members.find((m) => m.id === "p1")?.rescueMs).toBe(0);
  });

  it("시간 안에 구조되지 못하면 기존 후송 규칙 그대로 실려 나간다 — 긴장을 물타기하지 않는다", () => {
    let state = crashP1();
    expect(state.members.find((m) => m.id === "p1")?.crisisStat).toBe("stamina");

    // 아무도 구조하러 오지 않은 채 위기 시간을 전부 흘려보낸다
    const result = step(state, { type: "tick", elapsedMs: CRISIS_MS + SECOND });

    expect(result.effects.some((e) => e.type === "memberEvacuated")).toBe(true);
    const failed = result.state.members.find((m) => m.id === "p1");
    expect(failed?.presence).toBe("evacuated");
    expect(failed?.crisisStat).toBeNull();
  });

  it("NPC 대리는 위기를 겪지 않는다 — 원래도 컨디션으로 후송되지 않았다 (봇 완주율 중립)", () => {
    let state = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );
    state = completeRequired(state);
    const proxy = state.members.find((m) => m.presence === "npcVacant");
    if (!proxy) throw new Error("대리 없음");
    proxy.stats.stamina = 0;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    expect(result.effects.some((e) => e.type === "crisisStarted")).toBe(false);
    expect(result.state.members.find((m) => m.id === proxy.id)?.crisisStat).toBeNull();
  });

  it("이미 위기 중이면 같은 트리거로 타이머가 리셋되지 않는다", () => {
    const state = beginDay(fullSquad());
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");

    const effects: Effect[] = [];
    enterCrisis(state, member, "stamina", effects);
    // 시간이 좀 흐른 상태를 흉내낸다 — 가드가 없으면 다음 줄에서 45초로 되돌아간다
    member.crisisMsLeft = 10_000;

    enterCrisis(state, member, "stamina", effects);

    expect(member.crisisMsLeft).toBe(10_000);
    expect(effects.filter((e) => e.type === "crisisStarted")).toHaveLength(1);
  });
});
