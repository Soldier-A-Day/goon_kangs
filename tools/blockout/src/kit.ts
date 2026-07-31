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

/**
 * 회전이 필요 없는 자리는 좌표만 적을 수 있게 한다.
 *
 * 맵 정의는 대부분 "여기에 하나" 뿐이라, 전부 `{ at, yaw: 0 }`으로 쓰면
 * 정작 중요한 좌표가 껍데기에 묻힌다.
 */
export type Spot = Placement | Vec3;

const normalize = (spots: readonly Spot[]): Placement[] =>
  spots.map((spot) => ("at" in spot ? spot : place(spot)));

export interface Module {
  readonly name: string;
  /** 이 모듈 하나의 형상 */
  readonly build: (detail: number) => Mesh;
  readonly placements: readonly Placement[];
}

/** 배치된 모듈 하나. 씬에서 GameObject 하나가 된다 */
export interface Part {
  readonly name: string;
  readonly mesh: Mesh;
}

/**
 * 모듈을 배치해 **조각 목록**으로 돌려준다.
 *
 * 처음에는 전부 하나의 메시로 합쳐 뽑았다. 폴리 수는 맞지만 씬에 렌더러가
 * 맵당 하나만 생겨서, §2가 모델링한 250~330 드로우콜이 6개로 나왔다 —
 * **배칭을 재려고 만든 씬이 배칭을 재지 못했다.**
 *
 * 실제 모듈러 키트는 배치된 인스턴스마다 GameObject가 하나씩이고, 정적 배칭이
 * 그것들을 빌드 시점에 묶는다. 묶이는지 아닌지가 §4가 보려는 것이므로
 * 묶이기 전 상태로 내보내야 한다.
 */
export function assembleParts(modules: readonly Module[], detail: number): Part[] {
  const parts: Part[] = [];
  for (const module of modules) {
    const piece = module.build(detail);
    module.placements.forEach((spot, index) => {
      const mesh = new Mesh();
      mesh.merge(piece, spot.at, spot.yaw);
      parts.push({ name: `${module.name}_${index}`, mesh });
    });
  }
  return parts;
}

/** 조각 전체의 삼각형 합. 예산 맞추기에 쓴다 */
export function totalTriangles(parts: readonly Part[]): number {
  return parts.reduce((sum, part) => sum + part.mesh.triangleCount, 0);
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

/**
 * Z축으로 늘어놓는다.
 *
 * `line()`이 X축인 것을 잊고 도로·행군 코스에 썼더니 **길이 끊겼다.**
 * 타일은 Z로 20m 길고 X로 8m 넓은데 X 방향으로 20m 간격을 뒀으니
 * 12m씩 벌어진 것이다. 폴리 수는 맞고 예산 검사도 통과했다 —
 * 그림을 찍기 전까지 아무도 몰랐다.
 */
export function lineZ(count: number, step: number, y = 0, x = 0, originZ = 0): Placement[] {
  const out: Placement[] = [];
  for (let i = 0; i < count; i += 1) out.push(place(v(x, y, originZ + i * step)));
  return out;
}

/** 상자 하나짜리 모듈. 블록아웃 대부분이 이 형태다 */
export function boxModule(
  name: string,
  size: Vec3,
  placements: readonly Spot[],
  detailScale = 1,
): Module {
  return {
    name,
    placements: normalize(placements),
    build: (detail) => box(size, Math.max(1, Math.round(detail * detailScale))),
  };
}

export function cylinderModule(
  name: string,
  radius: number,
  height: number,
  placements: readonly Spot[],
  detailScale = 1,
): Module {
  return {
    name,
    placements: normalize(placements),
    build: (detail) => cylinder(radius, height, Math.max(3, Math.round(detail * detailScale * 2))),
  };
}
