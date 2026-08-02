import questTable from "../data/quests.json";
import { UNLOCK, planFor, unlocked, type DayPlan } from "./curriculum.js";
import { disciplineBand } from "./discipline.js";
import { surpriseChanceMultiplier, trainingWorkMultiplier } from "./modifier.js";
import { nextInt, sample, type RngState } from "./rng.js";
import { trainingName, trainingPlace } from "./training.js";
import type {
  Member,
  Minigame,
  MinigameType,
  PhaseId,
  Quest,
  RunState,
  TempBand,
  Zone,
} from "./types.js";

interface QuestTemplate {
  readonly id: string;
  readonly label: string;
  readonly zone: string;
  readonly workSeconds: number;
  readonly bands?: string;
  readonly required?: boolean;
  readonly bivouacOnly?: boolean;
  /** 그 일이 벌어지는 물건. 맵 생성기가 그 구역에 놓았는지 굽는 자리에서 검사한다 */
  readonly spot?: string;
  /** 일과 69건은 전부 판을 갖는다. 없으면 시간만으로 완료된다 */
  readonly minigame?: Minigame;
}

interface CareTemplate {
  readonly id: string;
  readonly label: string;
  readonly zone: string;
  readonly phase: string;
  readonly workSeconds: number;
  readonly recovery: Readonly<Record<string, number>>;
}

const COUNTS = questTable.counts;
const CARE_POOL = questTable.care as readonly CareTemplate[];
const ROLE_POOL = questTable.role as Record<string, readonly QuestTemplate[]>;
const CHORE_POOL = questTable.chores as readonly QuestTemplate[];
const SURPRISE_POOL = questTable.surprise as readonly QuestTemplate[];
const JOINT = questTable.joint;
const JOINT_BOARDS = (questTable.joint as { boards?: Record<string, Record<string, unknown>> })
  .boards ?? {};

/**
 * F-1(WORKORDER) 잔여 — 원형별 도입 일차. `quests.json`의 `archetypeIntroDay`가
 * 소유한다. 여기 없는 원형(RANDOM처럼 14종 밖)은 게이트가 없다 — 항상 허용.
 */
const ARCHETYPE_INTRO_DAY = (
  questTable as { archetypeIntroDay?: Record<string, number> }
).archetypeIntroDay ?? {};

/** 그 원형이 그 일차에 배정 후보로 들어갈 수 있는가 */
function archetypeUnlockedOn(day: number, type: MinigameType | null | undefined): boolean {
  if (!type) return true; // 판이 없는 항목은 원형 다양성과 무관하다
  const introDay = ARCHETYPE_INTRO_DAY[type];
  return introDay === undefined || day >= introDay;
}

/**
 * 템플릿 하나가 그 일차에 허용되는가. `phase2`(20초 이상 퀘스트의 2페이즈)도
 * 같이 본다 — 안 그러면 주 원형은 1일차 원형인데 2페이즈만 미도입 원형인
 * 템플릿(예: `medic-mess` AUDIT+TIMING)이 조기 노출로 새는 구멍이 생긴다.
 */
function templateArchetypesUnlocked(day: number, minigame: Minigame | null | undefined): boolean {
  if (!minigame) return true;
  if (!archetypeUnlockedOn(day, minigame.type)) return false;
  if (minigame.phase2 && !archetypeUnlockedOn(day, minigame.phase2)) return false;
  return true;
}

/** 그날 도입된 원형만 남긴다 — 부족해도 채워 넣지 않는다(선택 폭이 줄어드는 것은 안전하다) */
function filterArchetypesLenient<T extends { minigame?: Minigame }>(
  templates: readonly T[],
  day: number,
): readonly T[] {
  return templates.filter((t) => templateArchetypesUnlocked(day, t.minigame ?? null));
}

/**
 * F-1 원형 게이트를 적용한 표본 추출 — **2단계**로 뽑는다.
 *
 * 1단계는 그날 도입된 원형(`filterArchetypesLenient`)에서만 뽑는다. 2단계는
 * 1단계가 `count`를 못 채웠을 때만 나머지 원형에서 부족분을 채운다.
 *
 * 이 순서가 중요하다 — `orderedPicked`에서 필수 자리는 배열 앞쪽 `roleRequired`
 * 개다(`generateDayQuests`). 1단계 결과를 배열 앞에 이어붙이므로, 1단계
 * 표본 수가 `roleRequired` 이상이기만 하면 **필수 퀘스트는 항상 그날 도입된
 * 원형 안에서만 나온다** — 2단계로 새는 것은 초과분(선택)뿐이다. 단순히 두
 * 풀을 합쳐서 한 번에 뽑으면(예전 버전) 이 순서가 안 지켜져 필수 자리에도
 * 미도입 원형이 무작위로 섞였다.
 *
 * 그래도 1단계만으로 `count`를 못 채우면(표본이 작은 보직 — comms가 특히
 * 그렇다) 2단계가 채운다 — "필수 건수는 커리큘럼과 정확히 같다"는 불변식이
 * 원형 다양화보다 우선이다.
 */
function sampleGated<T extends { minigame?: Minigame }>(
  rng: RngState,
  templates: readonly T[],
  day: number,
  count: number,
): readonly [T[], RngState] {
  const allowed = filterArchetypesLenient(templates, day);
  const [primary, afterPrimary] = sample(rng, allowed, count);
  if (primary.length >= count) return [primary, afterPrimary];

  const allowedSet = new Set(allowed);
  const rest = templates.filter((t) => !allowedSet.has(t));
  const [extra, afterExtra] = sample(afterPrimary, rest, count - primary.length);
  return [[...primary, ...extra], afterExtra];
}

/**
 * 합동 판 정의에서 `steps`·`asymmetric`을 떼어낸다.
 *
 * 둘 다 원형 파라미터가 아니다 — `steps`는 조각 수(`jointTotal`)이고
 * `asymmetric`은 B-1 역할 배정 여부(`Quest.jointAsymmetric`)다. 둘 다 이미
 * `Quest`의 다른 필드로 옮겨 실리므로, 여기 남으면 클라로 나가는 `Minigame`에
 * 원형이 모르는 키가 섞인다.
 */
function jointBoard(spec: Record<string, unknown>): Minigame {
  const out: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(spec)) {
    if (key === "steps" || key === "asymmetric") continue;
    out[key] = value;
  }
  return out as unknown as Minigame;
}

const COLD_BANDS: readonly TempBand[] = ["cold", "extremeCold"];
const WARM_BANDS: readonly TempBand[] = ["warm", "hot", "extremeHot"];

/** 필수가 배치되는 칸. 점호 칸에는 일과를 넣지 않는다 — 그 칸은 판정용이다. */
const REQUIRED_PHASES: readonly PhaseId[] = ["morning", "afternoon"];
/**
 * 선택이 배치되는 칸.
 *
 * **일하는 칸은 둘뿐이다 — 오전(08:00)과 오후(14:00).**
 *
 * 나머지 셋은 몸을 되돌리는 칸이다(7.0). 기상 칸에서 씻고 물을 채우고,
 * 중식 칸에서 먹고 쉬고, 개인정비 칸에서 다시 먹고 씻는다. 여기 일과가
 * 섞이면 회복할 시간이 없어지고, 하루 여섯 칸이 전부 일하는 칸이 된다.
 * 점호 칸은 판정용이라 애초에 아무것도 안 들어간다.
 */
const OPTIONAL_PHASES: readonly PhaseId[] = ["morning", "afternoon"];

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

  /* 1. 공통 일과 풀 — 분대 풀에서 나와 1인당 0~1건으로 분배된다.
   *
   * **D-2부터 열린다**(표 14-1). 첫날은 보직 일과만으로 하루가 돌아가야
   * 조작을 익힐 여유가 생긴다 — D-1의 필수는 보직 2건뿐이라 공통이 없어도
   * 하루가 성립한다.
   */
  const [poolSize, afterSize] = nextInt(
    rng,
    COUNTS.chorePoolPerDay.min,
    COUNTS.chorePoolPerDay.max,
  );
  rng = afterSize;

  // F-1 — 공통 일과 풀도 원형 도입 일차를 탄다. 부족해도 채워 넣지 않는다
  // (`filterArchetypesLenient`) — 공통 일과는 인당 최대 1건이라 풀이 줄어도
  // "그날 못 나온다"로 끝날 뿐, 필수 불변식과는 무관하다
  const available = unlocked(state.day, UNLOCK.chores, state.elapsedRealMs)
    ? filterArchetypesLenient(choresFor(state.weather.band, plan), state.day)
    : [];
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

    // 훈련 체크포인트는 각각 필수 1건으로 센다 (TRN-02) — 훈련을 수행하는 것이 곧 필수 처리다.
    //
    // **그날의 훈련장에서 벌어진다.** 예전에는 전부 부대 안 훈련장(Z50)이었는데,
    // 그러면 사격도 화생방도 행군도 같은 자리에서 끝난다 — 훈련 맵 10종(§6.4)이
    // 있어도 갈 이유가 없다. 장소가 다르면 그날의 동선이 달라진다(§6.1)
    const place = trainingPlace(plan.training, state.day, state.weather.band);
    // C-3 훈련 강화 주간 — 체크포인트 소요가 +10%다
    const checkpointWorkMs = Math.round(20_000 * trainingWorkMultiplier(state.weeklyModifier));

    for (let i = 0; i < plan.required.trainingCheckpoints; i += 1) {
      quests.push({
        id: `d${state.day}-${member.id}-cp${i}`,
        kind: "role",
        label: `${trainingName(plan.training)} ${i + 1}구간`,
        training: plan.training,
        ownerId: member.id,
        required: true,
        phase: i < 2 ? "morning" : "afternoon",
        zone: place?.zone ?? "Z50",
        spot: null,
        workMs: checkpointWorkMs,
        workedMs: 0,
        minActors: 1,
        status: "pending",
        delegatedFrom: null,
        // 훈련 체크포인트는 §9.0의 코스가 따로 있다 — 일과 미니게임을 얹지 않는다
        minigame: null,
        grade: null,
        jointTotal: 0,
        jointDone: 0,
        jointAsymmetric: false,
      });
    }

    // 복귀 후 2일간은 본인 필수가 +1 된다 (JDG-03 재활) —
    // 계급이 낮아 잃을 축적이 없는 플레이어에게도 실질 비용을 부과하는 장치다
    const memberTarget = requiredTarget + (member.rehabDaysLeft > 0 ? 1 : 0);
    const roleRequired = Math.max(
      0,
      memberTarget - plan.required.trainingCheckpoints - requiredChores,
    );
    const [roleTotalRoll, afterRoleTotal] = nextInt(
      rng,
      COUNTS.roleQuestsPerDay.min,
      COUNTS.roleQuestsPerDay.max,
    );
    rng = afterRoleTotal;
    // 이완 구간(20~39)에 빠지면 다음 날 선택 퀘스트가 늘어난다 (12.0)
    const roleTotal = Math.max(roleRequired, roleTotalRoll) + state.nextDayExtraOptional;

    // F-1 — 원형 도입 일차로 후보를 좁힌다(`sampleGated`). 1단계 표본이
    // `roleRequired`에 못 미치면 2단계가 채우지만, 채워도 필수 건수는
    // 절대 못 채워지지 않는다 — 불변식이 원형 다양화보다 우선이다
    const templates = ROLE_POOL[member.role] ?? [];
    const [picked, afterPick] = sampleGated(rng, templates, state.day, roleTotal);
    rng = afterPick;

    // S5 — 같은 미니게임 원형이 연달아 배치되면 같은 판을 두 번 내리 하게 된다
    // ("너무 진부하다" 평가의 한 원인, 와리오웨어의 교훈). 재배열은 순수 함수라
    // rng를 더 소비하지 않고, required 프리픽스를 유지한 채 필수·선택 그룹
    // 내부에서만 섞으므로 `requiredCountFor` 불변식도 그대로다.
    const orderedPicked = [
      ...avoidAdjacentMinigame(picked.slice(0, roleRequired), roleQuestMinigameType),
      ...avoidAdjacentMinigame(picked.slice(roleRequired), roleQuestMinigameType),
    ];

    orderedPicked.forEach((template, index) => {
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

  /* 3. 회복 행동 — 중식 · 개인정비 칸. 사람마다 제 몫이 있다.
   *
   * 판정에 안 들어가므로 `required`는 언제나 false이고, 안 해도 아무도
   * 뭐라 하지 않는다 — 다음 날 아침에 몸이 말한다(7.0).
   */
  // 숙영일에는 **부대 안 시설을 못 쓴다.** 세면장도 식당도 영내에 있고 분대는
  // 숙영지에 있다. 그렇다고 이틀 내리 아무것도 못 먹고 못 씻으면 청결이 반드시
  // 0으로 떨어지므로, 회복은 숙영지에서 야전식으로 한다 — 몫은 줄어든다.
  // 숙영이 청결을 미는 압박(`bivouacHygienePenalty`)은 그 차액에서 나온다.
  const bivouac = plan.training === "bivouac";
  const camp = trainingPlace("bivouac", state.day, state.weather.band);

  for (const member of active) {
    for (const template of CARE_POOL) {
      quests.push({
        id: `d${state.day}-${member.id}-${template.id}`,
        kind: "care",
        label: bivouac ? `야전 ${template.label}` : template.label,
        training: null,
        ownerId: member.id,
        required: false,
        phase: template.phase as PhaseId,
        zone: bivouac ? ((camp?.zone ?? template.zone) as Zone) : (template.zone as Zone),
        spot: null,
        workMs: template.workSeconds * 1000,
        workedMs: 0,
        minActors: 1,
        status: "pending",
        delegatedFrom: null,
        // 회복은 일과가 아니다. 밥 먹는 데 판을 통과하라고 할 이유가 없다
        minigame: null,
        grade: null,
        jointTotal: 0,
        jointDone: 0,
        jointAsymmetric: false,
      });
    }
  }

  /* 4. 합동 퀘스트 — 요구 인원은 커리큘럼이 정한다 (표 14-1) */
  if (plan.jointActors > 0 && plan.joint) {
    const spec = JOINT_BOARDS[plan.joint];
    const steps = spec ? Number(spec.steps ?? 0) : 0;
    // B-1 정보 비대칭 — SEQ·TRACE 합동판에만 quests.json이 켜 둔다(JOINT.md
    // 미구현 항목). 다른 원형은 지금도 전원이 같은 화면을 보는 것이 맞다
    const asymmetric = spec ? Boolean(spec.asymmetric) : false;
    const board = spec ? jointBoard(spec) : null;
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
      spot: null,
      workMs: JOINT.workSeconds * 1000,
      workedMs: 0,
      minActors: plan.jointActors,
      status: "pending",
      delegatedFrom: null,
      // 합동도 판이 있다. 다만 혼자 통과하는 판이 아니라 **인원이 나눠 채우는**
      // 판이라, 완료는 `questCleared`가 아니라 조각 수로 결정된다
      minigame: board ?? null,
      grade: null,
      jointTotal: board ? steps : 0,
      jointDone: 0,
      jointAsymmetric: asymmetric,
    });
  }

  return [quests, rng];
}

/**
 * 회복 행동을 마쳤을 때의 몫.
 *
 * 값은 `quests.json`의 care 항목이 들고 있다 — 스탯 수치가 규칙이므로 서버에만
 * 있어야 하고(ARCH-02), 화면은 완료 여부만 보고 그린다.
 *
 * 모르는 id면 빈 것을 돌려준다. 데이터에서 항목이 빠졌다고 완료가 막히면 안 된다.
 */
export function careRecovery(
  questId: string,
  bivouac = false,
): Readonly<Record<string, number>> {
  for (const template of CARE_POOL) {
    if (!questId.endsWith(template.id)) continue;
    if (!bivouac) return template.recovery;

    // 야전판은 절반이다. 세면장 대신 수통물로 씻고 식당 대신 전투식량을 먹는다
    const scaled: Record<string, number> = {};
    for (const [key, amount] of Object.entries(template.recovery)) {
      scaled[key] = Math.round(amount * CARE_BIVOUAC_RATIO);
    }
    return scaled;
  }
  return {};
}

/** 숙영지 회복 비율. 값 하나가 숙영 이틀의 난이도를 정한다 — 밸런싱 대상이다 */
const CARE_BIVOUAC_RATIO = 0.54;

/**
 * QST-02 돌발 퀘스트. 시간대 진입 시 확률 롤이며, 군기가 낮을수록 자주 터진다.
 * 돌발은 필수를 밀어내지 않는다 — 시간을 잡아먹는 것 자체가 페널티다.
 */
export function rollSurprise(
  state: RunState,
  phase: PhaseId,
): readonly [Quest | null, RngState] {
  if (phase === "rollcall") return [null, state.rngState];
  // D-4부터 열린다 (표 14-1). 조작도 안 익은 첫 사흘에 일과를 끊고 들어오면
  // 그건 압박이 아니라 사고다
  if (!unlocked(state.day, UNLOCK.surprise, state.elapsedRealMs)) return [null, state.rngState];

  // C-3 검열 주간 — 돌발 확률이 +10%다 (예: 기본 18% → 19.8%)
  const chance = Math.min(
    1,
    surpriseChance(state.discipline) * surpriseChanceMultiplier(state.weeklyModifier),
  );
  const [value, afterRoll] = nextInt(state.rngState, 0, 9999);
  let rng = afterRoll;
  if (value / 10000 >= chance) return [null, rng];

  // F-1 — 돌발도 원형 도입 일차를 탄다. 돌발은 필수가 아니므로 부족하면
  // 그냥 안 터지는 쪽으로 끝난다(top-up 없음) — `filterArchetypesLenient`
  const pool = filterArchetypesLenient(SURPRISE_POOL, state.day);
  const [template, afterPick] = sample(rng, pool, 1);
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

/**
 * 돌발 발생률은 군기 구간이 결정한다 (12.0 DISC-01).
 * 우수분대는 −10%p, 이완 구간은 +15%p — 6.0의 "기본 18% · 최대 40%"와 같은 말이다.
 */
export function surpriseChance(discipline: number): number {
  return disciplineBand(discipline).surpriseChance;
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

function roleQuestMinigameType(template: QuestTemplate): string | null {
  return template.minigame?.type ?? null;
}

/**
 * 인접한 두 항목이 같은 원형(`typeOf`가 돌려주는 값)을 갖지 않도록 재배열한다(S5).
 *
 * **표준 "no-two-adjacent" 재배치 — 빈도 내림차순으로 짝수 자리부터 채운다.**
 * 최다빈도 원형의 개수가 `ceil(n/2)`를 넘지 않으면 이 방식으로 언제나 인접
 * 충돌 없는 배치가 나온다는 것이 증명된 결과다. 넘으면(예: 전부 같은 원형)
 * 애초에 회피가 불가능하므로 원래 순서 그대로 돌려준다 — 억지로 섞어봐야
 * 다른 자리에서 새 충돌만 생긴다.
 *
 * `typeOf`가 null을 돌려주는 항목(판이 없는 일과)은 항목마다 유일한 키를 매겨
 * 서로도, 다른 원형과도 충돌로 치지 않는다.
 *
 * rng를 전혀 쓰지 않는 순수 함수다 — 입력 배열의 내용에만 의존하는 결정론적
 * 재배열이라, 이 함수를 끼워 넣어도 `generateDayQuests`의 rng 소비 순서는
 * 바뀌지 않는다(세이브 호환 유지).
 */
export function avoidAdjacentMinigame<T>(
  items: readonly T[],
  typeOf: (item: T) => string | null,
): T[] {
  const n = items.length;
  if (n <= 1) return [...items];

  // 원형별로 묶는다. 그룹 내부는 원래 등장 순서를 그대로 지켜 안정적이다
  const buckets = new Map<string, T[]>();
  const order: string[] = [];
  items.forEach((item, index) => {
    const key = typeOf(item) ?? `__none_${index}__`;
    if (!buckets.has(key)) {
      buckets.set(key, []);
      order.push(key);
    }
    buckets.get(key)!.push(item);
  });

  const maxFreq = Math.max(...order.map((key) => buckets.get(key)!.length));
  if (maxFreq > Math.ceil(n / 2)) return [...items];

  // 빈도 내림차순 — 동률이면 첫 등장 순서를 지켜 결정론을 유지한다
  const keys = [...order].sort((a, b) => {
    const diff = buckets.get(b)!.length - buckets.get(a)!.length;
    return diff !== 0 ? diff : order.indexOf(a) - order.indexOf(b);
  });

  const result: T[] = new Array(n);
  let index = 0;
  for (const key of keys) {
    for (const item of buckets.get(key)!) {
      if (index >= n) index = 1;
      result[index] = item;
      index += 2;
    }
  }
  return result;
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
    spot: template.spot ?? null,
    workMs: template.workSeconds * 1000,
    workedMs: 0,
    minActors: 1,
    status: "pending",
    delegatedFrom: null,
    minigame: template.minigame ?? null,
    grade: null,
    jointTotal: 0,
    jointDone: 0,
    jointAsymmetric: false,
  };
}

/** 특정 분대원의 그날 필수 건수 — 불변식 검증용 */
export function requiredCountFor(quests: readonly Quest[], member: Member): number {
  return quests.filter((q) => q.required && q.ownerId === member.id).length;
}
