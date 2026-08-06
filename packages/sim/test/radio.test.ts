import { describe, expect, it } from "vitest";
import { evaluateRadio, refreshRadio, step, type Quest, type RunState } from "../src/index.js";
import { createRun } from "../src/index.js";
import { beginDay, fullSquad, SECOND, withQuests } from "./helpers.js";

function upkeep(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q-radio",
    kind: "role",
    label: "무전기 배터리 교체",
    training: null,
    ownerId: "p2",
    required: true,
    phase: "morning",
    zone: "Z01",
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
    workMs: 10_000,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: null,
    grade: null,
    ...overrides,
  };
}

describe("8.0 무전 상태", () => {
  it("통신병이 조작 중이고 유지 일과가 멀쩡하면 정상", () => {
    const state = beginDay(fullSquad());
    expect(evaluateRadio(state)).toBe("ok");
  });

  it("통신병이 이탈하면 두절 — 유지할 사람이 없다", () => {
    const state = fullSquad();
    const left = step(beginDay(state), { type: "leaveRun", memberId: "p2" }).state;
    expect(evaluateRadio(left)).toBe("down");
  });

  it("후송 대리는 약함 — 필수만 수행하므로 유지는 되되 열화한다", () => {
    const state = beginDay(fullSquad());
    const comms = state.members.find((m) => m.id === "p2");
    if (comms) comms.presence = "npcEvac";
    expect(evaluateRadio(state)).toBe("weak");
  });

  it("유지 일과가 잠기면 약함, 실패하면 두절", () => {
    const base = beginDay(fullSquad());

    const locked = withQuests(base, [upkeep({ status: "locked" })]);
    expect(evaluateRadio(locked)).toBe("weak");

    const failed = withQuests(base, [upkeep({ status: "failed" })]);
    expect(evaluateRadio(failed)).toBe("down");
  });

  it("통신병 것이 아닌 무전 일과는 상태를 바꾸지 않는다", () => {
    const base = beginDay(fullSquad());
    const other = withQuests(base, [upkeep({ ownerId: "p1", status: "failed" })]);
    expect(evaluateRadio(other)).toBe("ok");
  });

  it("상태가 바뀔 때만 이벤트가 나간다", () => {
    let state: RunState = beginDay(fullSquad());
    state = withQuests(state, [upkeep({ status: "failed" })]);

    // 갱신은 **시간대 경계**에서 돈다. `applyTick`에서 매 틱 세면 헤드리스
    // 배치 시뮬이 퀘스트 배열을 수백만 번 훑어 밸런스 테스트가 타임아웃난다
    expect(refreshRadio(state)).toBe("down");
    expect(state.radio).toBe("down");

    // 같은 상태가 이어지면 조용하다 — 10Hz로 같은 이벤트를 흘리면
    // 클라이언트 알림 스택이 그것만으로 찬다
    expect(refreshRadio(state)).toBeNull();
  });

  it("시간대가 끝나면 갱신된다 — 못 끝낸 유지 일과는 잠기고 무전이 약해진다", () => {
    let state: RunState = beginDay(fullSquad());
    state = withQuests(state, [upkeep()]);

    // 시간대 종료가 미완료 퀘스트를 `locked`로 잠근다(4.0). 그래서 실제 플레이에서
    // 무전이 끊기는 경로는 대개 두절이 아니라 **열화**다 — 통신병이 자리에
    // 있는데 교신 시각을 놓친 상태다
    const ended = step(state, { type: "tick", elapsedMs: 10 * 60 * SECOND });
    expect(ended.state.radio).toBe("weak");
    expect(ended.effects.some((e) => e.type === "radioChanged")).toBe(true);
  });
});

describe("런 시작 무전 상태", () => {
  it("통신병이 NPC면 시작부터 두절이다 — 첫 시간대만 살아 있는 척하지 않는다", () => {
    // 회귀: `createRun`이 `radio: "ok"`를 하드코딩해 두고 "첫 tick에서 갱신된다"고
    // 적어 뒀는데, 실제 `refreshRadio`는 `endPhase`(시간대 경계)에서만 돈다.
    // 그래서 통신병이 NPC인 편성에서 기상·점호 내내 무전이 살아 있다가
    // 오전 일과가 시작하는 순간 두절로 뒤집혔다(사용자 신고).
    const state = createRun({
      runId: "r-npc-comms",
      seed: 1,
      members: [{ id: "p1", name: "소총", role: "rifle" }],
    });

    const comms = state.members.find((m) => m.role === "comms");
    expect(comms?.presence).toBe("npcVacant");
    expect(state.radio).toBe("down");
  });

  it("사람 통신병이 있으면 시작은 정상이다", () => {
    const state = createRun({
      runId: "r-human-comms",
      seed: 1,
      members: [
        { id: "p1", name: "소총", role: "rifle" },
        { id: "p2", name: "통신", role: "comms" },
      ],
    });
    expect(state.radio).toBe("ok");
  });
});
