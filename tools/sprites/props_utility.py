"""
소품 설비 계열 그림 (C3 — 조각 합성).

`parts.py`의 조각 어휘로 소품을 **여러 조각을 겹쳐** 만든다. 단색 사각형이던
`tiles._box()`를 대신한다.

**타일 크기(w,h)는 절대 바꾸지 마라** — `base_map.json`이 그 크기로 배치돼 있어
바꾸면 맵이 어긋난다. 이 파일은 **그리는 함수만** 갈아 끼운다.

`BUILDERS`에 등록된 이름은 `tiles.PROPS`의 같은 이름 항목의 그리는 함수를 대체한다.

## 이 파일이 추가한 지역 헬퍼

설비 계열은 캐릭터·가구에 없던 조각이 필요하다 — 계기판 다이얼, 파이프·밸브,
물 찬 면, 손잡이 달린 통, 배선, 김(증기). `parts.py`는 공유 파일이라 못 고치므로
여기 지역 헬퍼로 둔다. 전부 `parts.py`의 두 철칙을 그대로 따른다:
색은 `P.W[key]`/`P.neighbor()`만, 좌표는 리터럴/고정값만(난수 없음).
"""

from __future__ import annotations

import parts
import palette as P
import pixel as PX

TILE = parts.TILE

#: 소품 이름 → 그리는 함수 `f(w, h) -> Image`. 비어 있으면 기존 그림이 그대로 쓰인다
BUILDERS: dict[str, object] = {}


# ═══════════════════════════════════════════════════════════ 지역 헬퍼 (설비 전용)

def _dial(img, cx: int, cy: int, r: int = 6, face: str = "metal0",
          ring: str = "metal2", needle: str = "alert") -> None:
    """계기판 다이얼 — 링 + 12/6시 눈금 + 바늘(우상향 고정). 압력계·유량계·
    콘솔 표시부 전부가 이 하나를 공유한다. 바늘 방향을 고정하는 것은 난수를
    쓰지 않기 위해서다(파일 머리말 두 철칙 2)."""
    PX.ellipse(img, cx, cy, r, r, P.W[ring])
    PX.ellipse(img, cx, cy, max(1, r - 2), max(1, r - 2), P.W[face])
    PX.rect(img, cx - 1, cy - r, cx, cy - r + 1, P.W[ring])
    PX.rect(img, cx - 1, cy + r - 1, cx, cy + r, P.W[ring])
    for i in range(max(0, r - 2)):
        PX.rect(img, cx + i, cy - i, cx + i, cy - i, P.W[needle])


def _pipe(img, x0: int, y0: int, x1: int, y1: int, key: str = "metal2", width: int = 3) -> None:
    """직선 배관 한 구간 — 가로 또는 세로(격자 파이프만 지원, 대각선 없음)."""
    half = width // 2
    if x0 == x1:
        PX.rect(img, x0 - half, min(y0, y1), x0 - half + width - 1, max(y0, y1), P.W[key])
    else:
        PX.rect(img, min(x0, x1), y0 - half, max(x0, x1), y0 - half + width - 1, P.W[key])


def _valveWheel(img, cx: int, cy: int, r: int = 4, wheel: str = "metal0", stem: str = "metal2") -> None:
    """밸브 손잡이 — 원판 + 십자 살. 배관을 잠그는 물건이라는 표식."""
    PX.ellipse(img, cx, cy, r, r, P.W[wheel])
    PX.rect(img, cx - 1, cy - r, cx, cy + r, P.W[stem])
    PX.rect(img, cx - r, cy - 1, cx + r, cy, P.W[stem])


def _lights(img, x: int, y: int, n: int = 3, keys: tuple[str, ...] = ("alert", "cold", "accent")) -> None:
    """표시등 한 줄 — 콘솔·비상 발전기의 "전원이 들어와 있다" 표식."""
    for i in range(n):
        k = keys[i % len(keys)]
        PX.rect(img, x + i * 5, y, x + i * 5 + 3, y + 3, P.W[k])


def _cable(img, x: int, y0: int, y1: int, key: str = "night0") -> None:
    """늘어진 배선 한 가닥."""
    PX.rect(img, x, y0, x + 1, y1, P.W[key])


def _steam(img, cx: int, y: int, key: str = "snow0") -> None:
    """김 두 가닥 — 뚜껑 위로 피어오르는 표식(조리솥·취반기)."""
    PX.rect(img, cx - 1, y - 4, cx, y - 3, P.W[key])
    PX.rect(img, cx + 2, y - 7, cx + 3, y - 6, P.W[key])


def _basin(img, cx: int, cy: int, rx: float, ry: float, key: str = "water1") -> None:
    """물 찬 타원 면 — 세면대·세척대의 수조."""
    PX.ellipse(img, cx, cy, rx, ry, P.W[key])


# ═══════════════════════════════════════════════════════════════ 세면·세척 계열

def _sink(w: int = 1, h: int = 1):
    """세면대 — 타원 수조 + 수도꼭지 + 배수구"""
    img = parts.slab(w, h, "conc0")
    W, H = w * TILE, h * TILE
    _basin(img, W // 2, int(H * 0.58), W * 0.32, H * 0.22, "water1")
    _pipe(img, W // 2, 2, W // 2, int(H * 0.32), "metal0", width=3)
    PX.rect(img, W // 2 - 5, int(H * 0.30), W // 2 + 5, int(H * 0.30) + 2, P.W["metal0"])
    PX.ellipse(img, W // 2, int(H * 0.58), 2, 2, P.W["metal2"])
    parts.rim(img, "conc0")
    return img


def _showerStall(w: int = 2, h: int = 3):
    """샤워 칸 — 커튼레일 + 샤워헤드 + 물줄기 + 배수구"""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 2, 2, W - 3, 3, P.W["metal2"])
    PX.rect(img, W // 2 - 4, 4, W // 2 + 4, 8, P.W["metal0"])
    for i in range(3):
        _pipe(img, W // 2 - 3 + i * 3, 9, W // 2 - 3 + i * 3, 12, "water1", width=1)
    PX.ellipse(img, W // 2, H - 6, 3, 2, P.W["metal2"])
    parts.rim(img, "conc1")
    return img


def _washCounter(w: int = 2, h: int = 1):
    """세척대 — 긴 수조 + 수도꼭지 + 건조살"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    _basin(img, W // 2, int(H * 0.6), W * 0.4, H * 0.28, "water1")
    _pipe(img, W // 2, 2, W // 2, 10, "metal0", width=3)
    parts.vent(img, 4, 4, W - 8, 2, "metal1")
    parts.rim(img, "metal1")
    return img


def _dishReturn(w: int = 1, h: int = 1):
    """식기 반납대 — 어두운 반납구 + 쌓인 접시"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, H - 14, W - 5, H - 10, P.W["night0"])
    PX.ellipse(img, W // 2 - 6, 6, 4, 2, P.W["snow0"])
    PX.ellipse(img, W // 2 + 2, 8, 4, 2, P.W["snow0"])
    parts.rim(img, "metal1")
    return img


def _mirror(w: int = 6, h: int = 1):
    """거울 — 프레임 + 반사면 + 물때"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 4, W - 5, H - 6, P.W["water1"])
    for x in range(8, W - 8, 14):
        PX.rect(img, x, 6, x + 3, H - 8, P.W["dirt1"])
    parts.rim(img, "metal2")
    return img


# ═══════════════════════════════════════════════════════════════ 열·화기 계열

def _stove(w: int = 1, h: int = 1):
    """난로 — 화구 + 연통 + 접지 자국"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 6, H - 14, W - 7, H - 3, P.W["heat"])
    PX.rect(img, W // 2 - 3, 0, W // 2 + 2, 8, P.W["metal1"])
    PX.rect(img, W // 2 - 3, 0, W // 2 + 2, 2, P.W["metal0"])
    parts.feet(img, "metal2")
    return img


def _boiler(w: int = 3, h: int = 5):
    """보일러 본체 — 원통 몸체 + 배관 2~3개 + 압력계 + 점검구"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    _pipe(img, int(W * 0.2), 20, int(W * 0.2), H - 20, "metal2", width=4)
    _pipe(img, int(W * 0.8), 30, int(W * 0.8), H - 30, "metal2", width=4)
    _pipe(img, 10, int(H * 0.3), int(W * 0.2), int(H * 0.3), "metal2", width=4)
    _dial(img, W // 2, int(H * 0.22), 8, needle="alert")
    PX.ellipse(img, W // 2, int(H * 0.6), 10, 10, P.W["metal2"])
    PX.ellipse(img, W // 2, int(H * 0.6), 7, 7, P.W["metal0"])
    for dx, dy in ((-8, 0), (8, 0), (0, -8), (0, 8)):
        PX.rect(img, W // 2 + dx - 1, int(H * 0.6) + dy - 1, W // 2 + dx, int(H * 0.6) + dy, P.W["metal2"])
    parts.rim(img, "metal1")
    parts.feet(img, "metal1")
    return img


def _riceCooker(w: int = 3, h: int = 2):
    """취반기 — 둥근 몸통 + 뚜껑 + 김 + 다이얼"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    PX.ellipse(img, W // 2, int(H * 0.58), W * 0.35, H * 0.36, P.W["metal0"])
    PX.rect(img, int(W * 0.3), 6, int(W * 0.7), int(H * 0.3), P.W["metal2"])
    _steam(img, W // 2, 6)
    _dial(img, int(W * 0.78), int(H * 0.6), 6, needle="cold")
    return img


def _cookPot(w: int = 2, h: int = 2):
    """조리솥 — 원형 솥 + 손잡이 양쪽 + 뚜껑 김"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    top = int(H * 0.4)
    PX.rect(img, 6, top, W - 7, H - 4, P.W["metal1"])
    PX.rect(img, 4, top - 4, W - 5, top + 2, P.W["metal2"])
    PX.rect(img, 0, top - 2, 6, top + 4, P.W["metal2"])
    PX.rect(img, W - 6, top - 2, W - 1, top + 4, P.W["metal2"])
    _steam(img, W // 2, top - 4)
    _steam(img, W // 2 + 10, top - 6)
    return img


def _servingPot(w: int = 1, h: int = 1):
    """배식통 — 작은 통 + 손잡이 하나 + 뚜껑"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    top = int(H * 0.42)
    PX.rect(img, 4, top, W - 5, H - 3, P.W["metal1"])
    PX.rect(img, 3, top - 3, W - 4, top + 1, P.W["metal2"])
    PX.rect(img, W // 2 - 2, top - 7, W // 2 + 1, top - 3, P.W["metal2"])
    return img


def _serving(w: int = 5, h: int = 1):
    """배식대 — 배식 칸 + 보온 빛 + 김"""
    img = parts.slab(w, h, "metal0")
    W, H = w * TILE, h * TILE
    parts.panelize(img, w, 1, "metal0")
    for i in range(w):
        PX.rect(img, i * TILE + 6, H - 12, i * TILE + TILE - 7, H - 8, P.W["heat"])
        _steam(img, i * TILE + TILE // 2, H - 14)
    parts.rim(img, "metal0")
    return img


# ═══════════════════════════════════════════════════════════════ 전기·계기 계열

def _console(w: int = 4, h: int = 3):
    """무전 콘솔 — 경사 패널 + 다이얼 여러 개 + 표시등 + 케이블"""
    img = parts.slab(w, h, "device")
    W, H = w * TILE, h * TILE
    PX.rect(img, 6, 8, W - 7, int(H * 0.55), P.W["night0"])
    _dial(img, int(W * 0.25), int(H * 0.32), 7, needle="alert")
    _dial(img, int(W * 0.45), int(H * 0.32), 7, needle="cold")
    _dial(img, int(W * 0.65), int(H * 0.32), 7, needle="accent")
    _lights(img, int(W * 0.75), int(H * 0.14), 4)
    _cable(img, W - 10, int(H * 0.55), H - 6)
    parts.label(img, 6, H - 12, 10, 6)
    return img


def _terminal(w: int = 1, h: int = 1):
    """단말 — 화면 + 자판 줄"""
    img = parts.slab(w, h, "device")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 4, W - 5, 18, P.W["cold"])
    for i in range(3):
        PX.rect(img, 4 + i * 8, 22, 8 + i * 8, 24, P.W["metal2"])
    return img


def _flowMeter(w: int = 1, h: int = 1):
    """유량계 — 배관 + 다이얼(청 바늘)"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    _pipe(img, 4, H // 2, W - 4, H // 2, "metal1", width=6)
    _dial(img, W // 2, H // 2, 8, needle="cold")
    return img


def _pressureGauge(w: int = 1, h: int = 1):
    """압력계 — 배관 + 다이얼(적 바늘) + 위험 구간 표시"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    _pipe(img, W // 2, H - 4, W // 2, int(H * 0.6), "metal1", width=4)
    _dial(img, W // 2, int(H * 0.35), 10, needle="alert")
    PX.rect(img, W // 2 - 6, int(H * 0.35) - 9, W // 2 - 2, int(H * 0.35) - 6, P.W["alert"])
    return img


def _shutoffValve(w: int = 1, h: int = 1):
    """차단 밸브 — 배관 + 밸브 손잡이"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    _pipe(img, 4, H // 2, W - 4, H // 2, "metal1", width=6)
    _valveWheel(img, W // 2, H // 2, 6)
    return img


def _pipeJunction(w: int = 1, h: int = 1):
    """배관 분기 — 십자 배관 + 작은 밸브"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    _pipe(img, 4, H // 2, W - 4, H // 2, "metal2", width=6)
    _pipe(img, W // 2, 4, W // 2, H - 4, "metal2", width=6)
    _valveWheel(img, W // 2, H // 2, 4, wheel="metal0")
    return img


def _powerTerminal(w: int = 1, h: int = 1):
    """급전선 단자 — 볼트 단자 4개 + 배선 + 경고 띠"""
    img = parts.slab(w, h, "device")
    W, H = w * TILE, h * TILE
    for i in range(2):
        for j in range(2):
            PX.rect(img, 8 + i * 12, 8 + j * 10, 12 + i * 12, 12 + j * 10, P.W["metal2"])
    _cable(img, W - 6, 14, H - 4)
    PX.rect(img, 2, 2, W - 3, 4, P.W["alert"])
    return img


def _patchbay(w: int = 3, h: int = 2):
    """접속부 — 단자반 격자 + 배선"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    for y in range(10, H - 8, 12):
        for x in range(8, W - 8, 12):
            band = (x + y) % 24
            k = "cold" if band == 8 else ("accent" if band == 20 else "night0")
            PX.rect(img, x, y, x + 6, y + 6, P.W[k])
    _cable(img, W - 6, int(H * 0.3), H - 6)
    return img


def _batteryBox(w: int = 3, h: int = 2):
    """배터리함 — 통풍구 + 배터리 셀 여러 개 + 걸쇠 + 이름표"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    parts.vent(img, 8, 8, W - 16, 6, "metal1")
    for i in range(4):
        x = 12 + i * ((W - 24) // 4)
        PX.rect(img, x, 20, x + 10, H - 10, P.W["metal2"])
        PX.rect(img, x + 3, 22, x + 7, 26, P.W["accent"])
    parts.handle(img, W - 10, H // 2, "latch")
    parts.label(img, 6, H - 10, 8, 5)
    return img


# ═══════════════════════════════════════════════════════════════ 동력·연료 계열

def _generator(w: int = 3, h: int = 3):
    """발전기 — 엔진 블록 + 배기관 + 통풍구 + 다이얼 + 연료 캡"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 8, int(H * 0.35), int(W * 0.5), H - 10, P.W["olive2"])
    _pipe(img, int(W * 0.75), 6, int(W * 0.75), int(H * 0.35), "metal2", width=5)
    parts.vent(img, int(W * 0.55), int(H * 0.42), int(W * 0.3), 4, "metal2")
    _dial(img, int(W * 0.8), int(H * 0.65), 6)
    parts.handle(img, int(W * 0.2), int(H * 0.2), "knob")
    parts.feet(img, "metal2")
    return img


def _backupGenerator(w: int = 3, h: int = 3):
    """비상 발전기 — 발전기와 같은 골격이나 경고등 + 케이블 릴로 "예비용" 표식"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 8, int(H * 0.35), int(W * 0.5), H - 10, P.W["olive1"])
    _pipe(img, int(W * 0.75), 6, int(W * 0.75), int(H * 0.35), "metal2", width=5)
    _lights(img, int(W * 0.68), int(H * 0.15), 1, keys=("alert",))
    PX.ellipse(img, int(W * 0.22), int(H * 0.78), 6, 6, P.W["metal0"])
    parts.feet(img, "metal2")
    return img


def _fuelCan(w: int = 1, h: int = 1):
    """유류통 — 손잡이 + 주둥이(금속) + 이름표"""
    img = parts.slab(w, h, "olive2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 8, 4, W - 9, 8, P.W["metal2"])
    PX.rect(img, W // 2 - 2, 2, W // 2 + 2, 5, P.W["metal2"])
    parts.label(img, 4, H - 10, 8, 5)
    parts.feet(img, "olive2")
    return img


def _waterCan(w: int = 1, h: int = 1):
    """급수통 — 손잡이 + 주둥이(물색)로 유류통과 구분"""
    img = parts.slab(w, h, "metal0")
    W, H = w * TILE, h * TILE
    PX.rect(img, 8, 4, W - 9, 8, P.W["metal2"])
    PX.rect(img, W // 2 - 2, 2, W // 2 + 2, 5, P.W["water1"])
    parts.feet(img, "metal0")
    return img


def _pressureWasher(w: int = 1, h: int = 1):
    """고압 세척기 — 호스릴 + 분사대 + 손잡이"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    PX.ellipse(img, 8, H - 10, 6, 6, P.W["metal2"])
    PX.rect(img, 14, 6, 26, 8, P.W["metal0"])
    parts.handle(img, W - 8, 10, "bar")
    return img


def _waterRig(w: int = 3, h: int = 3):
    """급수 정수 설비 — 수위창 + 배관 2개 + 밸브 + 다이얼"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 8, 10, W - 9, int(H * 0.55), P.W["water1"])
    PX.rect(img, 8, 10, W - 9, 12, P.W["metal2"])
    _pipe(img, 12, int(H * 0.55), 12, H - 10, "metal2", width=5)
    _pipe(img, W - 12, int(H * 0.55), W - 12, H - 10, "metal2", width=5)
    _valveWheel(img, W // 2, H - 14, 6)
    _dial(img, int(W * 0.8), int(H * 0.2), 6, needle="cold")
    return img


# ═══════════════════════════════════════════════════════════════ 보관함 계열

def _medCabinet(w: int = 4, h: int = 3):
    """약품장 — 문 여럿(패널) + 적십자 + 손잡이 + 통풍구"""
    img = parts.slab(w, h, "white")
    W, H = w * TILE, h * TILE
    parts.panelize(img, w, 2, "white")
    PX.rect(img, W // 2 - 3, H // 2 - 8, W // 2 + 2, H // 2 + 7, P.W["cross"])
    PX.rect(img, W // 2 - 8, H // 2 - 3, W // 2 + 7, H // 2 + 2, P.W["cross"])
    for i in range(w):
        parts.handle(img, i * TILE + TILE // 2, H // 2, "knob")
    parts.vent(img, 6, 6, W - 12, 2, "white")
    parts.rim(img, "white")
    parts.feet(img, "white")
    return img


def _medBox(w: int = 1, h: int = 1):
    """약제함 — 작은 적십자 상자 + 손잡이"""
    img = parts.slab(w, h, "white")
    W, H = w * TILE, h * TILE
    PX.rect(img, W // 2 - 4, 8, W // 2 + 3, 22, P.W["cross"])
    PX.rect(img, W // 2 - 9, 12, W // 2 + 8, 18, P.W["cross"])
    parts.handle(img, W // 2, H - 8, "knob")
    return img


def _coldStorage(w: int = 2, h: int = 3):
    """부식고 — 냉장 통풍구 + 걸쇠 + 이름표(2단 패널)"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    parts.panelize(img, 1, 2, "metal1")
    parts.vent(img, 8, 14, W - 16, 5, "metal1")
    parts.handle(img, W - 10, H // 2, "latch")
    parts.label(img, 6, H - 14, 10, 6)
    parts.rim(img, "metal1")
    parts.feet(img, "metal1")
    return img


def _foodWasteStorage(w: int = 2, h: int = 3):
    """잔반장 — 사용감 있는 올리브색 저장함 + 통풍구 + 걸쇠(부식고와 색으로 구분)"""
    img = parts.slab(w, h, "olive1")
    W, H = w * TILE, h * TILE
    parts.vent(img, 8, 14, W - 16, 5, "olive1")
    parts.handle(img, W - 10, H // 2, "latch")
    parts.wear(img, "olive1", seed=53, amount=10)
    parts.label(img, 6, H - 14, 10, 6)
    return img


def _foodBin(w: int = 1, h: int = 1):
    """잔반통 — 뚜껑 + 페달 + 사용감"""
    img = parts.slab(w, h, "olive1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 4, W - 5, 10, P.W["olive2"])
    PX.rect(img, W // 2 - 1, H - 8, W // 2 + 1, H - 4, P.W["metal2"])
    parts.wear(img, "olive1", seed=17, amount=8)
    return img


def _recycleBins(w: int = 2, h: int = 1):
    """분리수거함 — 두 통 + 서로 다른 뚜껑 색 + 이름표 둘"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    parts.seam(img, W // 2, 2, W // 2, H - 2, "metal1")
    PX.rect(img, 4, 4, W // 2 - 3, 10, P.W["accent"])
    PX.rect(img, W // 2 + 3, 4, W - 5, 10, P.W["cold"])
    parts.label(img, 6, H - 10, 6, 4)
    parts.label(img, W // 2 + 6, H - 10, 6, 4)
    return img


def _disposalBin(w: int = 1, h: int = 1):
    """폐기함 — 뚜껑 + 대각 경고 줄무늬"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 4, W - 5, 9, P.W["metal1"])
    for i in range(4):
        x0 = 4 + i * 5
        PX.rect(img, x0, H - 6 - i, x0 + 3, H - 5 - i, P.W["alert"])
    return img


def _returnWindow(w: int = 1, h: int = 1):
    """퇴식구 — 어두운 반납 개구부 + 선반 턱"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 10, W - 5, 20, P.W["night0"])
    PX.rect(img, 4, 20, W - 5, 22, P.W["metal2"])
    return img


# ═══════════════════════════════════════════════════════════════ 세탁·위생 계열

def _washer(w: int = 1, h: int = 1):
    """세탁기 — 원형 드럼 창 + 손잡이 + 다이얼"""
    img = parts.slab(w, h, "conc0")
    W, H = w * TILE, h * TILE
    PX.ellipse(img, W // 2, int(H * 0.6), 10, 10, P.W["metal2"])
    PX.ellipse(img, W // 2, int(H * 0.6), 7, 7, P.W["water0"])
    parts.handle(img, W // 2, 8, "knob")
    _dial(img, 6, 6, 3, needle="cold")
    parts.feet(img, "conc0")
    return img


def _washerBig(w: int = 2, h: int = 2):
    """세척기 — 더 큰 드럼 + 배관 + 밸브(세탁기보다 산업용으로 크다)"""
    img = parts.slab(w, h, "conc0")
    W, H = w * TILE, h * TILE
    PX.ellipse(img, W // 2, int(H * 0.6), 18, 18, P.W["metal2"])
    PX.ellipse(img, W // 2, int(H * 0.6), 13, 13, P.W["water0"])
    _pipe(img, 6, 8, 6, int(H * 0.3), "metal1", width=3)
    _valveWheel(img, W - 10, 10, 5)
    parts.feet(img, "conc0")
    return img


def _laundryDesk(w: int = 4, h: int = 3):
    """세탁물 수령대 — 개켜진 세탁물 더미 + 천 재질결 + 이름표"""
    img = parts.slab(w, h, "wood1")
    W, H = w * TILE, h * TILE
    for i, shade in enumerate(("snow0", "olive1", "snow0")):
        y0 = 8 + i * 18
        PX.rect(img, 8, y0, W - 30, y0 + 12, P.W[shade])
    parts.grain(img, "fabric", "wood1", seed=41)
    parts.label(img, W - 24, H - 14, 12, 6)
    parts.rim(img, "wood1")
    parts.feet(img, "wood1")
    return img


def _uniformDesk(w: int = 3, h: int = 2):
    """상하키 수령대 — 개켜진 군복 더미 + 천 재질결 + 이름표(세탁물 수령대보다 낮고 짧다)"""
    img = parts.slab(w, h, "wood1")
    W, H = w * TILE, h * TILE
    for i, shade in enumerate(("olive1", "snow0")):
        y0 = 8 + i * 20
        PX.rect(img, 8, y0, W - 30, y0 + 14, P.W[shade])
    parts.grain(img, "fabric", "wood1", seed=29)
    parts.label(img, W - 20, H - 14, 10, 6)
    return img


def _hygieneDesk(w: int = 4, h: int = 2):
    """위생 검수대 — 클립보드(이름표) + 온도계 다이얼 + 합격 스탬프"""
    img = parts.slab(w, h, "metal0")
    W, H = w * TILE, h * TILE
    parts.label(img, 8, 8, 20, 10)
    _dial(img, int(W * 0.75), int(H * 0.4), 8, needle="cold")
    PX.rect(img, int(W * 0.58), int(H * 0.65), int(W * 0.58) + 10, int(H * 0.65) + 6, P.W["accent"])
    return img


def _reagentDesk(w: int = 1, h: int = 1):
    """시약대 — 색색 시약병 + 이름표"""
    img = parts.slab(w, h, "wood1")
    W, H = w * TILE, h * TILE
    for i, k in enumerate(("alert", "cold", "accent")):
        PX.rect(img, 6 + i * 7, 8, 9 + i * 7, 16, P.W[k])
    parts.label(img, 4, H - 9, 8, 5)
    return img


def _floorDrain(w: int = 1, h: int = 1):
    """바닥 배수구 — 격자 배수 구멍 + 중앙 물 고임"""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 0, 0, W - 1, H - 1, P.W["metal2"])
    for y in range(4, H - 4, 6):
        for x in range(4, W - 4, 6):
            PX.ellipse(img, x, y, 1, 1, P.W["night0"])
    PX.ellipse(img, W // 2, H // 2, 3, 3, P.W["water2"])
    return img


def _pipeJunctionFloor(w: int = 1, h: int = 1):
    """배관 분기 — 바닥에 박힌 분기 관 이음(바닥 배수구와 달리 관이 도드라진다)"""
    return _pipeJunction(w, h)


def _toiletStall(w: int = 2, h: int = 2):
    """변기칸 — 칸막이 + 문 + 걸쇠 + 틈으로 보이는 변기 실루엣"""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    PX.rect(img, int(W * 0.6), 4, W - 4, H - 4, P.W["conc0"])
    parts.handle(img, int(W * 0.65), H // 2, "latch")
    PX.ellipse(img, int(W * 0.3), int(H * 0.65), 8, 10, P.W["snow0"])
    parts.rim(img, "conc1")
    return img


def _toiletStallBig(w: int = 3, h: int = 4):
    """화장실 칸 — 변기칸과 같은 조각이나 더 크고 물탱크가 추가로 보인다"""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    PX.rect(img, int(W * 0.55), 4, W - 4, H - 4, P.W["conc0"])
    parts.handle(img, int(W * 0.6), H // 2, "latch")
    PX.ellipse(img, int(W * 0.28), int(H * 0.7), 10, 14, P.W["snow0"])
    PX.rect(img, int(W * 0.15), int(H * 0.15), int(W * 0.4), int(H * 0.15) + 4, P.W["metal2"])
    parts.rim(img, "conc1")
    return img


# ═══════════════════════════════════════════════════════════════ 운동·운반 계열

def _treadmill(w: int = 2, h: int = 4):
    """러닝머신 — 벨트 트레드 줄무늬 + 콘솔 다이얼 + 양쪽 손잡이"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    belt_top = int(H * 0.3)
    PX.rect(img, 8, belt_top, W - 9, H - 10, P.W["night0"])
    for y in range(belt_top + 4, H - 10, 8):
        PX.rect(img, 8, y, W - 9, y + 2, P.W["metal2"])
    PX.rect(img, 4, 4, W - 5, belt_top - 2, P.W["device"])
    _dial(img, W // 2, int(belt_top * 0.5), 6, needle="cold")
    parts.handle(img, 6, belt_top + 6, "bar")
    parts.handle(img, W - 7, belt_top + 6, "bar")
    return img


def _cableMachine(w: int = 3, h: int = 3):
    """케이블 머신 — 프레임 기둥 + 웨이트 스택 + 도르래 + 케이블"""
    img = parts.slab(w, h, "metal2")
    W, H = w * TILE, h * TILE
    PX.rect(img, 6, 4, 12, H - 4, P.W["metal1"])
    PX.rect(img, W - 13, 4, W - 7, H - 4, P.W["metal1"])
    for i in range(5):
        y = 10 + i * 10
        PX.rect(img, W // 2 - 14, y, W // 2 + 14, y + 6, P.W["metal0"])
    PX.ellipse(img, W // 2, 8, 4, 4, P.W["metal2"])
    _cable(img, W // 2, 12, 60)
    return img


def _conveyor(w: int = 5, h: int = 2):
    """세척 컨베이어 — 긴 벨트 살 + 분사 노즐 + 모터함"""
    img = parts.slab(w, h, "metal1")
    W, H = w * TILE, h * TILE
    for x in range(4, W - 4, 10):
        PX.rect(img, x, 10, x + 4, H - 10, P.W["metal2"])
    for i in range(3):
        PX.rect(img, 20 + i * (W // 3), 4, 24 + i * (W // 3), 8, P.W["water1"])
    PX.rect(img, W - 20, H // 2 - 8, W - 4, H // 2 + 8, P.W["device"])
    return img


# ═══════════════════════════════════════════════════════════════ 등록

BUILDERS.update({
    "세면대": _sink,
    "샤워 칸": _showerStall,
    "난로": _stove,
    "보일러 본체": _boiler,
    "무전 콘솔": _console,
    "약품장": _medCabinet,
    "배식대": _serving,
    "세척대": _washCounter,
    "식기 반납대": _dishReturn,
    "세탁기": _washer,
    "발전기": _generator,
    "유류통": _fuelCan,
    "거울": _mirror,
    "변기칸": _toiletStall,
    "약제함": _medBox,
    "바닥 배수구": _floorDrain,
    "세탁물 수령대": _laundryDesk,
    "러닝머신": _treadmill,
    "케이블 머신": _cableMachine,
    "퇴식구": _returnWindow,
    "잔반통": _foodBin,
    "분리수거함": _recycleBins,
    "취반기": _riceCooker,
    "세척 컨베이어": _conveyor,
    "부식고": _coldStorage,
    "급수통": _waterCan,
    "시약대": _reagentDesk,
    "폐기함": _disposalBin,
    "위생 검수대": _hygieneDesk,
    "배터리함": _batteryBox,
    "유량계": _flowMeter,
    "고압 세척기": _pressureWasher,
    "압력계": _pressureGauge,
    "비상 발전기": _backupGenerator,
    "차단 밸브": _shutoffValve,
    "배관 분기": _pipeJunctionFloor,
    "단말": _terminal,
    "배식통": _servingPot,
    "조리솥": _cookPot,
    "세척기": _washerBig,
    "급수 정수 설비": _waterRig,
    "잔반장": _foodWasteStorage,
    "접속부": _patchbay,
    "상하키 수령대": _uniformDesk,
    "화장실 칸": _toiletStallBig,
    "급전선 단자": _powerTerminal,
})
