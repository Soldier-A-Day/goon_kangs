import { describe, expect, it } from "vitest";
import {
  CATALOG,
  STARTING_KIT,
  SUPPLY_DAYS,
  accrueSupplyPoints,
  bandRule,
  claimCost,
  clothingWarmth,
  fileClaim,
  hasRequiredGear,
  isSupplyDay,
  missingGear,
  runSupplyDay,
  standardClaim,
  step,
  weatherFor,
  type Effect,
  type RunState,
  type TempBand,
} from "../src/index.js";
import { SECOND, beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function supply(state: RunState): Extract<Effect, { type: "supplyClaimed" }> | undefined {
  const effects: Effect[] = [];
  runSupplyDay(state, effects);
  return effects.find((e) => e.type === "supplyClaimed");
}

describe("SUP-01 보급 사이클", () => {
  it("보급일은 3일마다 온다 (3·6·9·12·15)", () => {
    expect(SUPPLY_DAYS).toEqual([3, 6, 9, 12, 15]);
    expect(isSupplyDay(3)).toBe(true);
    expect(isSupplyDay(4)).toBe(false);
  });

  it("보급은 아침에 도착한다 — 그날의 조건 D를 막을 수 있어야 한다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.weather = weatherFor(state.seed, 3, state.season);
    state.supplyPoints = 40;

    const before = state.members[0]?.inventory.length ?? 0;
    const claimed = supply(state);

    expect(claimed).toBeDefined();
    expect(state.members[0]?.inventory.length).toBeGreaterThanOrEqual(before);
  });

  it("보급일이 아니면 아무 일도 없다", () => {
    const state = beginDay(fullSquad());
    state.day = 4;
    expect(supply(state)).toBeUndefined();
  });

  it("포인트가 모자라면 사지 못한다 — 포인트는 항상 부족하다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.supplyPoints = 0;

    const claimed = supply(state);
    expect(claimed?.items).toHaveLength(0);
    expect(state.supplyPoints).toBe(0);
  });

  it("산 만큼 포인트가 빠진다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.supplyPoints = 100;
    state.pendingClaim = ["parka"];

    supply(state);
    expect(state.supplyPoints).toBe(100 - 6);
    expect(state.members.every((m) => m.inventory.includes("parka"))).toBe(true);
  });

  it("이미 가진 것은 다시 사지 않는다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.supplyPoints = 100;
    for (const member of state.members) member.inventory.push("parka");
    state.pendingClaim = ["parka"];

    supply(state);
    expect(state.supplyPoints).toBe(100);
  });
});

describe("청구서", () => {
  it("행정병만 청구서를 작성한다", () => {
    const state = fullSquad();
    expect(fileClaim(state, "p4", ["parka"])).toBe(true); // 행정병
    expect(state.pendingClaim).toEqual(["parka"]);

    expect(fileClaim(state, "p1", ["medkit"])).toBe(false); // 소총수
    expect(state.pendingClaim).toEqual(["parka"]);
  });

  it("카탈로그에 없는 물건은 걸러진다", () => {
    const state = fullSquad();
    fileClaim(state, "p4", ["parka", "탱크"]);
    expect(state.pendingClaim).toEqual(["parka"]);
  });

  it("청구서를 내면 표준 청구를 덮어쓴다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.supplyPoints = 100;
    fileClaim(state, "p4", ["medkit"]);

    const claimed = supply(state);
    expect(claimed?.items).toEqual(["medkit"]);
  });

  it("청구서는 한 번 쓰이면 비워진다", () => {
    const state = beginDay(fullSquad());
    state.day = 3;
    state.supplyPoints = 100;
    fileClaim(state, "p4", ["medkit"]);

    supply(state);
    expect(state.pendingClaim).toEqual([]);
  });

  it("표준 청구는 다음 보급일까지의 밴드를 미리 본다", () => {
    const state = beginDay(fullSquad({ seed: 4242, config: { season: "cold" } }));
    state.day = 9;
    state.weather = weatherFor(state.seed, 9, state.season);

    const claim = standardClaim(state);
    // D-10은 극단 확정이므로 그 장비가 목록에 들어와야 한다
    const d10Gear = bandRule(weatherFor(state.seed, 10, state.season).band).requiredGear;
    for (const id of d10Gear) {
      if (STARTING_KIT.includes(id)) continue;
      expect(claim, `D-10 필수 장비 ${id}`).toContain(id);
    }
  });

  it("예산을 미리 계산할 수 있다", () => {
    expect(claimCost(["parka", "medkit"])).toBe(11);
    expect(claimCost([])).toBe(0);
  });
});

describe("표 5-1 필수 장비와 조건 D", () => {
  it("기본 지급품으로 평시·한랭·온난은 충족된다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");

    for (const band of ["normal", "cold", "warm"] as TempBand[]) {
      expect(hasRequiredGear(member, band), band).toBe(true);
    }
  });

  it("극단 밴드는 청구해야 갖춘다 — 여기가 보급의 긴장점이다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");

    expect(hasRequiredGear(member, "extremeCold")).toBe(false);
    expect(missingGear(member, "extremeCold")).toContain("parka");

    member.inventory.push("parka", "winterBoots", "insulatedCanteen");
    expect(hasRequiredGear(member, "extremeCold")).toBe(true);
  });

  it("장비가 없으면 조건 D가 깨진다", () => {
    let state = beginDay(fullSquad());
    state.weather = { ...state.weather, band: "extremeCold" };
    state = completeRequired(state);

    let guard = 0;
    while (state.status === "running" && state.day === 1 && guard++ < 100) {
      state = step(state, { type: "tick", elapsedMs: 30 * SECOND }).state;
    }

    expect(state.judgements[0]?.failedAt).toBe("D");
  });
});

describe("피복 보온치", () => {
  it("체감온도 공식의 피복 항이 실제 착용 상태에서 나온다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");

    // 기본 지급: 전투복 +2 · 방한 내피 +5
    expect(clothingWarmth(member)).toBe(7);

    member.inventory.push("parka");
    expect(clothingWarmth(member)).toBe(16);
  });

  it("방상외피를 청구했는지가 곧바로 보온으로 돌아온다", () => {
    const state = fullSquad();
    const [bare, equipped] = state.members;
    if (!bare || !equipped) throw new Error("분대원 없음");

    equipped.inventory.push("parka", "winterBoots");
    expect(clothingWarmth(equipped)).toBeGreaterThan(clothingWarmth(bare));
  });
});

describe("포인트 획득", () => {
  it("정시 완수 보너스가 붙는다", () => {
    const lazy = beginDay(fullSquad());
    const lazyBefore = lazy.supplyPoints;
    accrueSupplyPoints(lazy);
    const lazyGain = lazy.supplyPoints - lazyBefore;

    const diligent = completeRequired(beginDay(fullSquad()));
    const diligentBefore = diligent.supplyPoints;
    accrueSupplyPoints(diligent);
    const diligentGain = diligent.supplyPoints - diligentBefore;

    expect(diligentGain).toBeGreaterThan(lazyGain);
  });

  it("우수분대는 포인트를 20% 더 받는다 (12.0)", () => {
    const normal = beginDay(fullSquad());
    normal.discipline = 60;
    const normalBefore = normal.supplyPoints;
    accrueSupplyPoints(normal);
    const normalGain = normal.supplyPoints - normalBefore;

    const excellent = beginDay(fullSquad());
    excellent.discipline = 85;
    const excellentBefore = excellent.supplyPoints;
    accrueSupplyPoints(excellent);
    const excellentGain = excellent.supplyPoints - excellentBefore;

    expect(excellentGain).toBeGreaterThan(normalGain);
  });
});

describe("18일 진행", () => {
  it("표준 청구만으로도 조건 D 때문에 죽지 않는다", () => {
    for (const season of ["cold", "hot"] as const) {
      for (const seed of [1, 77, 4242]) {
        const state = playDays(fullSquad({ seed, config: { season } }), 18);
        const failedOnD = state.judgements.find((j) => j.failedAt === "D");
        expect(failedOnD, `${season} 시드 ${seed}`).toBeUndefined();
      }
    }
  });

  it("카탈로그 항목은 전부 비용과 라벨을 갖는다", () => {
    for (const item of CATALOG) {
      expect(item.cost).toBeGreaterThan(0);
      expect(item.label.length).toBeGreaterThan(0);
    }
  });
});
