import {
  bestOfficer,
  careRecovery,
  createRngState,
  createRun,
  markDayEndAck,
  OFFICER_RELIEF_TRUST_THRESHOLD,
  phaseAt,
  PHASES,
  planFor,
  roll,
  step,
  useOfficerRelief,
  useRelief,
  type Grade,
  type JudgementCondition,
  type Member,
  type PhaseId,
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
  // B-4 이후 구제는 "누가 분대장인가"부터 필요하다(relief.ts canUseRelief) — 실제
  // 게임은 투표(services/gameserver room.ts)로 정하지만, 이 헤드리스 봇에는 투표할
  // 사람이 없으니 소총수를 고정 분대장으로 둔다. 누가 분대장이든 판정 결과는
  // 동일하므로 밸런스에는 영향이 없다.
  state.leaderId = SQUAD[0].id;
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

    // B-4 — 구제는 자동 상쇄가 아니라 발동이다(relief.ts). 오늘 필수가 이미
    // 결과까지 다 굴려졌으니(위 for 루프), 미달 여부는 시간대를 흘려보내기
    // 전부터 확정돼 있다 — 그 정보로 오늘 저녁 간부 구제를 시도할지, 분대장이
    // 지금 하나를 봐줘야 할지를 먼저 정한다.
    const wantOfficerRelief = planRelief(state);

    // 일차가 바뀔 때까지 흘려보낸다. 매 틱 지금 칸의 회복 행동을 처리한다 —
    // 실제 플레이가 그 칸에 들어서서 먹고 씻는 것과 같은 타이밍이다.
    let guard = 0;
    while (state.status === "running" && state.day === day) {
      state = step(state, { type: "tick", elapsedMs: TICK_MS }).state;
      if (state.status === "running" && state.day === day) {
        applyDueCare(state);
        // 간부 구제는 저녁 개인정비 시간에만 발동을 받아 준다(relief.ts
        // canUseOfficerRelief) — 그 칸에 들어선 첫 틱에서 쏜다. 거부돼도
        // 무해하다(신뢰도 미달·이미 발동 등 — 발동 시도 자체가 손해가 아니다).
        if (
          wantOfficerRelief &&
          !state.officerReliefArmedToday &&
          phaseAt(state.phaseIndex).id === "personal"
        ) {
          useOfficerRelief(state, state.leaderId ?? SQUAD[0].id, []);
        }
      }
      // D1 — 판정이 끝나면 하루 마감 창이 열려 다음 날 진입이 미뤄진다. 이
      // 봇에는 눈으로 확인할 사람이 없으니 즉시 전원 확인한 것으로 친다 —
      // 안 그러면 매일 백스톱(DAY_END_BACKSTOP_MS)만큼 헛도는 틱이 쌓여
      // 밸런스 배치(수천 런)가 눈에 띄게 느려진다.
      if (state.dayEndWindowMsLeft > 0) {
        for (const member of SQUAD) markDayEndAck(state, member.id, []);
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

/**
 * 봇의 구제 판단 — B-4 긴급 수리.
 *
 * B-4가 자동 상쇄를 발동형으로 바꾼 뒤(relief.ts) 봇이 구제를 한 번도 쏘지
 * 않아 완주율이 목표 구간(98% 15~25%)에서 0.15%로 무너졌다. judge.ts는
 * 더 이상 알아서 구제를 깎아 주지 않으므로, 봇도 사람 분대장처럼 "오늘 못
 * 끝낼 것 같으면 직접 발동"해야 한다.
 */

/**
 * 분대장 몫에는 날짜 문턱을 두지 않는다.
 *
 * 처음엔 "D-3 이후부터"로 초반 낭비를 막아 봤는데, 실측(deathsByDay)이 반대
 * 결론을 냈다 — D-1·D-2 사망이 전체 사망의 3분의 1을 넘었다. 간부 신뢰도는
 * 시작값 50에서 하루 최대 +3씩만 오르므로(discipline.json dailyDelta) 문턱
 * 70에 닿으려면 최소 7일이 걸리고, 그 전까지 간부 구제는 구조적으로 불가능하다.
 * 초반에 분대장 몫을 아껴 봐야 그 시점에 대신 써 줄 것이 없다 — "이르게
 * 낭비"가 아니라 "아무도 못 쓸 몫을 붙들고 있다가 그대로 사망"이었다.
 *
 * `regular` 난이도는 어느 하루에 실패하든 런이 똑같이 끝난다(judge.ts
 * `applyJudgement`) — 하루의 미달을 살리는 가치는 날짜와 무관하다. 그래서
 * "미달이 실제로 났고, 간부 구제로 못 메우는 만큼만" 즉시 쓰는 탐욕적 판단이
 * 곧 최선이다. `planRelief`가 이미 그 조건(shortfall > 0, officer가 못
 * 메우는 gap)만 걸고 발동하므로 별도 날짜 게이트는 불필요하다.
 */

const PHASE_ORDER: readonly PhaseId[] = PHASES.map((p) => p.id);

/** 시간대가 하루 안에서 얼마나 늦은지 — 강등 대상을 고를 때 "더 급한 쪽"을 가른다 */
function phaseRank(phase: PhaseId): number {
  return PHASE_ORDER.indexOf(phase);
}

/** 조건 A의 미달 — judge.ts `countShortfall`과 정확히 같은 조건이다 */
function computeShortfall(state: RunState): number {
  return state.quests.filter((q) => q.required && q.status !== "done").length;
}

/**
 * 강등 대상 고르기 — "가장 가망 없는 필수 1개".
 *
 * 이 봇은 정확도 굴림을 하루 시작에 몰아 처리하므로(위 for 루프) "오후 칸
 * 후반이라 이제 늦었다"를 실시간으로 느낄 수 없다 — 결과는 이미 나와 있다.
 * 대신 더 늦은 시간대에 배정됐던 쪽을 우선으로 고른다 — 같은 정보 부재 속에서
 * 그나마 "임박했던" 쪽에 가깝다. 하루에 둘 이상 미달이 겹치는 날은 드물지만
 * (정확도 98%에서 하루 2건 확률은 0.2% 안팎), 그런 날에도 순서는 결정적이다.
 */
function pickWorstRequiredQuest(state: RunState): Quest | null {
  const candidates = state.quests.filter((q) => q.required && q.status !== "done");
  if (candidates.length === 0) return null;
  candidates.sort((a, b) => phaseRank(b.phase) - phaseRank(a.phase));
  return candidates[0] ?? null;
}

/**
 * 하루 시작 시점의 구제 판단.
 *
 * 간부 구제는 사실상 공짜다 — 발동해도 그날 실제로 미달이 없었으면 총량이
 * 안 깎인다(judge.ts `applyJudgement`: 판정이 실패로 끝나야만 소모되고, 통과한
 * 판은 소모되지 않는다). 그래서 신뢰도 문턱(70)만 넘으면 아낄 이유가 없다.
 *
 * 분대장 몫은 반대로 발동하는 즉시 영구히 깎인다(런당 2회뿐). 오늘 간부 구제가
 * 될지는 오늘 트러스트 값으로 이미 알 수 있으므로(트러스트는 전날 밤에 확정되고
 * 하루 동안 안 바뀐다) 미리 내다보고, "간부 구제로도 못 메우는 나머지"에만
 * 분대장 몫을 쓴다.
 *
 * 반환값은 "오늘 저녁 개인정비에 간부 구제 발동을 시도해야 하는가"다 — 실제
 * 발동은 `useOfficerRelief`가 개인정비 시간대에만 받아 주므로 여기서 바로 쏘지
 * 못하고 신호만 돌려준다.
 */
function planRelief(state: RunState): boolean {
  const shortfall = computeShortfall(state);
  if (shortfall <= 0) return false;

  const officerCanCover =
    state.officerReliefsRemaining > 0 &&
    state.trust[bestOfficer(state)] >= OFFICER_RELIEF_TRUST_THRESHOLD;
  const gap = shortfall - (officerCanCover ? 1 : 0);

  if (gap > 0 && state.leaderId) {
    const toUse = Math.min(gap, state.leaderReliefsRemaining);
    for (let i = 0; i < toUse; i += 1) {
      const target = pickWorstRequiredQuest(state);
      if (!target) break;
      useRelief(state, state.leaderId, target.id, []);
    }
  }

  return officerCanCover;
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
