import zoneTable from "../data/zones.json";
import type { Zone } from "./types.js";

/**
 * 4.3 구역.
 *
 * **방 하나가 곧 구역이다.** 예전에는 서버가 8개(barracks · drillGround …)만 알고
 * 아트가 방 25개를 따로 들고 있었다. 그러면 같은 사실이 두 곳에 살면서 반드시
 * 어긋난다 — 실제로 퀘스트 17건에서 서버 구역과 방이 서로 다른 건물을 가리켰다.
 *
 * 합치고 나면 "이 방 하나가 곧 구역"이라는 PLAN 02의 설계가 규칙에서도 성립하고,
 * 세면장 일과를 생활관에 선 채로 끝내는 일이 원천적으로 막힌다.
 *
 * 좌표는 여전히 **논리 격자**이며 렌더링과 무관하다(맵은 `basemap.py`가 그린다).
 * 다만 맵 쪽이 이 파일을 읽어 배치가 인접 관계를 지키는지 검증한다 — 규칙이
 * 먼저고 그림이 따른다(ARCH-02).
 */

export interface ZoneInfo {
  readonly id: Zone;
  readonly name: string;
  /**
   * 지도에 넣을 짧은 이름.
   *
   * 좁은 방에 "사이버지식정보방"을 넣으면 잘리고, **잘린 이름은 없는 이름과
   * 같다.** 부대에서 실제로 부르는 말(사지방 · 체단장)이 짧기도 하고 더 맞다.
   */
  readonly short: string;
  /** 속한 동. 야외는 null */
  readonly building: string | null;
  readonly buildingName: string | null;
  /** 5.0 지형보정 — 실내는 체감온도 +8 */
  readonly indoor: boolean;
  /** 5.0 열원 — 보온 게이지가 리셋되는 곳 */
  readonly heat: boolean;
  readonly x: number;
  readonly y: number;
  /** 걸어서 한 번에 넘어갈 수 있는 곳 */
  readonly adjacent: readonly Zone[];
}

const TABLE = zoneTable.zones as readonly ZoneInfo[];
const MS_PER_STEP = zoneTable.msPerStep;

const BY_ID = new Map<Zone, ZoneInfo>(TABLE.map((z) => [z.id, z]));

export const ZONES = TABLE.map((z) => z.id);

export function zoneInfo(zone: Zone): ZoneInfo {
  const found = BY_ID.get(zone);
  if (!found) throw new Error(`정의되지 않은 구역: ${zone}`);
  return found;
}

/** 표시용 이름. 규칙과 무관하다 */
export function zoneName(zone: Zone): string {
  return BY_ID.get(zone)?.name ?? zone;
}

/** 지도용 짧은 이름 */
export function zoneShort(zone: Zone): string {
  return BY_ID.get(zone)?.short ?? zoneName(zone);
}

/**
 * 걸어서 한 번에 넘어갈 수 있는가.
 *
 * 클라이언트가 "걸어왔다"고만 하면 믿을 수 없다 — 순간이동으로 동선 비용을
 * 통째로 건너뛸 수 있기 때문이다. 인접한 구역으로만 허용하면, 먼 곳에 가려면
 * 그 사이를 실제로 지나야 하고 그때마다 이 표를 한 칸씩 통과한다.
 */
export function isAdjacent(from: Zone, to: Zone): boolean {
  if (from === to) return true;
  return BY_ID.get(from)?.adjacent.includes(to) ?? false;
}

/** 두 구역 사이 이동 소요(ms). 같은 구역이면 0. */
export function travelMs(from: Zone, to: Zone): number {
  const a = zoneInfo(from);
  const b = zoneInfo(to);
  return (Math.abs(a.x - b.x) + Math.abs(a.y - b.y)) * MS_PER_STEP;
}

/**
 * 5.0 열원 — 보온 게이지가 리셋되는 곳.
 *
 * §5.0이 열원으로 든 것은 "난로 · 차량 히터 · 실내"다. 실내이거나 난로가 있는
 * 구역이 곧 열원이고, 연병장 · 초소 · 훈련장이 빠진 것이 이 표의 전부다.
 * 극혹한에 야외 일과를 받으면 90초마다 실내로 들어와야 하고, 그 왕복이
 * §6.1이 말한 시간 비용으로 돌아온다.
 */
export function isHeatSource(zone: Zone): boolean {
  return BY_ID.get(zone)?.heat ?? false;
}

/** 5.0 지형보정 — 실내는 체감온도가 오른다 */
export function isIndoor(zone: Zone): boolean {
  return BY_ID.get(zone)?.indoor ?? false;
}
