import { describe, expect, it } from "vitest";
import {
  CURRICULUM,
  TOTAL_REQUIRED_PER_MEMBER,
  planFor,
  slackFor,
} from "../src/index.js";

describe("14.0 커리큘럼 불변식", () => {
  it("18일이다", () => {
    expect(CURRICULUM).toHaveLength(18);
    expect(CURRICULUM.map((d) => d.day)).toEqual(
      Array.from({ length: 18 }, (_, i) => i + 1),
    );
  });

  it("1인 필수 총량은 76건이다 — 이 값이 완주율 계산의 기준이다", () => {
    expect(TOTAL_REQUIRED_PER_MEMBER).toBe(76);
  });

  it("필수 = 훈련 체크포인트 + 보직 퀘스트 (TRN-02 카운트 규칙)", () => {
    for (const day of CURRICULUM) {
      expect(
        day.required.trainingCheckpoints + day.required.roleQuests,
        `D-${day.day}`,
      ).toBe(day.required.total);
    }
  });

  it("D-17은 7 = 체크포인트 5 + 보직 2로 정확히 채워진다", () => {
    expect(planFor(17).required).toEqual({
      total: 7,
      trainingCheckpoints: 5,
      roleQuests: 2,
    });
  });

  it("체크포인트는 훈련이 있는 날에만 존재한다", () => {
    for (const day of CURRICULUM) {
      if (day.required.trainingCheckpoints > 0) {
        expect(day.training, `D-${day.day}`).not.toBeNull();
      }
    }
  });

  it("합동 요구 인원은 4를 넘지 않고, 4인 전원은 훈련·심사일에만 쓴다", () => {
    for (const day of CURRICULUM) {
      expect(day.jointActors).toBeLessThanOrEqual(4);
      if (day.jointActors === 4) {
        expect(
          day.training !== null || day.day === 18,
          `D-${day.day}는 4인 전원인데 훈련일이 아니다`,
        ).toBe(true);
      }
    }
  });

  it("여유 인원 분포가 표 14-1과 일치한다 (4인 7일 / 3인 5일 / 2인 5일 / 정비일 1일)", () => {
    const bucket = (actors: number) =>
      CURRICULUM.filter((d) => d.jointActors === actors).length;
    expect(bucket(4)).toBe(7);
    expect(bucket(3)).toBe(5);
    expect(bucket(2)).toBe(5);
    expect(bucket(0)).toBe(1);
  });

  it("정비일 3회가 난이도 리듬을 만든다 (D-06 / D-11 / D-14)", () => {
    const maintenance = CURRICULUM.filter((d) => d.maintenanceDay).map((d) => d.day);
    expect(maintenance).toEqual([6, 11, 14]);
    for (const day of maintenance) {
      expect(planFor(day).required.total).toBeLessThanOrEqual(3);
    }
  });

  it("여유 인원은 4 − 합동 인원이다", () => {
    expect(slackFor(6)).toBe(4);
    expect(slackFor(1)).toBe(2);
    expect(slackFor(4)).toBe(1);
    expect(slackFor(17)).toBe(0);
  });

  it("튜토리얼 구간과 심사일의 기후는 고정이다", () => {
    expect(planFor(1).climate).toBe("fixedNormal");
    expect(planFor(2).climate).toBe("fixedNormal");
    expect(planFor(18).climate).toBe("fixedNormal");
    expect(planFor(10).climate).toBe("climateEvent");
    expect(planFor(15).climate).toBe("bandBranch");
  });

  it("범위를 벗어난 일차는 던진다", () => {
    expect(() => planFor(0)).toThrow();
    expect(() => planFor(19)).toThrow();
  });
});
