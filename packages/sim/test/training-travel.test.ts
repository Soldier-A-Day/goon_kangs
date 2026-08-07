import { describe, expect, it } from "vitest";
import trainingTable from "../data/training.json";
import { phaseAt, phaseDurationMsFor, type Quest, type RunState } from "../src/index.js";
import { SECOND, fullSquad } from "./helpers.js";

/**
 * 훈련장 이동 시간 (§9.0 · §6.1 동선 비용).
 *
 * **회귀 방지가 목적이다.** 훈련장은 전부 부대 밖인데 시간대는 60초 고정이라,
 * 생활관에서 사격장까지 왕복 61초 · 혹서기 급수 라인은 71초가 걸렸다 —
 * 걷기만 해도 시간대가 끝나서 훈련 체크포인트(필수, TRN-02)를 시작조차 못 했다.
 * 난이도가 아니라 **필수를 물리적으로 수행할 수 없는 상태**였고, 사용자가
 * 플레이하다 신고했다.
 *
 * 실제 거리와 표가 맞는지는 여기서 못 본다(맵은 아트 쪽이다) —
 * `tools/sprites/basemap.py`의 `_assert_training_travel_time()`이 씬 생성
 * 단계에서 플러드필로 실측해 대조한다. 여기서는 **sim이 그 표를 실제로
 * 시간대 길이에 반영하는가**만 본다.
 */

const PHASE_INDEX = { reveille: 0, morning: 1, lunch: 2, afternoon: 3 } as const;

function trainingQuest(zone: string, phase: Quest["phase"]): Quest {
  return {
    id: `cp-${zone}`,
    kind: "role",
    label: "사격 1구간",
    training: "marksmanship",
    ownerId: "p1",
    required: true,
    phase,
    zone: zone as Quest["zone"],
    spot: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
    workMs: 20 * SECOND,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: null,
    grade: null,
  };
}

/** 훈련 퀘스트만 놓은 상태 — 다른 요인(이월·군기 보너스)을 지운다 */
function withQuests(quests: Quest[]): RunState {
  const state = fullSquad();
  state.quests = quests;
  state.carryoverMs = 0;
  return state;
}

describe("훈련장 이동 시간", () => {
  it("훈련이 없는 시간대는 기본 길이 그대로다", () => {
    const state = withQuests([]);
    const base = phaseAt(PHASE_INDEX.morning).baseSeconds * SECOND;
    expect(phaseDurationMsFor(state, PHASE_INDEX.morning)).toBe(base);
  });

  it("훈련장 일과가 있는 시간대는 그만큼 길어진다", () => {
    const state = withQuests([trainingQuest("TR01", "morning")]);
    const base = phaseAt(PHASE_INDEX.morning).baseSeconds * SECOND;
    const travel = trainingTable.travelSeconds.TR01 * SECOND;

    expect(travel).toBeGreaterThan(0);
    expect(phaseDurationMsFor(state, PHASE_INDEX.morning)).toBe(base + travel);
  });

  it("훈련이 든 시간대에만 붙는다 — 그날의 다른 시간대는 그대로다", () => {
    const state = withQuests([trainingQuest("TR01", "morning")]);
    const base = phaseAt(PHASE_INDEX.lunch).baseSeconds * SECOND;
    expect(phaseDurationMsFor(state, PHASE_INDEX.lunch)).toBe(base);
  });

  it("가까운 훈련장보다 먼 훈련장에 더 준다", () => {
    // TR04 숙영지가 정문에서 가장 가깝고 TR06 혹서기 급수 라인이 가장 멀다
    const near = withQuests([trainingQuest("TR04", "morning")]);
    const far = withQuests([trainingQuest("TR06", "morning")]);
    expect(phaseDurationMsFor(far, PHASE_INDEX.morning))
      .toBeGreaterThan(phaseDurationMsFor(near, PHASE_INDEX.morning));
  });

  it("목적지는 퀘스트의 zone을 따른다 — 계절 훈련의 갈래가 여기서 갈린다", () => {
    // 계절 훈련은 그날 기온으로 TR05(혹한)·TR06(혹서)가 갈린다. 시간을 줄 때
    // 밴드를 다시 재면 퀘스트가 가리키는 곳과 어긋날 수 있어 zone을 그대로 쓴다
    const cold = withQuests([trainingQuest("TR05", "afternoon")]);
    const hot = withQuests([trainingQuest("TR06", "afternoon")]);
    const base = phaseAt(PHASE_INDEX.afternoon).baseSeconds * SECOND;

    expect(phaseDurationMsFor(cold, PHASE_INDEX.afternoon))
      .toBe(base + trainingTable.travelSeconds.TR05 * SECOND);
    expect(phaseDurationMsFor(hot, PHASE_INDEX.afternoon))
      .toBe(base + trainingTable.travelSeconds.TR06 * SECOND);
  });

  it("한 시간대에 훈련장이 둘이면 먼 쪽을 기준으로 준다", () => {
    // 가까운 쪽에 맞추면 먼 쪽이 다시 도달 불가가 된다
    const state = withQuests([
      trainingQuest("TR04", "morning"),
      trainingQuest("TR06", "morning"),
    ]);
    const base = phaseAt(PHASE_INDEX.morning).baseSeconds * SECOND;
    expect(phaseDurationMsFor(state, PHASE_INDEX.morning))
      .toBe(base + trainingTable.travelSeconds.TR06 * SECOND);
  });

  it("훈련장 10곳 전부 이동 시간이 정해져 있다", () => {
    // 한 곳이라도 빠지면 그 훈련일만 조용히 옛 상태(수행 불가)로 돌아간다
    const zones = Object.keys(trainingTable.travelSeconds);
    for (let i = 1; i <= 10; i += 1) {
      expect(zones).toContain(`TR${String(i).padStart(2, "0")}`);
    }
    for (const seconds of Object.values(trainingTable.travelSeconds)) {
      expect(seconds).toBeGreaterThan(0);
    }
  });

  it("이월분과 같이 붙는다 — 스킵으로 아낀 시간이 사라지지 않는다", () => {
    const state = withQuests([trainingQuest("TR01", "morning")]);
    state.carryoverMs = 7 * SECOND;
    const base = phaseAt(PHASE_INDEX.morning).baseSeconds * SECOND;
    const travel = trainingTable.travelSeconds.TR01 * SECOND;
    expect(phaseDurationMsFor(state, PHASE_INDEX.morning))
      .toBe(base + travel + 7 * SECOND);
  });
});
