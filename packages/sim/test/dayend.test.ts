import { describe, expect, it } from "vitest";
import {
  DAY_END_BACKSTOP_MS,
  isDayEndWindow,
  step,
  type RunState,
} from "../src/index.js";
import { FULL_DAY, SECOND, beginDay, fullSquad, withQuests } from "./helpers.js";

/**
 * D1 — 하루 마감 확인 창.
 *
 * 사용자 신고 2건("확인 누르면 다음날 요약이 뜬다" · "확인 전에 시간이 간다")의
 * 공통 원인은 하나였다 — `endDay`가 판정 직후 곧바로 `state.day += 1`과
 * `beginDay`까지 동기로 끝내버려 서버가 확인을 기다리지 않았다는 것.
 *
 * 여기서는 하달 창(QST-04)과 같은 자리·같은 방식으로 만든 확인 게이트가
 * 실제로 하루 경계에서 시간을 멈추고, 확인이 다 모이거나 백스톱을 넘겨야만
 * 다음 날이 열리는지를 확인한다.
 */

/** 필수 없는 빈 일과로 하루를 끝까지 흘려 판정을 통과시키고, 마감 창이 열린 상태로 멈춘다 */
function reachDayEnd(state: RunState = fullSquad()): RunState {
  const started = withQuests(beginDay(state), []);
  return step(started, { type: "tick", elapsedMs: FULL_DAY }).state;
}

describe("D1 — 판정 직후: 마감 창이 열리고 day는 그대로다", () => {
  it("판정·승급·취침 정산이 끝나도 day는 아직 안 오른다", () => {
    const state = reachDayEnd();

    expect(state.status).toBe("running");
    expect(state.day).toBe(1);
    expect(isDayEndWindow(state)).toBe(true);
    expect(state.dayEndWindowMsLeft).toBe(DAY_END_BACKSTOP_MS);
    // 취침 정산까지는 끝났다 — 판정 이벤트가 이미 나갔다는 뜻이다
    expect(state.judgements).toHaveLength(1);
    expect(state.judgements[0]?.passed).toBe(true);
  });

  it("런이 여기서 끝나면(discharged 등) 마감 창은 열리지 않는다 — 다음 날이 없다", () => {
    const started = withQuests(beginDay(fullSquad({ config: { difficulty: "regular" } })), [
      {
        id: "a",
        kind: "role",
        label: "미완료 필수",
        training: null,
        ownerId: "p1",
        required: true,
        phase: "reveille",
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
      },
    ]);
    const result = step(started, { type: "tick", elapsedMs: FULL_DAY }).state;

    expect(result.status).toBe("discharged");
    expect(isDayEndWindow(result)).toBe(false);
  });
});

describe("D1 — 창이 열린 동안은 시간대 타이머가 멈춘다", () => {
  it("tick이 와도 day·phaseIndex는 그대로고, 창 잔여만 줄어든다", () => {
    let state = reachDayEnd();
    const phaseIndexAtEnd = state.phaseIndex;
    const phaseElapsedAtEnd = state.phaseElapsedMs;

    state = step(state, { type: "tick", elapsedMs: 10 * SECOND }).state;

    expect(state.day).toBe(1);
    expect(state.phaseIndex).toBe(phaseIndexAtEnd);
    expect(state.phaseElapsedMs).toBe(phaseElapsedAtEnd);
    expect(state.dayEndWindowMsLeft).toBe(DAY_END_BACKSTOP_MS - 10 * SECOND);
  });
});

describe("D1 — 전원 확인", () => {
  it("사람 참석자 전원이 확인하면 백스톱을 기다리지 않고 즉시 다음 날이 열린다", () => {
    let state = reachDayEnd();

    for (const memberId of ["p1", "p2", "p3"]) {
      state = step(state, { type: "dayEndAck", memberId }).state;
      // 아직 한 명 남았으니 안 넘어간다
      expect(state.day).toBe(1);
      expect(isDayEndWindow(state)).toBe(true);
    }

    state = step(state, { type: "dayEndAck", memberId: "p4" }).state;

    expect(state.day).toBe(2);
    expect(state.phaseIndex).toBe(0);
    expect(isDayEndWindow(state)).toBe(false);
  });

  it("NPC 대리는 확인 대상이 아니다 — 1~3인 방도 나머지 사람만 확인하면 넘어간다", () => {
    let state = reachDayEnd(fullSquad({ members: [{ id: "p1", name: "김소총", role: "rifle" }] }));
    expect(isDayEndWindow(state)).toBe(true);

    state = step(state, { type: "dayEndAck", memberId: "p1" }).state;

    expect(state.day).toBe(2);
    expect(isDayEndWindow(state)).toBe(false);
  });

  it("이미 닫힌 창에 확인을 또 보내도 무해하다", () => {
    let state = reachDayEnd();
    for (const memberId of ["p1", "p2", "p3", "p4"]) {
      state = step(state, { type: "dayEndAck", memberId }).state;
    }
    expect(state.day).toBe(2);

    const again = step(state, { type: "dayEndAck", memberId: "p1" }).state;
    expect(again.day).toBe(2);
  });
});

describe("D1 — 백스톱", () => {
  it("아무도 확인하지 않아도 백스톱을 넘기면 자동으로 다음 날이 열린다", () => {
    let state = reachDayEnd();

    // 백스톱 문턱 바로 아래까지는 아직 안 넘어간다
    state = step(state, { type: "tick", elapsedMs: DAY_END_BACKSTOP_MS - 1 }).state;
    expect(state.day).toBe(1);
    expect(state.dayEndWindowMsLeft).toBe(1);

    state = step(state, { type: "tick", elapsedMs: 1 }).state;
    expect(state.day).toBe(2);
    expect(isDayEndWindow(state)).toBe(false);
  });

  it("백스톱 초과분(leftover ms)은 버려지지 않고 다음 날 시간대에 이어진다", () => {
    let state = reachDayEnd();

    // 백스톱을 10초 넘겨서 한 번에 준다 — reveille 칸은 하달 창이 없으므로
    // (data/phases.json) 남은 10초는 곧바로 다음 날 reveille의 시간대
    // 타이머로 이어져야 한다
    state = step(state, { type: "tick", elapsedMs: DAY_END_BACKSTOP_MS + 10 * SECOND }).state;

    expect(state.day).toBe(2);
    expect(state.phaseIndex).toBe(0);
    expect(state.phaseElapsedMs).toBe(10 * SECOND);
  });
});

describe("D1 — 창 중 이탈", () => {
  it("창이 열린 동안 나간 사람은 확인 대기 대상에서 빠진다", () => {
    let state = reachDayEnd();

    state = step(state, { type: "leaveRun", memberId: "p4" }).state;
    expect(state.members.find((m) => m.id === "p4")?.presence).toBe("npcLeave");

    for (const memberId of ["p1", "p2", "p3"]) {
      state = step(state, { type: "dayEndAck", memberId }).state;
    }

    expect(state.day).toBe(2);
    expect(isDayEndWindow(state)).toBe(false);
  });
});
