import { bx, cy, prop, type Prop } from "./parts.js";
import { v } from "./mesh.js";

/**
 * 지급 장비 (ASSETS.md §4.1).
 *
 * 보직마다 하나씩 대응한다(3.0의 1:1 구조). 장비가 빠진 보직이 있으면
 * 그 보직만 손이 비어 보이고, 그건 "이 보직은 할 일이 없다"로 읽힌다.
 *
 * 전부 캐릭터에 부착되므로 **부착점 기준 원점**을 맞춘다 — 손에 드는 것은
 * 손잡이가 원점 근처, 등에 메는 것은 등판이 원점 근처다.
 */
export const EQUIPMENT: readonly (Prop & { readonly assetId: string })[] = [
  {
    ...prop("radio", "무전기", [
      bx(v(0.2, 0.3, 0.12), undefined, 0, 4),
      cy(0.006, 0.9, v(0.07, 0.6, 0), "y", 2),
      bx(v(0.06, 0.13, 0.05), v(-0.12, 0.05, 0.08), 0, 3),
      bx(v(0.16, 0.04, 0.02), v(0, -0.17, 0), 0, 2),
    ]),
    assetId: "equip.radio",
  },
  {
    ...prop("medicBag", "의무낭", [
      bx(v(0.3, 0.22, 0.16), undefined, 0, 4),
      bx(v(0.28, 0.05, 0.14), v(0, 0.12, 0), 0, 3),
      bx(v(0.08, 0.02, 0.01), v(0, 0, 0.085), 0, 2),
      bx(v(0.02, 0.08, 0.01), v(0, 0, 0.085), 0, 2),
    ]),
    assetId: "equip.medicBag",
  },
  {
    ...prop("toolbox", "공구함", [
      bx(v(0.4, 0.22, 0.2), undefined, 0, 4),
      bx(v(0.38, 0.04, 0.18), v(0, 0.12, 0), 0, 3),
      cy(0.012, 0.18, v(0, 0.2, 0), "x", 2),
      bx(v(0.06, 0.05, 0.02), v(0, 0.11, 0.1), 0, 2),
    ]),
    assetId: "equip.toolbox",
  },
  {
    ...prop("nightVision", "야시장비", [
      bx(v(0.12, 0.07, 0.05), undefined, 0, 3),
      cy(0.026, 0.09, v(-0.032, 0, 0.06), "z", 3),
      cy(0.026, 0.09, v(0.032, 0, 0.06), "z", 3),
      bx(v(0.05, 0.03, 0.04), v(0, 0.05, -0.02), 0, 2),
    ]),
    assetId: "equip.nightVision",
  },
  {
    ...prop("clipboard", "클립보드", [
      bx(v(0.23, 0.012, 0.32), undefined, 0, 3),
      bx(v(0.09, 0.02, 0.04), v(0, 0.014, 0.14), 0, 2),
    ]),
    assetId: "equip.clipboard",
  },
];
