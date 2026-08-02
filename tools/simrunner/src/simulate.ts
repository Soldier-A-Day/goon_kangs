import {
  careRecovery,
  createRngState,
  createRun,
  phaseAt,
  planFor,
  roll,
  step,
  type Grade,
  type JudgementCondition,
  type Member,
  type Quest,
  type RngState,
  type RunConfig,
  type RunState,
  type RunStatus,
} from "@sad/sim";

const TICK_MS = 30 * 1000;

/**
 * 봇 정책.
 *
 * `accuracy`는 10.0 완주율 표의 "개인 정확도"다 — 필수 퀘스트 1건을 제시간에 끝낼 확률.
 * 98%는 런당 실수 6회를 허용한다는 뜻이며, 이 값에서 완주율 15%가 나와야 한다.
 *
 * `gradeA` · `gradeC`는 **끝낸 일과를 얼마나 깨끗하게 했는가**의 분포다. 완주와는
 * 다른 축이다 — 못 통과하면 완료가 아니라 재시도이므로 정확도가 완주를 정하고,
 * 등급은 통과한 판의 여유만 가른다. 승급 요구치(18 · 70 · 150)가 이 분포에서
 * 성립하는지가 밸런싱 질문이다.
 */
export interface BotPolicy {
  readonly accuracy: number;
  /** A등급 확률. 기본은 "웬만큼 하는 사람" */
  readonly gradeA?: number;
  /** C등급 확률. 나머지가 B다 */
  readonly gradeC?: number;
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
    const day = state.day;

    // sim이 배정한 그날의 일과를 봇이 정확도만큼 처리한다. 퀘스트당 시도는 하루 한 번뿐이다 —
    // 하루 길이가 고정이 아니므로(우수분대 +20초) 고정 시간으로 끊으면 재시도가 생긴다.
    //
    // **회복(`kind: "care"`)은 여기서 처리하지 않는다.** 필수·합동은 스탯을 바꾸지
    // 않으니 하루 시작에 몰아 끝내도 무방하지만, 회복은 스탯을 0~100으로 클램프한다.
    // 하루 시작(청결이 전날 마감치에 가까운 상태)에 세면·샤워를 먼저 밀어 넣으면
    // 회복분이 상한에서 잘려 나가고, 정작 하루 종일 깎인 뒤인 저녁엔 되돌릴 게
    // 남지 않는다 — `packages/sim/test/helpers.ts`의 `completeCareNow`가 같은 이유로
    // "그 칸에 들어섰을 때"만 적용한다. 처음엔 이 함수도 회복을 하루 시작에 몰아
    // 처리했다가, 숙영 이틀(D-9·D-10)에서 정확도 100%인 봇도 매번 청결 0으로
    // 전멸하는 것으로 드러났다 — sim 문제가 아니라 이 타이밍 버그였다.
    for (const quest of state.quests) {
      if (quest.status === "done") continue;
      if (quest.kind === "care") continue;

      if (quest.kind === "joint") {
        // 협동 실패 모델은 아직 없다 — 합동은 항상 완수한 것으로 둔다
        quest.workedMs = quest.workMs;
        quest.status = "done";
        continue;
      }

      if (!quest.required) continue;

      const [succeeded, next] = roll(rng, policy.accuracy);
      rng = next;
      if (succeeded) {
        quest.workedMs = quest.workMs;
        quest.status = "done";
        // 판이 없는 일과는 등급도 없다 — 회복 · 합동 · 훈련 체크포인트가 그렇다
        if (quest.minigame !== null) {
          const [grade, afterGrade] = rollGrade(rng, policy);
          rng = afterGrade;
          quest.grade = grade;
        }
      }
    }

    // 일차가 바뀔 때까지 흘려보낸다. 매 틱 지금 칸의 회복 행동을 처리한다 —
    // 실제 플레이가 그 칸에 들어서서 먹고 씻는 것과 같은 타이밍이다.
    let guard = 0;
    while (state.status === "running" && state.day === day) {
      state = step(state, { type: "tick", elapsedMs: TICK_MS }).state;
      if (state.status === "running" && state.day === day) {
        applyDueCare(state);
      }
      if (guard++ > 200) throw new Error(`하루가 끝나지 않는다: D-${day}`);
    }
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

/**
 * 지금 칸에 배정된 회복 행동을 처리한다.
 *
 * 정확도 굴림은 적용하지 않는다 — 회복은 판정 대상이 아니고(`discipline.ts`의
 * `optionalMissed`도 `care`는 명시적으로 뺀다), 밥 먹고 씻는 데 "실수"라는
 * 개념이 없다. 대신 **타이밍은 지킨다**: 그 칸이 지금 칸일 때만 처리한다.
 */
function applyDueCare(state: RunState): void {
  const phase = phaseAt(state.phaseIndex).id;
  for (const quest of state.quests) {
    if (quest.kind !== "care" || quest.phase !== phase) continue;
    if (quest.status === "done") continue;
    applyCareQuest(state, quest);
  }
}

/**
 * 회복 행동(kind === "care") 하나의 완료 처리.
 *
 * sim의 실제 완료 경로(`step.ts`의 `complete` → `applyCare`)는 `work`/`questCleared`
 * 이벤트를 거쳐야만 돈다. 봇은 서버 없이 상태를 직접 조작하는 단순 모델이라 그 경로를
 * 타지 않으므로, 여기서 `careRecovery`(공개 API)로 몫을 그대로 계산해 스탯에 적용한다 —
 * `packages/sim`의 회복표를 다시 베끼지 않고 그 함수를 그대로 부른다.
 *
 * 클램프(0~100)는 `condition.ts`의 `clampStats`와 같은 규칙이다.
 */
function applyCareQuest(state: RunState, quest: Quest): void {
  const member = state.members.find((m: Member) => m.id === quest.ownerId);
  if (!member) return;

  const bivouac = planFor(state.day).training === "bivouac";
  const recovery = careRecovery(quest.id, bivouac);
  for (const [key, amount] of Object.entries(recovery)) {
    const stat = key as keyof Member["stats"];
    member.stats[stat] = Math.min(100, Math.max(0, member.stats[stat] + amount));
  }

  quest.workedMs = quest.workMs;
  quest.status = "done";
}

/** 기본 분포 — 대부분 B, 셋 중 하나쯤 A, 가끔 C */
const DEFAULT_GRADE_A = 0.3;
const DEFAULT_GRADE_C = 0.15;

function rollGrade(rng: RngState, policy: BotPolicy): readonly [Grade, RngState] {
  const [isA, afterA] = roll(rng, policy.gradeA ?? DEFAULT_GRADE_A);
  if (isA) return ["A", afterA];
  const [isC, afterC] = roll(afterA, policy.gradeC ?? DEFAULT_GRADE_C);
  return [isC ? "C" : "B", afterC];
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
