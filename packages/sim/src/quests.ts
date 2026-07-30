import questTable from "../data/quests.json";
import { planFor, type DayPlan } from "./curriculum.js";
import { nextInt, sample, type RngState } from "./rng.js";
import type { Member, PhaseId, Quest, RunState, TempBand, Zone } from "./types.js";

interface QuestTemplate {
  readonly id: string;
  readonly label: string;
  readonly zone: string;
  readonly workSeconds: number;
  readonly bands?: string;
  readonly required?: boolean;
  readonly bivouacOnly?: boolean;
}

const COUNTS = questTable.counts;
const ROLE_POOL = questTable.role as Record<string, readonly QuestTemplate[]>;
const CHORE_POOL = questTable.chores as readonly QuestTemplate[];
const SURPRISE_POOL = questTable.surprise as readonly QuestTemplate[];
const JOINT = questTable.joint;

const COLD_BANDS: readonly TempBand[] = ["cold", "extremeCold"];
const WARM_BANDS: readonly TempBand[] = ["warm", "hot", "extremeHot"];

/** 필수가 배치되는 칸. 점호 칸에는 일과를 넣지 않는다 — 그 칸은 판정용이다. */
const REQUIRED_PHASES: readonly PhaseId[] = ["morning", "afternoon"];
const OPTIONAL_PHASES: readonly PhaseId[] = ["reveille", "afternoon", "personal"];

/**
 * 하루치 퀘스트 배정.
 *
 * 핵심 불변식은 하나다 — **1인당 필수 건수는 커리큘럼이 정한 값과 정확히 같다**(표 14-1).
 * 공통 일과의 필수분은 그 안에 포함되며 별도로 얹히지 않는다(6.0). 이 규칙이 깨지면
 * 하루 판정 총량이 늘어나 10.0의 완주율 계산이 통째로 무너진다.
 */
export function generateDayQuests(state: RunState): readonly [Quest[], RngState] {
  const plan = planFor(state.day);
  const quests: Quest[] = [];
  let rng = state.rngState;

  /* 1. 공통 일과 풀 — 분대 풀에서 나와 1인당 0~1건으로 분배된다 */
  const [poolSize, afterSize] = nextInt(
    rng,
    COUNTS.chorePoolPerDay.min,
    COUNTS.chorePoolPerDay.max,
  );
  rng = afterSize;

  const available = choresFor(state.weather.band, plan);
  const [chosen, afterChores] = sample(rng, available, poolSize);
  rng = afterChores;

  const active = state.members.filter((m) => m.presence !== "evacuated");
  const [receivers, afterReceivers] = sample(rng, active, chosen.length);
  rng = afterReceivers;

  const choreByMember = new Map<string, { template: QuestTemplate; required: boolean }>();
  chosen.forEach((template, index) => {
    const receiver = receivers[index];
    if (!receiver) return;
    // 절반은 필수, 절반은 선택. 온도 밴드 전용 일과는 대부분 필수다(표 6-2)
    const required = template.required === true || index < Math.floor(chosen.length / 2);
    choreByMember.set(receiver.id, { template, required });
  });

  /* 2. 개인별 배정 */
  const requiredTarget = plan.required.total + state.nextDayExtraRequired;

  for (const member of active) {
    const chore = choreByMember.get(member.id);
    const requiredChores = chore?.required === true ? 1 : 0;

    // 훈련 체크포인트는 각각 필수 1건으로 센다 (TRN-02) — 훈련을 수행하는 것이 곧 필수 처리다
    for (let i = 0; i < plan.required.trainingCheckpoints; i += 1) {
      quests.push({
        id: `d${state.day}-${member.id}-cp${i}`,
        kind: "role",
        label: `${plan.training ?? "훈련"} ${i + 1}구간`,
        training: plan.training,
        ownerId: member.id,
        required: true,
        phase: i < 2 ? "morning" : "afternoon",
        zone: "trainingField",
        workMs: 20_000,
        workedMs: 0,
        minActors: 1,
        status: "pending",
        delegatedFrom: null,
      });
    }

    const roleRequired = Math.max(
      0,
      requiredTarget - plan.required.trainingCheckpoints - requiredChores,
    );
    const [roleTotalRoll, afterRoleTotal] = nextInt(
      rng,
      COUNTS.roleQuestsPerDay.min,
      COUNTS.roleQuestsPerDay.max,
    );
    rng = afterRoleTotal;
    const roleTotal = Math.max(roleRequired, roleTotalRoll);

    const templates = ROLE_POOL[member.role] ?? [];
    const [picked, afterPick] = sample(rng, templates, roleTotal);
    rng = afterPick;

    picked.forEach((template, index) => {
      const required = index < roleRequired;
      quests.push(
        toQuest(template, {
          id: `d${state.day}-${member.id}-${template.id}`,
          kind: "role",
          ownerId: member.id,
          required,
          phase: pickPhase(required, index),
        }),
      );
    });

    if (chore) {
      quests.push(
        toQuest(chore.template, {
          id: `d${state.day}-${member.id}-${chore.template.id}`,
          kind: "chore",
          ownerId: member.id,
          required: chore.required,
          phase: pickPhase(chore.required, 0),
        }),
      );
    }
  }

  /* 3. 합동 퀘스트 — 요구 인원은 커리큘럼이 정한다 (표 14-1) */
  if (plan.jointActors > 0 && plan.joint) {
    quests.push({
      id: `d${state.day}-joint`,
      kind: "joint",
      label: plan.joint,
      training: null,
      ownerId: null,
      // 합동은 조건 B로 따로 판정되므로 조건 A의 필수 수를 늘리지 않는다
      required: false,
      phase: "afternoon",
      zone: jointZone(state.day),
      workMs: JOINT.workSeconds * 1000,
      workedMs: 0,
      minActors: plan.jointActors,
      status: "pending",
      delegatedFrom: null,
    });
  }

  return [quests, rng];
}

/**
 * QST-02 돌발 퀘스트. 시간대 진입 시 확률 롤이며, 군기가 낮을수록 자주 터진다.
 * 돌발은 필수를 밀어내지 않는다 — 시간을 잡아먹는 것 자체가 페널티다.
 */
export function rollSurprise(
  state: RunState,
  phase: PhaseId,
): readonly [Quest | null, RngState] {
  if (phase === "rollcall") return [null, state.rngState];

  const chance = surpriseChance(state.discipline);
  const [value, afterRoll] = nextInt(state.rngState, 0, 9999);
  let rng = afterRoll;
  if (value / 10000 >= chance) return [null, rng];

  const [template, afterPick] = sample(rng, SURPRISE_POOL, 1);
  rng = afterPick;
  const picked = template[0];
  if (!picked) return [null, rng];

  return [
    toQuest(picked, {
      id: `d${state.day}-${phase}-${picked.id}`,
      kind: "surprise",
      ownerId: null,
      required: false,
      phase,
    }),
    rng,
  ];
}

export function surpriseChance(discipline: number): number {
  const { surpriseBaseChance: base, surpriseMaxChance: max, surpriseDisciplinePivot: pivot } =
    COUNTS;
  if (discipline >= pivot) return base;
  const ratio = (pivot - discipline) / pivot;
  return Math.min(max, base + ratio * (max - base));
}

/* ---------------------------------------------------------------- 내부 */

function choresFor(band: TempBand, plan: DayPlan): readonly QuestTemplate[] {
  const bivouac = plan.training === "bivouac";
  return CHORE_POOL.filter((chore) => {
    if (chore.bivouacOnly === true && !bivouac) return false;
    // 온도 밴드가 공통 일과 풀을 갈아낀다 — 제설은 한랭 이하, 제초는 온난 이상 (6.1)
    if (chore.bands === "coldOrBelow") return COLD_BANDS.includes(band);
    if (chore.bands === "warmOrAbove") return WARM_BANDS.includes(band);
    return true;
  });
}

function pickPhase(required: boolean, index: number): PhaseId {
  const pool = required ? REQUIRED_PHASES : OPTIONAL_PHASES;
  return pool[index % pool.length] as PhaseId;
}

function jointZone(day: number): Zone {
  for (const [zone, days] of Object.entries(JOINT.zoneByDay)) {
    if (Array.isArray(days) && days.includes(day)) return zone as Zone;
  }
  return JOINT.zoneByDay.default as Zone;
}

function toQuest(
  template: QuestTemplate,
  overrides: {
    id: string;
    kind: Quest["kind"];
    ownerId: string | null;
    required: boolean;
    phase: PhaseId;
  },
): Quest {
  return {
    id: overrides.id,
    kind: overrides.kind,
    label: template.label,
    training: null,
    ownerId: overrides.ownerId,
    required: overrides.required,
    phase: overrides.phase,
    zone: template.zone as Zone,
    workMs: template.workSeconds * 1000,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
  };
}

/** 특정 분대원의 그날 필수 건수 — 불변식 검증용 */
export function requiredCountFor(quests: readonly Quest[], member: Member): number {
  return quests.filter((q) => q.required && q.ownerId === member.id).length;
}
