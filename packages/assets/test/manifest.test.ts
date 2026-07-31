import { describe, expect, it } from "vitest";
import {
  ASSETS,
  byCategory,
  categoryTris,
  checkManifest,
  concurrentTris,
  initialDownloadMb,
  manifest,
  requiredBy,
} from "../src/index.js";

describe("에셋 카탈로그", () => {
  it("스스로 모순되지 않는다", () => {
    const findings = checkManifest();
    const errors = findings.filter((f) => f.severity === "error");
    expect(errors, errors.map((e) => e.message).join("\n")).toEqual([]);
  });

  it("문서가 적어둔 총계와 항목 유도값이 같다", () => {
    // ASSETS.md가 산문으로 적은 값들. 하나라도 어긋나면 문서와 데이터가
    // 갈라진 것이고, 그 시점부터 어느 쪽도 믿을 수 없다.
    expect(categoryTris("clothing")).toBe(27_100);
    expect(categoryTris("baseMap")).toBe(243_000);
    expect(categoryTris("trainingMap")).toBe(920_000);
    expect(categoryTris("prop")).toBe(60_000);
    expect(categoryTris("equipment")).toBe(7_750);
  });

  it("동시 표시 최대 부하가 화면 예산 안에 있다", () => {
    expect(concurrentTris()).toBe(436_000);
    expect(concurrentTris()).toBeLessThan(manifest.budgets.screenTris.value);
  });

  it("초기 다운로드가 예산 안에 있다", () => {
    expect(initialDownloadMb()).toBe(81);
    expect(initialDownloadMb()).toBeLessThanOrEqual(
      manifest.budgets.initialDownloadMb.value,
    );
  });

  it("맵 번들이 개당 상한을 넘지 않는다", () => {
    const cap = manifest.budgets.mapBundleMb.value;
    for (const map of byCategory("trainingMap")) {
      expect(map.bundleMb, `${map.id}`).toBeLessThanOrEqual(cap);
    }
  });

  it("행군 코스가 훈련 맵 폴리의 3분의 1을 넘는다 — 축소 순위 2번의 근거다", () => {
    const march = ASSETS.find((a) => a.id === "train.march")!;
    expect(march.lod0 / categoryTris("trainingMap")).toBeGreaterThan(0.33);
    // 스트리밍 없이는 이 하나가 화면 예산의 절반을 먹는다
    expect(march.streamingSegments).toBe(4);
  });

  it("M0 범위가 성능 게이트에 필요한 것만 담는다", () => {
    const m0 = requiredBy("M0");
    // 19.0 M0: 캐릭터 베이스 1 + 피복 1세트, 야외 맵 1, 온도 파티클 1밴드
    expect(m0.map((a) => a.id)).toContain("char.base.player");
    expect(m0.map((a) => a.id)).toContain("base.drillGround");
    // 훈련 맵 9종은 M3다. M0에서 만들면 게이트 통과 전에 물량이 쌓인다
    expect(m0.filter((a) => a.category === "trainingMap")).toEqual([]);
    // 간부 NPC도 M4다
    expect(m0.map((a) => a.id)).not.toContain("char.base.cadre");
  });

  it("보직 4종에 각각 지급 장비가 있다", () => {
    // 3.0의 보직 1:1 대응. 장비가 빠진 보직이 있으면 그 보직만 비어 보인다
    const roles = new Set(
      byCategory("equipment").map((e) => e.role).filter(Boolean),
    );
    expect(roles).toEqual(new Set(["rifle", "comms", "medic", "admin"]));
  });

  it("피복이 6슬롯을 모두 채운다", () => {
    // 11.0의 6슬롯 구조. 슬롯이 비면 그 자리 피복을 스왑할 수 없다
    const slots = byCategory("clothing").map((c) => c.slot);
    expect(new Set(slots)).toEqual(
      new Set(["상의", "하의", "외피", "두부", "수족", "군장"]),
    );
  });
});
