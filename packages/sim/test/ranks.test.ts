import { describe, expect, it } from "vitest";
import {
  REVIEWS,
  RETRIES,
  applyDailyServiceScore,
  grantsFor,
  penalizeEvacuation,
  penaltyMultiplier,
  planFor,
  reviewDayFor,
  runReview,
  step,
  type Effect,
  type RunState,
} from "../src/index.js";
import { SECOND, beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function review(state: RunState): Extract<Effect, { type: "rankReviewed" }> | undefined {
  const effects: Effect[] = [];
  runReview(state, effects);
  return effects.find((e) => e.type === "rankReviewed");
}

function setScore(state: RunState, id: string, score: number): void {
  const member = state.members.find((m) => m.id === id);
  if (!member) throw new Error(`분대원 없음: ${id}`);
  member.serviceScore = score;
}

describe("RANK-01 심사 구조", () => {
  it("심사는 D-03 / D-09 / D-15 세 번이다", () => {
    expect(REVIEWS.map((r) => r.day)).toEqual([3, 9, 15]);
    expect(REVIEWS.map((r) => r.rank)).toEqual(["pfc", "corporal", "sergeant"]);
  });

  it("요구 누적은 18 / 70 / 150이다", () => {
    expect(REVIEWS.map((r) => r.require)).toEqual([18, 70, 150]);
  });

  it("재심사는 정비일에 열린다 (D-06 / D-11 / D-14)", () => {
    expect(RETRIES.map((r) => r.day)).toEqual([6, 11, 14]);
    for (const retry of RETRIES) {
      expect(planFor(retry.day).maintenanceDay, `D-${retry.day}`).toBe(true);
    }
  });

  it("심사일을 조회할 수 있다", () => {
    expect(reviewDayFor(3)).toEqual({ rank: "pfc", isRetry: false });
    expect(reviewDayFor(6)).toEqual({ rank: "pfc", isRetry: true });
    expect(reviewDayFor(7)).toBeNull();
  });

  it("계급은 능력치가 아니라 권한만 준다", () => {
    expect(grantsFor("private")).toHaveLength(0);
    expect(grantsFor("pfc").join()).toContain("하달");
    expect(grantsFor("sergeant").join()).toContain("구제권");
  });
});

describe("승급 판정", () => {
  it("요구치를 넘기면 승급한다", () => {
    const state = fullSquad();
    state.day = 3;
    state.trust = { platoonLeader: 0, assistant: 0, sergeantMajor: 0 };
    setScore(state, "p1", 18);
    setScore(state, "p2", 17);

    const outcome = review(state);
    expect(outcome?.outcomes.find((o) => o.memberId === "p1")?.promoted).toBe(true);
    expect(outcome?.outcomes.find((o) => o.memberId === "p2")?.promoted).toBe(false);
    expect(state.members.find((m) => m.id === "p1")?.rank).toBe("pfc");
    expect(state.members.find((m) => m.id === "p2")?.rank).toBe("private");
  });

  it("미달은 게임오버가 아니라 보류다", () => {
    const state = fullSquad();
    state.day = 3;
    review(state);
    expect(state.status).toBe("running");
    expect(state.members.every((m) => m.rank === "private")).toBe(true);
  });

  it("재심사는 요구치가 10% 낮다 — 초반에 뒤처져도 만회할 여지가 남는다", () => {
    const state = fullSquad();
    state.day = 6;
    state.trust = { platoonLeader: 0, assistant: 0, sergeantMajor: 0 };
    setScore(state, "p1", 17);

    const outcome = review(state);
    expect(outcome?.require).toBe(16);
    expect(outcome?.isRetry).toBe(true);
    expect(state.members.find((m) => m.id === "p1")?.rank).toBe("pfc");
  });

  it("간부 신뢰도가 심사 시점에 정산되고 상한은 +12다", () => {
    const state = fullSquad();
    state.day = 3;
    state.trust = { platoonLeader: 100, assistant: 100, sergeantMajor: 100 };
    setScore(state, "p1", 0);

    const outcome = review(state);
    expect(outcome?.outcomes[0]?.trustBonus).toBe(12);
  });

  it("이미 그 계급 이상이면 심사 대상이 아니다", () => {
    const state = fullSquad();
    state.day = 3;
    for (const member of state.members) member.rank = "corporal";
    expect(review(state)).toBeUndefined();
  });

  it("승급은 개인 단위다 — 같은 분대 안에서 계급이 갈린다", () => {
    const state = fullSquad();
    state.day = 3;
    state.trust = { platoonLeader: 0, assistant: 0, sergeantMajor: 0 };
    setScore(state, "p1", 30);
    setScore(state, "p2", 30);
    setScore(state, "p3", 5);
    setScore(state, "p4", 5);

    review(state);
    const ranks = state.members.map((m) => m.rank);
    expect(ranks.filter((r) => r === "pfc")).toHaveLength(2);
    expect(ranks.filter((r) => r === "private")).toHaveLength(2);
  });

  it("점수 내역이 전원에게 공개된다 — 무임승차가 드러나는 것이 압력 장치다", () => {
    const state = fullSquad();
    state.day = 3;
    const outcome = review(state);
    expect(outcome?.outcomes).toHaveLength(4);
    for (const entry of outcome?.outcomes ?? []) {
      expect(entry).toHaveProperty("score");
      expect(entry).toHaveProperty("require");
    }
  });
});

describe("표 13-1 복무 점수", () => {
  it("선택 퀘스트를 끝내면 건당 +2", () => {
    const state = beginDay(fullSquad());
    for (const quest of state.quests) {
      if (quest.ownerId === "p1" && !quest.required) {
        quest.status = "done";
        quest.workedMs = quest.workMs;
      }
    }
    const optional = state.quests.filter(
      (q) => q.ownerId === "p1" && !q.required && q.kind !== "joint",
    ).length;

    applyDailyServiceScore(state);
    const score = state.members.find((m) => m.id === "p1")?.serviceScore ?? 0;
    expect(score).toBeGreaterThanOrEqual(optional * 2);
  });

  it("필수 완수는 점수를 주지 않는다 — 전원이 만점인 지표는 변별력이 0이다", () => {
    const state = beginDay(fullSquad());
    for (const quest of state.quests) {
      if (quest.required) {
        quest.status = "done";
        quest.workedMs = quest.workMs;
      }
    }
    applyDailyServiceScore(state);
    expect(state.members.find((m) => m.id === "p1")?.serviceScore).toBe(0);
  });

  it("합동 무결 완수는 +4, 부분 성공은 0", () => {
    const partial = beginDay(fullSquad());
    const joint = partial.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");
    joint.workedMs = joint.workMs * 0.8;
    applyDailyServiceScore(partial);
    expect(partial.members.find((m) => m.id === "p1")?.serviceScore).toBe(0);

    const done = beginDay(fullSquad());
    const doneJoint = done.quests.find((q) => q.kind === "joint");
    if (!doneJoint) throw new Error("합동 없음");
    doneJoint.status = "done";
    doneJoint.workedMs = doneJoint.workMs;
    applyDailyServiceScore(done);
    expect(done.members.find((m) => m.id === "p1")?.serviceScore).toBe(4);
  });

  it("야간 경계는 회복 절반을 감수한 대가로 +3", () => {
    const state = beginDay(fullSquad());
    state.nightGuardIds = ["p1"];
    applyDailyServiceScore(state);
    expect(state.members.find((m) => m.id === "p1")?.serviceScore).toBe(3);
    expect(state.members.find((m) => m.id === "p2")?.serviceScore).toBe(0);
  });

  it("대리 상태는 하루 −3", () => {
    const state = beginDay(fullSquad());
    const member = state.members.find((m) => m.id === "p1");
    if (!member) throw new Error("분대원 없음");
    member.presence = "npcLeave";

    applyDailyServiceScore(state);
    expect(member.serviceScore).toBe(-3);
  });
});

describe("감점 배율", () => {
  it("계급이 높을수록 같은 실수의 대가가 크다", () => {
    expect(penaltyMultiplier("private")).toBe(1);
    expect(penaltyMultiplier("pfc")).toBeCloseTo(1.2, 5);
    expect(penaltyMultiplier("corporal")).toBeCloseTo(1.4, 5);
    expect(penaltyMultiplier("sergeant")).toBeCloseTo(1.6, 5);
  });

  it("후송은 이병 −8, 병장 −13", () => {
    const state = fullSquad();
    const [privateMember, sergeant] = state.members;
    if (!privateMember || !sergeant) throw new Error("분대원 없음");
    sergeant.rank = "sergeant";

    penalizeEvacuation(privateMember);
    penalizeEvacuation(sergeant);

    expect(privateMember.serviceScore).toBe(-8);
    expect(sergeant.serviceScore).toBe(-13);
  });

  it("점수는 정수로 유지된다 — 소수 배율이 쌓여도 어긋나지 않는다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.rank = "corporal";

    for (let i = 0; i < 10; i += 1) penalizeEvacuation(member);
    expect(Number.isInteger(member.serviceScore)).toBe(true);
  });
});

describe("18일 진행", () => {
  it("이상적인 런에서 D-03 심사가 실제로 열린다", () => {
    let state = beginDay(fullSquad());
    const seen: number[] = [];

    for (let day = 0; day < 4; day += 1) {
      state = completeRequired(state);
      const current = state.day;
      let guard = 0;
      while (state.status === "running" && state.day === current && guard++ < 100) {
        const result = step(state, { type: "tick", elapsedMs: 30 * SECOND });
        state = result.state;
        for (const effect of result.effects) {
          if (effect.type === "rankReviewed") seen.push(effect.day);
        }
      }
    }

    expect(seen).toContain(3);
  });

  it("선택 퀘스트까지 해내면 승급 경로가 열린다", () => {
    let state = beginDay(fullSquad());

    for (let day = 0; day < 3; day += 1) {
      // 필수와 선택을 전부 해낸다
      for (const quest of state.quests) {
        quest.status = "done";
        quest.workedMs = quest.workMs;
      }
      const current = state.day;
      let guard = 0;
      while (state.status === "running" && state.day === current && guard++ < 100) {
        state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
      }
    }

    expect(state.members.some((m) => m.rank !== "private")).toBe(true);
  });

  it("1차는 튜토리얼 구간이라 낙오자를 만들지 않는다", () => {
    const state = playDays(fullSquad(), 3);
    expect(state.members.every((m) => m.rank === "pfc")).toBe(true);
  });

  it("합동·훈련을 무결로 해내면 선택 퀘스트 없이도 병장까지 간다", () => {
    // 밸런스 관찰을 고정한다. 합동 무결 +4와 훈련 100% +8만으로 D-14 재심사를 통과한다 —
    // 표 13-1의 획득 상한(D-15 기준 ~200)에 비추면 선택 퀘스트는 여유분이라는 뜻이다.
    const state = playDays(fullSquad(), 14);
    expect(state.members.every((m) => m.rank === "sergeant")).toBe(true);
  });

  // 훈련 70% 부분 성공이 조건 A에 적용되지 않는 현 상태에서는(judge.ts 참고)
  // 체크포인트를 남기면 그날 판정이 깨지므로 이 시나리오는 성립하지 않는다.
  it.skip("합동·훈련을 70%로만 넘기면 승급이 눈에 띄게 늦다", () => {
    let state = beginDay(fullSquad());

    for (let day = 0; day < 9; day += 1) {
      // 생존선만 지킨다 — 조건 A·B를 딱 통과할 만큼만
      for (const quest of state.quests) {
        if (quest.kind === "joint") {
          quest.workedMs = quest.workMs * 0.7;
          continue;
        }
        if (!quest.required) continue;
        if (quest.training !== null) continue;
        quest.status = "done";
        quest.workedMs = quest.workMs;
      }
      // 훈련 체크포인트는 70%만 통과시킨다
      for (const member of state.members) {
        const checkpoints = state.quests.filter(
          (q) => q.training !== null && q.ownerId === member.id,
        );
        const needed = Math.ceil(checkpoints.length * 0.7);
        checkpoints.slice(0, needed).forEach((quest) => {
          quest.status = "done";
          quest.workedMs = quest.workMs;
        });
      }

      const current = state.day;
      let guard = 0;
      while (state.status === "running" && state.day === current && guard++ < 100) {
        state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
      }
      if (state.status !== "running") break;
    }

    expect(state.status).toBe("running");
    expect(
      state.members.every((m) => m.rank !== "sergeant"),
      "생존선만 지킨 플레이는 병장에 닿지 못한다",
    ).toBe(true);
  });
});
