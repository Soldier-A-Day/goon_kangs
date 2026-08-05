"""
소품 조각 어휘 (C3 — "캐릭터처럼 여러 조각을 합쳐서" 요청).

지금까지 소품 105종 중 26종이 `tiles._box()` 하나를 공유했고, 그 함수는 **단색
사각형을 채우는 것이 전부**였다. 그래서 관물대도 침상도 서류함도 "색만 다른 네모"로
보였다. 캐릭터가 8레이어 합성으로 사람처럼 보이는 것과 같은 방식을 소품에도 준다.

## 쓰는 법

    img = parts.slab(2, 1, "wood1")          # 몸통(윗면)
    parts.grain(img, "wood", "wood1", seed=1234)  # 재질 결
    parts.panelize(img, 2, 1, "wood1")       # 문/서랍 분할선
    parts.handle(img, 24, 16, "bar")         # 손잡이
    parts.rim(img, "wood1")                  # 윗변 빛 받는 선
    parts.feet(img, "wood1")                 # 접지 자국

`tiles.generate()`가 마지막에 `PX.shade()`로 광원(좌상단 45°)을 한 번에 먹이므로
**여기서 명암을 직접 굽지 마라.** 조각은 "무엇이 붙어 있는가"만 그린다.

## 이 파일의 두 철칙 (둘 다 과거에 사고가 났다)

1. **색을 계산하지 마라.** 곱셈·덧셈으로 새 색을 만들면 §4 팔레트를 벗어난다.
   반드시 `P.W[key]` 또는 `P.neighbor(key, step)`만 쓴다. 계열을 벗어난 색은
   날씨 그레이딩(팔레트 통째 이동)을 안 타서 혼자 원래 색으로 남는다.
   *실제 사고*: 벽 정면을 `face×1.4` 후 최근접 스냅 → 콘크리트가 초록으로 튀었다.
2. **`random`을 쓰지 마라.** 좌표·씨앗 해시만 쓴다. 다시 뽑을 때마다 결과가
   달라지면 산출물 diff가 매번 통째로 바뀌어 리뷰가 불가능해지고,
   `generate.py` 2회 실행 바이트 동일 검사가 깨진다.
"""

from __future__ import annotations

from PIL import Image

import palette as P
import pixel as PX

TILE = 32


def _hash(x: int, y: int, salt: int = 0) -> int:
    """`tiles._hash`와 같은 알고리즘 — 좌표 해시(결정적). `random` 금지의 대안이다."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return h ^ (h >> 16)


def _shade_key(key: str, step: int):
    """
    계열 안에서 `step`단 이동. 팔레트 이탈은 구조적으로 불가능하다.

    **계열 끝에서는 반대 방향으로 접는다.** `P.neighbor()`는 계열 끝에 닿으면
    자기 자신을 돌려주는데(클램프), 그러면 `wood2`·`metal2`·`conc3`·`olive2`처럼
    **이미 제 계열에서 가장 어두운 색**을 몸통으로 쓴 소품은 이음매·통풍살·다리·
    결이 전부 "같은 색으로 칠하기" = **보이지 않는 no-op**가 된다.

    실제로 그 사고가 났다: 집합 좌석이 옛 `_box()`와 똑같은 민무늬 사각형으로
    나왔고, 서류함 서랍선·총기 거치대 칸막이·물자 선반 선반선이 전부 사라졌다.
    조각을 아무리 넣어도 화면에는 안 나오니 발견도 늦다.

    어두운 몸통 위의 밝은 선은 어색하지 않다(모서리에 빛이 걸린 것으로 읽힌다).
    보이지 않는 것보다 낫고, 무엇보다 **조용히 사라지지 않는다.**
    """
    moved = P.neighbor(key, step)
    if moved != P.W[key]:
        return moved
    return P.neighbor(key, -step)


# ════════════════════════════════════════════════════════════════════ 몸통

def slab(w: int, h: int, body: str) -> Image.Image:
    """
    소품 몸통. `_box`의 단색 채우기를 대신한다.

    윗면은 `body` 그대로 두고 **아래 1/4만 한 단 어둡게** 깐다 — 탑다운에서
    물건의 앞면이 살짝 보이는 느낌을 만드는 최소한의 장치다. 명암 자체는
    `PX.shade()`가 나중에 얹으므로 여기서는 면을 나누기만 한다.
    """
    img = PX.blank(w * TILE, h * TILE)
    ww, hh = w * TILE, h * TILE
    PX.rect(img, 0, 0, ww - 1, hh - 1, P.W[body])
    front = max(2, hh // 4)
    PX.rect(img, 0, hh - front, ww - 1, hh - 1, _shade_key(body, -1))
    return img


# ════════════════════════════════════════════════════════════════════ 재질 결

def grain(img: Image.Image, kind: str, key: str, seed: int = 0) -> None:
    """
    재질 결. 같은 색 덩어리를 "나무/금속/천"으로 읽히게 하는 최소 단서다.

    `kind`: `wood`(세로 판자) · `metal`(가로 브러시) · `fabric`(성긴 점) ·
    `mesh`(격자). `key`는 몸통 색 키 — 결은 그보다 한 단 어두운 **같은 계열** 색이다.

    색을 픽셀에서 역추적하지 않고 **호출부가 키를 넘긴다.** 팔레트에 색→키 역참조가
    없기도 하고, 있더라도 역추적은 조각을 겹쳐 칠한 뒤엔 틀린 답을 준다.
    """
    w, h = img.size
    px = img.load()
    tone = (*_shade_key(key, -1), 255)

    def darker(x: int, y: int):
        return tone if px[x, y][3] else None

    if kind == "wood":
        for x in range(0, w):
            if _hash(x, 0, seed) % 7:      # 판자 이음매를 드문드문
                continue
            for y in range(h):
                if darker(x, y):
                    px[x, y] = tone
    elif kind == "metal":
        for y in range(0, h):
            if _hash(0, y, seed) % 6:
                continue
            for x in range(w):
                if darker(x, y):
                    px[x, y] = tone
    elif kind == "fabric":
        for y in range(h):
            for x in range(w):
                if _hash(x, y, seed) % 11:
                    continue
                if darker(x, y):
                    px[x, y] = tone
    elif kind == "mesh":
        for y in range(h):
            for x in range(w):
                if x % 4 and y % 4:
                    continue
                if darker(x, y):
                    px[x, y] = tone


# ════════════════════════════════════════════════════════════════════ 분할·하드웨어

def seam(img: Image.Image, x0: int, y0: int, x1: int, y1: int, key: str) -> None:
    """이음매 한 줄. 문·서랍이 갈라지는 자리."""
    PX.rect(img, x0, y0, x1, y1, _shade_key(key, -2))


def panelize(img: Image.Image, cols: int, rows: int, key: str) -> None:
    """
    몸통을 문/서랍으로 나눈다. 관물대 2문, 서류함 3서랍처럼 **개수가 곧 정체성**인
    소품에 쓴다 — 나누기만 해도 "칸이 있는 가구"로 읽힌다.
    """
    w, h = img.size
    for c in range(1, cols):
        x = w * c // cols
        seam(img, x, 1, x, h - 2, key)
    for r in range(1, rows):
        y = h * r // rows
        seam(img, 1, y, w - 2, y, key)


def handle(img: Image.Image, cx: int, cy: int, kind: str = "bar") -> None:
    """손잡이. `bar`(가로 막대) · `knob`(점) · `latch`(걸쇠)."""
    metal = P.W["metal0"]
    if kind == "bar":
        PX.rect(img, cx - 3, cy, cx + 3, cy, metal)
    elif kind == "knob":
        PX.rect(img, cx, cy, cx + 1, cy + 1, metal)
    elif kind == "latch":
        PX.rect(img, cx - 1, cy - 1, cx + 1, cy, metal)
        PX.rect(img, cx, cy + 1, cx, cy + 2, P.W["metal2"])


def vent(img: Image.Image, x: int, y: int, w: int, n: int, key: str) -> None:
    """통풍구 살. 관물대·약품장·발전기처럼 '금속 상자'를 구분해 준다."""
    for i in range(n):
        PX.rect(img, x, y + i * 2, x + w, y + i * 2, _shade_key(key, -2))


def rim(img: Image.Image, key: str) -> None:
    """윗변 1px — 빛을 먼저 받는 모서리. 물건이 바닥에서 떠 보이게 하는 값싼 장치."""
    w, _ = img.size
    PX.rect(img, 0, 0, w - 1, 0, _shade_key(key, 1))


def feet(img: Image.Image, key: str, inset: int = 2) -> None:
    """다리/접지 자국 둘. 그림자는 런타임이 그리므로 여기서는 **다리만**."""
    w, h = img.size
    dark = _shade_key(key, -2)
    PX.rect(img, inset, h - 2, inset + 2, h - 1, dark)
    PX.rect(img, w - inset - 3, h - 2, w - inset - 1, h - 1, dark)


def label(img: Image.Image, x: int, y: int, w: int = 6, h: int = 4) -> None:
    """이름표·서류 조각. 군용 물건에 붙는 흰 종이 한 장이 의외로 정체성을 만든다."""
    PX.rect(img, x, y, x + w, y + h, P.W["snow0"])
    PX.rect(img, x + 1, y + 1, x + w - 1, y + 1, P.W["conc2"])
    PX.rect(img, x + 1, y + 3, x + w - 1, y + 3, P.W["conc2"])


def wear(img: Image.Image, key: str, seed: int, amount: int = 5) -> None:
    """
    사용감(긁힘·얼룩). `amount`는 1000분율 — 5면 픽셀의 0.5%.
    좌표 해시라 다시 뽑아도 같은 자리에 같은 흠집이 난다.
    """
    w, h = img.size
    px = img.load()
    dark = (*_shade_key(key, -1), 255)
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0:
                continue
            if _hash(x, y, seed) % 1000 >= amount:
                continue
            px[x, y] = dark
