import { describe, expect, it } from "vitest";
import {
  createRngState,
  generateDayQuests,
  planFor,
  requiredCountFor,
  type MinigameType,
  type Quest,
  type RunState,
  type TempBand,
} from "../src/index.js";
import { fullSquad } from "./helpers.js";

/**
 * F-1(WORKORDER) 잔여 — "원형 14종이 첫 3일 안에 다 쏟아진다"는 문제의 재현·회귀 시험.
 *
 * `quests.json`의 `archetypeIntroDay`가 SCRUB·PLACE·AUDIT·BALANCE·TRACE를 D-1부터
 * (TRACE가 D-1인 것은 comms 보직 풀이 통신 계열(TRACE) 없이는 표본이 2종뿐이라서다),
 * SORT·TIMING·HOLD를 D-3부터, 나머지를 D-5부터 연다. `quests.ts`의 `sampleGated`가
 * 그 표를 2단계 표본으로 적용한다 — **필수 자리는 항상 그날 도입된 원형 안에서만
 * 나온다**(1단계가 부족할 때만 2단계로 넘어가고, 넘어가도 그 초과분은 선택 자리로만
 * 간다). 보직 풀이 작은 쪽(comms 등)은 선택 자리가 이따금 다음 원형을 조금 당겨
 * 보여줄 수 있다 — 그건 의도된 안전판이지 버그가 아니다. 그래서 이 시험은
 * "필수는 절대 안 샌다"와 "그날 새로 보여주는 원형 종수가 실제로 줄었다"를 본다.
 */
function stateAt(day: number, band: TempBand = "normal", seed = 1): RunState {
  const state = fullSquad({ seed });
  state.day = day;
  state.weather = { band, feelsLike: 12, airTemp: 12, humidity: 50, windSpeed: 0, rain: false };
  state.rngState = createRngState(seed * 31 + day);
  // 원형 게이트 자체를 보는 시험이라, F-1의 실시간 하한이 우연히 끼어들지
  // 않도록 충분히 큰 값을 준다 — 하한은 unlock.test.ts가 따로 잰다
  state.elapsedRealMs = Number.MAX_SAFE_INTEGER;
  return state;
}

function typesOf(quests: readonly Quest[], onlyRequired: boolean): Set<MinigameType> {
  const found = new Set<MinigameType>();
  for (const quest of quests) {
    if (onlyRequired && !quest.required) continue;
    if (!quest.minigame) continue;
    found.add(quest.minigame.type);
    if (quest.minigame.phase2) found.add(quest.minigame.phase2);
  }
  return found;
}

const DAY3_ARCHETYPES: readonly MinigameType[] = ["SORT", "TIMING", "HOLD"];
// TRACE는 D-1 도입이다 (comms 표본 확보 — quests.json _archetypeIntroDayNote 참고)
const DAY5_ARCHETYPES: readonly MinigameType[] = [
  "MASH",
  "SEQ",
  "RHYTHM",
  "TRACK",
  "SEARCH",
  "REACT",
];

describe("F-1 잔여 — 원형 도입 일차 게이트", () => {
  it("필수 퀘스트는 D-1~2에 day3·day5 원형이 절대 섞이지 않는다", () => {
    const late = [...DAY3_ARCHETYPES, ...DAY5_ARCHETYPES];
    for (let seed = 1; seed <= 60; seed += 1) {
      for (const day of [1, 2]) {
        const [quests] = generateDayQuests(stateAt(day, "normal", seed));
        const usedRequired = typesOf(quests, true);
        for (const type of late) {
          expect(
            usedRequired.has(type),
            `시드 ${seed} · D-${day} 필수 자리에 ${type}이 등장했다`,
          ).toBe(false);
        }
      }
    }
  });

  it("필수 퀘스트는 D-3~4에 day5 원형이 절대 섞이지 않는다", () => {
    for (let seed = 1; seed <= 60; seed += 1) {
      for (const day of [3, 4]) {
        const [quests] = generateDayQuests(stateAt(day, "normal", seed));
        const usedRequired = typesOf(quests, true);
        for (const type of DAY5_ARCHETYPES) {
          expect(
            usedRequired.has(type),
            `시드 ${seed} · D-${day} 필수 자리에 ${type}이 등장했다`,
          ).toBe(false);
        }
      }
    }
  });

  it("선택 자리는 보직 풀이 작을 때 안전판으로 다음 원형을 당겨 올 수 있다 — 그래도 다수는 그날 원형이다", () => {
    // comms(7종 중 통신 계열 TRACE 3종)처럼 표본이 작은 보직은 굴림이 높게 나오면
    // (roleTotal이 그 보직의 그날 도입분 개수를 넘으면) 선택 자리 한둘이 다음
    // 원형으로 샐 수 있다 — `sampleGated`의 2단계가 의도한 안전판이다(필수는
    // 절대 안 샌다, 위 두 시험). 그래도 "거의 다" 그날 도입분이어야 실질적인
    // 완화 효과가 있다고 볼 수 있다 — 실측(100시드)은 85% 안팎이라 75%를 문턱으로 둔다.
    const tier1: readonly MinigameType[] = ["SCRUB", "PLACE", "AUDIT", "BALANCE", "TRACE"];
    let tier1Count = 0;
    let total = 0;
    for (let seed = 1; seed <= 100; seed += 1) {
      const [quests] = generateDayQuests(stateAt(1, "normal", seed));
      for (const quest of quests) {
        if (quest.kind !== "role" || !quest.minigame) continue;
        total += 1;
        if (tier1.includes(quest.minigame.type)) tier1Count += 1;
      }
    }
    expect(total).toBeGreaterThan(0);
    expect(tier1Count / total).toBeGreaterThan(0.75);
  });

  it("D-5부터는 필수 자리에도 day5 원형이 나올 수 있다 — 제한이 풀린다", () => {
    let sawLateInRequired = false;
    for (let seed = 1; seed <= 60 && !sawLateInRequired; seed += 1) {
      const [quests] = generateDayQuests(stateAt(5, "normal", seed));
      const usedRequired = typesOf(quests, true);
      if (DAY5_ARCHETYPES.some((t) => usedRequired.has(t))) sawLateInRequired = true;
    }
    expect(sawLateInRequired, "D-5 필수 자리에서 day5 원형이 60시드 내내 한 번도 안 나왔다").toBe(
      true,
    );
  });

  it("게이트가 걸려도 필수 건수 불변식은 깨지지 않는다 — 18일 × 여러 시드", () => {
    for (let seed = 1; seed <= 25; seed += 1) {
      for (const day of [1, 2, 3, 4, 5, 6, 9, 13, 17]) {
        const state = stateAt(day, "normal", seed);
        const [quests] = generateDayQuests(state);
        const plan = planFor(day);

        for (const member of state.members) {
          expect(
            requiredCountFor(quests, member),
            `시드 ${seed} · D-${day} · ${member.role}`,
          ).toBe(plan.required.total);
        }
      }
    }
  });

  it("게이트가 걸려도 보직 퀘스트는 여전히 그 보직에게만 간다", () => {
    for (let seed = 1; seed <= 10; seed += 1) {
      const state = stateAt(1, "normal", seed);
      const [quests] = generateDayQuests(state);
      for (const member of state.members) {
        const roleQuests = quests.filter(
          (q) => q.kind === "role" && q.ownerId === member.id && q.training === null,
        );
        for (const quest of roleQuests) {
          expect(quest.id).toContain(member.role);
        }
      }
    }
  });

  it("결정론 — 같은 상태면 같은 배정이 나온다(게이트가 rng 소비 규칙을 흔들지 않는다)", () => {
    const a = generateDayQuests(stateAt(2, "normal", 7))[0];
    const b = generateDayQuests(stateAt(2, "normal", 7))[0];
    expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  });
});
