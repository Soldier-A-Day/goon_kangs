import { humanCount } from "./run.js";
import type { Effect, Judgement, JudgementCondition, Quest, RunState } from "./types.js";

/** 10.0 조건 C — 분대 군기 하한 */
export const DISCIPLINE_FLOOR = 40;

/** 10.0 조건 D — 청결 하한 */
export const HYGIENE_FLOOR = 20;

/** 9.0 · 10.0 — 훈련과 합동 퀘스트는 구간 통과율 70% 이상이면 필수 판정을 통과한다 */
export const PARTIAL_SUCCESS_RATIO = 0.7;

/**
 * JDG-01 점호 판정.
 *
 * 조건 A~D는 AND다. 하나라도 깨지면 분대 전원의 런이 끝난다. 이 압력을 완충하는 것이
 * 구제권이며 총량은 3회다(분대장 우선순위 지정 2 + 간부 구제권 1) — 완주율 목표 15~25%에서
 * 역산한 값이므로 이 숫자를 바꾸면 10.0의 밸런스가 통째로 흔들린다.
 *
 * 순수 함수다. 상태를 바꾸지 않고 판정만 계산한다.
 */
export function judgeDay(state: RunState): Judgement {
  const required = state.quests.filter((q) => q.required);
  const requiredDone = required.filter((q) => q.status === "done").length;
  const shortfall = required.length - requiredDone;

  // 구제는 조건 A의 미완료 필수에만 쓴다 — 군기나 복장은 구제 대상이 아니다
  const reliefsUsed = Math.min(shortfall, state.reliefsRemaining);
  const conditionA = shortfall - reliefsUsed <= 0;

  const jointPassed = state.quests
    .filter((q) => q.kind === "joint")
    .every(isJointCleared);

  const conditionC = state.discipline >= DISCIPLINE_FLOOR;
  const conditionD = state.members
    .filter((m) => m.presence === "player")
    .every((m) => m.stats.hygiene >= HYGIENE_FLOOR);

  const failedAt = firstFailure({
    A: conditionA,
    B: jointPassed,
    C: conditionC,
    D: conditionD,
  });

  return {
    day: state.day,
    passed: failedAt === null,
    failedAt,
    requiredTotal: required.length,
    requiredDone,
    jointPassed,
    discipline: state.discipline,
    // 판정이 실패로 끝나면 구제권은 소모되지 않는다 — 살리지 못한 구제는 쓰지 않은 것이다
    reliefsUsed: failedAt === null ? reliefsUsed : 0,
  };
}

/** 합동 퀘스트는 부분 성공 70%까지 인정한다 */
function isJointCleared(quest: Quest): boolean {
  if (quest.status === "done") return true;
  if (quest.workMs <= 0) return true;
  return quest.workedMs / quest.workMs >= PARTIAL_SUCCESS_RATIO;
}

/**
 * 실패한 첫 조건만 돌려준다. JDG-02 연출이 한 줄에서 멈추고 화면 톤을 바꾸는 이유는
 * "다음 런에서 무엇을 고쳐야 하는지"를 한 가지로 지목하기 위해서다.
 */
function firstFailure(results: Record<JudgementCondition, boolean>): JudgementCondition | null {
  const order: JudgementCondition[] = ["A", "B", "C", "D"];
  return order.find((key) => !results[key]) ?? null;
}

/**
 * 판정 결과를 런에 반영한다. 하루 마감(step.ts endDay)에서만 호출된다.
 */
export function applyJudgement(state: RunState, effects: Effect[]): void {
  const judgement = judgeDay(state);
  state.judgements.push(judgement);
  state.reliefsRemaining -= judgement.reliefsUsed;
  effects.push({ type: "dayJudged", judgement });

  if (judgement.passed) {
    if (state.day >= state.config.totalDays) {
      state.status = "cleared";
      effects.push({ type: "runEnded", status: "cleared" });
    }
    return;
  }

  if (state.config.difficulty === "regular") {
    state.status = "discharged";
    effects.push({ type: "runEnded", status: "discharged" });
    return;
  }

  // 완화 난이도: 1차 경고(다음 날 필수 +2) → 2차 근신(개인정비 박탈) → 3차 종료
  state.warnings += 1;
  if (state.warnings >= 3) {
    state.status = "discharged";
    effects.push({ type: "runEnded", status: "discharged" });
    return;
  }
  if (state.warnings === 1) {
    state.nextDayExtraRequired += 2;
    effects.push({ type: "log", message: "1차 경고 — 다음 날 필수 퀘스트 +2" });
  } else {
    state.personalTimeRevoked = true;
    effects.push({ type: "log", message: "2차 근신 — 다음 날 개인정비 시간 박탈" });
  }

  // 경고로 버티는 것은 다음 날이 있을 때 얘기다. 마지막 날 판정을 통과하지 못하면
  // 완화 난이도라도 전역은 없다 — 승리 조건은 18일차 심사 통과다(2.0).
  if (state.day >= state.config.totalDays) {
    state.status = "discharged";
    effects.push({ type: "runEnded", status: "discharged" });
  }
}

/**
 * 실제 플레이어가 2명 미만으로 "떨어지면" 분대 해체로 런이 끝난다(JDG-03).
 * NPC 대리는 인원수에 포함하지 않는다 — 대리는 생존을 시켜줄 뿐 분대가 아니다.
 *
 * 처음부터 1~2인으로 시작한 방(ROLE-03 튜토리얼·연습)은 해체 대상이 아니다.
 * 인원이 빠져서 무너지는 것과 애초에 적게 시작하는 것은 다른 사건이다.
 */
export function checkDisband(state: RunState, effects: Effect[]): void {
  if (state.status !== "running") return;
  if (state.startedHumans < 2) return;
  if (humanCount(state) >= 2) return;
  state.status = "disbanded";
  effects.push({ type: "runEnded", status: "disbanded" });
}
