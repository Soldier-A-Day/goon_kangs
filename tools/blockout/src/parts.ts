import { Mesh, box, cylinder, v, type Axis, type Vec3 } from "./mesh.js";

/**
 * 조립식 형상 정의.
 *
 * 소품 45종을 저마다 함수로 쓰면 코드가 길어지기만 하고 읽히지 않는다.
 * 무엇으로 이루어졌는지를 **데이터로** 적고 조립은 한 곳에서 한다 —
 * 그러면 "빗자루는 자루 + 솔"이라는 사실이 코드에 그대로 보인다.
 */
export type Piece =
  | { readonly kind: "box"; readonly size: Vec3; readonly at?: Vec3; readonly yaw?: number; readonly weight?: number }
  | { readonly kind: "cyl"; readonly r: number; readonly h: number; readonly at?: Vec3; readonly axis?: Axis; readonly weight?: number };

export const bx = (size: Vec3, at?: Vec3, yaw = 0, weight = 1): Piece =>
  ({ kind: "box", size, at, yaw, weight });

/** 원기둥. 축을 지정한다 — 바퀴는 x, 가로 배관·들것 손잡이는 z */
export const cy = (r: number, h: number, at?: Vec3, axis: Axis = "y", weight = 1): Piece =>
  ({ kind: "cyl", r, h, at, axis, weight });

/**
 * 조각 목록을 하나의 메시로 조립한다.
 *
 * `weight`는 그 조각에 폴리를 얼마나 더 줄지다. 눈에 크게 읽히는 부분
 * (트럭의 적재함, 텐트의 지붕)에 더 주고 잔가지에는 덜 준다 — 예산이
 * 300~6,000으로 좁아서 균등 분배하면 실루엣이 뭉개진다.
 */
export function assemblePieces(pieces: readonly Piece[], detail: number): Mesh {
  const mesh = new Mesh();

  for (const piece of pieces) {
    const scaled = Math.max(1, Math.round(detail * (piece.weight ?? 1)));
    const at = piece.at ?? v(0, 0, 0);

    if (piece.kind === "box") {
      mesh.merge(box(piece.size, scaled), at, piece.yaw ?? 0);
    } else {
      mesh.merge(cylinder(piece.r, piece.h, Math.max(4, scaled * 2), true, piece.axis ?? "y"), at);
    }
  }

  return mesh;
}

/** 소품 하나의 정의 */
export interface Prop {
  readonly id: string;
  readonly label: string;
  readonly pieces: readonly Piece[];
}

export const prop = (id: string, label: string, pieces: readonly Piece[]): Prop =>
  ({ id, label, pieces });
