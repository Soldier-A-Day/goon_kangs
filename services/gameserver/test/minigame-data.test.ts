import { describe, expect, it } from "vitest";
import questTable from "@sad/sim/data/quests.json" with { type: "json" };
import { minigameSchema } from "@sad/protocol";

/**
 * 배정표가 와이어 스키마를 만족하는가.
 *
 * **`snapshot.ts`가 캐스트로 넘기는 근거가 여기다.** sim의 `Minigame`은
 * 파라미터를 색인 서명으로 열어 둔 느슨한 타입이라(판을 도는 것은 클라이고
 * sim은 값을 읽지 않는다) 프로토콜의 원형별 union으로 타입만으로는 좁혀지지
 * 않는다. 좁혀 주는 것은 타입이 아니라 이 테스트다.
 *
 * 여기가 깨지면 Unity에서는 조용히 빈 필드로 나타난다 — 오염 개수가 0인
 * `SCRUB`은 시작하자마자 통과하고, 아무도 원인을 못 찾는다.
 */
type Template = { readonly id: string; readonly minigame?: unknown };

const ALL: readonly Template[] = [
  ...Object.values(questTable.role as Record<string, readonly Template[]>).flat(),
  ...(questTable.chores as readonly Template[]),
  ...(questTable.surprise as readonly Template[]),
];

describe("배정표 ↔ 와이어 스키마", () => {
  it("69건의 판이 전부 `minigameSchema`를 통과한다", () => {
    const failed: string[] = [];
    for (const quest of ALL) {
      const result = minigameSchema.safeParse(quest.minigame);
      if (!result.success) {
        failed.push(`${quest.id}: ${result.error.issues.map((i) => `${i.path.join(".")} ${i.message}`).join(" · ")}`);
      }
    }
    expect(failed).toEqual([]);
    expect(ALL).toHaveLength(69);
  });

  it("원형이 요구하는 파라미터가 하나도 빠지지 않는다", () => {
    // union은 판별자(`type`)로 갈리므로, 파라미터가 빠지면 그 variant를
    // 통째로 못 맞춘다 — 위 테스트가 이미 잡지만 원인이 읽히게 따로 센다.
    for (const quest of ALL) {
      const board = quest.minigame as { type: string };
      const variant = minigameSchema.options.find(
        (option) => option.shape.type.value === board.type,
      );
      expect(variant, `알 수 없는 원형: ${quest.id} → ${board.type}`).toBeDefined();

      for (const [key, field] of Object.entries(variant!.shape)) {
        if (field.safeParse(undefined).success) continue; // 선택적 파라미터
        expect(Object.keys(board), `${quest.id}.${key}`).toContain(key);
      }
    }
  });

  it("회복 행동에는 판이 없다 — 밥 먹는 데 판을 통과하라고 할 이유가 없다", () => {
    for (const care of questTable.care as readonly Record<string, unknown>[]) {
      expect(care.minigame, String(care.id)).toBeUndefined();
    }
  });
});
