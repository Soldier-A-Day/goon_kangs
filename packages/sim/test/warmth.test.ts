import { describe, expect, it } from "vitest";
import { isHeatSource, step, type RunState } from "../src/index.js";
import { beginDay, fullSquad, SECOND } from "./helpers.js";

/** 극혹한 밴드를 강제한다 — 기온 롤을 기다리면 테스트가 시드에 묶인다 */
function freeze(state: RunState): RunState {
  state.weather = {
    band: "extremeCold",
    feelsLike: -14,
    airTemp: -14,
    humidity: 30,
    windSpeed: 4,
    rain: false,
  };
  return state;
}

function tick(state: RunState, seconds: number): RunState {
  return step(state, { type: "tick", elapsedMs: seconds * SECOND }).state;
}

function member(state: RunState, id: string) {
  const found = state.members.find((m) => m.id === id);
  if (!found) throw new Error(`분대원 없음: ${id}`);
  return found;
}

describe("5.0 보온 게이지", () => {
  it("극혹한이 아니면 게이지가 흐르지 않는다", () => {
    let state = beginDay(fullSquad());
    state.weather = { ...state.weather, band: "normal" };
    for (const m of state.members) m.zone = "Z11";

    state = tick(state, 120);

    expect(member(state, "p1").warmthRemainingMs).toBe(0);
    expect(member(state, "p1").frostbitten).toBe(false);
  });

  it("열원 구역에 있으면 게이지가 리셋된다", () => {
    let state = freeze(beginDay(fullSquad()));
    for (const m of state.members) m.zone = "Z01";

    state = tick(state, 200);

    // 90초를 훌쩍 넘겨도 실내에 있었으므로 가득 차 있다
    expect(member(state, "p1").warmthRemainingMs).toBe(90 * SECOND);
    expect(member(state, "p1").frostbitten).toBe(false);
  });

  it("야외에서 90초를 넘기면 동상이 붙는다", () => {
    let state = freeze(beginDay(fullSquad()));
    for (const m of state.members) m.zone = "Z11";

    const before = step(state, { type: "tick", elapsedMs: 80 * SECOND });
    expect(member(before.state, "p1").frostbitten).toBe(false);

    const after = step(before.state, { type: "tick", elapsedMs: 20 * SECOND });
    expect(member(after.state, "p1").frostbitten).toBe(true);
    expect(after.effects.some((e) => e.type === "frostbitten")).toBe(true);
  });

  it("동상은 열원에 돌아가는 것만으로는 안 풀린다 — 의무병이 있어야 한다", () => {
    let state = freeze(beginDay(fullSquad()));
    for (const m of state.members) m.zone = "Z11";

    state = tick(state, 100);
    expect(member(state, "p1").frostbitten).toBe(true);

    // 실내로 혼자 돌아가도 그대로다
    member(state, "p1").zone = "Z01";
    state = tick(state, 30);
    expect(member(state, "p1").frostbitten).toBe(true);

    // 의무병이 같은 열원 구역에 오면 풀린다
    member(state, "p3").zone = "Z01";
    member(state, "p3").frostbitten = false;
    const relieved = step(state, { type: "tick", elapsedMs: 1 * SECOND });
    expect(member(relieved.state, "p1").frostbitten).toBe(false);
    expect(relieved.effects.some((e) => e.type === "frostbiteRelieved")).toBe(true);
  });

  it("야외에서는 의무병이 옆에 있어도 못 푼다 — 실내로 데리고 들어가야 한다", () => {
    let state = freeze(beginDay(fullSquad()));
    for (const m of state.members) m.zone = "Z11";

    state = tick(state, 100);
    // 의무병도 같은 연병장에 있지만 열원이 아니라 처치가 안 된다
    expect(member(state, "p1").frostbitten).toBe(true);
    expect(member(state, "p3").zone).toBe("Z11");

    state = tick(state, 30);
    expect(member(state, "p1").frostbitten).toBe(true);
  });

  it("동상은 이동을 30% 느리게 만든다", () => {
    let state = freeze(beginDay(fullSquad()));
    for (const m of state.members) m.zone = "Z11";

    const healthy = step(state, { type: "move", memberId: "p1", to: "Z08" }).state;
    const baseline = member(healthy, "p1").travelRemainingMs;

    state = tick(state, 100);
    expect(member(state, "p1").frostbitten).toBe(true);
    member(state, "p1").zone = "Z11";
    member(state, "p1").travelRemainingMs = 0;

    const slowed = step(state, { type: "move", memberId: "p1", to: "Z08" }).state;
    expect(member(slowed, "p1").travelRemainingMs).toBeCloseTo(baseline / 0.7, 0);
  });

  it("열원 구역 표 — 야외는 열원이 아니다", () => {
    expect(isHeatSource("Z01")).toBe(true);
    expect(isHeatSource("Z14")).toBe(true);
    expect(isHeatSource("Z11")).toBe(false);
    expect(isHeatSource("Z12")).toBe(false);
  });
});
