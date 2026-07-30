import { describe, expect, it } from "vitest";
import {
  applyDailyDiscipline,
  bestOfficer,
  disciplineBand,
  disciplineCap,
  penalizeIncident,
  phaseDurationMsFor,
  step,
  strictestOfficer,
  surpriseChance,
  type Effect,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function tally(state: RunState): { state: RunState; effects: Effect[] } {
  const effects: Effect[] = [];
  applyDailyDiscipline(state, effects);
  return { state, effects };
}

describe("DISC-01 군기 구간", () => {
  it("60에서 시작한다", () => {
    expect(fullSquad().discipline).toBe(60);
  });

  it("구간이 표대로 나뉜다", () => {
    expect(disciplineBand(100).id).toBe("excellent");
    expect(disciplineBand(80).id).toBe("excellent");
    expect(disciplineBand(79).id).toBe("normal");
    expect(disciplineBand(40).id).toBe("normal");
    expect(disciplineBand(39).id).toBe("loose");
    expect(disciplineBand(20).id).toBe("loose");
    expect(disciplineBand(19).id).toBe("warning");
    expect(disciplineBand(0).id).toBe("warning");
  });

  it("우수분대는 돌발이 줄고 개인정비를 20초 더 받는다", () => {
    expect(surpriseChance(85)).toBeLessThan(surpriseChance(60));

    const state = fullSquad();
    const normalTime = phaseDurationMsFor(state, 4);
    state.discipline = 85;
    expect(phaseDurationMsFor(state, 4)).toBe(normalTime + 20 * SECOND);
  });

  it("이완 구간은 다음 날 선택 퀘스트를 늘린다", () => {
    const state = beginDay(fullSquad());
    state.discipline = 30;
    tally(state);
    expect(state.nextDayExtraOptional).toBe(2);

    state.discipline = 70;
    tally(state);
    expect(state.nextDayExtraOptional).toBe(0);
  });
});

describe("DISC-01 하루 정산", () => {
  it("필수 완수·합동 무결·무부상이 군기를 올린다", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    // 선택까지 전부 끝내면 감점이 없다
    for (const quest of state.quests) {
      quest.status = "done";
      quest.workedMs = quest.workMs;
    }
    const before = state.discipline;
    tally(state);
    expect(state.discipline).toBe(before + 5 + 12 + 6);
  });

  it("선택 퀘스트를 남기면 분대 단위로 −8", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const before = state.discipline;
    tally(state);
    // 정시 +5, 합동 +12, 무부상 +6, 선택 미완 −8
    expect(state.discipline).toBe(before + 15);
  });

  it("합동 부분 성공은 무결 보상을 받지 못한다", () => {
    const state = beginDay(fullSquad());
    const joint = state.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");
    joint.workedMs = joint.workMs * 0.8;
    joint.status = "active";

    const before = state.discipline;
    tally(state);
    expect(state.discipline).toBeLessThan(before + 12);
  });

  it("대리 NPC는 하루 −3씩, 처음부터 빈 자리는 면제된다", () => {
    const vacant = beginDay(fullSquad({ members: [{ id: "p1", name: "김", role: "rifle" }] }));
    const vacantBefore = vacant.discipline;
    tally(vacant);

    const proxy = beginDay(fullSquad());
    const member = proxy.members[1];
    if (!member) throw new Error("분대원 없음");
    member.presence = "npcLeave";
    const proxyBefore = proxy.discipline;
    tally(proxy);

    expect(proxy.discipline - proxyBefore).toBe(vacant.discipline - vacantBefore - 3);
  });

  it("군기는 0~100을 벗어나지 않는다", () => {
    const state = beginDay(fullSquad());
    state.discipline = 98;
    tally(state);
    expect(state.discipline).toBeLessThanOrEqual(100);

    state.discipline = 2;
    penalizeIncident(state);
    expect(state.discipline).toBeGreaterThanOrEqual(0);
  });

  it("대리가 2명 이상이면 군기 상한이 60으로 떨어진다", () => {
    const state = fullSquad();
    expect(disciplineCap(state)).toBe(100);

    const [a, b] = state.members;
    if (!a || !b) throw new Error("분대원 없음");
    a.presence = "npcLeave";
    b.presence = "npcEvac";
    expect(disciplineCap(state)).toBe(60);

    state.discipline = 100;
    // 상한이 내려간 뒤의 증감은 60을 넘지 못한다
    penalizeIncident(state);
    expect(state.discipline).toBeLessThanOrEqual(60);
  });

  it("군기 변화가 이벤트로 나간다", () => {
    const state = beginDay(fullSquad());
    const { effects } = tally(state);
    const changed = effects.find((e) => e.type === "disciplineChanged");
    expect(changed).toBeDefined();
  });
});

describe("판정과의 관계", () => {
  it("정산이 판정보다 먼저 돈다 — 이완 구간에서 일과로 만회할 수 있다", () => {
    let state = beginDay(fullSquad());
    state.discipline = 30; // 조건 C(40) 아래
    state = completeRequired(state);

    let guard = 0;
    while (state.status === "running" && state.day === 1 && guard++ < 100) {
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    // +5 +12 +6 −8 = +15 → 45 로 올라와 조건 C를 통과한다
    expect(state.discipline).toBe(45);
    expect(state.status).toBe("running");
    expect(state.judgements[0]?.passed).toBe(true);
  });

  it("경고 구간에서 만회하지 못하면 조건 C에서 끝난다", () => {
    let state = beginDay(fullSquad());
    state.discipline = 10;
    // 필수를 남겨 정시 보너스를 받지 못하게 한다
    state.reliefsRemaining = 0;

    let guard = 0;
    while (state.status === "running" && guard++ < 100) {
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    expect(state.status).toBe("discharged");
    expect(state.judgements[0]?.failedAt).not.toBeNull();
  });

  it("이상적인 런은 우수분대로 전역한다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
    expect(state.discipline).toBeGreaterThanOrEqual(80);
  });
});

describe("DISC-02 간부 신뢰도", () => {
  it("세 간부가 각각 다른 항목을 본다", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    const before = { ...state.trust };
    tally(state);

    // 필수·훈련을 해냈으므로 소대장·행보관은 오르고,
    // 청결이 아직 높으므로 부소대장도 오른다
    expect(state.trust.platoonLeader).toBeGreaterThan(before.platoonLeader);
    expect(state.trust.sergeantMajor).toBeGreaterThan(before.sergeantMajor);
  });

  it("복장이 무너지면 부소대장 신뢰도가 떨어진다", () => {
    let state = beginDay(fullSquad());
    state = completeRequired(state);
    for (const member of state.members) member.stats.hygiene = 10;

    const before = state.trust.assistant;
    tally(state);
    expect(state.trust.assistant).toBeLessThan(before);
  });

  it("구제권은 신뢰도가 가장 높은 간부만, 불시 점호는 가장 낮은 간부가 건다", () => {
    const state = fullSquad();
    state.trust = { platoonLeader: 80, assistant: 40, sergeantMajor: 60 };
    expect(bestOfficer(state)).toBe("platoonLeader");
    expect(strictestOfficer(state)).toBe("assistant");
  });
});
