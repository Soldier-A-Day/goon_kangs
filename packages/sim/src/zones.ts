import type { Zone } from "./types.js";

/**
 * 4.3 구역 그래프.
 *
 * 기획서 6.1은 공통 일과의 "시간 비용 대부분이 이동"이라고 못박는다. 3D가 없어도 이 비용은
 * 지금 모델링해야 하므로, 위치를 좌표가 아니라 구역 노드 + 이동 소요로 둔다.
 * 좌표는 격자 위 논리 좌표이며 렌더링과 무관하다 — Unity는 실제 좌표를 zone으로 매핑해 보고할 뿐이다.
 */
const GRID: Record<Zone, readonly [number, number]> = {
  barracks: [0, 0],
  boilerRoom: [3, 0],
  messHall: [2, 0],
  infirmary: [2, 1],
  drillGround: [1, 2],
  storage: [3, 2],
  guardPost: [0, 4],
  trainingField: [4, 4],
};

/** 격자 한 칸당 이동 소요 */
const MS_PER_STEP = 3000;

export const ZONES = Object.keys(GRID) as Zone[];

/** 두 구역 사이 이동 소요(ms). 같은 구역이면 0. */
export function travelMs(from: Zone, to: Zone): number {
  const a = GRID[from];
  const b = GRID[to];
  return (Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1])) * MS_PER_STEP;
}
