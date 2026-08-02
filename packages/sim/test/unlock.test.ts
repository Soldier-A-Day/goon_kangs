import { describe, expect, it } from "vitest";
import {
  CURRICULUM,
  UNLOCK,
  createRngState,
  generateDayQuests,
  rollSurprise,
  canDelegate,
  unlocked,
  type RunState,
} from "../src/index.js";
import { beginDay, fullSquad, toPhase } from "./helpers.js";

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
