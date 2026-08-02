import { describe, expect, it } from "vitest";
import table from "../data/zones.json";
import { ZONES, isAdjacent, isHeatSource, travelMs, zoneInfo, type Zone } from "../src/index.js";

/**
 * 구역 정의는 **sim이 소유한다**(`data/zones.json`). 예전에는 서버가 8개만 알고
 * 아트가 방 25개를 따로 들고 있어서 둘이 어긋났다. 하나로 합친 뒤에는 그 정의가
 * 스스로 성립하는지를 여기서 지킨다.
 */
describe("4.3 구역", () => {
  it("id가 중복되지 않는다", () => {
    expect(new Set(ZONES).size).toBe(ZONES.length);
  });

  it("인접은 서로를 가리킨다 — 한쪽만 이어져 있으면 들어갔다 못 나온다", () => {
    const bad: string[] = [];
    for (const zone of ZONES) {
      for (const other of zoneInfo(zone).adjacent) {
        if (!isAdjacent(other, zone)) bad.push(`${zone} → ${other} (역방향 없음)`);
      }
    }
    expect(bad).toEqual([]);
  });

  it("모든 구역이 이어져 있다 — 못 가는 방이 있으면 그 일과는 영영 못 끝낸다", () => {
    const seen = new Set<Zone>(["Z01"]);
    const queue: Zone[] = ["Z01"];
    while (queue.length) {
      const at = queue.shift();
      if (!at) break;
      for (const next of zoneInfo(at).adjacent) {
        if (seen.has(next)) continue;
        seen.add(next);
        queue.push(next);
      }
    }
    expect([...ZONES].filter((z) => !seen.has(z))).toEqual([]);
  });

  it("실내는 열원이다 — 5.0 보온 게이지가 실내 접촉으로 리셋된다", () => {
    const bad = ZONES.filter((z) => zoneInfo(z).indoor && !isHeatSource(z));
    expect(bad).toEqual([]);
  });

  it("야외에는 열원이 없다 — 있으면 극혹한에 밖에 서 있어도 안 얼어붙는다", () => {
    const outdoor = ZONES.filter((z) => !zoneInfo(z).indoor);
    expect(outdoor.filter(isHeatSource)).toEqual([]);
  });

  it("이동 소요는 대칭이고 자기 자신은 0이다", () => {
    expect(travelMs("Z01", "Z01")).toBe(0);
    expect(travelMs("Z01", "Z11")).toBe(travelMs("Z11", "Z01"));
  });

  it("타입 유니온과 데이터가 같다", () => {
    const ids = (table.zones as { id: string }[]).map((z) => z.id);
    expect(ids).toEqual([...ZONES]);
  });
});
