import { describe, expect, it } from "vitest";
import {
  createRun,
  evacuate,
  leaveRun,
  step,
  useRelief,
  type Effect,
  type Quest,
} from "../src/index.js";
import { SECOND, beginDay, fullSquad, withQuests } from "./helpers.js";

/**
 * G1 — 분대장이 아예 안 생기던 버그의 회귀 방지.
 *
 * 예전엔 `createRun`이 `leaderId: null`로 시작해 끝까지 아무도 채우지
 * 않았고, `voteLeader` 인텐트도 sim이 받지 않았다 — 그래서 `relief.ts`
 * `canUseRelief`의 `state.leaderId !== leaderId` 검사가 절대 통과할 수
 * 없었다. 이 파일은 그 공백이 실제로 메워졌는지, 그리고 구제권이 그 결과로
 * 실제 발동되는지를 확인한다.
 */

function quest(overrides: Partial<Quest> = {}): Quest {
  return {
    id: "q",
    kind: "role",
    label: "총기 수입",
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
    ...overrides,
  };
}

describe("G1 — 런 시작 시 분대장 자동 지정", () => {
  it("4인 편성이면 ROLES 순서(rifle→comms→medic→admin)의 첫 사람이 분대장이 된다", () => {
    const state = fullSquad();
    expect(state.leaderId).toBe("p1"); // p1 = rifle, 정렬상 첫 순서
  });

  it("같은 편성이면 언제나 같은 사람이 뽑힌다 — 결정적이다", () => {
    const a = fullSquad();
    const b = fullSquad();
    expect(a.leaderId).toBe(b.leaderId);
  });

  it("1인 방에서도 그 한 사람이 분대장이 된다", () => {
    const state = createRun({
      runId: "solo",
      seed: 1,
      members: [{ id: "solo1", name: "혼자", role: "rifle" }],
    });
    expect(state.leaderId).toBe("solo1");
  });

  it("rifle이 NPC 공석이면 다음 순서(comms) 사람이 분대장이 된다", () => {
    const state = createRun({
      runId: "no-rifle",
      seed: 1,
      members: [{ id: "c1", name: "통신", role: "comms" }],
    });
    expect(state.leaderId).toBe("c1");
    const rifle = state.members.find((m) => m.role === "rifle");
    expect(rifle?.presence).toBe("npcVacant");
  });
});

describe("G1 — 분대장 공백 재지정", () => {
  it("분대장이 이탈(leaveRun)하면 다음 사람 참석자로 다시 뽑는다", () => {
    const state = fullSquad();
    expect(state.leaderId).toBe("p1");

    const effects: Effect[] = [];
    leaveRun(state, "p1", effects);

    expect(state.leaderId).toBe("p2"); // comms, 남은 사람 중 ROLES 순서상 다음
    expect(effects).toContainEqual(
      expect.objectContaining({ type: "leaderChanged", leaderId: "p2" }),
    );
  });

  it("분대장이 후송(evacuate)되면 다음 사람으로 다시 뽑는다", () => {
    const state = beginDay(fullSquad());
    expect(state.leaderId).toBe("p1");

    const effects: Effect[] = [];
    evacuate(state, "p1", effects);

    expect(state.leaderId).toBe("p2");
    expect(effects).toContainEqual(
      expect.objectContaining({ type: "leaderChanged", leaderId: "p2" }),
    );
  });

  it("분대장이 아닌 사람이 나가면 분대장은 그대로다", () => {
    const state = fullSquad();
    expect(state.leaderId).toBe("p1");

    const effects: Effect[] = [];
    leaveRun(state, "p2", effects);

    expect(state.leaderId).toBe("p1");
    expect(effects.some((e) => e.type === "leaderChanged")).toBe(false);
  });

  it("1인 방에서 그 한 사람마저 나가면 leaderId는 null로 떨어진다(런은 해산으로 끝난다)", () => {
    const state = createRun({
      runId: "solo-leave",
      seed: 1,
      members: [{ id: "solo1", name: "혼자", role: "rifle" }],
    });

    // 해산 판정(checkDisband)은 step.ts가 leaveRun 뒤에 잇달아 부른다 —
    // 여기서는 leaveRun 자체가 leaderId를 null로 떨어뜨리는지만 본다.
    const effects: Effect[] = [];
    leaveRun(state, "solo1", effects);

    expect(state.leaderId).toBeNull();
  });
});

describe("G1 — 구제권이 실제로 발동된다 (버그 재현 + 수정 확인)", () => {
  it("자동 지정된 분대장이 구제를 발동할 수 있다 — leaderId를 손으로 채우지 않는다", () => {
    let state = beginDay(fullSquad());
    // **여기가 핵심이다.** 예전 relief.test.ts는 `state.leaderId = "p1"`을
    // 손으로 채워야만 통과했다 — 실전에서는 아무도 그렇게 채워주지 않았으므로
    // 화면엔 "구제권 3장"이 뜨는데 실제로는 100% `notLeader`로 막혔다. 이
    // 테스트는 `createRun`이 채운 값을 그대로 쓴다.
    expect(state.leaderId).toBe("p1");
    state = withQuests(state, [quest({ id: "a" })]);

    const effects: Effect[] = [];
    const ok = useRelief(state, state.leaderId as string, "a", effects);

    expect(ok).toBe(true);
    expect(state.quests[0]?.required).toBe(false);
    expect(effects).toContainEqual(
      expect.objectContaining({ type: "reliefGranted", by: "leader" }),
    );
  });

  it("1인 방에서도 구제권이 실제로 발동된다", () => {
    let state = beginDay(
      createRun({
        runId: "solo-relief",
        seed: 1,
        members: [{ id: "solo1", name: "혼자", role: "rifle" }],
      }),
    );
    expect(state.leaderId).toBe("solo1");
    state = withQuests(state, [quest({ id: "a", ownerId: "solo1" })]);

    const ok = useRelief(state, "solo1", "a", []);

    expect(ok).toBe(true);
    expect(state.quests[0]?.required).toBe(false);
  });
});

describe("G1 — voteLeader로 분대장을 바꾼다", () => {
  it("과반(4인 중 3표)을 얻으면 즉시 분대장이 바뀐다", () => {
    let state = fullSquad();
    expect(state.leaderId).toBe("p1");

    state = step(state, { type: "voteLeader", memberId: "p2", candidateId: "p2" }).state;
    state = step(state, { type: "voteLeader", memberId: "p3", candidateId: "p2" }).state;
    expect(state.leaderId).toBe("p1"); // 아직 2표 — 4인 과반(>2)에 못 미친다

    const result = step(state, { type: "voteLeader", memberId: "p4", candidateId: "p2" });
    state = result.state;

    expect(state.leaderId).toBe("p2");
    expect(result.effects).toContainEqual(
      expect.objectContaining({ type: "leaderChanged", leaderId: "p2" }),
    );
  });

  it("동수면 바뀌지 않는다 — 현직 유지", () => {
    let state = fullSquad();
    expect(state.leaderId).toBe("p1");

    state = step(state, { type: "voteLeader", memberId: "p1", candidateId: "p1" }).state;
    state = step(state, { type: "voteLeader", memberId: "p2", candidateId: "p2" }).state;
    // 2:2 동수(참석 4인 기준 과반은 3표) — 아무도 확정되지 않는다
    state = step(state, { type: "voteLeader", memberId: "p3", candidateId: "p1" }).state;
    // 이제 p1이 2표, p2가 1표 — 여전히 과반(>2)에 못 미친다
    expect(state.leaderId).toBe("p1");
  });

  it("나간 사람에게는 투표할 수 없고, 나간 사람의 지난 표는 집계에서 빠진다", () => {
    let state = fullSquad();
    leaveRun(state, "p4", []); // leaveRun은 state를 그 자리에서 변형한다(void 반환)
    expect(state.members.find((m) => m.id === "p4")?.presence).toBe("npcLeave");

    // 나간 p4가 투표를 보내도 무시된다(투표자 자격 없음)
    const before = state.leaderId;
    state = step(state, { type: "voteLeader", memberId: "p4", candidateId: "p2" }).state;
    expect(state.leaderId).toBe(before);

    // 남은 3인 중 2표면 과반(>1.5)이다
    state = step(state, { type: "voteLeader", memberId: "p2", candidateId: "p2" }).state;
    state = step(state, { type: "voteLeader", memberId: "p3", candidateId: "p2" }).state;
    expect(state.leaderId).toBe("p2");
  });
});
