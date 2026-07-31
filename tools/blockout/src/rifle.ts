import { Mesh, box, cylinder, v } from "./mesh.js";

/**
 * 소총 (ASSETS.md §4.1 · 3,000 tris).
 *
 * 전원 지급이므로(3.0 소총수 외에도 표 11-1 지급 목록에 있다) **4정이 항상
 * 화면에 있다.** 개당 3,000이면 12,000이고, §9의 동시 표시 장비 8,000 중
 * 가장 큰 몫이다. 손·등 두 곳에 부착되므로(§4.1) 실루엣이 양쪽에서 읽혀야 한다.
 *
 * 블록아웃이지만 총열·개머리판·탄창·조준선의 비례는 맞춘다 — 실루엣이 틀리면
 * 부착 위치와 애니메이션을 잡을 때 다시 만들어야 한다.
 */
export function buildRifle(detail: number): Mesh {
  const mesh = new Mesh();
  const seg = Math.max(1, Math.round(detail));
  const sides = Math.max(6, Math.round(detail * 2));

  // 기관부 — 총의 기준점. 부착 슬롯이 여기에 붙는다
  mesh.merge(box(v(0.36, 0.09, 0.05), seg), v(0, 0, 0));

  // 총열 + 소염기
  const barrel = cylinder(0.011, 0.34, sides);
  mesh.merge(barrel, v(0.34, 0.02, 0), v(1, 1, 1));
  mesh.merge(cylinder(0.018, 0.06, sides), v(0.5, 0.02, 0));

  // 총열덮개
  mesh.merge(box(v(0.24, 0.06, 0.05), seg), v(0.29, 0.015, 0));

  // 개머리판 — 어깨에 닿는 면이라 실루엣에서 가장 크게 읽힌다
  mesh.merge(box(v(0.22, 0.08, 0.045), seg), v(-0.28, -0.005, 0));
  mesh.merge(box(v(0.05, 0.11, 0.045), seg), v(-0.4, -0.01, 0));

  // 권총손잡이
  mesh.merge(box(v(0.045, 0.13, 0.04), seg), v(-0.06, -0.1, 0));

  // 탄창 — 아래로 튀어나와 실루엣을 정한다
  mesh.merge(box(v(0.055, 0.17, 0.035), seg), v(0.04, -0.13, 0));

  // 방아쇠울
  mesh.merge(box(v(0.07, 0.012, 0.03), seg), v(0.02, -0.075, 0));

  // 가늠자 · 가늠쇠 — 조준선 정렬이 보여야 한다
  mesh.merge(box(v(0.03, 0.035, 0.03), seg), v(-0.12, 0.06, 0));
  mesh.merge(box(v(0.02, 0.04, 0.025), seg), v(0.38, 0.06, 0));

  // 멜빵 고리 2점 (손·등 부착의 회전축 참조)
  mesh.merge(cylinder(0.008, 0.02, Math.max(6, sides / 2)), v(-0.3, 0.05, 0));
  mesh.merge(cylinder(0.008, 0.02, Math.max(6, sides / 2)), v(0.3, 0.05, 0));

  return mesh;
}
