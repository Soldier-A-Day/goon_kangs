import { describe, expect, it } from "vitest";
import {
  HIDDEN_FOR_RECORD_ENDING,
  HIDDEN_QUESTS,
  checkHiddenQuests,
  deserializeRun,
  resolveEnding,
  serializeRun,
  summarizeRun,
  type Effect,
  type RunState,
} from "../src/index.js";
import { beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function check(state: RunState): Effect[] {
  const effects: Effect[] = [];
  checkHiddenQuests(state, effects);
  return effects;
}

describe("표 6-1 히든 퀘스트", () => {
  it("런당 2~4개를 노릴 만한 수가 정의돼 있다", () => {
    expect(HIDDEN_QUESTS.length).toBeGreaterThanOrEqual(4);
    for (const quest of HIDDEN_QUESTS) {
      expect(quest.label.length).toBeGreaterThan(0);
      expect(quest.hint.length).toBeGreaterThan(0);
    }
  });

  it("완전한 하루 — 선택까지 하나도 남기지 않으면 열린다", () => {
    const state = beginDay(fullSquad());
    expect(check(state)).toHaveLength(0);

    for (const quest of state.quests) {
      quest.status = "done";
      quest.workedMs = quest.workMs;
    }
    expect(state.hiddenUnlocked).not.toContain("flawlessDay");
    check(state);
    expect(state.hiddenUnlocked).toContain("flawlessDay");
  });

  it("같은 히든은 두 번 열리지 않는다", () => {
    const state = beginDay(fullSquad());
    state.discipline = 95;

    check(state);
    const first = state.hiddenUnlocked.length;
    const second = check(state);

    expect(second).toHaveLength(0);
    expect(state.hiddenUnlocked).toHaveLength(first);
  });

  it("보직 한정 히든은 그 보직이 NPC면 열리지 않는다", () => {
    const withAdmin = beginDay(fullSquad());
    withAdmin.discipline = 95;
    check(withAdmin);
    expect(withAdmin.hiddenUnlocked).toContain("steadfast");

    const noAdmin = beginDay(
      fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }),
    );
    noAdmin.discipline = 95;
    check(noAdmin);
    expect(noAdmin.hiddenUnlocked).not.toContain("steadfast");
  });

  it("혹한을 넘다 — 극혹한에서 필수를 다 끝내야 한다", () => {
    const state = completeRequired(beginDay(fullSquad()));
    state.weather = { ...state.weather, band: "normal" };
    check(state);
    expect(state.hiddenUnlocked).not.toContain("coldSnap");

    state.weather = { ...state.weather, band: "extremeCold" };
    check(state);
    expect(state.hiddenUnlocked).toContain("coldSnap");
  });

  it("제 몫은 제가 — D-10까지 하달이 없어야 한다", () => {
    const clean = beginDay(fullSquad());
    clean.day = 10;
    check(clean);
    expect(clean.hiddenUnlocked).toContain("ledgerClean");

    const dirty = beginDay(fullSquad());
    dirty.day = 10;
    dirty.ledger.push({
      day: 4,
      phaseIndex: 1,
      fromId: "p1",
      toId: "p2",
      questId: "c1",
      outcome: "accepted",
    });
    check(dirty);
    expect(dirty.hiddenUnlocked).not.toContain("ledgerClean");
  });

  it("히든은 그날의 판정을 바꾸지 않는다 — 페널티가 없다", () => {
    const state = beginDay(fullSquad());
    const before = { discipline: state.discipline, quests: state.quests.length };
    state.discipline = 95;
    check(state);
    expect(state.quests).toHaveLength(before.quests);
  });
});

describe("META-02 엔딩 분기", () => {
  function cleared(): RunState {
    const state = fullSquad();
    state.status = "cleared";
    state.day = 18;
    return state;
  }

  it("히든 4개를 모으면 분대 기록 엔딩", () => {
    const state = cleared();
    state.hiddenUnlocked = HIDDEN_QUESTS.slice(0, HIDDEN_FOR_RECORD_ENDING).map(
      (q) => q.id,
    );
    expect(resolveEnding(state).id).toBe("record");
  });

  it("무실패 + 후송 0 + 군기 80 + 병장이면 모범 전역", () => {
    const state = cleared();
    state.discipline = 85;
    for (const member of state.members) member.rank = "sergeant";
    expect(resolveEnding(state).id).toBe("exemplary");
  });

  it("후송이 한 번이라도 있으면 모범 전역이 깨진다", () => {
    const state = cleared();
    state.discipline = 85;
    for (const member of state.members) member.rank = "sergeant";
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.evacuations = 1;

    expect(resolveEnding(state).id).not.toBe("exemplary");
  });

  it("구제를 쓴 런은 모범 전역이 아니다", () => {
    const state = cleared();
    state.discipline = 85;
    for (const member of state.members) member.rank = "sergeant";
    state.judgements.push({
      day: 5,
      passed: true,
      failedAt: null,
      requiredTotal: 4,
      requiredDone: 3,
      jointPassed: true,
      discipline: 60,
      reliefsUsed: 1,
    });

    expect(resolveEnding(state).id).toBe("normal");
  });

  it("경고가 있으면 간신히 전역", () => {
    const state = cleared();
    state.warnings = 1;
    expect(resolveEnding(state).id).toBe("barely");
  });

  it("기본은 정상 전역", () => {
    expect(resolveEnding(cleared()).id).toBe("normal");
  });
});

describe("17.0 이어하기 — 직렬화", () => {
  it("JSON 왕복에서 상태가 손실 없이 돌아온다", () => {
    const state = playDays(fullSquad({ seed: 909 }), 4);
    const restored = deserializeRun(serializeRun(state));

    expect(restored).not.toBeNull();
    expect(JSON.stringify(restored)).toBe(JSON.stringify(state));
  });

  it("되살린 상태로 이어서 진행해도 같은 결과가 나온다", () => {
    const state = playDays(fullSquad({ seed: 909 }), 4);
    const restored = deserializeRun(serializeRun(state));
    if (!restored) throw new Error("복구 실패");

    const direct = playDays(state, 3);
    const resumed = playDays(restored, 3);

    expect(JSON.stringify(resumed)).toBe(JSON.stringify(direct));
  });

  it("포맷이 다르면 되살리지 않는다 — 반쯤 맞는 상태가 가장 나쁘다", () => {
    expect(deserializeRun("{}")).toBeNull();
    expect(deserializeRun("망가진 데이터")).toBeNull();
    expect(deserializeRun(JSON.stringify({ version: 99, state: {} }))).toBeNull();
  });
});

describe("런 기록", () => {
  it("하달 장부가 기록에 함께 굳는다 (QST-05)", () => {
    const state = playDays(fullSquad(), 2);
    state.ledger.push({
      day: 1,
      phaseIndex: 1,
      fromId: "p1",
      toId: "p2",
      questId: "c1",
      outcome: "accepted",
    });

    const record = summarizeRun(state);
    const giver = record.members.find((m) => m.name === "김소총");
    const receiver = record.members.find((m) => m.name === "이통신");

    expect(giver?.delegationsGiven).toBe(1);
    expect(receiver?.delegationsReceived).toBe(1);
  });

  it("전역한 런에만 엔딩이 붙는다", () => {
    const running = summarizeRun(fullSquad());
    expect(running.ending).toBeNull();

    const state = fullSquad();
    state.status = "cleared";
    expect(summarizeRun(state).ending).not.toBeNull();
  });

  it("퇴소한 런은 깨진 조건을 남긴다", () => {
    const state = fullSquad();
    state.status = "discharged";
    state.judgements.push({
      day: 7,
      passed: false,
      failedAt: "C",
      requiredTotal: 4,
      requiredDone: 4,
      jointPassed: true,
      discipline: 30,
      reliefsUsed: 0,
    });

    expect(summarizeRun(state).failedAt).toBe("C");
  });

  it("처음부터 빈 자리는 기록에 남지 않는다", () => {
    const state = fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] });
    expect(summarizeRun(state).members).toHaveLength(1);
  });
});
