import {
  createRun,
  step,
  type CreateRunOptions,
  type Quest,
  type RunState,
} from "../src/index.js";

export const SECOND = 1000;
export const FULL_DAY = 6 * 60 * SECOND;

/** 4인 정원이 모두 찬 표준 분대 */
export function fullSquad(overrides: Partial<CreateRunOptions> = {}): RunState {
  return createRun({
    runId: "test-run",
    seed: 1234,
    members: [
      { id: "p1", name: "김소총", role: "rifle" },
      { id: "p2", name: "이통신", role: "comms" },
      { id: "p3", name: "박의무", role: "medic" },
      { id: "p4", name: "최행정", role: "admin" },
    ],
    ...overrides,
  });
}

export function beginDay(state: RunState): RunState {
  return step(state, { type: "beginDay" }).state;
}

/** 그날 배정된 필수를 전부 끝낸 것으로 친다 — 판정 외의 규칙을 시험할 때 쓴다 */
export function completeRequired(state: RunState): RunState {
  for (const quest of state.quests) {
    if (quest.required || quest.kind === "joint") {
      quest.workedMs = quest.workMs;
      quest.status = "done";
    }
  }
  return state;
}

/** 배정된 일과를 테스트용 퀘스트로 통째로 갈아끼운다 */
export function withQuests(state: RunState, quests: readonly Quest[]): RunState {
  state.quests = [...quests];
  return state;
}

/**
 * 하루를 끝까지 흘려보낸다. 하루가 끝나면 sim이 곧바로 다음 날을 시작하므로,
 * 필수를 미리 끝내두지 않으면 조건 A에서 걸린다.
 */
export function playDays(state: RunState, days: number): RunState {
  let current = beginDay(state);

  for (let i = 0; i < days; i += 1) {
    const day = current.day;
    current = completeRequired(current);

    // 하루 길이는 고정이 아니다 — 우수분대(80+)는 개인정비를 20초 더 받고,
    // 스킵 이월도 칸을 늘린다. 그래서 일차가 바뀔 때까지 흘려보낸다.
    let guard = 0;
    while (current.status === "running" && current.day === day) {
      current = step(current, { type: "tick", elapsedMs: 30 * SECOND }).state;
      if (guard++ > 100) throw new Error(`하루가 끝나지 않는다: D-${day}`);
    }
    if (current.status !== "running") break;
  }

  return current;
}
