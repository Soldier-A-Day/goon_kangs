"""
Unity 없이 산출물을 눈으로 확인한다.

    python3 tools/sprites/preview.py [out.png]

에디터를 띄우지 않고 스프라이트가 실제로 어떻게 보이는지 보는 것이 목적이다.
32×48 픽셀아트는 코드만 봐서는 맞는지 알 수 없다 — 어깨가 1px 어긋난 것은
그림에서만 보인다.
"""

from __future__ import annotations

import json
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import palette as P
import pixel as PX

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART = os.path.join(ROOT, "unity", "Assets", "Art", "2d")

#: §5.3 4보직 — head · mark · hand 3레이어만 교체한다(목업 §C)
ROLE_LOOK = {
    "rifle": dict(head="cap", hand="rifle", gear="belt"),
    "comms": dict(head="cap", hand="radio", gear="belt"),
    "medic": dict(head="cap", hand="none", gear="medicBag"),
    "admin": dict(head="bare", hand="clipboard", gear="none"),
}


def _sheet(name: str) -> Image.Image:
    return Image.open(os.path.join(ART, "chars", name)).convert("RGBA")


def compose(manifest: dict, role: str, rank: str, row: str, frame: int,
            cloth: str = "field", skin: str = "skin0", outer: str = "none") -> Image.Image:
    """§5.2 레이어 스택을 아래에서 위로 합성한다."""
    look = ROLE_LOOK[role]
    order = [
        f"body_{skin}.png", f"legs_{cloth}.png", f"torso_{cloth}.png",
        f"outer_{outer}.png", f"gear_{look['gear']}.png", f"head_{look['head']}.png",
        f"mark_{role}_{rank}.png", f"hand_{look['hand']}.png",
    ]

    w, h = manifest["cellW"], manifest["cellH"]
    y = manifest["rows"].index(row)
    out = PX.blank(w, h)
    for name in order:
        path = os.path.join(ART, "chars", name)
        if not os.path.exists(path):
            continue
        cell = Image.open(path).convert("RGBA").crop(
            (frame * w, y * h, (frame + 1) * w, (y + 1) * h))
        out.alpha_composite(cell)
    return out


def main(dest: str) -> int:
    art = json.load(open(os.path.join(ART, "art2d.json"), encoding="utf-8"))
    manifest = art["chars"]
    cw, ch = manifest["cellW"], manifest["cellH"]
    S = 5  # 정수배 확대 — 픽셀 폰트·아이콘은 정수배만 허용된다(§11)

    show = [
        ("idle_S", 0), ("idle_N", 0), ("idle_E", 0),
        ("walk_S", 1), ("walk_S", 2), ("walk_S", 4),
        ("work_ground_S", 2), ("work_panel_S", 2), ("salute_S", 4),
        ("shiver_S", 1), ("pant_S", 0), ("down_idle_S", 0),
    ]
    roles = ("rifle", "comms", "medic", "admin")

    pad, head, label = 8, 40, 22
    cellw, cellh = cw * S + pad, ch * S + pad + label
    W = head + cellw * len(show)
    H = head + cellh * len(roles) + 220

    img = Image.new("RGBA", (W, H), P.UI["paper"])
    draw = ImageDraw.Draw(img)

    draw.text((8, 10), "SAD-ART-001 §5 — 캐릭터 4보직 × 클립", fill=P.UI["ink"])
    for cx, (row, frame) in enumerate(show):
        draw.text((head + cx * cellw + 4, head - 14), f"{row}:{frame}", fill=P.UI["ink2"])

    for ry, role in enumerate(roles):
        rank = ("private", "pfc", "corporal", "sergeant")[ry]
        y = head + ry * cellh
        draw.text((6, y + ch * S // 2), f"{role}\n{rank}", fill=P.UI["ink2"])
        for cx, (row, frame) in enumerate(show):
            if row not in manifest["rows"]:
                continue
            sprite = compose(manifest, role, rank, row, frame)
            x = head + cx * cellw
            draw.rectangle([x, y, x + cw * S, y + ch * S], fill=P.UI["paper2"])
            img.alpha_composite(PX.scale(sprite, S), (x, y))

    # ── 피복 스왑 (§5.5 축소안: legs/torso를 팔레트 스왑으로 처리) ──
    base = head + len(roles) * cellh + 16
    draw.text((8, base - 14), "피복 스왑 · outer · head 변형", fill=P.UI["ink"])
    variants = [
        ("field", "none", "cap", "전투복"), ("winter", "parka", "winterCap", "방한"),
        ("pt", "none", "bare", "활동복"), ("rain", "raincoat", "cap", "우의"),
        ("field", "fieldCoat", "helmet", "야전+철모"), ("field", "none", "mask", "방독면"),
    ]
    for i, (cloth, outer, hd, name) in enumerate(variants):
        x = head + i * cellw
        draw.rectangle([x, base, x + cw * S, base + ch * S], fill=P.UI["paper2"])
        look = dict(ROLE_LOOK["rifle"], head=hd)
        old = ROLE_LOOK["rifle"]
        ROLE_LOOK["rifle"] = look
        sprite = compose(manifest, "rifle", "corporal", "idle_S", 0, cloth=cloth, outer=outer)
        ROLE_LOOK["rifle"] = old
        img.alpha_composite(PX.scale(sprite, S), (x, base))
        draw.text((x + 2, base + ch * S + 4), name, fill=P.UI["ink2"])

    # ── 타일 · 소품 ──
    base2 = base + ch * S + 40
    draw.text((8, base2 - 14), "§6 타일 · 소품", fill=P.UI["ink"])
    x = head
    for entry in art["tiles"]["floors"]:
        tile = Image.open(os.path.join(ART, entry["file"])).convert("RGBA")
        img.alpha_composite(PX.scale(tile, 2), (x, base2))
        draw.text((x, base2 + 68), entry["kind"][:9], fill=P.UI["ink2"])
        x += 72
    x = head
    y2 = base2 + 92
    for name in ("관물대", "침상", "보일러 본체", "무전 콘솔", "배식대", "차량", "나무", "총기 거치대"):
        spec = next(p for p in art["tiles"]["props"] if p["name"] == name)
        prop = Image.open(os.path.join(ART, spec["file"])).convert("RGBA")
        img.alpha_composite(PX.scale(prop, 2), (x, y2))
        x += prop.width * 2 + 16

    img.convert("RGB").save(dest)
    print(f"{dest} ({W}×{H})")
    return 0


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ART, "..", "..", "..", "preview.png")
    raise SystemExit(main(os.path.abspath(out)))
