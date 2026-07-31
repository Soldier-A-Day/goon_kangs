import { describe, expect, it } from "vitest";
import { ASSETS } from "@sad/assets";
import { assembleParts, totalTriangles } from "../src/kit.js";
import {
  BARRACKS, BOILER_ROOM, DRILL_GROUND, GUARD_POST, INFIRMARY,
  MESS_HALL, PERIMETER, STORAGE, TRAINING_FIELD,
} from "../src/maps.js";
import { EQUIPMENT } from "../src/equipment.js";
import { LARGE, MEDIUM, SMALL } from "../src/props.js";
import { fitToBudget, toObj } from "../src/mesh.js";
import { buildRifle } from "../src/rifle.js";

const budgetOf = (id: string) => {
  const entry = ASSETS.find((a) => a.id === id)!;
  return entry.lod0 * entry.count;
};

describe("블록아웃 생성기", () => {
  it("부대 맵 9종의 모듈 수가 카탈로그와 같다", () => {
    // §2가 정한 모듈 수는 장식이 아니라 드로우콜·배칭의 전제다.
    // 어긋나면 폴리가 맞아도 배칭 특성이 실제와 달라진다.
    const kits = {
      "base.drillGround": DRILL_GROUND,
      "base.barracks": BARRACKS,
      "base.storage": STORAGE,
      "base.messHall": MESS_HALL,
      "base.guardPost": GUARD_POST,
      "base.perimeter": PERIMETER,
      "base.boilerRoom": BOILER_ROOM,
      "base.infirmary": INFIRMARY,
      "base.trainingField": TRAINING_FIELD,
    };

    for (const [id, kit] of Object.entries(kits)) {
      expect(kit.length, id).toBe(ASSETS.find((a) => a.id === id)!.modules);
    }

    // 카탈로그의 부대 맵을 하나도 빠뜨리지 않았는가.
    // 의무실이 빠진 채로 M0까지 온 적이 있다.
    const cataloged = ASSETS.filter((a) => a.category === "baseMap").map((a) => a.id);
    expect(Object.keys(kits).sort()).toEqual(cataloged.sort());
  });

  it("소품·장비 종수가 카탈로그와 같다", () => {
    expect(SMALL.length).toBe(ASSETS.find((a) => a.id === "prop.small")!.count);
    expect(MEDIUM.length).toBe(ASSETS.find((a) => a.id === "prop.medium")!.count);
    expect(LARGE.length).toBe(ASSETS.find((a) => a.id === "prop.large")!.count);

    // 소총은 별도 생성기라 여기 없다. 나머지 5종이 카탈로그와 맞아야 한다
    const cataloged = ASSETS
      .filter((a) => a.category === "equipment" && a.id !== "equip.rifle")
      .map((a) => a.id);
    expect(EQUIPMENT.map((e) => e.assetId).sort()).toEqual(cataloged.sort());
  });

  it("id가 중복되지 않는다", () => {
    // 파일명이 곧 id다. 겹치면 하나가 조용히 덮어써지고 예산 검사에서
    // 종수만 모자라게 나온다 — 어느 것이 사라졌는지는 안 나온다.
    const ids = [...SMALL, ...MEDIUM, ...LARGE, ...EQUIPMENT].map((p) => p.id);
    expect(new Set(ids).size).toBe(ids.length);
  });

  it("예산을 넘지 않는다", () => {
    for (const [id, modules] of [
      ["base.drillGround", DRILL_GROUND],
      ["base.barracks", BARRACKS],
    ] as const) {
      const budget = budgetOf(id);
      const fitted = fitToBudget(budget, (d) => assembleParts(modules, d), totalTriangles);
      expect(fitted.triangles, id).toBeLessThanOrEqual(budget);
      // 너무 낮으면 부하를 과소평가한다. 이등분 탐색이 계단에 걸려
      // 생활관이 53%에서 멈춘 적이 있다 — 그 회귀를 막는다.
      expect(fitted.triangles / budget, id).toBeGreaterThan(0.8);
    }

    const rifle = fitToBudget(budgetOf("equip.rifle"), buildRifle, (m) => m.triangleCount);
    expect(rifle.triangles).toBeLessThanOrEqual(budgetOf("equip.rifle"));
  });

  it("배치마다 조각이 하나씩 나온다", () => {
    // 전부 하나의 메시로 합쳐 뽑았더니 씬에 렌더러가 맵당 하나만 생겨,
    // 배칭을 재려고 만든 씬이 배칭을 재지 못했다.
    const parts = assembleParts(DRILL_GROUND, 2);
    const placements = DRILL_GROUND.reduce((sum, m) => sum + m.placements.length, 0);
    expect(parts.length).toBe(placements);
    expect(parts.length).toBeGreaterThan(200);
  });

  it("OBJ가 조각마다 g 태그를 찍는다", () => {
    // `o` 로는 Unity가 오브젝트를 나누지 않는다. `g` 여야 한다.
    const parts = assembleParts(DRILL_GROUND, 1);
    const obj = toObj(parts, "테스트");
    const groups = obj.split("\n").filter((line) => line.startsWith("g "));
    expect(groups.length).toBe(parts.length);
    expect(obj.split("\n").some((line) => line.startsWith("o "))).toBe(false);
  });

  it("OBJ 정점 인덱스가 파일 전체에 걸쳐 누적된다", () => {
    // 조각을 나누면서 인덱스를 조각마다 1부터 다시 매기면 지오메트리가
    // 통째로 뒤엉킨다. OBJ 인덱스는 파일 전역이다.
    const parts = assembleParts(DRILL_GROUND, 1);
    const obj = toObj(parts, "테스트");
    const vertices = obj.split("\n").filter((line) => line.startsWith("v ")).length;

    let max = 0;
    for (const line of obj.split("\n")) {
      if (!line.startsWith("f ")) continue;
      for (const token of line.slice(2).split(" ")) max = Math.max(max, Number(token));
    }
    expect(max).toBe(vertices);
  });

  it("둘레 배치가 좌우 변을 90도 돌린다", () => {
    // 회전이 없으면 담장이 벽이 아니라 갈빗대처럼 바깥으로 튀어나온다.
    const fence = DRILL_GROUND.find((m) => m.name === "담장 패널")!;
    const yaws = new Set(fence.placements.map((p) => p.yaw));
    expect(yaws).toEqual(new Set([0, 90]));
  });
});
