import { describe, expect, it } from "vitest";
import {
  REVIEWS,
  gradeGain,
  step,
  type Grade,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, completeCareNow, fullSquad } from "./helpers.js";

/**
 * 수행 등급 → 복무 점수 (표 13-1).
 *
 * 등급은 **새 점수원이 아니라 기존 항목의 차등**이다. 퀘스트마다 점수를 새로
 * 얹으면 1인당 하루 6~8건이라 획득이 기존 경제의 2~3배가 되고, 심사 요구치가
 * 통째로 무의미해진다.
 *
 * 그래서 B를 기준선으로 잡았다 — **B만 받는 런은 등급이 없던 시절과 정확히 같은
 * 점수를 쌓는다.** 요구치(18 · 70 · 150)를 그대로 둘 수 있는 이유가 이것이고,
 * A는 승급을 여유 있게 만들고 C는 보류로 밀어 넣는다.
 */

/** 그날의 일과를 전부 같은 등급으로 끝내며 하루를 흘려보낸다 */
function playWithGrade(state: RunState, days: number, grade: Grade): RunState {
  let current = beginDay(state);

  for (let i = 0; i < days; i += 1) {
    const day = current.day;

    for (const quest of current.quests) {
      // 회복은 판정에도 점수에도 들어가지 않는다 — 아래 루프가 제 칸에서 처리한다
      if (quest.kind === "care") continue;
      quest.workedMs = quest.workMs;
      quest.status = "done";
      // 판이 없는 일과는 등급도 없다
      quest.grade = quest.minigame === null ? null : grade;
    }

    let guard = 0;
    while (current.status === "running" && current.day === day) {
      current = step(current, { type: "tick", elapsedMs: 30 * SECOND }).state;
      current = completeCareNow(current);
      if (guard++ > 100) throw new Error(`하루가 끝나지 않는다: D-${day}`);
    }
    if (current.status !== "running") break;
  }

  return current;
}

const scoreAfter = (days: number, grade: Grade): number => {
  const state = playWithGrade(fullSquad({ seed: 4242 }), days, grade);
  return state.members[0]!.serviceScore;
};

describe("등급이 점수를 가른다", () => {
  it("선택 퀘스트는 A 3 · B 2 · C 1이다", () => {
    expect(gradeGain("A")).toBe(3);
    expect(gradeGain("B")).toBe(2);
    expect(gradeGain("C")).toBe(1);
  });

  it("등급이 없으면 B로 친다", () => {
    // 판이 붙지 않은 일과(합동 · 훈련 체크포인트)와 NPC 대리가 끝낸 일과가
    // 여기 해당한다. 0으로 두면 아직 안 만든 원형이 승급을 막고, A로 두면
    // 대리에게 맡기는 편이 이득이 된다.
    expect(gradeGain(null)).toBe(gradeGain("B"));
  });

  it("A만 받는 런이 C만 받는 런보다 언제나 앞선다", () => {
    for (const days of [3, 9, 15]) {
      const a = scoreAfter(days, "A");
      const b = scoreAfter(days, "B");
      const c = scoreAfter(days, "C");
      expect(a, `D-${days}`).toBeGreaterThan(b);
      expect(b, `D-${days}`).toBeGreaterThan(c);
    }
  });
});

describe("승급 요구치가 등급 분포 위에서 성립한다", () => {
  // 심사는 D-03 / D-09 / D-15에 열리고, 그날 아침까지 쌓은 누적으로 본다.
  const reviewDays = REVIEWS.map((r) => r.day);

  it("B는 등급 도입 전과 정확히 같은 값을 준다 — 요구치를 손대지 않은 근거다", () => {
    // 예전 표 13-1의 `optionalRoleQuest`가 2였다. B가 그 2이고 필수는 0점이므로,
    // B만 받는 런은 등급이 없던 시절과 점수가 한 점도 다르지 않다.
    expect(gradeGain("B")).toBe(2);
  });

  it("B만 받아도 세 심사를 전부 통과한다", () => {
    for (const review of REVIEWS) {
      const score = scoreAfter(review.day, "B");
      expect(score, `D-${review.day} ${review.rank}`).toBeGreaterThanOrEqual(review.require);
    }
    expect(reviewDays).toEqual([3, 9, 15]);
  });

  it("A만 받으면 요구치를 여유 있게 넘긴다 — 잘한 것이 눈에 보인다", () => {
    for (const review of REVIEWS) {
      const a = scoreAfter(review.day, "A");
      const b = scoreAfter(review.day, "B");
      expect(a).toBeGreaterThan(b);
      expect(a, `D-${review.day}`).toBeGreaterThan(review.require);
    }
  });

  it("C는 뒤처지지만 완벽한 플레이라면 그래도 통과한다", () => {
    // **이 테스트가 재는 것은 등급이 아니라 요구치다.** 여기 런은 선택까지
    // 하나도 안 빠뜨리는 이상적인 런이라, C만 받아도 D-15에 요구치의 배를 쌓는다
    // (150 요구에 184). 등급을 넣기 전에도 마찬가지였다 — 18 · 70 · 150은
    // 완벽한 플레이를 기준으로는 원래 헐겁고, 실제 압력은 빠뜨린 선택 퀘스트
    // 쪽에서 온다. 등급은 그 위에 얹히는 차등이지 새 관문이 아니다.
    for (const review of REVIEWS) {
      const c = scoreAfter(review.day, "C");
      expect(c, `D-${review.day}`).toBeLessThan(scoreAfter(review.day, "B"));
      expect(c, `D-${review.day}`).toBeGreaterThanOrEqual(review.require);
    }
  });
});
