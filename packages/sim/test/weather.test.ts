import { describe, expect, it } from "vitest";
import {
  BANDS,
  bandFor,
  bandRule,
  feelsLike,
  forecast,
  isForbidden,
  weatherFor,
  step,
  type TempBand,
} from "../src/index.js";
import { fullSquad } from "./helpers.js";

describe("TEMP-01 체감온도", () => {
  it("체감온도 = 기온 + 습도보정 − 풍속×0.6 + 피복 + 지형", () => {
    // 기온 0 · 습도 무시(저온) · 풍속 10 · 전투복 +2 → 0 − 6 + 2 = −4
    expect(feelsLike({ airTemp: 0, humidity: 80, windSpeed: 10 })).toBe(-4);
  });

  it("습도는 고온에서만 더해진다", () => {
    const cold = feelsLike({ airTemp: 0, humidity: 100, windSpeed: 0 });
    const hot = feelsLike({ airTemp: 30, humidity: 100, windSpeed: 0 });
    expect(cold).toBe(2);
    expect(hot).toBe(37);
  });

  it("실내는 +8, 야간은 −6", () => {
    const outdoor = feelsLike({ airTemp: -10, humidity: 40, windSpeed: 0 });
    const indoor = feelsLike({ airTemp: -10, humidity: 40, windSpeed: 0, terrain: 8 });
    const night = feelsLike({ airTemp: -10, humidity: 40, windSpeed: 0, terrain: -6 });
    expect(indoor - outdoor).toBe(8);
    expect(night - outdoor).toBe(-6);
  });

  it("젖은 피복은 보온치를 크게 깎는다", () => {
    const dry = feelsLike({ airTemp: -5, humidity: 40, windSpeed: 0, clothing: 9 });
    const wet = feelsLike({ airTemp: -5, humidity: 40, windSpeed: 0, clothing: 9 - 6 });
    expect(dry - wet).toBe(6);
  });
});

describe("표 5-1 밴드", () => {
  it("6밴드가 겹치지 않고 빈틈 없이 이어진다", () => {
    expect(BANDS).toHaveLength(6);
    for (let i = 1; i < BANDS.length; i += 1) {
      const prev = BANDS[i - 1];
      const cur = BANDS[i];
      if (!prev || !cur) throw new Error("밴드 누락");
      expect(cur.min).toBe(prev.max + 1);
    }
  });

  it("경계값이 표대로 나뉜다", () => {
    const cases: readonly [number, TempBand][] = [
      [-13, "extremeCold"],
      [-12, "extremeCold"],
      [-11, "cold"],
      [0, "cold"],
      [1, "normal"],
      [24, "normal"],
      [25, "warm"],
      [30, "warm"],
      [31, "hot"],
      [34, "hot"],
      [35, "extremeHot"],
    ];
    for (const [feels, band] of cases) {
      expect(bandFor(feels), `${feels}도`).toBe(band);
    }
  });

  it("극단 밴드일수록 드레인이 세다", () => {
    expect(bandRule("extremeCold").drain.fatigue).toBeGreaterThan(
      bandRule("normal").drain.fatigue,
    );
    expect(bandRule("extremeHot").drain.hydration).toBeLessThan(
      bandRule("hot").drain.hydration,
    );
  });

  it("혹서는 주간 행군과 유격을 금지한다", () => {
    expect(isForbidden("hot", "daytimeMarch")).toBe(true);
    expect(isForbidden("hot", "commando")).toBe(true);
    expect(isForbidden("normal", "daytimeMarch")).toBe(false);
  });

  it("극혹한에만 보온 게이지가 붙는다", () => {
    expect(bandRule("extremeCold").warmthGauge?.seconds).toBe(90);
    expect(bandRule("cold").warmthGauge).toBeUndefined();
  });
});

describe("기온 롤", () => {
  it("튜토리얼·심사일은 평시로 고정된다", () => {
    for (const day of [1, 2, 18]) {
      const weather = weatherFor(day, day, "cold");
      expect(weather.band, `D-${day}`).toBe("normal");
    }
  });

  it("혹한기 런은 추운 밴드로, 혹서기 런은 더운 밴드로 간다", () => {
    const cold = countBand(400, 7, "cold");
    const hot = countBand(400, 7, "hot");

    expect(cold.extremeCold + cold.cold).toBeGreaterThan(340);
    expect(hot.hot + hot.extremeHot + hot.warm).toBeGreaterThan(340);
  });

  it("반대편 밴드는 낮은 확률로만 등장한다 — 존재하되 지배적이지 않다", () => {
    const cold = countBand(400, 7, "cold");
    const opposite = cold.warm + cold.hot + cold.extremeHot;
    expect(opposite).toBeGreaterThan(0);
    expect(opposite / 400).toBeLessThan(0.1);
  });

  it("D-15는 계절의 극단으로 못 박힌다", () => {
    // 14.0 D-15 "혹한기 훈련 또는 혹서기 대비 훈련" — 갈래가 계절 둘뿐이라
    // 시드와 무관하게 극단이다
    for (let seed = 0; seed < 60; seed += 1) {
      expect(weatherFor(seed, 15, "cold").band, `시드 ${seed} 혹한기`).toBe("extremeCold");
      expect(weatherFor(seed, 15, "hot").band, `시드 ${seed} 혹서기`).toBe("extremeHot");
    }
  });

  it("D-10은 폭우 · 한파 · 폭염 셋으로 갈린다", () => {
    // 14.0 D-10 "기상 악화(폭우/한파/폭염)". 계절이 어느 둘을 무대에 올릴지
    // 정한다 — 추운 계절에는 한파, 더운 계절에는 폭우 아니면 폭염.
    // 영하에 쏟아지는 것은 비가 아니라 눈이고, 그건 한파 쪽이 이미 표현한다.
    for (let seed = 0; seed < 120; seed += 1) {
      const w = weatherFor(seed, 10, "cold");
      expect(w.rain, `시드 ${seed} 추운 계절에 비`).toBe(false);
      expect(w.band, `시드 ${seed}`).toBe("extremeCold");
    }

    let storms = 0;
    let heat = 0;
    for (let seed = 0; seed < 200; seed += 1) {
      const w = weatherFor(seed, 10, "hot");
      if (w.rain) {
        storms += 1;
        // 폭우는 폭염을 **깨뜨린다.** 악천후 둘을 한 날에 겹쳐 쌓지 않고,
        // 대신 그날의 어려움이 더위에서 젖는 쪽으로 옮겨간다
        expect(w.band, `시드 ${seed}`).not.toBe("extremeHot");
      } else {
        heat += 1;
        expect(w.band, `시드 ${seed}`).toBe("extremeHot");
      }
    }
    expect(storms, "폭우가 한 번도 안 나왔다").toBeGreaterThan(20);
    expect(heat, "폭염이 한 번도 안 나왔다").toBeGreaterThan(20);
  });

  it("폭우는 D-10에만 온다 — 매일 오면 악천후가 아니다", () => {
    for (let seed = 0; seed < 50; seed += 1) {
      for (let day = 1; day <= 18; day += 1) {
        if (day === 10) continue;
        expect(weatherFor(seed, day, "cold").rain, `시드 ${seed} D-${day}`).toBe(false);
      }
    }
  });

  it("유격일(D-12·13)은 평시·온난을 벗어나지 않는다", () => {
    for (let seed = 0; seed < 100; seed += 1) {
      for (const day of [12, 13]) {
        const weather = weatherFor(seed, day, "hot");
        expect(["normal", "warm"]).toContain(weather.band);
      }
    }
  });

  it("같은 시드는 같은 날씨를 낸다", () => {
    const a = weatherFor(5, 7, "cold");
    const b = weatherFor(5, 7, "cold");
    expect(a).toEqual(b);
  });
});

describe("예보", () => {
  it("행정병만 내일 밴드를 정확히 본다", () => {
    let state = fullSquad({ config: { season: "cold" } });
    state = step(state, { type: "beginDay" }).state;

    const admin = forecast(state, "cold", "admin");
    const rifle = forecast(state, "cold", "rifle");

    expect(admin.band).not.toBeNull();
    expect(rifle.band).toBeNull();
    expect(rifle.hint).toMatch(/추워|더워/);
  });

  it("예보가 실제 날씨와 일치한다 — 그렇지 않으면 예보가 예보가 아니다", () => {
    let state = fullSquad({ seed: 777, config: { season: "cold" } });
    state = step(state, { type: "beginDay" }).state;

    const predicted = forecast(state, state.season, "admin").band;

    // 하루를 흘려 실제로 그날을 맞이한다
    for (const quest of state.quests) {
      quest.status = "done";
      quest.workedMs = quest.workMs;
    }
    let guard = 0;
    while (state.status === "running" && state.day === 1 && guard++ < 100) {
      state = step(state, { type: "tick", elapsedMs: 30_000 }).state;
    }

    expect(state.day).toBe(2);
    expect(state.weather.band).toBe(predicted);
  });

  it("마지막 날에는 내일이 없다", () => {
    let state = fullSquad();
    state.day = 18;
    expect(forecast(state, "cold", "admin").band).toBeNull();
  });
});

describe("하루 시작", () => {
  it("beginDay가 그날의 날씨를 확정하고 이벤트로 알린다", () => {
    const result = step(fullSquad({ config: { season: "hot" } }), { type: "beginDay" });
    const rolled = result.effects.find((e) => e.type === "weatherRolled");
    expect(rolled).toBeDefined();
    expect(result.state.weather.band).toBe("normal"); // D-01은 평시 고정
  });

  it("계절은 런 시작 시 한 번 확정되고 바뀌지 않는다", () => {
    const state = fullSquad({ config: { season: "hot" } });
    expect(state.season).toBe("hot");
    const random = fullSquad({ config: { season: "random" } });
    expect(["cold", "hot"]).toContain(random.season);
  });
});

function countBand(
  runs: number,
  day: number,
  season: "cold" | "hot",
): Record<TempBand, number> {
  const counts: Partial<Record<TempBand, number>> = {};
  for (let seed = 0; seed < runs; seed += 1) {
    const weather = weatherFor(seed, day, season);
    const band: TempBand = weather.band;
    counts[band] = (counts[band] ?? 0) + 1;
  }
  return {
    extremeCold: counts.extremeCold ?? 0,
    cold: counts.cold ?? 0,
    normal: counts.normal ?? 0,
    warm: counts.warm ?? 0,
    hot: counts.hot ?? 0,
    extremeHot: counts.extremeHot ?? 0,
  };
}
