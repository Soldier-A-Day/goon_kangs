import { Mesh, box, cylinder, v, type Vec3 } from "./mesh.js";

/**
 * 모듈러 키트.
 *
 * ASSETS.md §2가 부대 맵을 "모듈러 키트로 조립한다"고 하고 구역마다 **모듈 종류 수**를
 * 못박았다(생활관 24, 연병장 12). 그 수는 장식이 아니라 **드로우콜과 배칭의 전제**다 —
 * 모듈 종류가 늘면 재질이 갈라지고, 재질이 갈라지면 정적 배칭이 깨진다(§2).
 *
 * 그래서 블록아웃도 같은 수의 모듈로 조립한다. 하나의 큰 메시로 뽑으면 폴리 수는
 * 맞출 수 있지만 **배칭 특성이 실제와 달라져** 게이트가 거짓 통과를 낸다.
 */

/**
 * 배치 한 자리. 위치와 Y축 회전을 함께 담는다.
 *
 * 처음에는 위치만 뒀는데 담장이 벽이 되지 않았다 — 좌우 변의 패널이 앞뒤 변과
 * 같은 방향으로 서서 갈빗대처럼 바깥으로 튀어나왔다. 둘레를 두르는 모듈은
 * **변마다 방향이 달라야** 벽이 된다.
 */
export interface Placement {
  readonly at: Vec3;
  readonly yaw: number;
}

const place = (at: Vec3, yaw = 0): Placement => ({ at, yaw });

export interface Module {
  readonly name: string;
  /** 이 모듈 하나의 형상 */
  readonly build: (detail: number) => Mesh;
  readonly placements: readonly Placement[];
}

export function assemble(modules: readonly Module[], detail: number): Mesh {
  const world = new Mesh();
  for (const module of modules) {
    const piece = module.build(detail);
    for (const spot of module.placements) world.merge(piece, spot.at, spot.yaw);
  }
  return world;
}

/** 격자 배치. 모듈러 키트가 실제로 놓이는 방식이다 */
export function grid(
  countX: number,
  countZ: number,
  spacing: number,
  y = 0,
  originX = 0,
  originZ = 0,
): Placement[] {
  const out: Placement[] = [];
  for (let i = 0; i < countX; i += 1) {
    for (let j = 0; j < countZ; j += 1) {
      out.push(place(v(originX + i * spacing, y, originZ + j * spacing)));
    }
  }
  return out;
}

/**
 * 사각 둘레 배치. 담장·연석처럼 경계를 두르는 모듈에 쓴다.
 *
 * **좌우 변은 90도 돌린다.** 모듈은 X축으로 긴 형태로 만들어지므로, 그대로
 * 두면 좌우 변에서 벽이 바깥을 향해 튀어나온다.
 */
export function perimeter(width: number, depth: number, step: number, y = 0): Placement[] {
  const out: Placement[] = [];
  const hx = width / 2;
  const hz = depth / 2;

  // 앞뒤 변 — 모듈의 긴 축이 그대로 변을 따라간다
  for (let x = -hx; x <= hx; x += step) {
    out.push(place(v(x, y, -hz)));
    out.push(place(v(x, y, hz)));
  }

  // 좌우 변 — 긴 축이 Z를 향해야 하므로 돌린다.
  // 모서리는 앞뒤 변이 이미 채웠으므로 한 칸씩 안쪽에서 시작한다.
  for (let z = -hz + step; z < hz; z += step) {
    out.push(place(v(-hx, y, z), 90));
    out.push(place(v(hx, y, z), 90));
  }

  return out;
}

export function line(count: number, step: number, y = 0, z = 0, originX = 0): Placement[] {
  const out: Placement[] = [];
  for (let i = 0; i < count; i += 1) out.push(place(v(originX + i * step, y, z)));
  return out;
}

/** 상자 하나짜리 모듈. 블록아웃 대부분이 이 형태다 */
export function boxModule(
  name: string,
  size: Vec3,
  placements: readonly Placement[],
  detailScale = 1,
): Module {
  return {
    name,
    placements,
    build: (detail) => box(size, Math.max(1, Math.round(detail * detailScale))),
  };
}

export function cylinderModule(
  name: string,
  radius: number,
  height: number,
  placements: readonly Placement[],
  detailScale = 1,
): Module {
  return {
    name,
    placements,
    build: (detail) => cylinder(radius, height, Math.max(3, Math.round(detail * detailScale * 2))),
  };
}
