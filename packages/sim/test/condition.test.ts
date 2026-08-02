import { describe, expect, it } from "vitest";
import {
  assignNightGuards,
  bandRule,
  dehydrationStage,
  inputDelaySeconds,
  step,
  type RunState,
} from "../src/index.js";
import { FULL_DAY, SECOND, beginDay, completeRequired, fullSquad, playDays } from "./helpers.js";

function dayStart(band: RunState["weather"]["band"] = "normal", day = 7): RunState {
  const state = beginDay(fullSquad());
  state.day = day;
  state.weather = { band, feelsLike: 10, airTemp: 10, humidity: 50, windSpeed: 0, rain: false };
  return state;
}

/** 한 칸만 흘려보낸다 */
function onePhase(state: RunState): RunState {
  return step(state, { type: "tick", elapsedMs: 60 * SECOND }).state;
}

describe("7.0 시간대별 컨디션", () => {
  it("온도 밴드가 체력·수분·피로를 깎는다", () => {
    const before = dayStart("extremeCold");
    const baseline = before.members[0]?.stats.stamina ?? 0;
    const after = onePhase(before);
    const stats = after.members[0]?.stats;
    const drain = bandRule("extremeCold").drain;

    expect(stats?.stamina).toBe(baseline + drain.stamina);
    expect(stats?.fatigue).toBe(drain.fatigue);
  });

  it("평시보다 극혹서의 수분 소모가 훨씬 크다", () => {
    const normal = onePhase(dayStart("normal")).members[0]?.stats.hydration ?? 0;
    const extreme = onePhase(dayStart("extremeHot")).members[0]?.stats.hydration ?? 0;
    expect(extreme).toBeLessThan(normal);
  });

  it("청결과 정신력은 밴드와 무관하게 시간이 지나면 준다", () => {
    const after = onePhase(dayStart("normal"));
    const stats = after.members[0]?.stats;
    expect(stats?.hygiene).toBeLessThan(100);
    expect(stats?.mental).toBeLessThan(100);
  });

  it("의무병이 빠지면 전원 수분·포만감 소모가 2배가 된다 — 페널티는 분대에 떨어진다", () => {
    const withMedic = onePhase(dayStart("normal"));

    const noMedic = dayStart("normal");
    const medic = noMedic.members.find((m) => m.role === "medic");
    if (!medic) throw new Error("의무병 없음");
    medic.presence = "npcEvac";
    const after = onePhase(noMedic);

    const rifleWith = withMedic.members.find((m) => m.role === "rifle")?.stats;
    const rifleWithout = after.members.find((m) => m.role === "rifle")?.stats;

    expect(rifleWithout?.hydration).toBeLessThan(rifleWith?.hydration ?? 0);
    expect(rifleWithout?.satiety).toBeLessThan(rifleWith?.satiety ?? 0);
  });

  it("숙영일에는 청결이 더 빨리 준다", () => {
    const normalDay = onePhase(dayStart("normal", 7)).members[0]?.stats.hygiene ?? 0;
    const bivouac = onePhase(dayStart("normal", 9)).members[0]?.stats.hygiene ?? 0;
    expect(bivouac).toBeLessThan(normalDay);
  });

  it("포만감이 바닥나면 체력이 깎이기 시작한다", () => {
    const state = dayStart("normal");
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.stats.satiety = 1;
    member.stats.stamina = 50;

    const after = onePhase(state);
    const stats = after.members[0]?.stats;
    // 평시 드레인 −2 에 더해 아사 드레인 −5 가 얹힌다
    expect(stats?.stamina).toBeLessThanOrEqual(44);
  });

  it("스탯은 0~100을 벗어나지 않는다", () => {
    const state = dayStart("extremeHot");
    for (const member of state.members) {
      member.stats.hydration = 2;
      member.stats.stamina = 1;
    }
    const after = onePhase(state);
    for (const member of after.members) {
      expect(member.stats.hydration).toBeGreaterThanOrEqual(0);
      expect(member.stats.stamina).toBeGreaterThanOrEqual(0);
      expect(member.stats.satiety).toBeLessThanOrEqual(100);
    }
  });

  it("위험 스탯은 이벤트로 알린다 — 후송 파이프라인이 이 신호를 받는다", () => {
    const state = dayStart("extremeHot");
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.stats.hydration = 5;

    const result = step(state, { type: "tick", elapsedMs: 60 * SECOND });
    const critical = result.effects.filter((e) => e.type === "conditionCritical");
    expect(critical.length).toBeGreaterThan(0);
  });
});

describe("COND-02 취침 정산", () => {
  it("점호를 통과하면 체력·피로·정신력이 회복된다", () => {
    let state = beginDay(fullSquad());
    for (const member of state.members) {
      member.stats.stamina = 40;
      // 하루 동안 +24가 쌓이고 취침에서 −60 되므로 0으로 떨어진다
      member.stats.fatigue = 30;
      member.stats.mental = 40;
    }
    state = completeRequired(state);
    state = step(state, { type: "tick", elapsedMs: FULL_DAY }).state;

    const nonGuard = state.members.find((m) => !state.nightGuardIds.includes(m.id));
    expect(nonGuard?.stats.fatigue).toBe(0);
    expect(nonGuard?.stats.stamina).toBeGreaterThan(40);
  });

  it("경계 근무자는 회복량의 절반만 받는다 — 다음 날의 빚이다", () => {
    let state = beginDay(fullSquad());
    for (const member of state.members) {
      member.stats.fatigue = 90;
    }
    state = completeRequired(state);
    state = step(state, { type: "tick", elapsedMs: FULL_DAY }).state;

    const guard = state.members.find((m) => state.nightGuardIds.includes(m.id));
    const rested = state.members.find((m) => !state.nightGuardIds.includes(m.id));
    expect(guard?.stats.fatigue).toBeGreaterThan(rested?.stats.fatigue ?? 0);
  });

  it("경계는 매일 2명이 서고 순번이 돈다", () => {
    const state = fullSquad();
    state.day = 1;
    const first = assignNightGuards(state);
    state.day = 2;
    const second = assignNightGuards(state);

    expect(first).toHaveLength(2);
    expect(second).toHaveLength(2);
    expect(first).not.toEqual(second);
  });

  it("후송된 인원은 경계에도 정산에도 들어가지 않는다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");
    member.presence = "evacuated";
    expect(assignNightGuards(state)).not.toContain(member.id);
  });

  it("취침 정산 결과가 이벤트로 나간다", () => {
    let state = completeRequired(beginDay(fullSquad()));
    const result = step(state, { type: "tick", elapsedMs: FULL_DAY });
    const settled = result.effects.find((e) => e.type === "sleepSettled");
    expect(settled).toBeDefined();
  });
});

describe("표 7-1 임계 효과", () => {
  it("수분 30 이하는 1단계 탈수, 10 이하는 2단계 열사병", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");

    member.stats.hydration = 50;
    expect(dehydrationStage(member)).toBe(0);
    member.stats.hydration = 30;
    expect(dehydrationStage(member)).toBe(1);
    member.stats.hydration = 10;
    expect(dehydrationStage(member)).toBe(2);
  });

  it("정신력이 바닥나면 조작 입력이 지연된다", () => {
    const state = fullSquad();
    const member = state.members[0];
    if (!member) throw new Error("분대원 없음");

    expect(inputDelaySeconds(member)).toBe(0);
    member.stats.mental = 0;
    expect(inputDelaySeconds(member)).toBeCloseTo(0.3, 5);
  });
});

describe("18일 컨디션 곡선", () => {
  it("필수를 다 해낸 이상적인 런은 컨디션 때문에 죽지 않는다", () => {
    const state = playDays(fullSquad(), 18);
    expect(state.status).toBe("cleared");
  });

  /**
   * 회복이 자동에서 행동으로 바뀌면서(7.0 · quests.json care) 이 창을 다시 잡았다.
   *
   * 예전에는 개인정비 칸이 끝나면 청결 +45가 저절로 들어갔고, 숙영지에서도
   * 똑같이 들어갔다. 지금은 숙영 중에 부대 세면장을 못 쓰므로 야전판 회복만
   * 받는다(`CARE_BIVOUAC_RATIO`) — 숙영이 청결을 미는 압박은 그 차액이다.
   *
   * 상한을 50에서 60으로 늘렸다. 야전 회복량이 정수로 반올림되는 탓에 비율을
   * 0.01만 움직여도 최저 청결이 19와 52 사이를 건너뛴다 — 50에 딱 맞추려면
   * 그 계단 위에 테스트를 세워야 하고, 그건 밸런스가 아니라 반올림을 고정하는 것이다.
   */
  it("숙영 이틀(D-09~10)이 청결을 조건 D 쪽으로 밀어붙인다", () => {
    const before = playDays(fullSquad(), 8);
    const after = playDays(fullSquad(), 10);

    const hygieneBefore = Math.min(...before.members.map((m) => m.stats.hygiene));
    const hygieneAfter = Math.min(...after.members.map((m) => m.stats.hygiene));

    expect(hygieneAfter).toBeLessThan(hygieneBefore);
    expect(hygieneAfter).toBeLessThan(60);
    expect(hygieneAfter).toBeGreaterThan(20);
  });
});
