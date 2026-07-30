import { describe, expect, it } from "vitest";
import { runBatch, simulateRun } from "../src/simulate.js";

/**
 * 10.0 완주율 표를 회귀 테스트로 고정한다.
 *
 * 표가 예측한 값: 정확도 98.0% → 15% / 98.5% → 34% / 99.0% → 65% (구제 3회 기준).
 * 이 수치가 흔들리면 커리큘럼(76건)이나 구제 총량(3회)이 바뀐 것이다.
 */
describe("완주율 밸런스", () => {
  const RUNS = 2000;

  it("정확도 98%에서 완주율이 목표 구간(15~25%) 하단에 들어온다", () => {
    const report = runBatch(RUNS, { accuracy: 0.98 });
    expect(report.clearRate).toBeGreaterThan(0.11);
    expect(report.clearRate).toBeLessThan(0.2);
  });

  it("정확도가 오르면 완주율도 오른다", () => {
    const low = runBatch(RUNS, { accuracy: 0.98 }).clearRate;
    const mid = runBatch(RUNS, { accuracy: 0.985 }).clearRate;
    const high = runBatch(RUNS, { accuracy: 0.99 }).clearRate;
    expect(mid).toBeGreaterThan(low);
    expect(high).toBeGreaterThan(mid);
    expect(mid).toBeGreaterThan(0.27);
    expect(mid).toBeLessThan(0.42);
  });

  it("구제권을 늘리면 완주율이 급격히 오른다 — 3회는 되돌릴 수 없는 손잡이다", () => {
    const three = runBatch(RUNS, { accuracy: 0.98 }).clearRate;
    const relaxed = runBatch(RUNS, { accuracy: 0.98 }, { difficulty: "relaxed" }).clearRate;
    expect(relaxed).toBeGreaterThan(three);
  });

  it("실패는 필수 미달(조건 A)로만 발생한다 — 다른 시스템이 아직 없기 때문", () => {
    const report = runBatch(200, { accuracy: 0.98 });
    expect(report.failuresByCondition.B).toBe(0);
    expect(report.failuresByCondition.C).toBe(0);
    expect(report.failuresByCondition.D).toBe(0);
    expect(report.failuresByCondition.A).toBeGreaterThan(0);
  });

  it("같은 시드는 같은 결과를 낸다", () => {
    expect(simulateRun(77, { accuracy: 0.98 })).toEqual(
      simulateRun(77, { accuracy: 0.98 }),
    );
  });

  it("정확도 100%면 반드시 전역한다", () => {
    const report = runBatch(100, { accuracy: 1 });
    expect(report.clearRate).toBe(1);
  });
});
