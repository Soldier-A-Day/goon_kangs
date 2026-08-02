"""
부대 맵을 Unity 없이 한 장으로 본다.

    python3 tools/sprites/mappreview.py out.png [배율]

씬을 세우고 캡처하는 데 몇 분이 걸리므로, 배치가 맞는지 보는 것만이라면
여기서 확인하는 편이 훨씬 빠르다. 도면(`files-4/PLAN_01`)과 나란히 놓고
같은 그림인지 보라는 것이 목적이다.
"""

from __future__ import annotations

import json
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import palette as P

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART = os.path.join(ROOT, "unity", "Assets", "Art", "2d")


def main(dest: str, scale: int = 6) -> int:
    base = json.load(open(os.path.join(ART, "base_map.json"), encoding="utf-8"))
    art = json.load(open(os.path.join(ART, "art2d.json"), encoding="utf-8"))

    W, H = base["width"], base["height"]
    img = Image.new("RGB", (W * scale, H * scale), P.W["night1"])
    draw = ImageDraw.Draw(img)

    #: 타일 이름 → 대표색. 실제 타일 그림 대신 색만 칠한다 — 배치를 보는 것이 목적이다
    floor_color = {
        "dirt": P.W["dirt1"], "drill": P.W["dirt0"], "grass": P.W["grass1"],
        "concrete": P.W["conc1"], "concreteLight": P.W["conc0"], "tile": P.W["conc0"],
        "wood": P.W["wood1"], "asphalt": P.W["conc3"], "snow": P.W["snow1"],
        "water": P.W["water1"],
        # §9.0 사이드뷰 하늘 — 밟는 바닥이 아니다
        "skyDay": P.W["water0"], "skyNight": P.W["night0"],
    }
    wall_color = {
        "interior": P.W["conc2"], "utility": P.W["conc3"],
        "outdoor": P.W["conc3"], "wood": P.W["wood2"], "fence": P.W["metal2"],
    }

    def paint(runs, table, fallback):
        for run in runs or []:
            kind = run["tile"].split(":", 1)[-1]
            color = table.get(kind, fallback)
            cells = run["cells"]
            for i in range(0, len(cells) - 1, 2):
                x, y = cells[i], cells[i + 1]
                draw.rectangle([x * scale, y * scale,
                                x * scale + scale - 1, y * scale + scale - 1], fill=color)

    paint(base["layers"]["ground"], floor_color, P.W["dirt1"])
    paint(base["layers"]["wall"], wall_color, P.W["conc3"])

    # 소품
    props = {p["name"]: p for p in art["tiles"]["props"]}
    for p in base["props"]:
        color = P.W["accent"] if p["name"] == "이정표" else P.W["olive1"]
        draw.rectangle([p["x"] * scale, p["y"] * scale,
                        (p["x"] + p["w"]) * scale - 1, (p["y"] + p["h"]) * scale - 1],
                       fill=color)

    # 문 — 걸어서 지나갈 유일한 통로다. 눈에 띄어야 배치 오류가 보인다
    for d in base.get("doors", []):
        draw.rectangle([d["x"] * scale, d["y"] * scale,
                        (d["x"] + d["w"]) * scale - 1, (d["y"] + d["h"]) * scale - 1],
                       fill=P.W["heat"])

    # 구역 경계와 이름
    for z in base["zones"]:
        box = [z["x"] * scale, z["y"] * scale,
               (z["x"] + z["w"]) * scale - 1, (z["y"] + z["h"]) * scale - 1]
        edge = P.W["accent"] if z["kind"] == "corridor" else \
            P.W["cold"] if z["kind"] == "outdoor" else P.W["alert"]
        draw.rectangle(box, outline=edge)
        draw.text((box[0] + 3, box[1] + 2), f"{z['id']} {z['name']}", fill=P.UI["ink"])

    img.save(dest)
    print(f"{dest} ({W}×{H} 타일 · {img.width}×{img.height}px) · "
          f"구역 {len(base['zones'])} · 문 {len(base.get('doors', []))}")
    return 0


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "map.png"
    raise SystemExit(main(out, int(sys.argv[2]) if len(sys.argv) > 2 else 6))
