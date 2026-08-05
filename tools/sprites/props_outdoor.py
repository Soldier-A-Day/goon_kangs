"""
소품 야외·잡화 계열 그림 (C3 — 조각 합성).

`parts.py`의 조각 어휘로 소품을 **여러 조각을 겹쳐** 만든다. 단색 사각형이던
`tiles._box()`를 대신한다.

**타일 크기(w,h)는 절대 바꾸지 마라** — `base_map.json`이 그 크기로 배치돼 있어
바꾸면 맵이 어긋난다. 이 파일은 **그리는 함수만** 갈아 끼운다.

`BUILDERS`에 등록된 이름은 `tiles.PROPS`의 같은 이름 항목의 그리는 함수를 대체한다.

## 지역 헬퍼에 대한 메모 — `parts.grain()`/`parts.wear()` 호출 순서

`parts.grain()`과 `parts.wear()`는 알파만 보고 **캔버스 전체**를 훑는다(`wood`는
열 전체, `metal`은 행 전체, `fabric`/`mesh`는 픽셀 단위지만 역시 전체 스캔).
소품 하나에 재질이 여러 개 섞여 있을 때(예: 차량의 차체+창+바퀴) 다른 재질을
이미 그려둔 **뒤에** 부르면 그 재질 위에도 잘못 칠할 수 있다. 그래서 이 파일은
`grain`/`wear`를 **그 재질의 첫 번째(유일한) 불투명 영역을 그린 직후, 다른 재질을
얹기 전에만** 부른다. `seam`/`panelize`/`handle`/`vent`/`rim`/`feet`/`label`은
좌표로 한정된 사각형만 그리므로 순서와 무관하게 안전하다.
"""

from __future__ import annotations

import parts
import palette as P
import pixel as PX

TILE = parts.TILE

#: 소품 이름 → 그리는 함수 `f(w, h) -> Image`. 비어 있으면 기존 그림이 그대로 쓰인다
BUILDERS: dict[str, object] = {}


# ════════════════════════════════════════════════════════════════ 지역 헬퍼

def _diag_line(img, x0: int, y0: int, x1: int, y1: int, color, width: int = 1) -> None:
    """직선 하나 (브레젠험, `random` 없음). 로프·전선·버팀대·다리처럼 대각선이
    필요한 실루엣에 쓴다 — `parts.py`엔 사각형 어휘뿐이라 여기서 보탠다."""
    dx = abs(x1 - x0)
    sx = 1 if x0 < x1 else -1
    dy = -abs(y1 - y0)
    sy = 1 if y0 < y1 else -1
    err = dx + dy
    x, y = x0, y0
    while True:
        PX.rect(img, x, y, x + width - 1, y + width - 1, color)
        if x == x1 and y == y1:
            break
        e2 = 2 * err
        if e2 >= dy:
            err += dy
            x += sx
        if e2 <= dx:
            err += dx
            y += sy


# ════════════════════════════════════════════════════════════════ 게양대·마스트

def _flagPole(w: int = 1, h: int = 2):
    """국기 게양대 — 기둥 + 나부끼는 깃발 + 받침. 실루엣이 사각형이 아니다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx = W // 2
    PX.rect(img, cx - 1, 3, cx + 1, H - 1, P.W["metal0"])
    parts.grain(img, "metal", "metal0", seed=11)          # 기둥만 불투명한 시점 — 안전
    PX.rect(img, cx - 2, 0, cx + 2, 3, P.W["metal2"])     # 첨탑 캡
    fx0, fy0, fw, fh = cx + 1, 6, 13, 14
    PX.rect(img, fx0, fy0, fx0 + fw, fy0 + fh, P.W["olive1"])   # 깃발 천
    parts.seam(img, fx0, fy0 + fh // 2, fx0 + fw, fy0 + fh // 2, "olive1")  # 접힌 자국
    for i in range(6):
        PX.rect(img, fx0 + fw - i, fy0 + fh - i, fx0 + fw, fy0 + fh, (0, 0, 0, 0))  # 제비꼬리 절개(펄럭임)
    PX.rect(img, cx - 4, H - 6, cx + 4, H - 1, P.W["conc2"])    # 받침
    return img


def _antennaMast(w: int = 1, h: int = 2):
    """안테나 마스트 — 게양대와 실루엣을 공유하지 않도록 천 대신 가로 안테나 살."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx = W // 2
    PX.rect(img, cx - 1, 2, cx + 1, H - 1, P.W["metal1"])
    parts.grain(img, "metal", "metal1", seed=31)          # 기둥만 불투명한 시점 — 안전
    for y, half in ((8, 10), (18, 7), (26, 5)):
        PX.rect(img, cx - half, y, cx + half, y + 1, P.W["antenna"])
    PX.rect(img, cx - 2, 0, cx + 2, 3, P.W["alert"])      # 점멸등
    PX.rect(img, cx - 5, H - 5, cx + 5, H - 1, P.W["conc2"])   # 받침판
    return img


# ════════════════════════════════════════════════════════════════ 자연물

def _tree(w: int = 2, h: int = 2):
    """나무 — 불규칙한 잎 덩어리(겹친 타원 5개) + 줄기 + 덩어리 사이 틈."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx = W // 2
    PX.rect(img, cx - 3, H - 16, cx + 3, H - 1, P.W["wood2"])
    parts.grain(img, "wood", "wood2", seed=41)            # 줄기만 불투명한 시점 — 안전
    blobs = [
        (cx - 14, H - 30, 12, 10, "grass2"),
        (cx + 4, H - 34, 14, 11, "grass1"),
        (cx - 6, H - 42, 13, 10, "grass0"),
        (cx + 14, H - 26, 10, 9, "grass2"),
        (cx - 20, H - 20, 9, 8, "grass1"),
    ]
    for bx, by, rx, ry, key in blobs:
        PX.ellipse(img, bx, by, rx, ry, P.W[key])
    for gx, gy, grx, gry in ((cx - 3, H - 30, 3, 3), (cx + 10, H - 32, 2, 2), (cx - 13, H - 22, 2, 2)):
        PX.ellipse(img, gx, gy, grx, gry, (0, 0, 0, 0))   # 잎 사이 틈
    return img


def _soilMound(w: int = 2, h: int = 1):
    """토사 퇴적 — 자루가 아니라 흙더미. `_sandbag`과 실루엣을 다르게 가져간다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx = W // 2
    PX.ellipse(img, cx, H - 8, 24, 10, P.W["dirt2"])
    PX.ellipse(img, cx - 8, H - 11, 14, 8, P.W["dirt1"])
    PX.ellipse(img, cx + 10, H - 9, 10, 7, P.W["dirt0"])
    for cx2, cy2 in ((cx - 16, H - 6), (cx + 18, H - 5), (cx - 2, H - 4)):
        PX.ellipse(img, cx2, cy2, 3, 2, P.W["dirt3"])     # 흩어진 흙덩이
    return img


def _sandbag(w: int = 1, h: int = 1):
    """모래주머니 — 자루 여러 개를 엇갈려 쌓은 실루엣. 천 결로 자루 질감을 준다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for row, top in enumerate((8, 17, 26)):
        offset = 0 if row % 2 == 0 else 5
        for x in range(offset, W - 4, 11):
            cx = min(x + 4, W - 5)
            PX.ellipse(img, cx, top + 4, 5, 4, P.W["dirt2"])
            PX.ellipse(img, cx, top + 2, 5, 3, P.W["dirt1"])
    parts.grain(img, "fabric", "dirt2", seed=91)          # dirt 계열 안에서만 어두워짐 — 안전
    return img


# ════════════════════════════════════════════════════════════════ 차량·초소·차단

def _vehicle(w: int = 5, h: int = 4):
    """차량 — 차체(운전석) + 앞유리 + 적재함 + 전조등 + 바퀴 4개."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, W - 42, 10, W - 8, H - 24, P.W["olive1"])     # 운전석 차체
    parts.grain(img, "metal", "olive1", seed=52)               # 차체만 불투명한 시점 — 안전
    PX.rect(img, W - 38, 14, W - 14, 30, P.W["night0"])        # 앞유리
    PX.rect(img, 8, 20, W - 46, H - 30, P.W["olive2"])         # 적재함
    PX.rect(img, W - 12, H - 24, W - 8, H - 20, P.W["lamp0"])  # 전조등
    PX.rect(img, W - 44, 12, W - 40, 16, P.W["metal2"])        # 사이드미러
    for wx in (20, 62, W - 56, W - 20):
        PX.ellipse(img, wx, H - 8, 9, 9, P.W["boot"])
        PX.ellipse(img, wx, H - 8, 4, 4, P.W["metal2"])
    return img


def _guardBox(w: int = 3, h: int = 3):
    """초소 벽 — 감시창(창틀·창살) + 출입문 + 지붕 모서리."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 0, 0, W - 1, H - 1, P.W["conc1"])
    parts.panelize(img, w, h, "conc1")
    PX.rect(img, 8, 16, W - 9, 40, P.W["night0"])
    PX.rect(img, 8, 16, W - 9, 18, P.W["metal1"])
    for x in range(8, W - 9, 14):
        PX.rect(img, x, 16, x + 2, 40, P.W["metal1"])
    PX.rect(img, W // 2 - 10, H - 30, W // 2 + 10, H - 2, P.W["wood1"])
    parts.handle(img, W // 2 + 6, H - 16, "knob")
    parts.rim(img, "conc1")
    return img


def _barrier(w: int = 4, h: int = 5):
    """차단기 — 지주 + 균형추 + 적백 줄무늬 차단봉 + 경고등 + 받침."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    post_x = W - 18
    PX.rect(img, post_x, 18, post_x + 10, H - 1, P.W["metal2"])
    parts.grain(img, "metal", "metal2", seed=71)               # 지주만 불투명한 시점 — 안전
    PX.rect(img, post_x - 6, 18, post_x + 16, 32, P.W["metal1"])   # 균형추
    arm_y0, arm_y1 = 22, 30
    PX.rect(img, 4, arm_y0, post_x + 6, arm_y1, P.W["snow0"])
    for i, x in enumerate(range(4, post_x, 14)):
        if i % 2 == 0:
            PX.rect(img, x, arm_y0, min(x + 10, post_x), arm_y1, P.W["alert"])
    PX.rect(img, post_x, 4, post_x + 10, 16, P.W["lamp0"])      # 경고등
    PX.rect(img, post_x - 8, H - 10, post_x + 18, H - 1, P.W["conc2"])  # 받침
    return img


def _hurdle(w: int = 2, h: int = 1):
    """장애물 — 지주 2개 + 상하 가로대 + 대각 버팀목."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for x in (4, W - 9):
        PX.rect(img, x, 4, x + 5, H - 1, P.W["wood2"])
    parts.grain(img, "wood", "wood2", seed=81)                 # 지주만 불투명한 시점 — 안전
    PX.rect(img, 2, 8, W - 3, 13, P.W["wood1"])
    PX.rect(img, 2, H - 13, W - 3, H - 8, P.W["wood0"])
    _diag_line(img, 6, H - 6, W - 8, 10, P.W["wood2"], width=2)
    return img


def _wireFence(w: int = 2, h: int = 1):
    """철조망 — 마름모 격자 + 가시(짧은 틱) — 벽이 아니라 절단 대상이다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for x in (0, W - 4):
        PX.rect(img, x, 0, x + 3, H - 1, P.W["metal2"])
    for x in range(2, W - 4, 8):
        for y in range(4, H - 4, 8):
            PX.rect(img, x, y, x + 5, y + 1, P.W["metal1"])
            PX.rect(img, x + 2, y, x + 3, y + 5, P.W["metal1"])
    for x in range(4, W - 4, 6):
        PX.rect(img, x, 6, x + 1, 8, P.W["metal0"])
        PX.rect(img, x, H - 10, x + 1, H - 8, P.W["metal0"])
    return img


# ════════════════════════════════════════════════════════════════ 바닥·배수·상자

def _drain(w: int = 1, h: int = 1):
    """배수로 — 물 채움 + 콘크리트 턱 + 금속 살."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 0, 0, W - 1, H - 1, P.W["water2"])
    PX.rect(img, 0, 0, W - 1, 2, P.W["conc2"])
    PX.rect(img, 0, H - 3, W - 1, H - 1, P.W["conc2"])
    for i in range(4, W, 6):
        PX.rect(img, i, 2, i + 2, H - 3, P.W["metal2"])
    return img


def _crate(w: int = 1, h: int = 1):
    """물자 상자 — 나무 몸통 + 결 + 분할선 + 손잡이 + 접지."""
    img = parts.slab(w, h, "wood1")
    parts.grain(img, "wood", "wood1", seed=101)
    W, H = w * TILE, h * TILE
    parts.panelize(img, max(1, w), 1, "wood1")
    parts.rim(img, "wood1")
    parts.handle(img, W // 2, H // 2, "bar")
    parts.feet(img, "wood1")
    return img


def _doorway(w: int = 1, h: int = 1):
    """문지방 — 나무 바닥 + 상하 마감 + 중앙 금속 문턱대."""
    img = parts.slab(w, h, "wood1")
    parts.grain(img, "wood", "wood1", seed=111)
    W, H = w * TILE, h * TILE
    parts.rim(img, "wood1")
    PX.rect(img, 0, H - 3, W - 1, H - 1, P.W["wood2"])
    PX.rect(img, 0, H // 2 - 1, W - 1, H // 2, P.W["metal1"])
    return img


def _ammoBox(w: int = 2, h: int = 2):
    """탄약함 — 금속 몸통 + 결 + 통풍 리브 + 걸쇠 + 이름표 + 접지."""
    img = parts.slab(w, h, "metal1")
    parts.grain(img, "metal", "metal1", seed=121)
    W, H = w * TILE, h * TILE
    parts.rim(img, "metal1")
    parts.vent(img, 6, H // 2 - 6, W - 14, 4, "metal1")
    parts.handle(img, W // 2, 8, "latch")
    parts.label(img, 6, H - 12, w=10, h=6)
    parts.feet(img, "metal1")
    return img


def _sandBox(w: int = 1, h: int = 1):
    """모래함 — 상자 위로 모래가 쌓여 보이는 둔덕 + 표찰. 물자 상자와 실루엣을 가른다."""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    parts.rim(img, "conc1")
    PX.ellipse(img, W // 2, 8, 12, 7, P.W["dirt1"])
    PX.ellipse(img, W // 2, 6, 9, 5, P.W["dirt0"])
    parts.label(img, W - 12, H - 10, w=8, h=5)
    return img


# ════════════════════════════════════════════════════════════════ 천·깔개·문

def _mat(w: int = 4, h: int = 2):
    """수입 깔개 — 안감을 먼저 채우고 결을 준 다음, 테두리를 별도 띠로 두른다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 6, 8, W - 7, H - 9, P.W["olive1"])
    parts.grain(img, "fabric", "olive1", seed=131)             # 안감만 불투명한 시점 — 안전
    PX.rect(img, 2, 4, W - 3, 7, P.W["olive0"])                # 테두리 상
    PX.rect(img, 2, H - 8, W - 3, H - 5, P.W["olive0"])        # 테두리 하
    PX.rect(img, 2, 4, 5, H - 5, P.W["olive0"])                # 테두리 좌
    PX.rect(img, W - 6, 4, W - 3, H - 5, P.W["olive0"])        # 테두리 우
    for gx, gy in ((10, 10), (W - 11, 10), (10, H - 11), (W - 11, H - 11)):
        PX.rect(img, gx, gy, gx + 3, gy + 3, P.W["metal2"])    # 그로밋
    for fx in (W // 4, W // 2, 3 * W // 4):
        parts.seam(img, fx, 8, fx, H - 9, "olive1")            # 접힌 자국
    return img


def _dockDoor(w: int = 2, h: int = 1):
    """하역 출입구 — 골강판 셔터 + 흑황 경계 문턱."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 0, 0, W - 1, H - 1, P.W["metal1"])
    for y in range(2, H - 2, 4):
        PX.rect(img, 2, y, W - 3, y + 1, P.W["metal2"])
    for i, x in enumerate(range(0, W, 8)):
        PX.rect(img, x, H - 4, x + 4, H - 1, P.W["alert"] if i % 2 == 0 else P.W["snow0"])
    return img


def _barracksDoor(w: int = 2, h: int = 1):
    """생활관 출입문 — 나무 문 + 창 삽입 + 손잡이. 문지방보다 큰 문짝 실루엣."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 4, 2, W - 5, H - 1, P.W["wood1"])
    parts.grain(img, "wood", "wood1", seed=311)                # 문짝만 불투명한 시점 — 안전
    PX.rect(img, 4, 2, W - 5, 4, P.W["wood2"])
    PX.rect(img, W // 2 - 8, 6, W // 2 + 8, 16, P.W["night0"])
    parts.handle(img, W - 12, H // 2, "knob")
    return img


def _slipper(w: int = 1, h: int = 1):
    """슬리퍼 — 한 켤레를 어긋나게 배치하고 각각 다른 자리에 발등 끈을 얹는다."""
    img = PX.blank(w * TILE, h * TILE)
    PX.rect(img, 5, 15, 14, 26, P.W["olive2"])
    PX.rect(img, 6, 16, 13, 20, P.W["olive1"])
    PX.rect(img, 18, 17, 27, 28, P.W["olive2"])
    PX.rect(img, 19, 22, 26, 26, P.W["olive1"])
    return img


# ════════════════════════════════════════════════════════════════ 관측·정렬·전달

def _observationPost(w: int = 2, h: int = 2):
    """관측 지점 — 삼각대 다리 3개 + 조준경 몸통 + 렌즈 + 모래주머니 받침."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx = W // 2
    for lx in (10, cx, W - 10):
        _diag_line(img, lx, H - 2, cx, H - 34, P.W["metal2"], width=2)
    PX.rect(img, cx - 10, H - 44, cx + 10, H - 30, P.W["metal1"])
    PX.rect(img, cx - 14, H - 40, cx - 10, H - 34, P.W["night0"])
    for x in range(6, W - 6, 11):
        PX.ellipse(img, x + 5, H - 6, 5, 4, P.W["dirt2"])
    return img


def _formationLine(w: int = 4, h: int = 1):
    """대열 정렬선 — 바닥에 그은 흰 선 + 번호 눈금 + 페인트 마모."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.rect(img, 0, H // 2 - 2, W - 1, H // 2 + 1, P.W["conc0"])
    for x in range(0, W, 12):
        PX.rect(img, x, H // 2 - 5, x + 2, H // 2 + 4, P.W["conc0"])
    parts.wear(img, "conc0", seed=151, amount=15)
    return img


def _watchWindow(w: int = 4, h: int = 2):
    """감시창 — 창틀 + 세로 창살 여러 개 + 아래 선반."""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    PX.rect(img, 6, 10, W - 7, H - 24, P.W["night0"])
    PX.rect(img, 6, 10, W - 7, 12, P.W["metal1"])
    for x in range(6, W - 7, 18):
        PX.rect(img, x, 10, x + 2, H - 24, P.W["metal1"])
    PX.rect(img, 4, H - 20, W - 5, H - 14, P.W["metal2"])
    parts.rim(img, "conc1")
    return img


def _handoverDesk(w: int = 1, h: int = 1):
    """인수인계대 — 나무 책상 + 인계 장부 한 장 + 펜 + 접지."""
    img = parts.slab(w, h, "wood1")
    parts.grain(img, "wood", "wood1", seed=161)
    W, H = w * TILE, h * TILE
    parts.label(img, 6, 8, w=W - 16, h=6)
    PX.rect(img, W - 10, 6, W - 8, 14, P.W["metal2"])
    parts.feet(img, "wood1")
    return img


def _guardPhone(w: int = 1, h: int = 1):
    """초소 전화 — 벽부 전화기 몸체 + 수화기 + 코드 + 버튼."""
    img = parts.slab(w, h, "device")
    W, H = w * TILE, h * TILE
    parts.rim(img, "device")
    cx = W // 2
    PX.rect(img, cx - 6, 8, cx + 6, 12, P.W["metal2"])
    PX.rect(img, cx - 8, 6, cx - 6, 14, P.W["metal2"])
    PX.rect(img, cx + 6, 6, cx + 8, 14, P.W["metal2"])
    _diag_line(img, cx, 12, cx + 4, 20, P.W["metal1"])
    for i in range(3):
        PX.rect(img, 8 + i * 4, 22, 9 + i * 4, 23, P.W["lamp0"])
    return img


def _checkpointCounter(w: int = 3, h: int = 2):
    """검문대 — 콘크리트 카운터 + 분할 + ID 확인창 + 서류 라벨 + 접지."""
    img = parts.slab(w, h, "conc1")
    W, H = w * TILE, h * TILE
    parts.panelize(img, w, 1, "conc1")
    parts.rim(img, "conc1")
    PX.rect(img, 8, 6, W - 9, 18, P.W["night0"])
    parts.label(img, W - 24, H - 12, w=14, h=6)
    parts.feet(img, "conc1")
    return img


def _registryStand(w: int = 3, h: int = 3):
    """출입 대장 — 나무 스탠드 + 결 + 장부 3권 쌓기 + 접지. 인수인계대보다 크고 무겁게."""
    img = parts.slab(w, h, "wood1")
    parts.grain(img, "wood", "wood1", seed=231)
    W, H = w * TILE, h * TILE
    parts.rim(img, "wood1")
    for i, yoff in enumerate((10, 18, 26)):
        parts.label(img, 10 + i * 4, yoff, w=W - 40, h=6)
    parts.feet(img, "wood1")
    return img


def _cargoRack(w: int = 4, h: int = 2):
    """운반 적재대 — 나무 선반 위 상자 여러 개(이음매) + 접지."""
    img = parts.slab(w, h, "wood2")
    parts.grain(img, "wood", "wood2", seed=241)
    W, H = w * TILE, h * TILE
    for cx in range(10, W - 20, 26):
        PX.rect(img, cx, 8, cx + 20, H - 16, P.W["wood1"])
        parts.seam(img, cx + 10, 8, cx + 10, H - 16, "wood1")
    parts.rim(img, "wood2")
    parts.feet(img, "wood2")
    return img


# ════════════════════════════════════════════════════════════════ 숙영지

def _tent(w: int = 3, h: int = 3):
    """텐트 — 삼각 천 + 봉제선(실루엣 안에서만) + 입구 틈 + 팩 줄."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    bottom = H - 1
    peak = W // 2
    seam_offset = max(6, W // 6)
    for i in range(H - 6):
        half = int(i * (peak - 3) / max(1, H - 7))
        y = bottom - i
        PX.rect(img, peak - half, y, peak + half, y, P.W["olive0"])
        for sx in (peak - seam_offset, peak + seam_offset):
            if peak - half <= sx <= peak + half:
                PX.rect(img, sx, y, sx, y, P.W["olive2"])
    PX.rect(img, peak - 3, bottom - 12, peak + 2, bottom, P.W["olive2"])   # 입구 틈
    for px_ in (6, W - 6):
        _diag_line(img, peak, bottom - 10, px_, bottom, P.W["metal2"])    # 팩 줄
        PX.rect(img, px_ - 1, bottom - 2, px_ + 1, bottom, P.W["metal2"])
    return img


def _fieldKitchen(w: int = 3, h: int = 3):
    """야전 취사장 — 양옆 기둥이 보이는 열린 캐노피(텐트와 다른 실루엣) + 화덕 + 솥."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for px_ in (8, W - 8):
        PX.rect(img, px_ - 2, 10, px_ + 1, H - 1, P.W["wood2"])
    span = W // 2 - 10
    for i in range(16):
        half = int(i * span / 15)
        PX.rect(img, W // 2 - half, 10 + i, W // 2 + half, 11 + i, P.W["olive0"])
    PX.rect(img, W // 2 - 14, H - 20, W // 2 + 14, H - 6, P.W["metal1"])
    PX.rect(img, W // 2 - 6, H - 16, W // 2 + 6, H - 8, P.W["heat"])
    PX.ellipse(img, W // 2, H - 24, 8, 5, P.W["metal2"])
    return img


def _cookingKit(w: int = 1, h: int = 1):
    """야전 취사도구 — 손잡이 달린 솥 + 국자. 다른 상자류와 실루엣을 가른다."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    PX.ellipse(img, W // 2, H - 9, 10, 6, P.W["metal1"])
    PX.rect(img, W // 2 - 2, H - 17, W // 2 + 2, H - 12, P.W["metal2"])
    PX.rect(img, 6, 7, 8, 20, P.W["metal2"])
    PX.ellipse(img, 7, 6, 3, 2, P.W["metal1"])
    return img


def _choppingBlock(w: int = 2, h: int = 2):
    """모탕 — 나이테가 보이는 둥근 그루터기 + 박힌 도끼."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    cx, cy = W // 2, H // 2 + 6
    PX.ellipse(img, cx, cy, 20, 13, P.W["wood1"])
    PX.ellipse(img, cx, cy, 14, 9, P.W["wood0"])
    PX.ellipse(img, cx, cy, 7, 5, P.W["wood2"])
    PX.rect(img, cx + 10, cy - 26, cx + 13, cy - 4, P.W["metal2"])
    PX.rect(img, cx + 5, cy - 30, cx + 17, cy - 24, P.W["metal0"])
    return img


def _stakeBundle(w: int = 1, h: int = 1):
    """그늘막 팩 — 말뚝 여러 개를 노끈으로 묶은 다발."""
    img = PX.blank(w * TILE, h * TILE)
    W, H = w * TILE, h * TILE
    for off in (-6, -2, 2, 6):
        _diag_line(img, W // 2 + off - 5, H - 3, W // 2 + off + 5, 4, P.W["wood2"])
    PX.rect(img, W // 2 - 9, H // 2 - 2, W // 2 + 9, H // 2 + 1, P.W["webbing"])
    return img


BUILDERS.update({
    "국기 게양대": _flagPole,
    "안테나 마스트": _antennaMast,
    "나무": _tree,
    "토사 퇴적": _soilMound,
    "모래주머니": _sandbag,
    "차량": _vehicle,
    "초소 벽": _guardBox,
    "차단기": _barrier,
    "장애물": _hurdle,
    "철조망": _wireFence,
    "배수로": _drain,
    "물자 상자": _crate,
    "문지방": _doorway,
    "탄약함": _ammoBox,
    "모래함": _sandBox,
    "수입 깔개": _mat,
    "하역 출입구": _dockDoor,
    "생활관 출입문": _barracksDoor,
    "슬리퍼": _slipper,
    "관측 지점": _observationPost,
    "대열 정렬선": _formationLine,
    "감시창": _watchWindow,
    "인수인계대": _handoverDesk,
    "초소 전화": _guardPhone,
    "검문대": _checkpointCounter,
    "출입 대장": _registryStand,
    "운반 적재대": _cargoRack,
    "텐트": _tent,
    "야전 취사장": _fieldKitchen,
    "야전 취사도구": _cookingKit,
    "모탕": _choppingBlock,
    "그늘막 팩": _stakeBundle,
})
