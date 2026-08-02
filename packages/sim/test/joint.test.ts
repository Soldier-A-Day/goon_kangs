import { describe, expect, it } from "vitest";
import { step, type Quest, type RunState } from "../src/index.js";
import { SECOND, beginDay, fullSquad, toPhase } from "./helpers.js";

/**
 * QST-01 합동 — **판 하나를 인원이 나눠 채운다.**
 *
 * 목적은 하나다: 한 명이 잘해서 캐리하는 구조를 물리적으로 막는다.
 * 그래서 여기서 지키는 것도 하나다 — **인원이 모이지 않으면 한 조각도 오르지
 * 않는다.** 나머지(대리 기여 · 완료 판정)는 그 규칙이 1~3인 방을 죽이지
 * 않는지를 본다(ROLE-03).
 */

const squad = (n: number) =>
  fullSquad({
    members: [
      { id: "p1", name: "김소총", role: "rifle" as const },
      { id: "p2", name: "이통신", role: "comms" as const },
      { id: "p3", name: "박의무", role: "medic" as const },
      { id: "p4", name: "최행정", role: "admin" as const },
    ].slice(0, n),
  });

/** 합동 퀘스트와 그 자리에 선 분대 */
function atJoint(members: number): { state: RunState; joint: Quest } {
  let state = beginDay(squad(members));
  const joint = state.quests.find((q) => q.kind === "joint");
  if (!joint) throw new Error("합동 없음");

  state = toPhase(state, joint.phase);
  for (const member of state.members) {
    state = step(state, { type: "move", memberId: member.id, to: joint.zone }).state;
    state.members.find((m) => m.id === member.id)!.travelRemainingMs = 0;
  }
  return { state, joint };
}

const find = (state: RunState, id: string) => state.quests.find((q) => q.id === id)!;

describe("합동 판", () => {
  it("17건 전부 판과 조각 수를 갖는다", () => {
    // 판이 없으면 그 합동은 시간으로 끝나고, 그러면 강제 협동이 아니다
    for (let day = 1; day <= 18; day += 1) {
      const state = beginDay(Object.assign(squad(4), { day }));
      const joint = state.quests.find((q) => q.kind === "joint");
      if (!joint) continue;
      expect(joint.minigame, `D-${day} ${joint.label}`).not.toBeNull();
      expect(joint.jointTotal, `D-${day} ${joint.label}`).toBeGreaterThan(0);
      // 한 사람이 다 채울 수 있으면 협동이 아니라 대기다
      expect(joint.jointTotal, `D-${day}`).toBeGreaterThanOrEqual(joint.minActors);
    }
  });

  it("요구 인원이 모이면 조각이 오른다", () => {
    const { state, joint } = atJoint(4);
    const after = step(state, {
      type: "jointStep",
      memberId: "p1",
      questId: joint.id,
    }).state;
    expect(find(after, joint.id).jointDone).toBe(1);
  });

  it("혼자면 한 조각도 오르지 않는다 — 이게 강제 협동이다", () => {
    // 4인 분대에서 셋이 딴 데 있으면, 남은 하나가 아무리 눌러도 안 오른다.
    // 게이지가 안 차는 것이 곧 경고이고, 화면은 그것을 읽어 준다.
    const { state, joint } = atJoint(4);
    for (const id of ["p2", "p3", "p4"]) {
      state.members.find((m) => m.id === id)!.zone = joint.zone === "Z01" ? "Z11" : "Z01";
    }

    let after = state;
    for (let i = 0; i < 20; i += 1) {
      after = step(after, { type: "jointStep", memberId: "p1", questId: joint.id }).state;
    }
    expect(find(after, joint.id).jointDone).toBe(0);
    expect(find(after, joint.id).status).not.toBe("done");
  });

  it("조각을 다 채우면 완료된다", () => {
    const { state, joint } = atJoint(4);
    let after = state;
    for (let i = 0; i < joint.jointTotal; i += 1) {
      after = step(after, { type: "jointStep", memberId: "p1", questId: joint.id }).state;
    }
    expect(find(after, joint.id).status).toBe("done");
    // 합동에는 등급이 없다 — 분대가 같이 채운 것을 개인 등급으로 가를 수 없다
    expect(find(after, joint.id).grade).toBeNull();
  });

  it("채운 조각은 목표를 넘지 않는다", () => {
    const { state, joint } = atJoint(4);
    let after = state;
    for (let i = 0; i < joint.jointTotal + 10; i += 1) {
      after = step(after, { type: "jointStep", memberId: "p1", questId: joint.id }).state;
    }
    expect(find(after, joint.id).jointDone).toBe(joint.jointTotal);
  });

  it("대리가 제 몫을 채운다 — 그래야 1인 방이 성립한다 (ROLE-03)", () => {
    // 대리는 사람보다 느리다. 소요 시간을 꽉 채워야 한 사람 몫이고,
    // 사람은 판을 돌려 그보다 빨리 채운다.
    const { state, joint } = atJoint(1);
    let after = state;
    for (let i = 0; i < 8; i += 1) {
      after = step(after, { type: "tick", elapsedMs: 5 * SECOND }).state;
    }
    expect(find(after, joint.id).jointDone).toBeGreaterThan(0);
  });

  it("제 칸이 아니면 조각이 오르지 않는다", () => {
    // 합동은 오후 칸이다. 오전에 미리 채워두는 길이 있으면 안 된다 (4.0)
    let state = beginDay(squad(4));
    const joint = state.quests.find((q) => q.kind === "joint");
    if (!joint) throw new Error("합동 없음");
    expect(joint.phase).toBe("afternoon");

    state = toPhase(state, "morning");
    for (const member of state.members) {
      state = step(state, { type: "move", memberId: member.id, to: joint.zone }).state;
      state.members.find((m) => m.id === member.id)!.travelRemainingMs = 0;
    }

    const after = step(state, {
      type: "jointStep",
      memberId: "p1",
      questId: joint.id,
    }).state;
    expect(find(after, joint.id).jointDone).toBe(0);
  });
});
