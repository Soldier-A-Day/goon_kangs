import { describe, expect, it } from "vitest";
import {
  CURRICULUM,
  REAL_TIME_FLOOR_MS,
  UNLOCK,
  createRngState,
  generateDayQuests,
  rollSurprise,
  canDelegate,
  step,
  unlocked,
  type RunState,
} from "../src/index.js";
import { beginDay, completeRequired, fullSquad, toPhase } from "./helpers.js";

/**
 * 표 14-1 해금 일정 — **문서가 아니라 규칙이다.**
 *
 * 예전에는 `unlocks`를 아무도 읽지 않았다. 커리큘럼은 "공통 일과는 D-2부터,
 * 돌발은 D-4부터"라고 적어놨는데 배정기는 D-1부터 전부 냈다. 표에 적힌
 * 난이도 곡선이 실제로는 존재하지 않았고, 첫날이 3일차와 같은 밀도로 시작했다.
 *
 * 여기서 지키는 것은 두 가지다 — 열리기 전에는 안 나오고, 열린 뒤에는 계속 나온다.
 */

function stateAt(day: number, seed = 7): RunState {
  const state = fullSquad({ seed });
  state.day = day;
  state.rngState = createRngState(seed * 31 + day);
  // 이 아래 "D-N부터" 묘사들은 일차 게이트를 잰다 — F-1의 실시간 하한이
  // 우연히 끼어들지 않도록 충분히 큰 값으로 채워 둔다. 하한 자체는 별도
  // describe 블록에서 elapsedRealMs를 직접 눌러 잰다.
  state.elapsedRealMs = Number.MAX_SAFE_INTEGER;
  return state;
}

const choresOn = (day: number, seeds = 20) => {
  let n = 0;
  for (let seed = 1; seed <= seeds; seed += 1) {
    const [quests] = generateDayQuests(stateAt(day, seed));
    n += quests.filter((q) => q.kind === "chore").length;
  }
  return n;
};

describe("해금은 누적이다", () => {
  it("한 번 열리면 마지막 날까지 열려 있다", () => {
    expect(unlocked(1, UNLOCK.chores)).toBe(false);
    expect(unlocked(2, UNLOCK.chores)).toBe(true);
    expect(unlocked(18, UNLOCK.chores)).toBe(true);
  });

  it("커리큘럼에 없는 열쇠는 영영 안 열린다 — 오타가 조용히 게이트를 열지 않게", () => {
    expect(unlocked(18, "없는 기능")).toBe(false);
  });
});

describe("공통 일과 — D-2부터", () => {
  it("D-1에는 한 건도 없다", () => {
    // D-1의 필수는 보직 2건뿐이라 공통이 없어도 하루가 성립한다
    expect(choresOn(1)).toBe(0);
    expect(CURRICULUM[0]!.required.roleQuests).toBe(CURRICULUM[0]!.required.total);
  });

  it("D-2부터 나온다", () => {
    expect(choresOn(2)).toBeGreaterThan(0);
    expect(choresOn(5)).toBeGreaterThan(0);
  });
});

describe("돌발 — D-4부터", () => {
  it("D-1~3에는 뜨지 않는다", () => {
    // 조작도 안 익은 첫 사흘에 일과를 끊고 들어오면 압박이 아니라 사고다
    for (const day of [1, 2, 3]) {
      for (let seed = 1; seed <= 40; seed += 1) {
        const state = stateAt(day, seed);
        // 군기를 바닥에 두어 발생률을 최대로 올려도 안 나와야 한다
        state.discipline = 0;
        const [surprise] = rollSurprise(state, "morning");
        expect(surprise, `D-${day} seed ${seed}`).toBeNull();
      }
    }
  });

  it("D-4부터 뜬다", () => {
    let seen = 0;
    for (let seed = 1; seed <= 60; seed += 1) {
      const state = stateAt(4, seed);
      state.discipline = 0;
      const [surprise] = rollSurprise(state, "morning");
      if (surprise) seen += 1;
    }
    expect(seen).toBeGreaterThan(0);
  });
});

describe("하달 — D-3부터", () => {
  it("D-2에는 거절된다", () => {
    // 계급으로도 막히지만 그건 승급 시점이 우연히 맞는 것이다.
    // 규칙은 커리큘럼이 소유한다
    let state = beginDay(stateAt(2));
    state = toPhase(state, "morning");
    const chore = state.quests.find((q) => q.kind === "chore" || q.kind === "role");
    if (!chore?.ownerId) throw new Error("일과 없음");

    const other = state.members.find((m) => m.id !== chore.ownerId)!;
    const result = canDelegate(state, chore.ownerId, other.id, chore.id);
    expect(result.ok).toBe(false);
    if (!result.ok) expect(result.reason).toBe("locked");
  });

  it("D-3부터는 해금이 막지 않는다", () => {
    let state = beginDay(stateAt(3));
    state = toPhase(state, "morning");
    const chore = state.quests.find((q) => q.kind === "chore");
    if (!chore?.ownerId) return; // 그날 공통이 안 뽑혔으면 볼 것이 없다

    const other = state.members.find((m) => m.id !== chore.ownerId)!;
    const result = canDelegate(state, chore.ownerId, other.id, chore.id);
    // 계급·한도로 거절될 수는 있어도 **해금으로는** 막히지 않는다
    if (!result.ok) expect(result.reason).not.toBe("locked");
  });
});

/**
 * F-1 — 일차만으로는 스킵 투표를 못 막는다.
 *
 * `services/gameserver/src/room.ts`의 스킵 투표는 쿨다운이 없다. 4인이 매
 * 시간대를 몰아서 스킵하면 `day`·`phaseIndex`는 실시간 없이도 앞으로 간다 —
 * 그러면 일차 게이트만으로는 D-2·D-3이 몇 초 만에 다 열린다. `elapsedRealMs`는
 * `tick` 이벤트로만 늘고 `skipPhase`는 절대 건드리지 않으므로(`step.ts`), 이
 * 값을 함께 보면 스킵으로 우회되지 않는다.
 */
describe("F-1 — 실시간 하한 (스킵 투표로 우회 못 한다)", () => {
  it("일차가 열려도 실시간이 하한 밑이면 안 열리고, 하한에 닿으면 열린다", () => {
    const floor = REAL_TIME_FLOOR_MS[UNLOCK.chores]!;
    expect(floor).toBeGreaterThan(0);
    expect(unlocked(2, UNLOCK.chores, 0)).toBe(false);
    expect(unlocked(2, UNLOCK.chores, floor - 1)).toBe(false);
    expect(unlocked(2, UNLOCK.chores, floor)).toBe(true);
  });

  it("elapsedRealMs를 생략한 옛 2-인자 호출은 일차만 본다 (하위호환)", () => {
    expect(unlocked(2, UNLOCK.chores)).toBe(true);
    expect(unlocked(3, UNLOCK.delegation)).toBe(true);
    expect(unlocked(4, UNLOCK.surprise)).toBe(true);
  });

  it("스킵 투표를 몰아쳐 일차를 밀어도 실시간이 0이면 세 게이트 다 잠긴 채다", () => {
    // 판정(조건 A·B)에 걸려 런이 일찍 끝나지 않게, 매 칸 필수·합동을 자동으로
    // 채우면서 오직 `skipPhase`로만 하루를 넘긴다 — 실제 tick은 한 번도 안 준다.
    let state = beginDay(fullSquad());
    completeRequired(state);
    for (let i = 0; i < 30 && state.status === "running" && state.day < 4; i += 1) {
      state = step(state, { type: "skipPhase" }).state;
      completeRequired(state);
    }

    expect(state.day).toBeGreaterThanOrEqual(4); // 스킵만으로 D-4까지 밀렸다
    expect(state.elapsedRealMs).toBe(0); // 그런데 실시간은 안 흘렀다 — 일차 게이트는 셋 다 열려 있어야 정상이다
    expect(unlocked(state.day, UNLOCK.chores)).toBe(true); // (하한 없이는 이미 열렸을 일차)
    expect(unlocked(state.day, UNLOCK.delegation)).toBe(true);
    expect(unlocked(state.day, UNLOCK.surprise)).toBe(true);

    // 그런데도 실시간 하한 때문에 셋 다 잠겨 있다
    expect(unlocked(state.day, UNLOCK.chores, state.elapsedRealMs)).toBe(false);
    expect(unlocked(state.day, UNLOCK.delegation, state.elapsedRealMs)).toBe(false);
    expect(unlocked(state.day, UNLOCK.surprise, state.elapsedRealMs)).toBe(false);
  });

  it("스킵과 실제 tick이 섞여도 하한은 실제로 흐른 시간만 센다", () => {
    let state = beginDay(fullSquad());
    // 60초 실 tick 한 번 + 스킵 반복 — 시간대는 몇 칸이든 넘어가지만
    // 실시간은 그 60초 하나로 고정된다
    state = step(state, { type: "tick", elapsedMs: 60_000 }).state;
    for (let i = 0; i < 30 && state.status === "running"; i += 1) {
      state = step(state, { type: "skipPhase" }).state;
    }

    expect(state.elapsedRealMs).toBe(60_000);
    expect(unlocked(state.day, UNLOCK.chores, state.elapsedRealMs)).toBe(false);
  });

  it("초행 4인 세션 첫 10분(실시간 600000ms) 안에는 실시간 하한이 걸린 키가 하나도 안 열린다", () => {
    for (const key of Object.keys(REAL_TIME_FLOOR_MS)) {
      expect(unlocked(18, key, 10 * 60 * 1000 - 1), key).toBe(false);
    }
  });
});
