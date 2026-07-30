import conditionTable from "../data/condition.json";
import { planFor } from "./curriculum.js";
import { mentalRecoveryMultiplier } from "./delegation.js";
import type { Effect, Member, PhaseId, RunState, Stats } from "./types.js";
import { bandRule } from "./weather.js";

const DECAY = conditionTable.perPhaseDecay;
const RECOVERY = conditionTable.phaseRecovery as Record<
  string,
  Partial<Record<keyof Stats, number>>
>;
const SLEEP = conditionTable.sleep;
const MODIFIERS = conditionTable.modifiers;
export const THRESHOLDS = conditionTable.thresholds;

/** 스탯 상한. 피로만 0에서 시작해 100으로 오른다. */
function clampStats(stats: Stats): void {
  stats.stamina = clamp(stats.stamina);
  stats.hydration = clamp(stats.hydration);
  stats.fatigue = clamp(stats.fatigue);
  stats.mental = clamp(stats.mental);
  stats.hygiene = clamp(stats.hygiene);
  stats.satiety = clamp(stats.satiety);
}

function clamp(value: number): number {
  return Math.min(100, Math.max(0, value));
}

/**
 * 시간대 종료 시의 컨디션 정산.
 *
 * 체력·수분·피로는 온도 밴드가 정한다(표 5-1) — 이게 온도 시스템이 몸으로 체감되는 경로다.
 * 청결·포만감은 밴드와 무관하게 시간이 지나면 준다.
 */
export function applyPhaseCondition(
  state: RunState,
  phase: PhaseId,
  effects: Effect[],
): void {
  const drain = bandRule(state.weather.band).drain;
  const recovery = RECOVERY[phase];
  const medicMissing = !hasActiveMedic(state);
  const bivouac = planFor(state.day).training === "bivouac";

  for (const member of state.members) {
    if (member.presence === "evacuated") continue;
    const stats = member.stats;

    stats.stamina += drain.stamina;
    stats.fatigue += drain.fatigue;
    stats.hygiene += DECAY.hygiene;
    stats.mental += DECAY.mental;

    // 3.0 — 의무병이 실패하면 페널티는 개인이 아니라 분대에 떨어진다
    const appetiteMultiplier = medicMissing ? MODIFIERS.noMedicMultiplier : 1;
    stats.hydration += drain.hydration * appetiteMultiplier;
    stats.satiety += DECAY.satiety * appetiteMultiplier;

    if (bivouac) stats.hygiene += MODIFIERS.bivouacHygienePenalty;

    // 포만감이 바닥나면 체력이 깎이기 시작한다 (표 7-1)
    if (stats.satiety <= 0) stats.stamina += MODIFIERS.starvingStaminaDrain;

    if (recovery) {
      // 3일 연속 하달을 받은 사람은 정신력 회복이 −30% 된다 (QST-05 남용 억제)
      const mentalMultiplier = mentalRecoveryMultiplier(state, member);
      for (const [key, amount] of Object.entries(recovery)) {
        const scaled = key === "mental" ? amount * mentalMultiplier : amount;
        stats[key as keyof Stats] += scaled;
      }
    }

    clampStats(stats);
    raiseCriticals(member, effects);
  }
}

/**
 * COND-02 취침 정산. 점호를 통과한 뒤에만 돈다.
 *
 * 경계 근무자는 회복량의 절반만 받는다 — 경계 순번은 실질적으로 "다음 날의 빚"이며,
 * 분대가 이 빚을 어떻게 나누는지가 사회적 결정이 된다.
 */
export function applySleep(state: RunState, effects: Effect[]): void {
  const guards = assignNightGuards(state);
  state.nightGuardIds = guards;

  for (const member of state.members) {
    if (member.presence === "evacuated") continue;
    const ratio = guards.includes(member.id) ? SLEEP.guardRecoveryRatio : 1;
    const stats = member.stats;

    stats.stamina += SLEEP.stamina * ratio;
    stats.fatigue += SLEEP.fatigue * ratio;
    stats.mental += SLEEP.mental * ratio * mentalRecoveryMultiplier(state, member);
    stats.hygiene += SLEEP.hygiene * ratio;

    clampStats(stats);
  }

  effects.push({ type: "sleepSettled", guardIds: guards });
}

/**
 * 야간 경계 로테이션. 일차를 기준으로 돌려서 특정 인원에게 몰리지 않게 한다.
 * 자원(13.1 복무 점수 +3)은 이 기본 배정을 덮어쓰는 형태로 나중에 붙는다.
 */
export function assignNightGuards(state: RunState): string[] {
  const eligible = state.members.filter((m) => m.presence !== "evacuated");
  if (eligible.length === 0) return [];

  const guards: string[] = [];
  const count = Math.min(SLEEP.guardsPerNight, eligible.length);
  for (let i = 0; i < count; i += 1) {
    const index = (state.day + i) % eligible.length;
    const member = eligible[index];
    if (member) guards.push(member.id);
  }
  return guards;
}

/** 표 7-1의 0 도달 효과 — 실제 후송 파이프라인(JDG-03)은 이 신호를 받아 움직인다 */
function raiseCriticals(member: Member, effects: Effect[]): void {
  const stats = member.stats;

  if (stats.stamina <= 0) {
    effects.push({ type: "conditionCritical", memberId: member.id, stat: "stamina" });
  }
  if (stats.hydration <= THRESHOLDS.dehydrationStage2) {
    effects.push({ type: "conditionCritical", memberId: member.id, stat: "hydration" });
  }
  if (stats.fatigue >= THRESHOLDS.forcedSleepFatigue) {
    effects.push({ type: "conditionCritical", memberId: member.id, stat: "fatigue" });
  }
}

export function hasActiveMedic(state: RunState): boolean {
  return state.members.some(
    (m) => m.role === "medic" && m.presence === "player",
  );
}

/** 탈수 단계 — 1단계는 스태미나 회복 정지, 2단계는 60초 후 쓰러진다 (5.0) */
export function dehydrationStage(member: Member): 0 | 1 | 2 {
  if (member.stats.hydration <= THRESHOLDS.dehydrationStage2) return 2;
  if (member.stats.hydration <= THRESHOLDS.dehydrationStage1) return 1;
  return 0;
}

/** 정신력이 바닥나면 조작 입력이 지연된다 — 분대 전체로 번지는 페널티다 (QST-05 남용 억제) */
export function inputDelaySeconds(member: Member): number {
  return member.stats.mental <= THRESHOLDS.panicMental
    ? THRESHOLDS.inputDelaySecondsOnPanic
    : 0;
}
