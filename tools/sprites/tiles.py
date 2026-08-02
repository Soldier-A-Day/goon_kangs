"""
타일셋 생성기 (SAD-ART-001 §6).

타일 32×32 = 1유닛(PPU 32). 캐릭터 셀 폭과 같으므로 사람이 한 칸을 정확히
채우고, 그게 §6.1이 타일 크기를 캐릭터 폭에 맞춘 이유다 — 방 크기를 타일로
세면 그대로 "몇 명이 지나갈 수 있는가"가 된다.

**벽은 오토타일**이다. 이웃 4방향 비트마스크(0~15)로 16장을 뽑아두면 맵 오소링
쪽에서 벽을 사각형으로만 찍어도 모서리가 맞는다. 손으로 모서리를 고르게 하면
90×70 타일 맵에서 반드시 틀린 곳이 나온다.

노이즈는 **결정적**이다. `random`을 쓰지 않고 좌표 해시를 쓴다 — 다시 뽑을
때마다 바닥 얼룩이 달라지면 diff가 매번 통째로 바뀌어 리뷰가 불가능해진다.
"""

from __future__ import annotations

import os

from PIL import Image

import palette as P
import pixel as PX

TILE = 32


def _hash(x: int, y: int, salt: int = 0) -> int:
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return h ^ (h >> 16)


# ══════════════════════════════════════════════════════════════════════ 바닥

#: 바닥 종류 → (기본색, 얼룩색, 무늬)
FLOORS = {
    "concrete": ("conc1", "conc2", "slab"),
    "concreteLight": ("conc0", "conc1", "slab"),
    "tile": ("conc0", "conc1", "grid4"),
    "wood": ("wood1", "wood2", "plank"),
    "dirt": ("dirt0", "dirt1", "speck"),       # 부대 바탕 — 마사토
    "drill": ("dirt1", "dirt2", "speck"),      # 연병장 — 다져진 흙. 바탕보다 어둡다
    "grass": ("grass1", "grass2", "blade"),
    "asphalt": ("conc3", "night0", "lane"),   # 길 — 중앙선으로 길임을 못박는다
    "snow": ("snow1", "snow2", "speck"),
    "water": ("water1", "water2", "flat"),

    # 사이드뷰 전용 하늘 (§9.0). 밟는 바닥이 아니라 **배경**이라 무늬를 거의
    # 안 넣는다 — 얼룩이 있으면 발 디딜 곳으로 읽힌다
    "skyDay": ("water0", "water1", "flat"),
    "skyNight": ("night0", "night1", "flat"),
}


#: §6.3 `TS_Snow` — 바닥 위에 얹는 **오버레이**. 4단계로 쌓인다.
#:
#: 별도 맵을 만들지 않고 겹치는 것이 핵심이다(§6.3 "눈 처리"). 그래야
#: 제설 일과가 이 레이어만 지워서 **작업 결과가 눈에 보인다.**
SNOW_LEVELS = 4


def snow_cover(level: int) -> Image.Image:
    """
    쌓인 눈 한 칸.

    반투명을 쓰지 않는다 — 픽셀아트에서 알파 중간값은 확대하면 뿌옇게 뜨고
    §4.2 팔레트도 깨진다. 대신 **칸을 비워서** 아래 바닥이 그대로 보이게 한다.
    옅게 쌓인 눈이 실제로 그렇게 보인다: 흙이 군데군데 드러난다.

    level 0이 가장 옅고 3이면 바닥이 안 보인다.
    """
    img = PX.blank(TILE, TILE)
    density = (0.30, 0.55, 0.78, 1.0)[level]

    for y in range(TILE):
        for x in range(TILE):
            # 좌표 해시 — `random`을 쓰면 다시 뽑을 때마다 얼룩이 달라져
            # diff가 통째로 바뀐다(이 파일 머리말)
            h = _hash(x, y, level * 7717) % 1000 / 1000.0
            if h > density:
                continue
            # 위쪽이 밝다. 눈은 빛을 받는 면이 하얗게 뜬다
            shade = "white" if _hash(x, y, 31) % 5 else "snow1"
            img.putpixel((x, y), P.W[shade])

    # 칸 아랫변에 그늘을 넣어봤지만 **줄무늬가 됐다.** 눈이 넓게 깔리면 32px마다
    # 어두운 선이 그어져 밭고랑처럼 보인다 — 타일 경계는 안 보이는 편이 맞다.
    return img


def floor(kind: str) -> Image.Image:
    base, dark, pattern = FLOORS[kind]
    img = PX.blank(TILE, TILE)
    PX.rect(img, 0, 0, TILE - 1, TILE - 1, P.W[base])
    px = img.load()
    d = P.W[dark]

    if pattern == "slab":
        # 콘크리트 슬래브 — 이음매 십자
        PX.rect(img, 0, 0, TILE - 1, 0, d)
        PX.rect(img, 0, 0, 0, TILE - 1, d)
    elif pattern == "grid4":
        # 실내 타일 — 16px 격자. 8px로 촘촘히 그으면 방 전체가 그물처럼 읽히고,
        # 그 위에 놓인 소품이 무늬에 묻힌다(§3.2 실루엣 우선)
        for i in range(0, TILE, 16):
            PX.rect(img, i, 0, i, TILE - 1, d)
            PX.rect(img, 0, i, TILE - 1, i, d)
    elif pattern == "plank":
        # 세로 판자. 가로선을 촘촘히 그으면 나무가 아니라 **벽돌**로 읽힌다 —
        # 판자는 한 방향으로 길고, 이음매는 드물게 어긋나 있어야 한다
        for i in range(0, TILE, 8):
            PX.rect(img, i, 0, i, TILE - 1, d)
        PX.rect(img, 0, 19, 7, 19, d)
        PX.rect(img, 16, 6, 23, 6, d)
    elif pattern == "speck":
        for y in range(TILE):
            for x in range(TILE):
                if _hash(x, y) % 23 == 0:
                    px[x, y] = d + (255,)
    elif pattern == "lane":
        # 아스팔트에 차선. 흙과 색만 다르면 "조금 어두운 흙"으로 읽히는데,
        # 선이 하나 그어지는 순간 **길**이 된다
        PX.rect(img, 0, 14, TILE - 1, 17, d)
        for x in range(2, TILE - 2, 10):
            PX.rect(img, x, 15, x + 5, 16, P.W["conc0"])
    elif pattern == "blade":
        # 잔디 — 세로 2px 날. 얼룩보다 풀로 읽힌다
        for y in range(TILE):
            for x in range(TILE):
                if _hash(x, y, 7) % 11 == 0:
                    PX.rect(img, x, y, x, min(TILE - 1, y + 1), d)

    return img


# ══════════════════════════════════════════════════════════════════════ 벽

#: 비트마스크 — 1 위, 2 오른쪽, 4 아래, 8 왼쪽. 켜진 방향은 **같은 벽이 이어진다**
def wall(kind: str, mask: int) -> Image.Image:
    """
    벽 오토타일 1장.

    탑다운에서 벽은 두께가 있어야 벽으로 읽힌다. 윗면(밝은 면)과 남쪽 면(어두운
    면)을 나눠 그리고, 이웃이 없는 쪽에만 마감선을 넣는다.
    """
    # 벽은 **바닥보다 어두워야** 한다. 실내 바닥이 `conc0`인데 벽도 그 근처면
    # 방 경계가 사라지고, 탑다운에서 어디까지 걸을 수 있는지가 안 읽힌다
    # 벽은 **바닥보다 두 단계 이상 어두워야** 한다. 실내 바닥이 `conc0`인데
    # 벽이 `conc1~2`면 밝기 차가 한 칸뿐이라 어디까지 걸을 수 있는지 안 읽힌다
    face, top, line = {
        "interior": ("conc3", "conc2", "night0"),
        "utility": ("night0", "conc3", "night1"),
        "outdoor": ("night0", "conc3", "night1"),
        "wood": ("wood2", "wood1", "night1"),
        "fence": ("metal2", "metal1", "night0"),
    }[kind]

    img = PX.blank(TILE, TILE)

    if kind == "fence":
        # 철조망은 **벽이 아니다.** 두께를 주면 콘크리트 담이 되고, 도면이
        # 점선으로 그린 외곽 철조망이 부대를 가른 벽처럼 보인다.
        # 기둥 하나와 가로 두 줄이면 철조망으로 읽힌다
        PX.rect(img, 14, 4, 16, TILE - 1, P.W[face])
        PX.rect(img, 0, 10, TILE - 1, 11, P.W[top])
        PX.rect(img, 0, 20, TILE - 1, 21, P.W[top])
        return img

    PX.rect(img, 0, 0, TILE - 1, TILE - 1, P.W[top])
    # 남쪽 면 — 두께 10px. 여기가 벽을 세워 보이게 한다
    PX.rect(img, 0, 22, TILE - 1, TILE - 1, P.W[face])
    PX.rect(img, 0, 22, TILE - 1, 22, P.W[line])

    if not mask & 1:
        PX.rect(img, 0, 0, TILE - 1, 0, P.W[line])
    if not mask & 2:
        PX.rect(img, TILE - 1, 0, TILE - 1, TILE - 1, P.W[line])
    if not mask & 4:
        PX.rect(img, 0, TILE - 1, TILE - 1, TILE - 1, P.W[line])
    if not mask & 8:
        PX.rect(img, 0, 0, 0, TILE - 1, P.W[line])

    return img


# ═══════════════════════════════════════════════════════════════════ 오브젝트
#
# §6.2대로 TM_Object는 Y-sort 대상이라 Tilemap이 아니라 개별 스프라이트로 나간다.
# 크기는 타일 단위이며, 상호작용 지점의 이름이 곧 이 키다(ZoneMap이 이름으로 찾는다).

def _box(w: int, h: int, body: str, top: str, line: str) -> Image.Image:
    img = PX.blank(w * TILE, h * TILE)
    H = h * TILE
    PX.rect(img, 0, 0, w * TILE - 1, H - 1, P.W[body])
    PX.rect(img, 0, 0, w * TILE - 1, 5, P.W[top])
    PX.rect(img, 0, H - 3, w * TILE - 1, H - 1, P.W[line])
    PX.rect(img, 0, 0, 0, H - 1, P.W[line])
    PX.rect(img, w * TILE - 1, 0, w * TILE - 1, H - 1, P.W[line])
    return img


#: (타일 폭, 타일 높이, 그리는 함수)
def _locker(w=1, h=1):
    img = _box(w, h, "metal1", "metal0", "metal2")
    for i in range(w):
        PX.rect(img, i * TILE + 4, 10, i * TILE + TILE - 5, 12, P.W["metal2"])
        PX.rect(img, i * TILE + 4, 20, i * TILE + TILE - 5, 22, P.W["metal2"])
        PX.rect(img, i * TILE + TILE - 8, 15, i * TILE + TILE - 7, 17, P.W["metal0"])
    return img


def _bunk(w=2, h=1):
    img = _box(w, h, "wood1", "wood0", "wood2")
    PX.rect(img, 3, 8, w * TILE - 4, TILE - 6, P.W["olive1"])
    PX.rect(img, 3, 8, 12, TILE - 6, P.W["conc0"])   # 베개
    return img


def _board(w=2, h=1):
    img = _box(w, h, "wood2", "wood1", "wood2")
    PX.rect(img, 3, 5, w * TILE - 4, TILE - 5, P.W["paper"])
    for i in range(4):
        PX.rect(img, 6, 9 + i * 5, w * TILE - 8, 10 + i * 5, P.W["paperLine"])
    return img


def _sink(w=1, h=1):
    img = _box(w, h, "conc0", "snow0", "conc2")
    PX.rect(img, 6, 8, TILE - 7, TILE - 8, P.W["water1"])
    PX.rect(img, 14, 4, 17, 9, P.W["metal0"])
    return img


def _stove(w=1, h=1):
    img = _box(w, h, "metal2", "metal1", "metal2")
    PX.rect(img, 7, 14, TILE - 8, TILE - 7, P.W["heat"])
    PX.rect(img, 13, 0, 18, 8, P.W["metal2"])   # 연통
    return img


def _boiler(w=2, h=2):
    img = _box(w, h, "metal1", "metal0", "metal2")
    PX.rect(img, 8, 20, 24, 40, P.W["metal2"])
    PX.rect(img, 12, 26, 20, 34, P.W["heat"])
    PX.rect(img, 34, 12, 40, 52, P.W["metal2"])  # 배관
    return img


def _shelf(w=2, h=1):
    img = _box(w, h, "wood2", "wood1", "wood2")
    for i in range(0, w * TILE - 6, 12):
        PX.rect(img, 4 + i, 8, 12 + i, TILE - 8, P.W["olive1"])
    return img


def _rack(w=2, h=1):
    """총기 거치대 — 소총수 전용 구역(Z09)의 상호작용 지점"""
    img = _box(w, h, "wood2", "wood1", "wood2")
    for i in range(w * 2):
        PX.rect(img, 5 + i * 14, 4, 7 + i * 14, TILE - 6, P.W["boot"])
    return img


def _console(w=2, h=1):
    """무전 콘솔 — 통신실(Z06)"""
    img = _box(w, h, "device", "metal1", "metal2")
    PX.rect(img, 5, 8, 26, 22, P.W["night0"])
    PX.rect(img, 8, 11, 23, 13, P.W["cold"])
    PX.rect(img, 8, 16, 18, 18, P.W["cold"])
    PX.rect(img, 36, 10, 40, 14, P.W["alert"])
    PX.rect(img, 44, 10, 48, 14, P.W["accent"])
    return img


def _medCabinet(w=1, h=1):
    img = _box(w, h, "white", "snow0", "conc2")
    PX.rect(img, 13, 8, 18, 22, P.W["cross"])
    PX.rect(img, 8, 13, 23, 18, P.W["cross"])
    return img


def _cot(w=2, h=1):
    img = _box(w, h, "metal0", "snow0", "metal2")
    PX.rect(img, 3, 6, w * TILE - 4, TILE - 6, P.W["white"])
    return img


def _serving(w=3, h=1):
    """배식대 — 식당(Z07)"""
    img = _box(w, h, "metal0", "snow0", "metal2")
    for i in range(w):
        PX.rect(img, i * TILE + 6, 10, i * TILE + TILE - 7, TILE - 10, P.W["metal2"])
    return img


def _table(w=2, h=1):
    return _box(w, h, "wood0", "wood1", "wood2")


def _washer(w=1, h=1):
    img = _box(w, h, "conc0", "snow0", "conc2")
    PX.rect(img, 8, 10, 23, 25, P.W["metal2"])
    PX.rect(img, 11, 13, 20, 22, P.W["water0"])
    return img


def _desk(w=2, h=1):
    img = _box(w, h, "wood1", "wood0", "wood2")
    PX.rect(img, 6, 4, 26, 16, P.W["paper"])
    PX.rect(img, 36, 6, 56, 20, P.W["device"])
    return img


def _generator(w=2, h=1):
    img = _box(w, h, "metal2", "metal1", "metal2")
    PX.rect(img, 6, 10, 26, 24, P.W["olive2"])
    PX.rect(img, 34, 8, 56, 26, P.W["metal1"])
    return img


def _toolbox(w=1, h=1):
    img = _box(w, h, "alert", "heat", "metal2")
    PX.rect(img, 4, 14, TILE - 5, 16, P.W["metal2"])
    return img


def _fuelCan(w=1, h=1):
    img = _box(w, h, "olive2", "olive1", "metal2")
    PX.rect(img, 10, 4, 21, 8, P.W["metal2"])
    return img


def _flagPole(w=1, h=2):
    img = PX.blank(w * TILE, h * TILE)
    PX.rect(img, 14, 0, 17, h * TILE - 1, P.W["metal0"])
    PX.rect(img, 10, h * TILE - 6, 21, h * TILE - 1, P.W["conc2"])
    return img


def _tree(w=2, h=2):
    img = PX.blank(w * TILE, h * TILE)
    H = h * TILE
    PX.rect(img, 28, H - 14, 35, H - 1, P.W["wood2"])
    PX.rect(img, 8, 6, 55, H - 14, P.W["grass2"])
    PX.rect(img, 14, 2, 49, 20, P.W["grass1"])
    PX.rect(img, 20, 8, 43, 16, P.W["grass0"])
    return img


def _vehicle(w=2, h=3):
    img = _box(w, h, "olive1", "olive0", "olive2")
    H = h * TILE
    PX.rect(img, 5, 10, 58, 34, P.W["olive2"])
    PX.rect(img, 9, 14, 54, 30, P.W["night0"])
    PX.rect(img, 0, 40, 8, 60, P.W["boot"])
    PX.rect(img, 55, 40, 63, 60, P.W["boot"])
    PX.rect(img, 0, H - 20, 8, H - 2, P.W["boot"])
    PX.rect(img, 55, H - 20, 63, H - 2, P.W["boot"])
    return img


def _guardBox(w=2, h=2):
    """근무 초소 — 감시창이 난 작은 건물 (PLAN 04-B)"""
    img = _box(w, h, "conc1", "conc0", "conc3")
    PX.rect(img, 6, 12, 57, 34, P.W["night0"])      # 감시창
    PX.rect(img, 6, 12, 57, 14, P.W["metal1"])      # 창틀
    PX.rect(img, 30, 12, 33, 34, P.W["metal1"])     # 창살
    PX.rect(img, 8, 44, 55, 52, P.W["conc2"])       # 출입 기록대
    return img


def _barrier(w=4, h=1):
    """
    차단기 — 위병소를 위병소로 보이게 하는 물건.

    PLAN 04-B가 정문 위병소에 차단기를 그려뒀다. 초소 건물만 세워두면
    창고와 구분되지 않는다.
    """
    img = PX.blank(w * TILE, TILE)
    PX.rect(img, 0, 12, w * TILE - 5, 19, P.W["alert"])
    for x in range(4, w * TILE - 8, 16):
        PX.rect(img, x, 12, x + 7, 19, P.W["conc0"])
    PX.rect(img, w * TILE - 6, 4, w * TILE - 1, TILE - 1, P.W["metal2"])   # 지주
    return img


def _lectern(w=1, h=1):
    return _box(w, h, "wood1", "wood0", "wood2")


def _hurdle(w=2, h=1):
    """
    장애물 — 훈련장의 도목(渡木).

    훈련장은 sim이 요구하는 구역인데(일과 6건이 여기서 벌어진다) 도면에는 없다.
    빈 사각형으로 두면 연병장과 구분되지 않아 "여기가 어디지"가 되므로,
    넘고 지나갈 물건을 세워 그 자리를 훈련장으로 읽히게 한다.
    """
    img = PX.blank(w * TILE, TILE)
    for x in (4, w * TILE - 10):
        PX.rect(img, x, 6, x + 5, TILE - 1, P.W["wood2"])      # 지주
    PX.rect(img, 2, 8, w * TILE - 3, 13, P.W["wood1"])         # 윗단
    PX.rect(img, 2, 20, w * TILE - 3, 25, P.W["wood0"])        # 아랫단
    return img


def _sandbag(w=1, h=1):
    """모래주머니 — 참호·엄체호 흉내. 훈련장 각개전투 구간"""
    img = PX.blank(TILE, TILE)
    for row, top in enumerate((8, 17, 26)):
        offset = 0 if row % 2 == 0 else 5
        for x in range(offset, TILE - 4, 11):
            PX.rect(img, x, top, min(x + 9, TILE - 1), top + 7, P.W["dirt2"])
            PX.rect(img, x, top, min(x + 9, TILE - 1), top + 1, P.W["dirt1"])
    return img


def _seat(w=1, h=1):
    img = _box(w, h, "wood2", "wood1", "wood2")
    PX.rect(img, 4, 4, TILE - 5, 12, P.W["olive1"])
    return img


def _drain(w=1, h=1):
    img = PX.blank(TILE, TILE)
    PX.rect(img, 0, 0, TILE - 1, TILE - 1, P.W["water2"])
    for i in range(4, TILE, 6):
        PX.rect(img, i, 2, i + 1, TILE - 3, P.W["metal2"])
    return img


def _crate(w=1, h=1):
    img = _box(w, h, "wood1", "wood0", "wood2")
    PX.rect(img, 4, 12, TILE - 5, 14, P.W["wood2"])
    return img


# ── files-6 목업이 요구하는 물건 (SAD-ART-003 FIELD 01~05) ──────────────────
#
# 나머지는 전부 기존 생성기를 나눠 쓴다(`PROPS` 표 참조). 여기 있는 여섯은
# 실루엣이 아예 다른 것들이라 따로 그린다 — 깔개와 정렬선은 **밟고 지나가는**
# 바닥 물건이고, 거울과 변기칸과 철조망과 텐트는 어느 것으로도 대체가 안 된다.


def _slipper(w=1, h=1):
    """슬리퍼 한 켤레 — 복도에 선 맞춰 놓는 것. 작지만 줄이 어긋난 것이 보여야 한다"""
    img = PX.blank(TILE, TILE)
    for x in (5, 17):
        PX.rect(img, x, 16, x + 9, 27, P.W["olive2"])
        PX.rect(img, x + 1, 17, x + 8, 21, P.W["olive1"])
    return img


def _pot(w=1, h=1):
    """조리솥 · 배식통 — 둥근 통. 뚜껑 손잡이가 실루엣을 만든다"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 2, H // 3, W - 3, H - 2, P.W["metal1"])
    PX.rect(img, 1, H // 3 - 4, W - 2, H // 3 + 1, P.W["metal2"])
    PX.rect(img, W // 2 - 2, H // 3 - 8, W // 2 + 1, H // 3 - 4, P.W["metal2"])
    return img


def _waterRig(w=3, h=3):
    """급수 · 정수 설비 — 수위계와 시약 검수가 여기 붙는다"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 2, 2, W - 3, H - 3, P.W["metal1"])
    PX.rect(img, 6, 8, W - 7, H // 2, P.W["water1"])      # 수위창
    PX.rect(img, 6, H // 2 + 6, W // 2, H - 10, P.W["metal2"])
    return img


def _patchbay(w=3, h=2):
    """접속부 — 단자반. 급전선이 여기로 들어온다"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 2, 2, W - 3, H - 3, P.W["metal2"])
    for y in range(8, H - 8, 10):
        for x in range(8, W - 8, 10):
            PX.rect(img, x, y, x + 5, y + 5, P.W["water1"])
    return img


def _fieldKitchen(w=3, h=3):
    """야전 취사장 — 천막 아래 화덕. 숙영지의 중심이다"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for i in range(H // 2):
        half = int(i * (W // 2 - 4) / max(1, H // 2 - 1))
        PX.rect(img, W // 2 - half, H // 2 - i, W // 2 + half, H // 2 - i, P.W["olive0"])
    PX.rect(img, 6, H // 2 + 2, W - 7, H - 3, P.W["metal1"])
    PX.rect(img, W // 2 - 6, H - 14, W // 2 + 5, H - 5, P.W["heat"] if "heat" in P.W else P.W["metal2"])
    return img


def _mat(w=2, h=1):
    """총기 수입 깔개 — 바닥에 펴는 것. 밟고 지나갈 수 있다"""
    img = PX.blank(w * TILE, TILE)
    PX.rect(img, 1, 6, w * TILE - 2, TILE - 3, P.W["olive0"])
    PX.rect(img, 3, 8, w * TILE - 4, TILE - 5, P.W["olive1"])
    return img


def _line(w=4, h=1):
    """대열 정렬선 — 연병장 바닥에 그은 흰 선. 여기 서서 번호를 센다"""
    img = PX.blank(w * TILE, TILE)
    PX.rect(img, 0, TILE // 2 - 2, w * TILE - 1, TILE // 2 + 1, P.W["conc0"])
    for x in range(0, w * TILE, 12):
        PX.rect(img, x, TILE // 2 - 5, x + 2, TILE // 2 + 4, P.W["conc0"])
    return img


def _mirror(w=2, h=1):
    """세면장 거울 — 물때가 끼는 면. 문지를수록 시야가 열린다"""
    img = PX.blank(w * TILE, TILE)
    PX.rect(img, 2, 2, w * TILE - 3, TILE - 6, P.W["metal2"])
    PX.rect(img, 4, 4, w * TILE - 5, TILE - 8, P.W["water1"])
    # 물때 — 이 물건이 무엇 때문에 있는지가 실루엣에서 읽혀야 한다
    for x in range(6, w * TILE - 8, 9):
        PX.rect(img, x, 6, x + 3, TILE - 10, P.W["dirt1"])
    return img


def _stall(w=1, h=1):
    """변기칸 — 칸막이와 문. 칸별로 이동하며 청소한다"""
    img = PX.blank(TILE, TILE)
    PX.rect(img, 1, 2, TILE - 2, TILE - 1, P.W["conc1"])
    PX.rect(img, 3, 4, TILE - 4, TILE - 3, P.W["conc0"])
    PX.rect(img, TILE // 2 - 1, 10, TILE // 2 + 1, 16, P.W["metal2"])   # 손잡이
    return img


def _fenceProp(w=2, h=1):
    """철조망 — 절단 흔적을 찾는 대상. 마름모 격자가 이 물건의 전부다"""
    img = PX.blank(w * TILE, TILE)
    for x in (0, w * TILE - 4):
        PX.rect(img, x, 0, x + 3, TILE - 1, P.W["metal2"])
    for x in range(2, w * TILE - 4, 8):
        for y in range(4, TILE - 4, 8):
            PX.rect(img, x, y, x + 5, y + 1, P.W["metal1"])
            PX.rect(img, x + 2, y, x + 3, y + 5, P.W["metal1"])
    return img


def _tent(w=2, h=2):
    """야전 텐트 — 숙영지. 주변 배수가 여기서 벌어진다"""
    img = PX.blank(w * TILE, h * TILE)
    bottom = h * TILE - 1
    peak = w * TILE // 2
    for i in range(h * TILE - 6):
        half = int(i * (peak - 3) / max(1, h * TILE - 7))
        PX.rect(img, peak - half, bottom - i, peak + half, bottom - i, P.W["olive0"])
    PX.rect(img, peak - 3, bottom - 12, peak + 2, bottom, P.W["olive2"])   # 입구
    return img


def floor_label(text: str, tiles_w: int = 4) -> Image.Image:
    """
    바닥에 새긴 구역 이름.

    문 안쪽 바닥에 글자를 박는다. 걸어서만 구역을 옮기는 설계에서 **여기가
    어디인지가 바닥에 있어야** 문을 지나는 순간 알 수 있다 — 머리 위 표지판은
    고개를 들어야 보이고, 탑다운에서는 그게 시선을 화면 위로 끌어올린다.

    한글이므로 픽셀 폰트를 쓰지 않는다. 32px 타일 한 칸에 한글은 안 들어가고,
    가로 4칸(128px)을 쓰면 네 글자까지 읽힌다.
    """
    from PIL import ImageDraw, ImageFont

    W, H = tiles_w * TILE, TILE
    img = PX.blank(W, H)

    paths = [
        os.path.join(os.path.dirname(os.path.abspath(__file__)),
                     "..", "..", "unity", "Assets", "Fonts", "SoldierKR.otf"),
        "/System/Library/Fonts/AppleSDGothicNeo.ttc",
    ]

    draw = ImageDraw.Draw(img)
    draw.fontmode = "1"

    # 이름 길이는 방마다 다르다("급양동" ~ "공용 세면장 · 샤워장"). 폭에 맞을
    # 때까지 줄인다 — 잘라내면 "무기고 (통제구역"처럼 읽히다 만 글자가 남는다
    font = None
    for size in range(20, 9, -1):
        for path in paths:
            try:
                candidate = ImageFont.truetype(os.path.normpath(path), size)
            except OSError:
                continue
            box = draw.textbbox((0, 0), text, font=candidate)
            if box[2] - box[0] <= W - 8:
                font = candidate
                break
        if font is not None:
            break
    if font is None:
        return img
    # 안티앨리어싱을 끈다. 픽셀아트에 중간색이 섞이면 §4.2 팔레트가 깨지고,
    # 32px 바닥 위에서 흐릿한 획은 얼룩으로 읽힌다
    box = draw.textbbox((0, 0), text, font=font)
    x = (W - (box[2] - box[0])) // 2 - box[0]
    y = (H - (box[3] - box[1])) // 2 - box[1]

    # 바닥에 칠한 페인트처럼 보이게 — 밝은 획 아래 어두운 그림자 1px.
    # 그림자가 없으면 밝은 콘크리트 위에서 글자가 사라진다
    draw.text((x, y + 1), text, font=font, fill=P.W["conc3"])
    draw.text((x, y), text, font=font, fill=P.W["lamp0"])
    return img


def _signpost(w=1, h=2):
    """
    이정표 — 기둥 + 판.

    걸어서만 구역을 옮기므로 **문 옆에 무엇이 있는지 적혀 있어야 한다.**
    없으면 플레이어는 문을 하나씩 열어보며 부대를 외우게 되고, 그건 §6.1이 말한
    동선 비용이 아니라 수색 비용이다. 글자는 월드 UI가 얹는다(§2.1 UI는 네이티브).
    """
    H = h * TILE
    img = PX.blank(w * TILE, H)
    PX.rect(img, 14, 12, 17, H - 1, P.W["metal2"])       # 기둥
    PX.rect(img, 2, 2, TILE - 3, 16, P.W["olive1"])      # 판
    PX.rect(img, 2, 2, TILE - 3, 4, P.W["olive0"])
    PX.rect(img, 4, 7, TILE - 5, 8, P.W["conc0"])        # 글자 자리 (실제 글자는 UI)
    PX.rect(img, 4, 11, TILE - 9, 12, P.W["conc0"])
    return img


def _doorway(w=1, h=1):
    """문지방 — 밟고 지나간다. 여기가 구역 경계라는 표시"""
    img = PX.blank(TILE, TILE)
    PX.rect(img, 0, 0, TILE - 1, TILE - 1, P.W["wood1"])
    PX.rect(img, 0, 0, TILE - 1, 2, P.W["wood2"])
    PX.rect(img, 0, TILE - 3, TILE - 1, TILE - 1, P.W["wood2"])
    return img


#: 이름 → (타일 폭, 타일 높이, 함수, 통과 가능한가)
#:
#: 이름이 곧 계약이다 — 맵 오소링과 `ZoneMap.AnchorFor`가 같은 문자열로 찾는다.
PROPS: dict[str, tuple[int, int, object, bool]] = {
    "관물대": (1, 1, _locker, False),
    "침상": (2, 1, _bunk, False),
    "게시판": (2, 5, _board, False),
    "세면대": (1, 1, _sink, False),
    "샤워 칸": (2, 3, _sink, False),
    "난로": (1, 1, _stove, False),
    "보일러 본체": (3, 5, _boiler, False),
    "물자 선반": (2, 5, _shelf, False),
    "총기 거치대": (2, 5, _rack, False),
    "무전 콘솔": (4, 3, _console, False),
    "약품장": (4, 3, _medCabinet, False),
    "처치대": (2, 1, _cot, False),
    "배식대": (5, 1, _serving, False),
    "식탁": (2, 1, _table, False),
    "세척대": (2, 1, _sink, False),
    "식기 반납대": (1, 1, _crate, False),
    "세탁기": (1, 1, _washer, False),
    "건조대": (2, 1, _shelf, False),
    "서류함": (1, 1, _locker, False),
    "행정 책상": (4, 2, _desk, False),
    "발전기": (3, 3, _generator, False),
    "공구함": (1, 1, _toolbox, False),
    "유류통": (1, 1, _fuelCan, False),
    "국기 게양대": (1, 2, _flagPole, False),
    "나무": (2, 2, _tree, False),
    "차량": (5, 4, _vehicle, False),
    "초소 벽": (3, 3, _guardBox, False),
    "차단기": (4, 5, _barrier, False),
    "강단": (1, 1, _lectern, False),
    "장애물": (2, 1, _hurdle, False),
    "모래주머니": (1, 1, _sandbag, False),
    "좌석": (1, 1, _seat, False),
    "배수로": (1, 1, _drain, True),
    "물자 상자": (1, 1, _crate, False),
    "문지방": (1, 1, _doorway, True),
    "탄약함": (2, 2, _crate, False),

    # ── files-6 목업 배치 (SAD-ART-003 FIELD 01~05) ────────────────────────
    #
    # **일과 69건이 저마다 벌어지는 물건이다.** 예전에는 방마다 소품을 몇 개
    # 흩어 두고 클라가 일과 **이름**을 보고 그 중 하나를 골랐다 — "정돈"이면
    # 아무 물건, "청소"면 또 아무 물건. 그래서 관물대 정돈과 복도 정돈이 같은
    # 자리에서 벌어졌다. 지금은 `quests.json`이 어느 물건인지 들고 있고,
    # 여기 없는 이름을 쓰면 맵 생성기가 거른다.
    #
    # 그림은 대부분 **기존 생성기를 나눠 쓴다.** 서른 개를 새로 그리는 것보다
    # 배치와 이름이 먼저 맞아야 하고, 실루엣이 비슷한 물건은 실제로 비슷하게
    # 생겼다 — 이 표에 이미 `샤워 칸`이 `_sink`를, `탄약함`이 `_crate`를 쓰고 있다.
    "상황판": (1, 4, _board, False),
    "수입 깔개": (4, 2, _mat, True),
    "청소도구함": (1, 1, _locker, False),
    "슬리퍼 선반": (2, 1, _shelf, False),
    "거울": (6, 1, _mirror, False),
    "변기칸": (2, 2, _stall, False),
    "약제함": (1, 1, _locker, False),
    "바닥 배수구": (1, 1, _drain, True),
    "세탁물 수령대": (4, 3, _desk, False),
    "집합 좌석": (4, 4, _seat, False),
    "러닝머신": (2, 4, _table, False),
    "덤벨 거치대": (4, 2, _shelf, False),
    "케이블 머신": (3, 3, _rack, False),
    "재물 대장": (4, 1, _desk, False),
    "퇴식구": (1, 1, _crate, False),
    "잔반통": (1, 1, _crate, False),
    "분리수거함": (2, 1, _crate, False),
    "취반기": (3, 2, _boiler, False),
    "세척 컨베이어": (5, 2, _shelf, False),
    "부식고": (2, 3, _locker, False),
    "급수통": (1, 1, _fuelCan, False),
    "시약대": (1, 1, _desk, False),
    "들것 거치대": (2, 4, _cot, False),
    "폐기함": (1, 1, _crate, False),
    "위생 검수대": (4, 2, _desk, False),
    "일지 보드": (4, 3, _board, False),
    "배터리함": (3, 2, _crate, False),
    "일과표 게시판": (5, 4, _board, False),
    "인원 대장": (4, 2, _desk, False),
    "공용장비함": (3, 3, _locker, False),
    "청구 데스크": (4, 2, _desk, False),
    "하역 출입구": (2, 1, _doorway, True),
    "섀도보드": (4, 3, _board, False),
    "유량계": (1, 1, _console, False),
    "고압 세척기": (1, 1, _fuelCan, False),
    "압력계": (1, 1, _console, False),
    "비상 발전기": (3, 3, _generator, False),
    "차단 밸브": (1, 1, _console, False),
    "배관 분기": (1, 1, _drain, True),
    "모래함": (1, 1, _crate, False),
    "안테나 마스트": (1, 2, _flagPole, False),
    "급전선 단자": (1, 1, _console, False),
    "관측 지점": (2, 2, _lectern, False),
    "대열 정렬선": (4, 1, _line, True),
    "감시창": (4, 2, _board, False),
    "인수인계대": (1, 1, _desk, False),
    "초소 전화": (1, 1, _console, False),
    "철조망": (2, 1, _fenceProp, False),
    "검문대": (3, 2, _desk, False),
    "출입 대장": (3, 3, _desk, False),
    "운반 적재대": (4, 2, _shelf, False),
    "텐트": (3, 3, _tent, False),
    "야전 취사도구": (1, 1, _crate, False),
    "모탕": (2, 2, _crate, False),
    "그늘막 팩": (1, 1, _sandbag, False),

    # ── files-6 목업 정밀 대조에서 더 나온 것 ─────────────────────────────
    "화장실 칸": (3, 4, _stall, False),
    "PC 책상": (5, 1, _desk, False),
    "단말": (1, 1, _console, False),
    "생활관 출입문": (2, 1, _doorway, True),
    "슬리퍼": (1, 1, _slipper, False),
    "배식통": (1, 1, _pot, False),
    "조리솥": (2, 2, _pot, False),
    "세척기": (2, 2, _washer, False),
    "급수 정수 설비": (3, 3, _waterRig, False),
    "잔반장": (2, 3, _locker, False),
    "접속부": (3, 2, _patchbay, False),
    "상하키 수령대": (3, 2, _desk, False),
    "야전 취사장": (3, 3, _fieldKitchen, False),
    "토사 퇴적": (2, 1, _sandbag, False),
}


# ══════════════════════════════════════════════════════════════════════ 표식
#
# §7.1.5 · §7.9 — 월드에 띄우는 마커. 없으면 어디로 가야 하는지 알 수가 없다.
# 일과 목록에 "생활관"이라고 적혀 있어도 생활관 어디인지는 안 적혀 있고,
# 물건 앞으로 걸어가라는 설계에서 그건 **찾기 놀이**가 된다 — §6.1이 말한
# 시간 비용은 동선이지 수색이 아니다.
#
# 흰색으로 그린다. 색은 런타임에 입힌다(필수=경고색, 선택=강조색).

def marker_quest() -> Image.Image:
    """퀘스트 목표 핀 — 원 + 아래로 뾰족한 꼬리. 목업이 월드·지도에 쓰는 형태"""
    W = (255, 255, 255)
    img = PX.blank(16, 20)
    for y in range(2, 12):
        half = 6 - abs(y - 6) // 2
        PX.rect(img, 8 - half, y, 7 + half, y, W)
    for y in range(12, 20):
        half = max(0, 5 - (y - 11))
        PX.rect(img, 8 - half, y, 7 + half, y, W)
    # 가운데를 비워 링으로 만든다 — 꽉 찬 원은 멀리서 소품과 구분되지 않는다
    for y in range(5, 10):
        half = 3 - abs(y - 7)
        if half > 0:
            for x in range(8 - half, 8 + half):
                img.putpixel((x, y), (0, 0, 0, 0))
    return img


def marker_door() -> Image.Image:
    """구역 이동 지점 — 위로 향한 두 겹 화살표"""
    W = (255, 255, 255)
    img = PX.blank(16, 16)
    for i in range(6):
        PX.rect(img, 8 - i - 1, 7 - i, 8 + i, 8 - i, W)
    for i in range(6):
        PX.rect(img, 8 - i - 1, 14 - i, 8 + i, 15 - i, W)
    return img


# ══════════════════════════════════════════════════════════════════════ 산출

def generate(out_dir: str) -> dict:
    """
    §6.3 타일셋을 뽑는다.

    스펙은 7종 580타일을 요구하지만, 그 숫자는 **손으로 그린 타일**의 물량이다.
    생성기는 바닥 종류 × 무늬와 벽 종류 × 오토타일 16장으로 같은 역할을 채운다.
    훈련 맵 전용(Nature / Training / External)은 부대 본영이 서고 난 뒤에 붙인다.
    """
    tiles_dir = os.path.join(out_dir, "tiles")
    props_dir = os.path.join(out_dir, "props")
    os.makedirs(tiles_dir, exist_ok=True)
    os.makedirs(props_dir, exist_ok=True)

    # Unity `JsonUtility`가 Dictionary를 못 읽으므로 전부 배열로 내보낸다
    index: dict = {"tile": TILE, "floors": [], "walls": [], "props": [], "markers": []}

    markers_dir = os.path.join(out_dir, "markers")
    os.makedirs(markers_dir, exist_ok=True)
    for name, fn in (("quest", marker_quest), ("door", marker_door)):
        fn().save(os.path.join(markers_dir, f"{name}.png"))
        index["markers"].append({"name": name, "file": f"markers/{name}.png"})

    for kind in FLOORS:
        floor(kind).save(os.path.join(tiles_dir, f"floor_{kind}.png"))
        index["floors"].append({"kind": kind, "file": f"tiles/floor_{kind}.png"})

    # §6.3 `TS_Snow` 오버레이
    index["snow"] = []
    for level in range(SNOW_LEVELS):
        name = f"snow_{level}.png"
        snow_cover(level).save(os.path.join(tiles_dir, name))
        index["snow"].append(f"tiles/{name}")

    for kind in ("interior", "utility", "outdoor", "wood", "fence"):
        files = []
        for mask in range(16):
            name = f"wall_{kind}_{mask:02d}.png"
            wall(kind, mask).save(os.path.join(tiles_dir, name))
            files.append(f"tiles/{name}")
        index["walls"].append({"kind": kind, "files": files})

    labels_dir = os.path.join(out_dir, "labels")
    os.makedirs(labels_dir, exist_ok=True)
    index["labels"] = []

    for name, (w, h, fn, walkable) in PROPS.items():
        safe = f"prop_{abs(_hash(sum(map(ord, name)), len(name))) % 100000:05d}"
        fn(w, h).save(os.path.join(props_dir, f"{safe}.png"))
        index["props"].append({"name": name, "file": f"props/{safe}.png",
                               "w": w, "h": h, "walkable": walkable})

    return index


def emit_labels(out_dir: str, index: dict, texts) -> None:
    """구역 이름 바닥 라벨을 필요한 것만 굽는다."""
    labels_dir = os.path.join(out_dir, "labels")
    os.makedirs(labels_dir, exist_ok=True)

    for i, text in enumerate(sorted(set(texts))):
        name = f"label_{i:02d}"
        floor_label(text).save(os.path.join(labels_dir, f"{name}.png"))
        index["labels"].append({"text": text, "file": f"labels/{name}.png", "w": 4, "h": 1})
