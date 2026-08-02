import { describe, expect, it } from "vitest";
import quests from "../data/quests.json";
import { ZONES, planFor, trainingPlace } from "../src/index.js";

interface Row { readonly id: string; readonly zone: string }

function every(): Row[] {
  const role = Object.values(quests.role as Record<string, Row[]>).flat();
  return [...role, ...(quests.chores as Row[]), ...(quests.surprise as Row[])];
}

describe("퀘스트 배치", () => {
  it("모든 일과가 정의된 구역에 있다", () => {
    const known = new Set<string>(ZONES);
    expect(every().filter((q) => !known.has(q.zone)).map((q) => `${q.id}:${q.zone}`)).toEqual([]);
  });

  it("부대 안 구역마다 최소 한 건은 있다 — 할 일이 없는 방은 배경이 된다", () => {
    const used = new Set(every().map((q) => q.zone));
    // 복도는 지나가는 곳이고, 잠긴 남의 생활관에는 우리 일과가 없다.
    // 훈련장(TR*)은 이 표에 없다 — 그날의 훈련이 만드는 것이라 아래 검사가 맡는다
    const skip = new Set(["Z02", "Z20", "Z21", "Z22", "Z01b", "Z01c"]);
    expect(
      [...ZONES].filter(
        (z) => !z.startsWith("TR") && !skip.has(z) && !used.has(z),
      ),
    ).toEqual([]);
  });

  it("훈련장 10곳이 18일 안에 전부 쓰인다 — 안 가는 맵은 만들 이유가 없다", () => {
    // §6.4가 훈련 맵 10종을 요구했고 §14.0이 훈련일 10일을 배치했다. 둘이
    // 맞물리는지는 커리큘럼과 장소 표를 같이 봐야만 알 수 있다.
    //
    // 갈래가 있는 날(D-15 계절 · D-12·13 유격)이 있으므로 밴드를 양쪽 다 넣는다
    const seen = new Set<string>();
    for (let day = 1; day <= 18; day += 1) {
      const training = planFor(day).training;
      for (const band of ["extremeCold", "extremeHot"] as const) {
        const place = trainingPlace(training, day, band);
        if (place) seen.add(place.zone);
      }
    }

    const maps = [...ZONES].filter((z) => z.startsWith("TR"));
    expect(maps.length, "훈련 맵은 10종이다").toBe(10);
    expect(maps.filter((z) => !seen.has(z)), "아무도 안 가는 훈련장").toEqual([]);
  });
});
