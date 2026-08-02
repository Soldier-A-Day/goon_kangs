"""
파티클 스프라이트 생성기 (SAD-ART-001 §9.1).

파티클 12종이 쓰는 알갱이를 뽑는다. **파티클 시스템 자체는 씬 빌더가 세우고**
(`BaseScene.BuildParticles`), 여기서는 그 시스템이 뿌릴 그림만 만든다 — 물량이
어디에 있는지가 분명해야 나중에 하나를 고칠 때 어디를 볼지 안다.

알갱이는 전부 **작다.** 논리 해상도가 640×360이므로(§2.1) 8px짜리 눈송이도
화면의 1/80이고, 그보다 크면 눈이 아니라 종잇조각이 된다. 그래서 대부분
4~12px이며, 큰 것은 화면을 덮는 용도(제독 분무)뿐이다.

색은 §4.2 팔레트에서만 가져온다. 생성기(`generate.py`)가 산출물을 전부
검사하므로 여기서 팔레트 밖 색을 쓰면 빌드가 멈춘다.
"""

from __future__ import annotations

import os

from PIL import Image

import palette as P
import pixel as PX


def _disc(size: int, color, rim=None) -> Image.Image:
    """
    둥근 알갱이.

    안티에일리어싱을 **쓰지 않는다.** 픽셀아트에서 반투명 가장자리는 팔레트
    밖 색을 만들고(§4.2 위반), 확대하면 그 자리가 뿌옇게 뜬다. 대신 `rim`으로
    바깥 한 겹을 다른 팔레트 색으로 두른다.
    """
    img = PX.blank(size, size)
    r = size / 2.0
    c = (size - 1) / 2.0
    for y in range(size):
        for x in range(size):
            d = ((x - c) ** 2 + (y - c) ** 2) ** 0.5
            if d > r - 0.5:
                continue
            img.putpixel((x, y), rim if rim is not None and d > r - 1.5 else color)
    return img


def _flake(size: int = 6) -> Image.Image:
    """눈송이 — 십자 + 대각. 원으로 만들면 비와 구분되지 않는다"""
    img = PX.blank(size, size)
    c = size // 2
    for i in range(size):
        img.putpixel((c, i), P.W["white"])
        img.putpixel((i, c), P.W["white"])
    for i in range(1, c):
        for (dx, dy) in ((i, i), (-i, i), (i, -i), (-i, -i)):
            x, y = c + dx, c + dy
            if 0 <= x < size and 0 <= y < size:
                img.putpixel((x, y), P.W["snow1"])
    return img


def _speck(size: int = 3, color=None) -> Image.Image:
    """가장 작은 알갱이. 눈보라·먼지처럼 물량으로 미는 것들이 쓴다"""
    img = PX.blank(size, size)
    PX.rect(img, 0, 0, size - 1, size - 1, color or P.W["white"])
    return img


def _breath() -> Image.Image:
    """
    입김 (§9.1 `VFX_Breath` "3프레임 스프라이트, 2초 주기").

    한랭 이하에서 캐릭터 머리 앞에 뜬다. **온도가 낮다는 것을 인물이 말해주는
    유일한 장치**라(§13.2 M2 검증 질문 "말 안 해줘도 알아채나") 크기를 키웠다 —
    4px짜리는 640×360에서 그냥 안 보인다.
    """
    frames = []
    for i, (w, h) in enumerate(((5, 4), (8, 6), (11, 7))):
        img = PX.blank(12, 8)
        x0, y0 = (12 - w) // 2, (8 - h) // 2
        PX.rect(img, x0, y0, x0 + w - 1, y0 + h - 1, P.W["snow2"])
        PX.rect(img, x0 + 1, y0 + 1, x0 + w - 2, y0 + h - 2, P.W["white"])
        frames.append(img)
    return PX.sheet(frames, (12, 8))


def _drop() -> Image.Image:
    """빗방울 — 세로로 늘어난 선. 떨어지는 속도를 모양이 말한다"""
    img = PX.blank(3, 9)
    PX.rect(img, 1, 0, 1, 8, P.W["water1"])
    PX.rect(img, 1, 6, 1, 8, P.W["water0"])
    return img


def _splash() -> Image.Image:
    """지면 물튀김 — §9.1 `VFX_Rain` "지면 물튀김" """
    img = PX.blank(8, 4)
    PX.rect(img, 0, 2, 7, 2, P.W["water1"])
    PX.rect(img, 2, 1, 5, 1, P.W["water2"])
    return img


def _muzzle() -> Image.Image:
    """
    총구 화염 (§9.1 `VFX_MuzzleFlash` "2프레임, 0.08초").

    §3.3이 유혈을 금지했지만 격발 자체는 훈련의 일부다. 짧고 밝게,
    피가 아니라 **소리가 보이는 것**처럼 만든다.
    """
    frames = []
    for scale in (1.0, 0.55):
        img = PX.blank(14, 14)
        c = 7
        for r, color in ((int(5 * scale), P.W["lamp1"]), (int(3 * scale), P.W["white"])):
            if r <= 0:
                continue
            PX.rect(img, c - r, c - 1, c + r, c + 1, color)
            PX.rect(img, c - 1, c - r, c + 1, c + r, color)
        frames.append(img)
    return PX.sheet(frames, (14, 14))


def _ring() -> Image.Image:
    """
    §9.1 `VFX_QuestComplete` "accent 확산 링 + 파티클 8 · 짧게 0.4s".

    일과가 끝났다는 것을 **월드에서** 알리는 유일한 신호다. HUD 숫자가 바뀌는
    것만으로는 자기 손이 한 일과 화면이 이어지지 않는다.
    """
    img = PX.blank(24, 24)
    c = 11.5
    for y in range(24):
        for x in range(24):
            d = ((x - c) ** 2 + (y - c) ** 2) ** 0.5
            if 9.0 <= d <= 11.0:
                img.putpixel((x, y), P.W["accent"])
    return img


def _sweat() -> Image.Image:
    """땀방울 (§9.1 `VFX_SweatDrop` 수분 ≤50) — 머리 옆에 맺힌다"""
    img = PX.blank(5, 7)
    PX.rect(img, 1, 2, 3, 6, P.W["water1"])
    PX.rect(img, 2, 0, 2, 2, P.W["water1"])
    PX.rect(img, 1, 3, 2, 4, P.W["water2"])
    return img


def _spray() -> Image.Image:
    """제독 분무 (§9.1 `VFX_Decon`) — 넓게 퍼지는 안개 알갱이"""
    return _disc(10, P.W["snow2"], rim=P.W["snow1"])


def _steam() -> Image.Image:
    """수증기 (§9.1 `VFX_Steam` 난로·취사·보일러) — 입김보다 크고 느리다"""
    img = PX.blank(10, 10)
    for y in range(10):
        for x in range(10):
            d = ((x - 4.5) ** 2 + (y - 4.5) ** 2) ** 0.5
            if d <= 4.5:
                img.putpixel((x, y), P.W["conc0"] if d > 3.0 else P.W["white"])
    return img


def _haze() -> Image.Image:
    """
    아지랑이 알갱이 (§9.1 `VFX_HeatHaze` "지면 근처 왜곡 마스크").

    왜곡 자체는 셰이더(`SH_HeatDistort`)가 한다. 이건 그 위에 얹는 **가시적인
    일렁임**이다 — 왜곡만으로는 화면이 조금 흔들릴 뿐 "덥다"가 읽히지 않는다.
    """
    img = PX.blank(12, 6)
    PX.rect(img, 0, 2, 11, 3, P.W["heat"])
    PX.rect(img, 2, 1, 4, 1, P.W["heat"])
    PX.rect(img, 7, 4, 9, 4, P.W["heat"])
    return img


#: 이름 → (그림, 프레임 수, 설명). 씬 빌더가 이 이름으로 찾는다.
#:
#: 프레임이 여럿인 것은 가로로 이어붙인 시트다 — Unity 쪽에서 Texture Sheet
#: Animation 모듈이 그 격자를 그대로 읽는다. 프레임마다 파일을 나누면
#: 파티클 하나에 머티리얼이 여러 개 생기고 드로콜이 그만큼 늘어난다.
SPRITES = {
    "flake": (_flake, 1, "눈송이 — VFX_Snow_Light"),
    "speck": (lambda: _speck(3, P.W["white"]), 1, "눈보라 알갱이 — VFX_Snow_Heavy"),
    "breath": (_breath, 3, "입김 — VFX_Breath"),
    "haze": (_haze, 1, "아지랑이 — VFX_HeatHaze"),
    "dust": (lambda: _speck(3, P.W["dirt1"]), 1, "먼지 — VFX_Dust"),
    "drop": (_drop, 1, "빗방울 — VFX_Rain"),
    "splash": (_splash, 1, "물튀김 — VFX_Rain"),
    "steam": (_steam, 1, "수증기 — VFX_Steam"),
    "muzzle": (_muzzle, 2, "총구 화염 — VFX_MuzzleFlash"),
    "spray": (_spray, 1, "제독 분무 — VFX_Decon"),
    "sweat": (_sweat, 1, "땀방울 — VFX_SweatDrop"),
    "ring": (_ring, 1, "완료 확산 링 — VFX_QuestComplete"),
    # 2px로 뽑았더니 화면에서 보이지 않았다. 쓰러짐은 §3.3이 유혈을 금지한
    # 자리를 대신 채우는 연출이라 눈에 띄어야 한다
    "ash": (lambda: _speck(6, P.W["conc2"]), 1, "쓰러짐 잔해 — VFX_Collapse"),
}


def generate(out_dir: str) -> dict:
    os.makedirs(out_dir, exist_ok=True)

    index = []
    for name, (make, frames, note) in SPRITES.items():
        img = make()
        img.save(os.path.join(out_dir, f"{name}.png"))
        index.append({
            "name": name,
            "w": img.width, "h": img.height,
            "frames": frames,
            "cellW": img.width // frames, "cellH": img.height,
            "note": note,
        })

    return {"sprites": index}
