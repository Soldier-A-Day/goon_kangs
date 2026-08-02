import {
  PHASE_COUNT,
  careRecovery,
  createRun,
  planFor,
  phaseAt,
  step,
  type CreateRunOptions,
  type PhaseId,
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

/**
 * 지금 칸의 회복 행동(밥 · 물 · 세면)을 해낸 것으로 친다.
 *
 * **필수와 같이 하루 시작에 몰아서 처리하면 안 된다.** 아침에는 스탯이 아직
 * 100에 가까워 회복이 상한에서 잘려 버려지고, 정작 깎인 뒤인 저녁에는 아무것도
 * 남지 않는다 — 그렇게 했더니 이상적인 런이 숙영 이틀에 청결 0으로 무너졌다.
 * 실제 플레이도 중식 칸에 가서 먹고 개인정비 칸에 가서 씻는다.
 */
export function completeCareNow(state: RunState): RunState {
  const phase = phaseAt(state.phaseIndex).id;
  // 숙영일에는 회복이 숙영지에서 야전식으로 벌어진다 — 몫이 절반이다.
  // 헬퍼가 그걸 무시하고 영내 몫을 주면 숙영이 청결을 미는 압박이 사라진다
  const bivouac = planFor(state.day).training === "bivouac";

  for (const quest of state.quests) {
    if (quest.kind !== "care" || quest.phase !== phase) continue;
    if (quest.status === "done") continue;
    quest.workedMs = quest.workMs;
    quest.status = "done";

    // `applyWork`가 완료 시 하는 일과 같다 — 헬퍼는 그 경로를 타지 않는다
    const member = state.members.find((m) => m.id === quest.ownerId);
    if (!member) continue;
    for (const [key, amount] of Object.entries(careRecovery(quest.id, bivouac))) {
      const stat = key as keyof typeof member.stats;
      member.stats[stat] = Math.min(100, Math.max(0, member.stats[stat] + amount));
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
      // 그 칸에 들어서면 그 칸의 회복 행동을 한다 — 이상적인 런은 먹고 씻는다
      current = completeCareNow(current);
      if (guard++ > 100) throw new Error(`하루가 끝나지 않는다: D-${day}`);
    }
    if (current.status !== "running") break;
  }

  return current;
}


/**
 * 그 일과의 시간대까지 진행시킨다.
 *
 * 일과는 **제 칸에서만** 할 수 있다(4.0 · `canWork`). 그래서 오후 일과를
 * 검사하려면 오후까지 가야 하고, 그러지 않으면 진척이 0으로 남는다.
 */
export function toPhase(state: RunState, phase: PhaseId): RunState {
  let at = state;
  for (let guard = 0; guard < PHASE_COUNT * 2; guard += 1) {
    if (phaseAt(at.phaseIndex).id === phase) return at;
    at = step(at, { type: "skipPhase" }).state;
  }
  throw new Error(`시간대에 못 닿았다: ${phase}`);
}
