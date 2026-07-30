import { describe, expect, it } from "vitest";
import {
  CURRICULUM,
  createRngState,
  generateDayQuests,
  planFor,
  rollSurprise,
  step,
  surpriseChance,
  type RunState,
  type TempBand,
} from "../src/index.js";
import { beginDay, fullSquad } from "./helpers.js";

function stateAt(day: number, band: TempBand = "normal", seed = 1): RunState {
  const state = fullSquad({ seed });
  state.day = day;
  state.weather = { band, feelsLike: 12, airTemp: 12, humidity: 50, windSpeed: 0 };
  state.rngState = createRngState(seed * 31 + day);
  return state;
}

describe("배정기 불변식 — 필수 건수는 커리큘럼이 정한 값과 같다", () => {
  it("18일 × 여러 시드에서 1인당 필수가 표 14-1과 정확히 일치한다", () => {
    for (let seed = 1; seed <= 20; seed += 1) {
      for (const plan of CURRICULUM) {
        const state = stateAt(plan.day, "normal", seed);
        const [quests] = generateDayQuests(state);

        for (const member of state.members) {
          const required = quests.filter((q) => q.required && q.ownerId === member.id);
          expect(
            required.length,
            `시드 ${seed} · D-${plan.day} · ${member.role}`,
          ).toBe(plan.required.total);
        }
      }
    }
  });

  it("공통 일과의 필수분은 별도로 얹히지 않는다 — 보직 퀘스트를 밀어낸다", () => {
    let sawRequiredChore = false;

    for (let seed = 1; seed <= 30; seed += 1) {
      const state = stateAt(4, "normal", seed);
      const [quests] = generateDayQuests(state);

      for (const member of state.members) {
        const mine = quests.filter((q) => q.ownerId === member.id && q.required);
        const chores = mine.filter((q) => q.kind === "chore");
        if (chores.length > 0) sawRequiredChore = true;
        // 공통이 필수로 들어와도 총량은 그대로다
        expect(mine.length).toBe(planFor(4).required.total);
      }
    }

    expect(sawRequiredChore, "필수 공통 일과가 한 번도 배정되지 않았다").toBe(true);
  });

  it("하루 판정 총량은 4인 × 필수이며 평균 17건 근처다", () => {
    let total = 0;
    for (const plan of CURRICULUM) {
      const state = stateAt(plan.day);
      const [quests] = generateDayQuests(state);
      const required = quests.filter((q) => q.required).length;
      expect(required).toBe(plan.required.total * 4);
      total += required;
    }
    expect(total).toBe(76 * 4);
    expect(total / 18).toBeCloseTo(16.9, 1);
  });

  it("완화 난이도의 경고 +2는 그날만 얹히고 사라진다", () => {
    const state = stateAt(4);
    state.nextDayExtraRequired = 2;
    const [quests] = generateDayQuests(state);

    for (const member of state.members) {
      const required = quests.filter((q) => q.required && q.ownerId === member.id);
      expect(required.length).toBe(planFor(4).required.total + 2);
    }

    const after = beginDay(state);
    expect(after.nextDayExtraRequired).toBe(0);
  });
});

describe("보직과 공통 일과", () => {
  it("보직 퀘스트는 그 보직에게만 간다", () => {
    const state = stateAt(7);
    const [quests] = generateDayQuests(state);

    for (const member of state.members) {
      const roleQuests = quests.filter(
        (q) => q.kind === "role" && q.ownerId === member.id && q.training === null,
      );
      for (const quest of roleQuests) {
        expect(quest.id).toContain(member.role);
      }
    }
  });

  it("공통 일과는 보직을 가리지 않는다 — 그래서 하달 대상이 된다", () => {
    const owners = new Set<string>();
    for (let seed = 1; seed <= 40; seed += 1) {
      const state = stateAt(7, "normal", seed);
      const [quests] = generateDayQuests(state);
      for (const quest of quests) {
        if (quest.kind === "chore" && quest.ownerId) owners.add(quest.ownerId);
      }
    }
    expect(owners.size).toBe(4);
  });

  it("공통 일과는 1인당 최대 1건이다", () => {
    for (let seed = 1; seed <= 30; seed += 1) {
      const state = stateAt(7, "normal", seed);
      const [quests] = generateDayQuests(state);
      for (const member of state.members) {
        const chores = quests.filter((q) => q.kind === "chore" && q.ownerId === member.id);
        expect(chores.length).toBeLessThanOrEqual(1);
      }
    }
  });

  it("온도 밴드가 공통 일과 풀을 갈아낀다 — 제설은 한랭 이하, 제초는 온난 이상", () => {
    const labelsFor = (band: TempBand) => {
      const found = new Set<string>();
      for (let seed = 1; seed <= 60; seed += 1) {
        const state = stateAt(7, band, seed);
        const [quests] = generateDayQuests(state);
        for (const quest of quests) {
          if (quest.kind === "chore") found.add(quest.label);
        }
      }
      return found;
    };

    const cold = labelsFor("cold");
    const hot = labelsFor("hot");

    expect(cold.has("제설")).toBe(true);
    expect(cold.has("제초")).toBe(false);
    expect(hot.has("제초")).toBe(true);
    expect(hot.has("제설")).toBe(false);
  });

  it("숙영 전용 일과는 숙영일에만 나온다", () => {
    const bivouacLabels = new Set<string>();
    const normalLabels = new Set<string>();
    for (let seed = 1; seed <= 60; seed += 1) {
      for (const quest of generateDayQuests(stateAt(9, "normal", seed))[0]) {
        if (quest.kind === "chore") bivouacLabels.add(quest.label);
      }
      for (const quest of generateDayQuests(stateAt(7, "normal", seed))[0]) {
        if (quest.kind === "chore") normalLabels.add(quest.label);
      }
    }
    expect(bivouacLabels.has("땔감 수집")).toBe(true);
    expect(normalLabels.has("땔감 수집")).toBe(false);
  });
});

describe("훈련 체크포인트와 합동", () => {
  it("체크포인트는 커리큘럼 수만큼 1인당 생성되고 필수로 센다", () => {
    for (const plan of CURRICULUM) {
      const state = stateAt(plan.day);
      const [quests] = generateDayQuests(state);
      for (const member of state.members) {
        const checkpoints = quests.filter(
          (q) => q.ownerId === member.id && q.training !== null,
        );
        expect(checkpoints.length, `D-${plan.day}`).toBe(
          plan.required.trainingCheckpoints,
        );
        expect(checkpoints.every((q) => q.required)).toBe(true);
      }
    }
  });

  it("합동 퀘스트의 요구 인원은 커리큘럼이 정한다", () => {
    for (const plan of CURRICULUM) {
      const state = stateAt(plan.day);
      const [quests] = generateDayQuests(state);
      const joint = quests.find((q) => q.kind === "joint");

      if (plan.jointActors === 0) {
        expect(joint, `D-${plan.day}는 합동이 없어야 한다`).toBeUndefined();
        continue;
      }
      expect(joint?.minActors, `D-${plan.day}`).toBe(plan.jointActors);
    }
  });

  it("합동은 조건 A의 필수 수를 늘리지 않는다 — 조건 B로 따로 판정된다", () => {
    const [quests] = generateDayQuests(stateAt(8));
    const joint = quests.find((q) => q.kind === "joint");
    expect(joint?.required).toBe(false);
  });

  it("합동은 오후 일과에 배치된다", () => {
    const [quests] = generateDayQuests(stateAt(8));
    expect(quests.find((q) => q.kind === "joint")?.phase).toBe("afternoon");
  });
});

describe("QST-02 돌발", () => {
  it("군기 구간이 발생률을 정한다 — 우수분대 −10%p, 이완 +15%p (12.0)", () => {
    expect(surpriseChance(60)).toBeCloseTo(0.18, 5);
    expect(surpriseChance(100)).toBeCloseTo(0.08, 5);
    expect(surpriseChance(30)).toBeCloseTo(0.33, 5);
    expect(surpriseChance(0)).toBeCloseTo(0.4, 5);
  });

  it("돌발은 필수가 아니다 — 시간을 잡아먹는 것이 페널티다", () => {
    for (let seed = 1; seed <= 50; seed += 1) {
      const state = stateAt(7, "normal", seed);
      const [quest] = rollSurprise(state, "morning");
      if (quest) {
        expect(quest.required).toBe(false);
        expect(quest.ownerId).toBeNull(); // 선착 개인
      }
    }
  });

  it("점호 칸에는 돌발이 뜨지 않는다", () => {
    for (let seed = 1; seed <= 50; seed += 1) {
      const [quest] = rollSurprise(stateAt(7, "normal", seed), "rollcall");
      expect(quest).toBeNull();
    }
  });

  it("실제 발생률이 기본 확률 근처에 있다", () => {
    let raised = 0;
    const trials = 600;
    for (let seed = 1; seed <= trials; seed += 1) {
      const [quest] = rollSurprise(stateAt(7, "normal", seed), "morning");
      if (quest) raised += 1;
    }
    expect(raised / trials).toBeGreaterThan(0.12);
    expect(raised / trials).toBeLessThan(0.25);
  });
});

describe("하루 시작", () => {
  it("beginDay가 일과표를 만들고 이벤트로 알린다", () => {
    const result = step(fullSquad(), { type: "beginDay" });
    expect(result.state.quests.length).toBeGreaterThan(0);
    expect(result.state.quests.some((q) => q.kind === "role")).toBe(true);
  });

  it("배정은 결정론이다 — 같은 상태에서 같은 일과가 나온다", () => {
    const a = generateDayQuests(stateAt(5))[0];
    const b = generateDayQuests(stateAt(5))[0];
    expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  });
});
