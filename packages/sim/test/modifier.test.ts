import { describe, expect, it } from "vitest";
import {
  accrueSupplyPoints,
  createRngState,
  createRun,
  generateDayQuests,
  nextInt,
  rollSurprise,
  rollWeeklyModifier,
  supplyPointsMultiplier,
  surpriseChanceMultiplier,
  trainingWorkMultiplier,
  weatherFor,
  weatherTempOffset,
  weeklyModifierById,
  type RngState,
  type WeeklyModifierId,
} from "../src/index.js";
import { fullSquad } from "./helpers.js";

const ALL_IDS: readonly WeeklyModifierId[] = [
  "coldSnap",
  "tightSupply",
  "inspection",
  "trainingPush",
];

describe("C-3 주간 변조 — 결정론", () => {
  it("같은 시드는 같은 변조를 낸다", () => {
    expect(rollWeeklyModifier(20260803).id).toBe(rollWeeklyModifier(20260803).id);
    expect(rollWeeklyModifier(1).id).toBe(rollWeeklyModifier(1).id);
  });

  it("createRun도 같은 시드면 같은 변조로 확정한다", () => {
    const a = fullSquad({ seed: 1234 });
    const b = fullSquad({ seed: 1234 });
    expect(a.weeklyModifier).toBe(b.weeklyModifier);
    // 런이 실제로 실은 값은 순수 함수(rollWeeklyModifier)가 시드만으로 내는 값과 같아야 한다
    expect(a.weeklyModifier).toBe(rollWeeklyModifier(1234).id);
  });

  it("계절 롤을 밀어내지 않는다 — 별도 스트림이라 계절 확정에 영향이 없다", () => {
    // 계절은 seed로 한 번 굴린 결과다(run.ts). 변조 롤을 추가해도 이 값이
    // 그대로면 계절 스트림을 건드리지 않았다는 뜻이다.
    const withSeasonRandom = createRun({
      runId: "r",
      seed: 55,
      members: [
        { id: "p1", name: "김소총", role: "rifle" },
        { id: "p2", name: "이통신", role: "comms" },
        { id: "p3", name: "박의무", role: "medic" },
        { id: "p4", name: "최행정", role: "admin" },
      ],
    });
    const again = createRun({
      runId: "r2",
      seed: 55,
      members: [
        { id: "p1", name: "김소총", role: "rifle" },
        { id: "p2", name: "이통신", role: "comms" },
        { id: "p3", name: "박의무", role: "medic" },
        { id: "p4", name: "최행정", role: "admin" },
      ],
    });
    expect(again.season).toBe(withSeasonRandom.season);
    expect(again.weeklyModifier).toBe(withSeasonRandom.weeklyModifier);
  });

  it("시드를 흩뿌리면 네 갈래가 전부 나온다", () => {
    const seen = new Set<WeeklyModifierId>();
    for (let seed = 1; seed <= 400; seed += 1) {
      seen.add(rollWeeklyModifier(seed).id);
    }
    expect([...seen].sort()).toEqual([...ALL_IDS].sort());
  });
});

describe("C-3 주간 변조 — 원형 조회", () => {
  it("id마다 한글 이름이 있다", () => {
    expect(weeklyModifierById("coldSnap").name).toBe("한파 주간");
    expect(weeklyModifierById("tightSupply").name).toBe("보급 빠듯한 주간");
    expect(weeklyModifierById("inspection").name).toBe("검열 주간");
    expect(weeklyModifierById("trainingPush").name).toBe("훈련 강화 주간");
  });

  it("정의되지 않은 id는 던진다", () => {
    expect(() => weeklyModifierById("unknown" as WeeklyModifierId)).toThrow();
  });
});

describe("C-3 주간 변조 — 파라미터 보정은 제 축만 건드린다", () => {
  it("coldSnap만 기온 오프셋을 갖는다", () => {
    for (const id of ALL_IDS) {
      expect(weatherTempOffset(id)).toBe(id === "coldSnap" ? -1.5 : 0);
    }
  });

  it("tightSupply만 보급 배율이 1이 아니다", () => {
    for (const id of ALL_IDS) {
      expect(supplyPointsMultiplier(id)).toBe(id === "tightSupply" ? 0.9 : 1);
    }
  });

  it("inspection만 돌발 확률 배율이 1이 아니다", () => {
    for (const id of ALL_IDS) {
      expect(surpriseChanceMultiplier(id)).toBe(id === "inspection" ? 1.1 : 1);
    }
  });

  it("trainingPush만 훈련 소요 배율이 1이 아니다", () => {
    for (const id of ALL_IDS) {
      expect(trainingWorkMultiplier(id)).toBe(id === "trainingPush" ? 1.1 : 1);
    }
  });
});

describe("C-3 — weatherFor에 한파 주간이 반영된다", () => {
  it("D-3(roll 기후)에서 coldSnap 시드군의 평균 기온이 눈에 띄게 낮다", () => {
    // 주간 변조 스트림(시드 XOR 상수)과 일차 날씨 스트림(시드 XOR 일차)은 서로 다른
    // 계산이라 상관이 없다 — 그래서 "coldSnap이 걸린 시드들"과 "그렇지 않은 시드들"을
    // 갈라 같은 날의 평균 기온을 비교하면 오프셋(−1.5도)만큼의 체계적 차이가 나와야 한다.
    const N = 250;
    const coldSnapTemps: number[] = [];
    const otherTemps: number[] = [];

    for (let seed = 1; seed <= 4000 && (coldSnapTemps.length < N || otherTemps.length < N); seed += 1) {
      const isColdSnap = rollWeeklyModifier(seed).id === "coldSnap";
      const weather = weatherFor(seed, 3, "cold");
      if (isColdSnap && coldSnapTemps.length < N) coldSnapTemps.push(weather.airTemp);
      if (!isColdSnap && otherTemps.length < N) otherTemps.push(weather.airTemp);
    }

    const avg = (xs: number[]) => xs.reduce((a, b) => a + b, 0) / xs.length;
    const diff = avg(coldSnapTemps) - avg(otherTemps);

    // 오프셋은 −1.5도다. 표본 변동을 감안해 방향과 대략의 크기만 본다 —
    // 정확한 값은 위 "파라미터 보정" 스위트가 단위로 고정한다.
    expect(diff).toBeLessThan(-0.5);
    expect(diff).toBeGreaterThan(-2.7);
  });
});

describe("C-3 — 보급 빠듯한 주간이 accrueSupplyPoints에 반영된다", () => {
  it("같은 하루 실적이면 tightSupply만 −10%다", () => {
    // weeklyModifier는 계절처럼 런 내내 고정인 readonly 필드다 — 시드를 바꿔
    // 다시 굴리는 대신, 같은 초기 상태를 복제해 그 필드만 갈아끼운다.
    const baseline = { ...fullSquad({ seed: 1 }), weeklyModifier: "coldSnap" as WeeklyModifierId };
    const tight = { ...fullSquad({ seed: 1 }), weeklyModifier: "tightSupply" as WeeklyModifierId };

    const before = baseline.supplyPoints;
    accrueSupplyPoints(baseline);
    accrueSupplyPoints(tight);

    const baselineGain = baseline.supplyPoints - before;
    const tightGain = tight.supplyPoints - before;

    expect(tightGain).toBe(Math.round(baselineGain * 0.9));
  });
});

describe("C-3 — 훈련 강화 주간이 체크포인트 소요에 반영된다", () => {
  it("D-3(체크포인트 1건) 소요가 trainingPush만 +10%다", () => {
    // 두 상태를 같은 rngState로 맞춰야 나머지 배정(일과 풀·롤 퀘스트)이 갈라지지
    // 않고 체크포인트 workMs 차이만 남는다.
    const push = { ...fullSquad({ seed: 3 }), day: 3, weeklyModifier: "trainingPush" as WeeklyModifierId };
    // 훈련 축은 건드리지 않는 값으로 비교군을 만든다
    const base = { ...fullSquad({ seed: 3 }), day: 3, weeklyModifier: "inspection" as WeeklyModifierId };

    const [pushQuests] = generateDayQuests(push);
    const [baseQuests] = generateDayQuests(base);

    const pushCp = pushQuests.find((q) => q.id.includes("-cp0"));
    const baseCp = baseQuests.find((q) => q.id.includes("-cp0"));
    expect(pushCp).toBeDefined();
    expect(baseCp).toBeDefined();
    expect(pushCp!.workMs).toBe(Math.round(baseCp!.workMs * 1.1));
  });
});

describe("C-3 — 검열 주간이 rollSurprise 확률에 반영된다", () => {
  it("기본 확률(18%)로는 안 걸리지만 검열 확률(19.8%)로는 걸리는 경계 롤을 찾아 비교한다", () => {
    // nextInt(rng, 0, 9999)가 [1800, 1980) 구간에 들어오는 rngState를 찾는다.
    // 그 구간은 value/10000이 [0.18, 0.198) 사이라 기본 군기 정상 구간(18%)에서는
    // "돌발 없음"(value/10000 >= chance)이고, 검열 배율(×1.1 ≈ 19.8%)에서는
    // 그 문턱을 넘지 못해 돌발이 터진다 — 배율이 실제로 롤 결과를 바꾼다는 증거다.
    let boundary: RngState | null = null;
    for (let s = 0; s < 200_000; s += 1) {
      const candidate = createRngState(s);
      const [value] = nextInt(candidate, 0, 9999);
      if (value >= 1800 && value < 1980) {
        boundary = candidate;
        break;
      }
    }
    expect(boundary).not.toBeNull();

    const state = fullSquad({ seed: 7 });
    state.day = 5; // D-4부터 열린다
    state.elapsedRealMs = 9_999_999; // realTimeFloorMs(40분)를 넘긴다
    state.discipline = 60; // 보통 구간 — surpriseChance 0.18
    state.rngState = boundary!;

    const normal = { ...state, weeklyModifier: "coldSnap" as WeeklyModifierId };
    const inspected = { ...state, weeklyModifier: "inspection" as WeeklyModifierId };

    const [normalQuest] = rollSurprise(normal, "morning");
    const [inspectedQuest] = rollSurprise(inspected, "morning");

    expect(normalQuest).toBeNull();
    expect(inspectedQuest).not.toBeNull();
  });
});
