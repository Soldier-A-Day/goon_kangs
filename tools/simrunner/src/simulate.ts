import {
  createRngState,
  createRun,
  planFor,
  roll,
  step,
  type JudgementCondition,
  type Quest,
  type RunConfig,
  type RunState,
  type RunStatus,
} from "@sad/sim";

const FULL_DAY_MS = 6 * 60 * 1000;

/**
 * 봇 정책.
 *
 * `accuracy`는 10.0 완주율 표의 "개인 정확도"다 — 필수 퀘스트 1건을 제시간에 끝낼 확률.
 * 98%는 런당 실수 6회를 허용한다는 뜻이며, 이 값에서 완주율 15%가 나와야 한다.
 */
export interface BotPolicy {
  readonly accuracy: number;
}

export interface RunOutcome {
  readonly status: RunStatus;
  readonly cleared: boolean;
  /** 런이 끝난 일차 */
  readonly endedDay: number;
  readonly failedAt: JudgementCondition | null;
  readonly reliefsUsed: number;
}

const SQUAD = [
  { id: "p1", name: "소총수", role: "rifle" },
  { id: "p2", name: "통신병", role: "comms" },
  { id: "p3", name: "의무병", role: "medic" },
  { id: "p4", name: "행정병", role: "admin" },
] as const;

export function simulateRun(
  seed: number,
  policy: BotPolicy,
  config: Partial<RunConfig> = {},
): RunOutcome {
  let state = createRun({
    runId: `sim-${seed}`,
    seed,
    members: [...SQUAD],
    config,
  });
  state = step(state, { type: "beginDay" }).state;

  // 봇 판단용 난수는 런 시드에서 파생시킨다. sim 내부 RNG와 섞이지 않아야
  // 나중에 기온 롤이 들어와도 봇의 성공/실패 수열이 바뀌지 않는다.
  let rng = createRngState(seed ^ 0x9e3779b9);

  while (state.status === "running") {
    const plan = planFor(state.day);
    const quests: Quest[] = [];

    for (const member of state.members) {
      for (let i = 0; i < plan.required.total; i += 1) {
        const [succeeded, next] = roll(rng, policy.accuracy);
        rng = next;
        quests.push({
          id: `d${state.day}-${member.id}-${i}`,
          kind: "role",
          label: "필수",
          ownerId: member.id,
          required: true,
          phase: "morning",
          zone: "barracks",
          workMs: 10_000,
          workedMs: succeeded ? 10_000 : 0,
          minActors: 1,
          status: succeeded ? "done" : "pending",
          delegatedFrom: null,
        });
      }
    }

    // 퀘스트 배정기(W2-3)가 들어오기 전까지의 임시 발판이다.
    // 배정기가 생기면 이 블록은 통째로 사라지고 sim이 직접 퀘스트를 만든다.
    state.quests = quests;
    state = step(state, { type: "tick", elapsedMs: FULL_DAY_MS }).state;
  }

  const last = state.judgements[state.judgements.length - 1];
  return {
    status: state.status,
    cleared: state.status === "cleared",
    endedDay: last?.day ?? state.day,
    failedAt: last?.failedAt ?? null,
    reliefsUsed: state.judgements.reduce((sum, j) => sum + j.reliefsUsed, 0),
  };
}

export interface BatchReport {
  readonly runs: number;
  readonly accuracy: number;
  readonly clearRate: number;
  /** 며칠차에서 가장 많이 죽는가 — 매 스프린트 측정 대상 (ARCH-02) */
  readonly deathsByDay: readonly number[];
  readonly failuresByCondition: Readonly<Record<JudgementCondition, number>>;
}

export function runBatch(
  runs: number,
  policy: BotPolicy,
  config: Partial<RunConfig> = {},
  seedBase = 1,
): BatchReport {
  const deathsByDay = new Array<number>(19).fill(0);
  const failuresByCondition: Record<JudgementCondition, number> = {
    A: 0,
    B: 0,
    C: 0,
    D: 0,
  };
  let cleared = 0;

  for (let i = 0; i < runs; i += 1) {
    const outcome = simulateRun(seedBase + i, policy, config);
    if (outcome.cleared) {
      cleared += 1;
      continue;
    }
    deathsByDay[outcome.endedDay] = (deathsByDay[outcome.endedDay] ?? 0) + 1;
    if (outcome.failedAt) failuresByCondition[outcome.failedAt] += 1;
  }

  return {
    runs,
    accuracy: policy.accuracy,
    clearRate: cleared / runs,
    deathsByDay,
    failuresByCondition,
  };
}
