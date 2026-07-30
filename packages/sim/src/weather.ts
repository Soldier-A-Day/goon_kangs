import temperatureTable from "../data/temperature.json";
import type { ClimateRule } from "./curriculum.js";
import { planFor } from "./curriculum.js";
import { createRngState, nextInt, roll } from "./rng.js";
import type { RunState, TempBand, WeatherState } from "./types.js";

export interface BandRule {
  readonly id: TempBand;
  readonly label: string;
  readonly min: number;
  readonly max: number;
  /** 시간대(칸)당 스탯 변화 */
  readonly drain: { readonly stamina: number; readonly hydration: number; readonly fatigue: number };
  readonly duties: readonly string[];
  readonly requiredGear: readonly string[];
  readonly forbidden: readonly string[];
  /** 극혹한 — 90초마다 열원 접촉이 필요한 보온 게이지 */
  readonly warmthGauge?: boolean;
  readonly afternoonOutdoorToIndoor?: boolean;
  readonly mandatoryShadeRestSeconds?: number;
  readonly deferDaytimeOutdoorToNight?: boolean;
}

export const BANDS = temperatureTable.bands as readonly BandRule[];
const FORMULA = temperatureTable.formula;
const SEASONS = temperatureTable.seasons;

export type Season = "cold" | "hot";

export function bandRule(band: TempBand): BandRule {
  const rule = BANDS.find((b) => b.id === band);
  if (!rule) throw new Error(`정의되지 않은 온도 밴드: ${band}`);
  return rule;
}

/**
 * TEMP-01 체감온도.
 *
 * 체감온도 = 기온 + 습도보정 − (풍속 × 0.6) + 피복 보온치 + 지형보정
 * 습도는 고온에서만 더해진다 — 추울 때의 습도는 이 게임에서 다루지 않는다.
 */
export function feelsLike(input: {
  airTemp: number;
  humidity: number;
  windSpeed: number;
  clothing?: number;
  terrain?: number;
}): number {
  const humidityBonus =
    input.airTemp >= FORMULA.humidityAppliesAboveTemp
      ? (input.humidity / 100) * FORMULA.humidityBonusMax
      : 0;
  const clothing = input.clothing ?? FORMULA.clothing.combatUniform;
  const terrain = input.terrain ?? 0;
  return (
    input.airTemp +
    humidityBonus -
    input.windSpeed * FORMULA.windFactor +
    clothing +
    terrain
  );
}

/**
 * 표 5-1의 경계는 정수로 적혀 있지만 체감온도는 실수로 나온다(습도·풍속 보정 때문).
 * 그래서 하한만 보고 고른다 — −11.2도는 한랭(−11~0)이 아니라 극혹한이다.
 */
export function bandFor(feels: number): TempBand {
  const ordered = [...BANDS].sort((a, b) => b.min - a.min);
  const rule = ordered.find((b) => feels >= b.min);
  if (!rule) throw new Error(`밴드를 찾을 수 없는 체감온도: ${feels}`);
  return rule.id;
}

/**
 * 하루의 기온.
 *
 * **일차마다 독립된 난수 스트림을 쓴다.** 메인 RNG를 소비하면 "내일 날씨"를 미리 볼 때와
 * 실제로 그날이 왔을 때의 값이 달라진다 — 그 사이에 퀘스트 배정·돌발이 RNG를 앞으로
 * 밀어놓기 때문이다. 그러면 5.0의 "행정병만 다음 날 밴드를 정확히 본다"가 거짓이 되고,
 * 예보를 보고 보급을 청구하는 11.0의 긴장 구조도 성립하지 않는다.
 *
 * 시드가 정해지면 18일치 날씨가 이미 결정돼 있어야 예보가 예보일 수 있다.
 *
 * 커리큘럼의 기후 규칙(14.0)이 롤의 범위를 제한한다 — 튜토리얼과 심사일은 평시로 고정하고,
 * D-10과 D-15는 기후가 콘텐츠를 결정하는 칸이라 극단으로 못 박는다.
 */
export function weatherFor(seed: number, day: number, season: Season): WeatherState {
  const climate = planFor(day).climate;
  // 일차를 섞어 스트림을 가른다. 같은 런의 같은 일차는 언제 물어도 같은 답이 나온다.
  let rng = createRngState((seed ^ (day * 0x9e3779b1)) | 0);

  if (climate === "fixedNormal") {
    return makeWeather(12, 50, 0);
  }

  // 선택한 계절이 기준값을 결정하고, 반대편은 낮은 확률로만 등장한다
  let effective = season;
  if (climate === "roll") {
    const [flipped, next] = roll(rng, SEASONS._oppositeBandChance);
    rng = next;
    if (flipped) effective = season === "cold" ? "hot" : "cold";
  }

  const profile = effective === "cold" ? SEASONS.cold : SEASONS.hot;
  const variation = SEASONS._dailyVariation;

  const [delta, afterDelta] = nextInt(rng, -variation, variation);
  rng = afterDelta;
  const [wind, afterWind] = nextInt(rng, 0, profile.windMax);
  rng = afterWind;

  let airTemp = profile.baseTemp + delta;

  if (climate === "climateEvent" || climate === "bandBranch") {
    // 기후가 콘텐츠를 결정하는 칸 — 계절의 극단으로 밀어붙인다
    airTemp = effective === "cold" ? profile.baseTemp - variation : profile.baseTemp + variation;
  }

  // 유격은 평시·온난 한정(표 9-1). 기온이 아니라 체감온도를 가둬야 밴드가 보장된다 —
  // 습도·피복 보정이 붙은 뒤에야 밴드가 정해지기 때문이다.
  const limit =
    climate === "normalOrWarm" ? ([1, 30] as const) : undefined;

  return makeWeather(airTemp, profile.humidity, wind, limit);
}

function makeWeather(
  airTemp: number,
  humidity: number,
  windSpeed: number,
  feelsLimit?: readonly [number, number],
): WeatherState {
  let feels = feelsLike({ airTemp, humidity, windSpeed });
  let effectiveAirTemp = airTemp;
  if (feelsLimit) {
    const bounded = clamp(feels, feelsLimit[0], feelsLimit[1]);
    effectiveAirTemp = airTemp + (bounded - feels);
    feels = bounded;
  }
  return {
    band: bandFor(feels),
    feelsLike: Math.round(feels * 10) / 10,
    airTemp: Math.round(effectiveAirTemp * 10) / 10,
    humidity,
    windSpeed,
  };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}

/**
 * 5.0 예보. 행정병만 다음 날 밴드를 정확히 본다 — 나머지는 텍스트 힌트뿐이다.
 * 이 정보 격차가 보급 청구(11.0)에서 행정병과 분대장이 대화해야 하는 이유가 된다.
 */
export function forecast(
  state: RunState,
  season: Season,
  viewerRole: string,
): { readonly band: TempBand | null; readonly hint: string } {
  if (state.day >= state.config.totalDays) {
    return { band: null, hint: "내일은 없다 — 오늘이 마지막이다" };
  }
  const weather = weatherFor(state.seed, state.day + 1, season);
  if (viewerRole === "admin") {
    return { band: weather.band, hint: bandRule(weather.band).label };
  }
  const colder = weather.feelsLike < state.weather.feelsLike;
  return {
    band: null,
    hint: colder ? "내일은 추워질 것 같다" : "내일은 더워질 것 같다",
  };
}

/** 그날 밴드에서 금지된 일과인지 */
export function isForbidden(band: TempBand, duty: string): boolean {
  return bandRule(band).forbidden.includes(duty);
}
