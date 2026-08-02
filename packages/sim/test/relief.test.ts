import { describe, expect, it } from "vitest";
import {
  LEADER_RELIEF_LIMIT,
  OFFICER_RELIEF_LIMIT,
  OFFICER_RELIEF_TRUST_THRESHOLD,
  PHASES,
  canUseOfficerRelief,
  canUseRelief,
  useOfficerRelief,
  useRelief,
  type Quest,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, fullSquad, withQuests } from "./helpers.js";

function quest(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q",
    kind: "role",
    label: "총기 수입",
    training: null,
    ownerId: "p1",
    required: true,
    phase: "morning",
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

const PERSONAL_PHASE_INDEX = PHASES.findIndex((p) => p.id === "personal");

/** 저녁 개인정비 칸으로 시간대만 옮긴다 — 판을 다 돌리지 않고 발동 자격만 본다 */
function atPersonalPhase(state: RunState): RunState {
  state.phaseIndex = PERSONAL_PHASE_INDEX;
  return state;
}

describe("B-4 분대장 우선순위 지정 (useRelief)", () => {
  it("분대장이 미완료 필수 퀘스트를 봐주면 필수→선택으로 강등되고 몫이 하나 준다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [quest({ id: "a" })]);

    const ok = useRelief(state, "p1", "a", []);

    expect(ok).toBe(true);
    expect(state.quests[0]?.required).toBe(false);
    expect(state.leaderReliefsRemaining).toBe(LEADER_RELIEF_LIMIT - 1);
    expect(state.reliefsRemaining).toBe(
      LEADER_RELIEF_LIMIT - 1 + OFFICER_RELIEF_LIMIT,
    );
  });

  it("발동 성공 시 reliefGranted 이펙트를 방출한다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [quest({ id: "a" })]);

    const effects: import("../src/index.js").Effect[] = [];
    useRelief(state, "p1", "a", effects);

    expect(effects).toContainEqual(
      expect.objectContaining({ type: "reliefGranted", by: "leader", questId: "a" }),
    );
  });

  it("분대장이 아니면 거부한다 — 자격 없음", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [quest({ id: "a" })]);

    const check = canUseRelief(state, "p2", "a");
    expect(check).toEqual({ ok: false, reason: "notLeader" });

    const effects: import("../src/index.js").Effect[] = [];
    expect(useRelief(state, "p2", "a", effects)).toBe(false);
    expect(state.quests[0]?.required).toBe(true);
    expect(effects).toContainEqual(
      expect.objectContaining({ type: "reliefRefused", by: "leader", reason: "notLeader" }),
    );
  });

  it("분대장 몫을 다 쓰면 한도로 거부한다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state.leaderReliefsRemaining = 0;
    state = withQuests(state, [quest({ id: "a" })]);

    expect(canUseRelief(state, "p1", "a")).toEqual({ ok: false, reason: "limitReached" });
  });

  it("이미 완료된 퀘스트나 선택 퀘스트는 대상이 아니다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [
      quest({ id: "done", status: "done" }),
      quest({ id: "optional", required: false }),
    ]);

    expect(canUseRelief(state, "p1", "done")).toEqual({
      ok: false,
      reason: "notEligibleQuest",
    });
    expect(canUseRelief(state, "p1", "optional")).toEqual({
      ok: false,
      reason: "notEligibleQuest",
    });
    expect(canUseRelief(state, "p1", "unknown")).toEqual({
      ok: false,
      reason: "notEligibleQuest",
    });
  });

  it("잠긴(locked) 필수 퀘스트도 대상이다 — 이미 shortfall에 잡히는 것과 같은 조건이다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [quest({ id: "a", status: "locked" })]);

    expect(canUseRelief(state, "p1", "a")).toEqual({ ok: true });
  });

  it("런당 2회를 다 쓰면 세 번째는 못 쓴다", () => {
    let state = beginDay(fullSquad());
    state.leaderId = "p1";
    state = withQuests(state, [quest({ id: "a" }), quest({ id: "b" }), quest({ id: "c" })]);

    expect(useRelief(state, "p1", "a", [])).toBe(true);
    expect(useRelief(state, "p1", "b", [])).toBe(true);
    expect(state.leaderReliefsRemaining).toBe(0);
    expect(useRelief(state, "p1", "c", [])).toBe(false);
    expect(state.quests.find((q) => q.id === "c")?.required).toBe(true);
  });
});

describe("B-4 간부 구제 (useOfficerRelief)", () => {
  it("저녁 개인정비에 신뢰도 최고 간부가 문턱 이상이면 발동된다", () => {
    let state = atPersonalPhase(beginDay(fullSquad()));
    state.trust.sergeantMajor = OFFICER_RELIEF_TRUST_THRESHOLD;

    const effects: import("../src/index.js").Effect[] = [];
    const ok = useOfficerRelief(state, "p1", effects);

    expect(ok).toBe(true);
    expect(state.officerReliefArmedToday).toBe(true);
    // 발동 시점에는 아직 그날 미달이 확정되지 않았으므로 총량은 그대로다
    expect(state.officerReliefsRemaining).toBe(OFFICER_RELIEF_LIMIT);
    expect(effects).toContainEqual(
      expect.objectContaining({ type: "reliefGranted", by: "officer", questId: null }),
    );
  });

  it("개인정비 시간이 아니면 거부한다", () => {
    const state = beginDay(fullSquad()); // reveille 칸 그대로
    state.trust.sergeantMajor = 90;

    expect(canUseOfficerRelief(state, "p1")).toEqual({
      ok: false,
      reason: "notPersonalPhase",
    });
  });

  it("최고 신뢰도 간부도 문턱에 못 미치면 거부한다", () => {
    const state = atPersonalPhase(beginDay(fullSquad()));
    // createRun 기본값 — 세 간부 모두 50, 문턱(70) 미달

    expect(canUseOfficerRelief(state, "p1")).toEqual({
      ok: false,
      reason: "trustTooLow",
    });
  });

  it("하루에 한 번만 발동할 수 있다", () => {
    const state = atPersonalPhase(beginDay(fullSquad()));
    state.trust.sergeantMajor = 90;

    expect(useOfficerRelief(state, "p1", [])).toBe(true);
    expect(canUseOfficerRelief(state, "p2")).toEqual({
      ok: false,
      reason: "alreadyArmed",
    });
  });

  it("간부 몫을 다 쓰면 한도로 거부한다", () => {
    const state = atPersonalPhase(beginDay(fullSquad()));
    state.trust.sergeantMajor = 90;
    state.officerReliefsRemaining = 0;

    expect(canUseOfficerRelief(state, "p1")).toEqual({
      ok: false,
      reason: "limitReached",
    });
  });

  it("존재하지 않는 분대원이면 거부한다", () => {
    const state = atPersonalPhase(beginDay(fullSquad()));
    expect(canUseOfficerRelief(state, "ghost")).toEqual({
      ok: false,
      reason: "unknownMember",
    });
  });
});
