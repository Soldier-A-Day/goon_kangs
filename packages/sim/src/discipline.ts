import disciplineTable from "../data/discipline.json";
import type { Effect, RunState } from "./types.js";

const GAINS = disciplineTable.gains;
const LOSSES = disciplineTable.losses;
const OFFICERS = disciplineTable.officers;

export interface DisciplineBand {
  readonly id: string;
  readonly min: number;
  readonly label: string;
  readonly surpriseChance: number;
  readonly supplyBonus?: number;
  readonly personalTimeBonusSeconds?: number;
  readonly nextDayExtraOptional?: number;
}

export const DISCIPLINE_BANDS = disciplineTable.bands as readonly DisciplineBand[];

export function disciplineBand(discipline: number): DisciplineBand {
  const band = [...DISCIPLINE_BANDS]
    .sort((a, b) => b.min - a.min)
    .find((b) => discipline >= b.min);
  if (!band) throw new Error(`군기 구간을 찾을 수 없다: ${discipline}`);
  return band;
}

export function adjustDiscipline(state: RunState, delta: number): void {
  state.discipline = Math.min(disciplineCap(state), Math.max(0, state.discipline + delta));
}

/**
 * JDG-03 — 대리가 2명 이상이면 군기 상한이 60으로 떨어진다.
 * 4인 중 절반이 NPC라는 뜻이므로 우수분대(80+)는 불가능해진다.
 */
export function disciplineCap(state: RunState): number {
  return countProxies(state) >= 2 ? 60 : 100;
}

/** 대리 수. 처음부터 빈 자리(npcVacant)는 사고가 아니므로 세지 않는다 (ROLE-03) */
function countProxies(state: RunState): number {
  return state.members.filter(
    (m) =>
      m.presence === "npcEvac" ||
      m.presence === "npcLeave" ||
      m.presence === "evacuated",
  ).length;
}

/**
 * DISC-01 하루치 군기 정산. 점호 판정 **직전**에 돈다.
 *
 * 순서가 중요하다 — 그날의 성과가 먼저 반영되어야 20~39(이완) 구간에 빠진 분대가
 * 일과를 잘 해내서 40 위로 올라온 뒤 조건 C를 통과할 수 있다.
 * 정산을 판정 뒤로 미루면 이완 구간이 곧바로 사망 구간이 되어 12.0의 구간표가 죽는다.
 */
export function applyDailyDiscipline(state: RunState, effects: Effect[]): void {
  const before = state.discipline;

  const required = state.quests.filter((q) => q.required);
  if (required.length > 0 && required.every((q) => q.status === "done")) {
    adjustDiscipline(state, GAINS.onTimeCompletion.value);
  }

  for (const quest of state.quests) {
    if (quest.kind === "joint" && quest.status === "done") {
      // 부분 성공(70%)은 제외한다 — 무결 완수에만 보상한다
      adjustDiscipline(state, GAINS.jointFlawless.value);
    }
    if (quest.kind === "surprise" && quest.status === "done") {
      adjustDiscipline(state, GAINS.surpriseSuccess.value);
    }
  }

  const injured = state.members.some(
    (m) => m.presence === "player" && m.stats.stamina <= 0,
  );
  if (!injured) {
    adjustDiscipline(state, GAINS.noInjuryDay.value);
  }

  // 회복 행동은 안 했다고 군기가 깎이지 않는다 — 몸으로 갚는다(7.0)
  const optionalMissed = state.quests.some(
    (q) => !q.required && q.kind !== "joint" && q.kind !== "care" && q.status !== "done",
  );
  if (optionalMissed) {
    adjustDiscipline(state, LOSSES.optionalMissed.value);
  }

  // 대리 유지 비용. 처음부터 비어 있던 자리(npcVacant)는 면제된다 — 사고와 선택은 다른 사건이다
  const proxies = countProxies(state);
  if (proxies > 0) {
    adjustDiscipline(state, LOSSES.npcProxy.value * proxies);
  }

  applyBandConsequences(state);
  applyOfficerTrust(state);

  if (state.discipline !== before) {
    effects.push({
      type: "disciplineChanged",
      from: before,
      to: state.discipline,
      band: disciplineBand(state.discipline).id,
    });
  }
}

/** 12.0 구간 효과 중 다음 날로 넘어가는 것 */
function applyBandConsequences(state: RunState): void {
  const band = disciplineBand(state.discipline);
  state.nextDayExtraOptional = band.nextDayExtraOptional ?? 0;
}

/**
 * DISC-02 간부 신뢰도. 세 간부가 각각 다른 항목을 본다 —
 * 그래서 어느 간부의 구제권을 노릴지가 저녁 시간 사용의 판단이 된다.
 */
function applyOfficerTrust(state: RunState): void {
  const delta = OFFICERS.dailyDelta;
  const max = OFFICERS.max;

  const trainingDone = state.quests
    .filter((q) => q.training !== null)
    .every((q) => q.status === "done");
  const appearanceOk = state.members
    .filter((m) => m.presence === "player")
    .every((m) => m.stats.hygiene >= 40);
  const requiredDone = state.quests
    .filter((q) => q.required)
    .every((q) => q.status === "done");

  state.trust.platoonLeader = clampTrust(
    state.trust.platoonLeader + (trainingDone ? delta : -delta),
    max,
  );
  state.trust.assistant = clampTrust(
    state.trust.assistant + (appearanceOk && state.discipline >= 60 ? delta : -delta),
    max,
  );
  state.trust.sergeantMajor = clampTrust(
    state.trust.sergeantMajor + (requiredDone ? delta : -delta),
    max,
  );
}

function clampTrust(value: number, max: number): number {
  return Math.min(max, Math.max(0, value));
}

/** 구제권을 발동할 수 있는 간부 — 신뢰도가 가장 높은 1명뿐이다 */
export function bestOfficer(state: RunState): keyof RunState["trust"] {
  const entries = Object.entries(state.trust) as [keyof RunState["trust"], number][];
  return entries.reduce((best, entry) => (entry[1] > best[1] ? entry : best))[0];
}

/** 신뢰도가 낮은 간부는 불시 점호 대상으로 우선 지정한다 */
export function strictestOfficer(state: RunState): keyof RunState["trust"] {
  const entries = Object.entries(state.trust) as [keyof RunState["trust"], number][];
  return entries.reduce((worst, entry) => (entry[1] < worst[1] ? entry : worst))[0];
}

/** 인원 사고 — 후송이 발생하면 계급과 무관하게 분대가 아프다 (JDG-03) */
export function penalizeIncident(state: RunState): void {
  adjustDiscipline(state, LOSSES.memberIncident.value);
}

export function penalizeLate(state: RunState): void {
  adjustDiscipline(state, LOSSES.late.value);
}

export function penalizeEquipmentLoss(state: RunState): void {
  adjustDiscipline(state, LOSSES.equipmentLost.value);
}
