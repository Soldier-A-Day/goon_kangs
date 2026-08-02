import disciplineTable from "../data/discipline.json";
import type { DisciplineDeltaEntry, DisciplineDeltaReason, Effect, RunState } from "./types.js";

const GAINS = disciplineTable.gains;
const LOSSES = disciplineTable.losses;
const OFFICERS = disciplineTable.officers;

/**
 * 항목별 짧은 라벨 — 로그 문장("무엇으로 몇 점")에 쓴다.
 *
 * `disciplineChanged.deltas`는 사유 코드만 실어 보내지만, Unity `Generated/Protocol.cs`는
 * 코드 생성이 이 발주 범위 밖이라(WORKORDER.md, "Generated/Protocol.cs 재생성·수정
 * 금지") 새 필드를 아직 읽을 수 없다. 그래서 같은 정보를 이미 배선된 `log` 이펙트의
 * `message`(judge.ts가 이미 쓰는 자유 텍스트 패턴)로도 흘려 오늘 빌드에서 바로 보이게
 * 한다 — 코드 생성이 따라잡으면 `deltas` 쪽이 정석 경로가 된다.
 */
const DISCIPLINE_DELTA_LABELS: Record<DisciplineDeltaReason, string> = {
  onTimeCompletion: "정시완수",
  jointFlawless: "합동무결",
  surpriseSuccess: "돌발성공",
  noInjuryDay: "무부상",
  optionalMissed: "선택미완",
  npcProxy: "대리유지",
};

function formatDisciplineDeltaLog(
  from: number,
  to: number,
  deltas: readonly DisciplineDeltaEntry[],
): string {
  const parts = deltas.map(
    (d) => `${DISCIPLINE_DELTA_LABELS[d.reason]} ${d.value > 0 ? "+" : ""}${d.value}`,
  );
  return `군기 정산 ${from} → ${to}: ${parts.join(" · ")}`;
}

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

  // WORKORDER.md E-2 잔여 — 항목별로 몇 점인지를 여기서 같이 적어 둔다.
  // 같은 사유가 하루에 여러 번 나오면(합동/돌발 다건) 사유별로 합산한다 —
  // "잔치 -8이 세 번"이 아니라 "잔치 -24" 한 줄이 항목별 분해에 맞다.
  const deltaTotals = new Map<DisciplineDeltaReason, number>();
  const record = (reason: DisciplineDeltaReason, value: number): void => {
    if (value === 0) return;
    adjustDiscipline(state, value);
    deltaTotals.set(reason, (deltaTotals.get(reason) ?? 0) + value);
  };

  const required = state.quests.filter((q) => q.required);
  if (required.length > 0 && required.every((q) => q.status === "done")) {
    record("onTimeCompletion", GAINS.onTimeCompletion.value);
  }

  for (const quest of state.quests) {
    if (quest.kind === "joint" && quest.status === "done") {
      // 부분 성공(70%)은 제외한다 — 무결 완수에만 보상한다
      record("jointFlawless", GAINS.jointFlawless.value);
    }
    if (quest.kind === "surprise" && quest.status === "done") {
      record("surpriseSuccess", GAINS.surpriseSuccess.value);
    }
  }

  const injured = state.members.some(
    (m) => m.presence === "player" && m.stats.stamina <= 0,
  );
  if (!injured) {
    record("noInjuryDay", GAINS.noInjuryDay.value);
  }

  // 회복 행동은 안 했다고 군기가 깎이지 않는다 — 몸으로 갚는다(7.0)
  const optionalMissed = state.quests.some(
    (q) => !q.required && q.kind !== "joint" && q.kind !== "care" && q.status !== "done",
  );
  if (optionalMissed) {
    record("optionalMissed", LOSSES.optionalMissed.value);
  }

  // 대리 유지 비용. 처음부터 비어 있던 자리(npcVacant)는 면제된다 — 사고와 선택은 다른 사건이다
  const proxies = countProxies(state);
  if (proxies > 0) {
    record("npcProxy", LOSSES.npcProxy.value * proxies);
  }

  applyBandConsequences(state);
  applyOfficerTrust(state);

  if (state.discipline !== before) {
    const deltas: DisciplineDeltaEntry[] = [...deltaTotals].map(([reason, value]) => ({
      reason,
      value,
    }));
    effects.push({
      type: "disciplineChanged",
      from: before,
      to: state.discipline,
      band: disciplineBand(state.discipline).id,
      deltas,
    });
    // Unity가 아직 `deltas`를 못 읽는 동안의 우회로 — 위 주석 참조.
    // E-3(오늘의 기록)이 그대로 주워 담을 수 있게 사람이 읽는 문장으로도 남긴다.
    if (deltas.length > 0) {
      effects.push({
        type: "log",
        message: formatDisciplineDeltaLog(before, state.discipline, deltas),
      });
    }
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
