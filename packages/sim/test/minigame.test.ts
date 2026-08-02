import { describe, expect, it } from "vitest";
import questTable from "../data/quests.json" with { type: "json" };
import { step, type MinigameType, type Quest, type RunState } from "../src/index.js";
import { SECOND, beginDay, fullSquad, toPhase } from "./helpers.js";

/**
 * 일과 미니게임 — 배정표와 완료 경로.
 *
 * 두 가지를 지킨다.
 *
 *   **배정** 69건이 전부 판을 갖는가, 원형별 건수가 설계안과 같은가.
 *            여기서 어긋나면 어떤 일과는 조용히 "그냥 시간만 흐르는" 것이 된다.
 *   **완료** 판이 붙은 일과는 시간이 아니라 통과로 끝나는가, 그리고 통과 신고를
 *            서버가 어디까지 거절하는가.
 */

type Template = {
  readonly id: string;
  readonly label: string;
  readonly zone: string;
  readonly workSeconds: number;
  readonly spot?: string;
  readonly minigame?: Record<string, unknown>;
};

const ROLE = Object.values(questTable.role as Record<string, readonly Template[]>).flat();
const ALL: readonly Template[] = [
  ...ROLE,
  ...(questTable.chores as readonly Template[]),
  ...(questTable.surprise as readonly Template[]),
];

/**
 * 원형별 사용 건수.
 *
 * 설계안의 표에서 하나가 옮겨졌다 — **배수로 정비가 `MASH`에서 `TRACE`로.**
 * 설계안 스스로 짚은 것이다: 연병장 11건 중 4건이 `MASH`(배수로·제설·제초·
 * 제초추가)라 야외 일과가 몰리는 구역에서 체감 반복이 가장 심하게 나온다.
 * 배수로를 물길 내기로 돌리면 3건으로 줄고 성격도 갈린다.
 */
const EXPECTED: Readonly<Record<MinigameType, number>> = {
  PLACE: 10,
  AUDIT: 10,
  SCRUB: 8,
  TRACE: 6,
  BALANCE: 5,
  HOLD: 5,
  MASH: 4,
  SORT: 4,
  SEARCH: 4,
  TIMING: 3,
  SEQ: 3,
  RHYTHM: 3,
  TRACK: 2,
  REACT: 1,
  RANDOM: 1,
};

describe("배정표", () => {
  it("일과 69건이 전부 판을 갖는다", () => {
    expect(ALL).toHaveLength(69);
    const bare = ALL.filter((q) => q.minigame === undefined).map((q) => q.id);
    expect(bare).toEqual([]);
  });

  it("원형별 건수가 설계안과 정확히 같다", () => {
    const counts: Record<string, number> = {};
    for (const quest of ALL) {
      const type = String(quest.minigame?.type);
      counts[type] = (counts[type] ?? 0) + 1;
    }
    expect(counts).toEqual(EXPECTED);
    expect(Object.values(EXPECTED).reduce((a, b) => a + b, 0)).toBe(69);
  });

  it("난이도는 1~3이다 — 런타임으로 +1 하는 여지를 남긴다", () => {
    for (const quest of ALL) {
      const difficulty = quest.minigame?.difficulty;
      expect(typeof difficulty, quest.id).toBe("number");
      expect(difficulty as number, quest.id).toBeGreaterThanOrEqual(1);
      expect(difficulty as number, quest.id).toBeLessThanOrEqual(3);
    }
  });

  it("제한 시간을 판 안에 두지 않는다 — `workSeconds`가 유일한 출처다", () => {
    // 두 곳에 두면 반드시 어긋난다. 설계안이 미니게임 정의를 별도 파일로 빼지
    // 말라고 한 것도 같은 이유다.
    const forbidden = ["duration", "seconds", "workSeconds", "timeLimit", "limitMs"];
    for (const quest of ALL) {
      for (const key of Object.keys(quest.minigame ?? {})) {
        expect(forbidden, `${quest.id}.${key}`).not.toContain(key);
      }
    }
  });

  it("`REACT`를 단독으로 쓰는 것은 `간부 순찰` 하나뿐이다", () => {
    // 나머지는 전부 다른 원형 위에 끼워 넣는 인터럽트 모디파이어다.
    // 이게 무너지면 돌발이 "일과와 같은 게임인데 이름만 다른 것"이 된다.
    const standalone = ALL.filter((q) => q.minigame?.type === "REACT").map((q) => q.id);
    expect(standalone).toEqual(["sur-officer"]);
    expect(ALL.filter((q) => q.minigame?.interrupt !== undefined).map((q) => q.id))
      .toEqual(["medic-evac", "sur-leak"]);
  });

  it("같은 이름의 일과가 두 구역에 있지 않다", () => {
    // `발전기 급유`가 Z10과 Z14에 각각 있었다. 이름이 같고 소요만 18/20초로
    // 달라, 플레이어에게는 같은 게임을 두 번 하는 것으로만 읽혔다.
    // 다른 일이면 이름을 갈라 둔다 — 정비고는 차량용, 보일러실은 비상용이다.
    const seen = new Map<string, string>();
    for (const quest of ALL) {
      const { label, zone } = quest;
      const previous = seen.get(label);
      expect(previous ?? zone, `${label}이 ${previous}와 ${zone} 두 곳에 있다`).toBe(zone);
      seen.set(label, zone);
    }
  });

  it("한 구역이 같은 원형으로 뒤덮이지 않는다", () => {
    // 연병장은 야외 일과가 몰려 11건이나 되므로, 여기가 한 원형으로 쏠리면
    // 체감 반복이 가장 심하게 나온다.
    const byZone = new Map<string, Map<string, number>>();
    for (const quest of ALL) {
      const zone = quest.zone;
      const type = String(quest.minigame?.type);
      const table = byZone.get(zone) ?? new Map<string, number>();
      table.set(type, (table.get(type) ?? 0) + 1);
      byZone.set(zone, table);
    }

    for (const [zone, table] of byZone) {
      const total = [...table.values()].reduce((a, b) => a + b, 0);
      if (total < 5) continue;
      for (const [type, n] of table) {
        expect(n / total, `${zone}이 ${type}로 뒤덮였다`).toBeLessThanOrEqual(0.4);
      }
    }
  });

  it("69건이 전부 어느 물건 앞에서 하는지 안다", () => {
    // 예전에는 클라가 일과 **이름**에서 키워드를 뽑아 방 안의 물건을 골랐다.
    // 표가 예순 줄이었는데도 "정돈"·"점검" 같은 말이 여러 일과에 걸쳐 있어
    // 관물대 정돈과 복도 정돈이 같은 자리에서 벌어졌다.
    //
    // 그 물건이 그 구역에 실제로 놓였는지는 맵 생성기(`tools/sprites/basemap.py`)가
    // 굽는 자리에서 검사한다 — 여기서는 비어 있지 않은지만 본다.
    const bare = ALL.filter((q) => !q.spot).map((q) => q.id);
    expect(bare).toEqual([]);
  });

  it("2페이즈는 20초 이상 퀘스트에만 붙는다", () => {
    // 12초짜리에 조작 두 개를 얹으면 설명만 하다 끝난다.
    for (const quest of ALL) {
      if (quest.minigame?.phase2 === undefined) continue;
      expect(quest.workSeconds, quest.id).toBeGreaterThanOrEqual(20);
    }
  });
});

/* ------------------------------------------------------------------ 완료 */

/**
 * 그 일과를 지금 할 수 있는 자리에 담당자를 세운다.
 *
 * 도착을 틱으로 기다리지 않고 이동 잔여를 직접 지운다. 틱으로 흘려보내면
 * 짧은 칸(기상 10초)은 도착하기 전에 끝나 버려 일과가 잠긴다.
 */
function atQuest(state: RunState, quest: Quest): RunState {
  const at = toPhase(state, quest.phase);
  const moved = step(at, { type: "move", memberId: quest.ownerId!, to: quest.zone }).state;
  moved.members.find((m) => m.id === quest.ownerId)!.travelRemainingMs = 0;
  return moved;
}

/** 판이 붙은 개인 일과 하나를 골라 그 자리까지 데려간다 */
function boardQuest(seed = 7): { state: RunState; quest: Quest } {
  let state = beginDay(fullSquad({ seed }));
  const quest = state.quests.find(
    (q) => q.minigame !== null && q.ownerId !== null && q.kind !== "surprise",
  );
  if (!quest) throw new Error("판이 붙은 일과 없음");
  state = atQuest(state, quest);
  return { state, quest };
}

const find = (state: RunState, id: string) => state.quests.find((q) => q.id === id)!;

/** 소요를 통째로 흘려 넣는다 — 판을 통과하지 않은 채 시간만 쓴 상태 */
function spendFullTime(state: RunState, quest: Quest): RunState {
  return step(state, {
    type: "work",
    memberId: quest.ownerId!,
    questId: quest.id,
    deltaMs: quest.workMs,
  }).state;
}

describe("판이 붙은 일과는 통과해야 끝난다", () => {
  it("시간이 다 흘러도 완료되지 않는다", () => {
    // 예전에는 붙잡고 있는 시간이 곧 완료였다. 그러면 판을 아무리 만들어도
    // 화면에서 하는 일과 서버가 세는 일이 갈라진다.
    const { state, quest } = boardQuest();
    const after = spendFullTime(state, quest);

    const done = find(after, quest.id);
    expect(done.workedMs).toBe(quest.workMs);
    expect(done.status).toBe("active");
    expect(done.grade).toBeNull();
  });

  it("통과를 신고하면 완료되고 등급이 남는다", () => {
    const { state, quest } = boardQuest();
    let after = spendFullTime(state, quest);
    after = step(after, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "A",
    }).state;

    expect(find(after, quest.id).status).toBe("done");
    expect(find(after, quest.id).grade).toBe("A");
  });

  it("실패는 아무것도 바꾸지 않는다 — 그 자리에서 다시 시도한다", () => {
    // 실패 신고라는 것이 없다. 판을 다시 여는 것은 순전히 클라 동작이고,
    // 계속 실패하면 시간대 종료로 잠긴다 — 잃은 시간이 곧 페널티다.
    const { state, quest } = boardQuest();
    let after = spendFullTime(state, quest);
    after = spendFullTime(after, quest);

    expect(find(after, quest.id).status).toBe("active");

    after = step(after, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "C",
    }).state;
    expect(find(after, quest.id).status).toBe("done");
  });

  it("판이 없는 일과는 예전처럼 시간으로 완료된다", () => {
    // 회복 행동 · 합동 · 훈련 체크포인트가 여기 해당하고, 아직 안 만든 원형도
    // 이쪽으로 떨어진다 — 미구현이 진행을 막지 않게 하는 안전장치다.
    let state = beginDay(fullSquad());
    const care = state.quests.find((q) => q.kind === "care" && q.phase === "reveille");
    if (!care) throw new Error("회복 행동 없음");
    expect(care.minigame).toBeNull();

    state = atQuest(state, care);
    state = spendFullTime(state, care);

    expect(find(state, care.id).status).toBe("done");
    expect(find(state, care.id).grade).toBeNull();
  });
});

describe("통과 신고를 서버가 거절하는 자리", () => {
  it("소요의 절반도 안 지났으면 무시한다", () => {
    // 서버는 커버리지도 오답 횟수도 볼 수 없다. 최소 진척 하나가 조작된 클라가
    // 하루를 즉시 끝내는 것을 막는 유일한 관문이다.
    const { state, quest } = boardQuest();
    const cleared = step(state, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "A",
    }).state;

    expect(find(cleared, quest.id).status).toBe("pending");
  });

  it("절반을 넘기면 받아들인다 — 일찍 끝낸 것은 실력이다", () => {
    const { state, quest } = boardQuest();
    let after = step(state, {
      type: "work",
      memberId: quest.ownerId!,
      questId: quest.id,
      deltaMs: quest.workMs * 0.6,
    }).state;
    after = step(after, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "B",
    }).state;

    expect(find(after, quest.id).status).toBe("done");
    // 남은 시간은 돌려주지 않는다 — 진척은 채워진 것으로 친다
    expect(find(after, quest.id).workedMs).toBe(quest.workMs);
  });

  it("다른 구역에 서 있으면 통과할 수 없다", () => {
    const { state, quest } = boardQuest();
    let after = spendFullTime(state, quest);
    const elsewhere = after.members.find((m) => m.id === quest.ownerId)!;
    elsewhere.zone = elsewhere.zone === "Z01" ? "Z11" : "Z01";

    after = step(after, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "A",
    }).state;
    expect(find(after, quest.id).status).not.toBe("done");
  });

  it("남의 일과는 통과할 수 없다", () => {
    const { state, quest } = boardQuest();
    const other = state.members.find((m) => m.id !== quest.ownerId)!;
    let after = spendFullTime(state, quest);
    after = step(after, { type: "move", memberId: other.id, to: quest.zone }).state;
    after = step(after, { type: "tick", elapsedMs: 60 * SECOND }).state;

    after = step(after, {
      type: "questCleared",
      memberId: other.id,
      questId: quest.id,
      grade: "A",
    }).state;
    expect(find(after, quest.id).status).not.toBe("done");
  });

  it("잠긴 일과는 되살아나지 않는다", () => {
    const { state, quest } = boardQuest();
    let after = spendFullTime(state, quest);
    after = step(after, { type: "skipPhase" }).state;
    expect(find(after, quest.id).status).toBe("locked");

    after = step(after, {
      type: "questCleared",
      memberId: quest.ownerId!,
      questId: quest.id,
      grade: "A",
    }).state;
    expect(find(after, quest.id).status).toBe("locked");
  });
});
