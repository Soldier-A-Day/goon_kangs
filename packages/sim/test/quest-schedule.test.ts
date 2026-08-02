import { describe, expect, it } from "vitest";
import {
  avoidAdjacentMinigame,
  createRngState,
  generateDayQuests,
  planFor,
  requiredCountFor,
  type RunState,
  type TempBand,
} from "../src/index.js";
import { fullSquad } from "./helpers.js";

function stateAt(day: number, band: TempBand = "normal", seed = 1): RunState {
  const state = fullSquad({ seed });
  state.day = day;
  state.weather = { band, feelsLike: 12, airTemp: 12, humidity: 50, windSpeed: 0, rain: false };
  state.rngState = createRngState(seed * 31 + day);
  state.elapsedRealMs = Number.MAX_SAFE_INTEGER;
  return state;
}

/**
 * 한 분대원의 보직 일과(role, 훈련 체크포인트 제외)만 phase 순서(생성 순서 =
 * `pickPhase`가 인덱스로 읽는 순서)대로 뽑아낸다 — S5가 인접 배치를 다루는
 * 대상이 이 배열이다.
 */
function roleMinigameSchedule(quests: readonly import("../src/index.js").Quest[], memberId: string) {
  return quests.filter(
    (q) => q.kind === "role" && q.ownerId === memberId && q.training === null,
  );
}

  /**
   * 재배열은 required 프리픽스를 유지하려고 **필수 그룹·선택 그룹 내부에서만**
   * 섞는다(요구사항 노트 참고 — "required 프리픽스 유지 + 그룹 내 재배열이면
   * 충분하다"). 그래서 인접 회피도 그룹 내부에서만 보장되고, 두 그룹의 경계
   * (필수 마지막 항목 ↔ 선택 첫 항목)는 대상이 아니다 — 이 시험도 그 경계를
   * 건너뛰고 그룹별로 따로 인접성을 본다.
   */
  function assertNoAdjacentDuplicateWithinGroup(
    schedule: readonly { required: boolean; minigame: { type: string } | null }[],
    label: string,
  ) {
    for (const wantRequired of [true, false]) {
      const group = schedule.filter((q) => q.required === wantRequired);
      const types = group.map((q) => q.minigame?.type ?? null).filter((t) => t !== null) as string[];
      const freq = new Map<string, number>();
      for (const t of types) freq.set(t, (freq.get(t) ?? 0) + 1);
      const maxFreq = types.length > 0 ? Math.max(...freq.values()) : 0;
      // 최다빈도 원형이 과반을 넘으면 애초에 회피 불가능하다 — 걸러낸다
      if (maxFreq > Math.ceil(types.length / 2)) continue;

      for (let i = 1; i < group.length; i += 1) {
        const prevType = group[i - 1]!.minigame?.type ?? null;
        const curType = group[i]!.minigame?.type ?? null;
        if (prevType === null || curType === null) continue;
        expect(prevType, `${label} · ${wantRequired ? "필수" : "선택"} · index ${i}`).not.toBe(
          curType,
        );
      }
    }
  }

describe("S5 — 같은 미니게임 원형 2연속 배치 회피", () => {
  it("여러 시드·일차에서 한 분대원의 보직 일과에 (그룹 내) 인접 동일 원형이 없다", () => {
    for (let seed = 1; seed <= 40; seed += 1) {
      for (const day of [3, 5, 8, 11, 14]) {
        const state = stateAt(day, "normal", seed);
        const [quests] = generateDayQuests(state);

        for (const member of state.members) {
          const schedule = roleMinigameSchedule(quests, member.id);
          assertNoAdjacentDuplicateWithinGroup(
            schedule,
            `시드 ${seed} · D-${day} · ${member.role}`,
          );
        }
      }
    }
  });

  it("requiredCountFor 불변식은 재배열 후에도 유지된다", () => {
    for (let seed = 1; seed <= 20; seed += 1) {
      for (const day of [2, 4, 9, 13]) {
        const state = stateAt(day, "normal", seed);
        const [quests] = generateDayQuests(state);
        const plan = planFor(day);

        for (const member of state.members) {
          expect(
            requiredCountFor(quests, member),
            `시드 ${seed} · D-${day} · ${member.role}`,
          ).toBe(plan.required.total + state.nextDayExtraRequired);
        }
      }
    }
  });

  it("같은 입력이면 항상 같은 출력이다 — 결정론", () => {
    const a = generateDayQuests(stateAt(6, "normal", 7))[0];
    const b = generateDayQuests(stateAt(6, "normal", 7))[0];
    expect(JSON.stringify(a)).toBe(JSON.stringify(b));
  });
});

describe("avoidAdjacentMinigame — 재배열 헬퍼 단위 시험", () => {
  const typeOf = (x: { type: string | null }) => x.type;

  it("인접 동일 원형을 흩어 놓는다", () => {
    const input = [
      { id: "a", type: "SCRUB" },
      { id: "b", type: "SCRUB" },
      { id: "c", type: "PLACE" },
    ];
    const out = avoidAdjacentMinigame(input, typeOf);
    for (let i = 1; i < out.length; i += 1) {
      expect(out[i]!.type).not.toBe(out[i - 1]!.type);
    }
    // 다중집합 자체는 바뀌지 않는다 — 순서만 바뀐다
    expect(out.map((x) => x.id).sort()).toEqual(["a", "b", "c"]);
  });

  it("회피 불가능하면(전부 같은 원형) 원래 순서 그대로 돌려준다", () => {
    const input = [
      { id: "a", type: "SCRUB" },
      { id: "b", type: "SCRUB" },
      { id: "c", type: "SCRUB" },
    ];
    const out = avoidAdjacentMinigame(input, typeOf);
    expect(out.map((x) => x.id)).toEqual(["a", "b", "c"]);
  });

  it("판이 없는 항목(null)은 서로도, 다른 원형과도 충돌로 치지 않는다", () => {
    const input = [
      { id: "a", type: null },
      { id: "b", type: null },
      { id: "c", type: "SCRUB" },
    ];
    const out = avoidAdjacentMinigame(input, typeOf);
    expect(out.map((x) => x.id).sort()).toEqual(["a", "b", "c"]);
  });

  it("0~1개짜리 입력은 그대로 돌려준다", () => {
    expect(avoidAdjacentMinigame([], typeOf)).toEqual([]);
    const single = [{ id: "a", type: "SCRUB" }];
    expect(avoidAdjacentMinigame(single, typeOf)).toEqual(single);
  });

  it("순수 함수다 — 같은 입력에 항상 같은 출력", () => {
    const input = [
      { id: "a", type: "SCRUB" },
      { id: "b", type: "PLACE" },
      { id: "c", type: "SCRUB" },
      { id: "d", type: "PLACE" },
      { id: "e", type: "AUDIT" },
    ];
    const out1 = avoidAdjacentMinigame(input, typeOf);
    const out2 = avoidAdjacentMinigame(input, typeOf);
    expect(out1).toEqual(out2);
  });
});
