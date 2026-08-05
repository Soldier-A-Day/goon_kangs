"""
소품 가구 계열 그림 (C3 — 조각 합성).

`parts.py`의 조각 어휘로 소품을 **여러 조각을 겹쳐** 만든다. 단색 사각형이던
`tiles._box()`를 대신한다.

**타일 크기(w,h)는 절대 바꾸지 마라** — `base_map.json`이 그 크기로 배치돼 있어
바꾸면 맵이 어긋난다. 이 파일은 **그리는 함수만** 갈아 끼운다.

`BUILDERS`에 등록된 이름은 `tiles.PROPS`의 같은 이름 항목의 그리는 함수를 대체한다.

## 이 파일의 지역 규칙 (parts.py 두 철칙을 그대로 물려받는다)

1. 색은 `P.W[key]` 또는 `P.neighbor(key, step)`으로만 가져온다 — 곱셈·덧셈
   계산 금지(§4 팔레트 이탈 방지).
2. `random` 대신 `_hash()`(좌표 해시, `parts._hash`·`tiles._hash`와 같은
   알고리즘 — 공유 모듈을 고치지 않고 이 파일 안에 지역 복제로 둔다).
3. 명암·그림자는 굽지 않는다 — `tiles.generate()`가 `PX.shade()`로 한 번에
   먹인다. 여기서는 "무엇이 붙어 있는가"만 그린다.
"""

from __future__ import annotations

import parts
import palette as P
import pixel as PX

TILE = parts.TILE


def _hash(x: int, y: int, salt: int = 0) -> int:
    """`parts._hash`·`tiles._hash`와 같은 알고리즘 — 좌표 해시(결정적)."""
    h = (x * 374761393 + y * 668265263 + salt * 2246822519) & 0xFFFFFFFF
    h = (h ^ (h >> 13)) * 1274126177 & 0xFFFFFFFF
    return h ^ (h >> 16)


# ════════════════════════════════════════════════════════════════ 지역 헬퍼
#
# `parts.py`는 손대지 않는다(공유 파일). 여기서 필요한 조합만 이 파일 안에
# 지역 헬퍼로 둔다 — 게시판류 5종·문 달린 상자류 3종이 공유한다.

def _paper(img, x: int, y: int, w: int = 6, h: int = 8) -> None:
    """서류 한 장. `parts.label`보다 크고, 안쪽에 인쇄선이 여러 줄 있다."""
    PX.rect(img, x, y, x + w, y + h, P.W["paper"])
    for i in range(2, h - 1, 2):
        PX.rect(img, x + 1, y + i, x + w - 1, y + i, P.W["paperLine"])


def _pin(img, x: int, y: int) -> None:
    """압정 1px."""
    PX.rect(img, x, y, x, y, P.W["alert"])


def _papers(img, x0: int, y0: int, x1: int, y1: int,
            cols: int, rows: int, seed: int, pinned: bool = True) -> None:
    """
    서류 여러 장을 격자로 붙인다 — 게시판류(코르크 판 + 압정 + 붙은 종이)의
    핵심 정체성. 칸마다 좌표 해시로 ±1px 흔들어 손으로 붙인 느낌을 낸다.
    """
    cols = max(1, cols)
    rows = max(1, rows)
    cw = max(6, (x1 - x0) // cols)
    ch = max(8, (y1 - y0) // rows)
    for r in range(rows):
        for c in range(cols):
            jx = _hash(c, r, seed) % 3 - 1
            jy = _hash(r, c, seed + 1) % 3 - 1
            px0 = x0 + c * cw + 2 + jx
            py0 = y0 + r * ch + 2 + jy
            pw = max(4, cw - 5)
            ph = max(5, ch - 5)
            if px0 + pw > x1 or py0 + ph > y1:
                continue
            _paper(img, px0, py0, pw, ph)
            if pinned and (r + c) % 2 == 0:
                _pin(img, px0 + pw // 2, py0)


def _venDoors(img, n: int, body: str, vkey: str) -> None:
    """문 n짝 — 세로 분할 + 손잡이 + 문마다 통풍살. 금속 상자류가 공유한다."""
    ww, hh = img.size
    parts.panelize(img, n, 1, body)
    for i in range(n):
        cx = ww * (2 * i + 1) // (2 * n)
        parts.handle(img, cx, hh // 2, "bar")
        x0 = ww * i // n + 4
        x1 = ww * (i + 1) // n - 4
        if x1 > x0:
            parts.vent(img, x0, 4, x1 - x0, 3, vkey)


def _plate(img, cx: float, cy: float, r: float, key: str) -> None:
    """원판(덤벨 웨이트)."""
    PX.ellipse(img, cx, cy, r, r, P.W[key])


# ═══════════════════════════════════════════════════════════════════ 침상류

def _bunk(w: int, h: int):
    """침상 = 매트리스 + 접은 모포 + 베개 + 프레임 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=12)
    PX.rect(img, 3, 5, ww - 4, hh - 4, P.W["olive1"])           # 매트리스
    parts.grain(img, "fabric", "olive1", seed=12)
    foot_w = max(8, ww // 4)
    PX.rect(img, ww - foot_w - 3, 5, ww - 4, hh - 4, P.neighbor("olive1", -1))  # 접은 모포
    PX.rect(img, 3, 5, 3 + ww // 6, hh - 4, P.W["snow0"])       # 베개
    parts.feet(img, "wood1")
    return img


def _cot(w: int, h: int):
    """처치대 = 흰 시트 + 적십자 + 금속 프레임."""
    img = parts.slab(w, h, "metal0")
    ww, hh = img.size
    parts.grain(img, "metal", "metal0", seed=16)
    PX.rect(img, 3, 5, ww - 4, hh - 5, P.W["white"])
    PX.rect(img, 5, 7, 12, 13, P.W["cross"])
    parts.feet(img, "metal0")
    return img


def _stretcherRack(w: int, h: int):
    """들것 거치대 = 금속 프레임 단마다 걸린 들것 천 — 총기 거치대와 같은 어휘."""
    img = parts.slab(w, h, "metal0")
    ww, hh = img.size
    parts.grain(img, "metal", "metal0", seed=33)
    n = max(1, h)
    for i in range(n):
        y0 = hh * i // n + 4
        y1 = hh * (i + 1) // n - 6
        if y1 <= y0:
            continue
        PX.rect(img, 4, y0, ww - 5, y0 + 2, P.W["metal2"])
        PX.rect(img, 4, y1 - 1, ww - 5, y1, P.W["metal2"])
        PX.rect(img, 5, y0 + 2, ww - 6, y1 - 1, P.W["white"])
    return img


# ═══════════════════════════════════════════════════════════════════ 상자류

def _locker(w: int, h: int):
    """관물대 = 문 2짝 + 손잡이 + 통풍살 + 이름표."""
    img = parts.slab(w, h, "metal1")
    parts.grain(img, "metal", "metal1", seed=11)
    _venDoors(img, 2, "metal1", "metal2")
    ww, hh = img.size
    parts.label(img, ww // 2 - 3, hh - 9, 6, 4)
    parts.rim(img, "metal1")
    parts.feet(img, "metal1")
    parts.wear(img, "metal1", seed=11, amount=4)
    return img


def _cabinet(w: int, h: int):
    """
    서류함 = 서랍 3단 + 손잡이 3개 + 이름표.

    몸통은 `metal1`(계열 중간 톤)로 둔다 — `metal2`(계열의 가장 어두운 끝)를
    쓰면 `panelize()`가 이음매를 한 단 더 어둡게 찍으려다 계열 끝에서 자기
    자신으로 되돌아가(§`parts.neighbor` 클램프) 서랍 경계선이 통째로
    사라진다(실측: `metal2` 몸통에서 손잡이만 떠 있고 칸이 안 보였다).
    """
    img = parts.slab(w, h, "metal1")
    ww, hh = img.size
    parts.grain(img, "metal", "metal1", seed=18)
    parts.panelize(img, 1, 3, "metal1")
    for i in range(3):
        cy = hh * (2 * i + 1) // 6
        parts.handle(img, ww // 2, cy, "bar")
    parts.label(img, 3, 2, 6, 3)
    parts.feet(img, "metal1")
    return img


def _commonCabinet(w: int, h: int):
    """공용장비함 = 문 여러 짝(폭만큼) + 통풍살 + 이름표 — 관물대의 대형판."""
    img = parts.slab(w, h, "metal1")
    ww, hh = img.size
    parts.grain(img, "metal", "metal1", seed=28)
    _venDoors(img, max(2, w), "metal1", "metal2")
    parts.label(img, ww // 2 - 4, hh - 9, 8, 4)
    parts.rim(img, "metal1")
    parts.feet(img, "metal1")
    return img


def _cleaningLocker(w: int, h: int):
    """청소도구함 = 통풍살 + 걸쇠 + 빗자루·대걸레 실루엣."""
    img = parts.slab(w, h, "metal1")
    ww, hh = img.size
    parts.grain(img, "metal", "metal1", seed=35)
    parts.vent(img, 4, 3, ww - 8, 3, "metal2")
    parts.handle(img, ww // 2, hh * 2 // 3, "latch")
    PX.rect(img, 6, 8, 7, hh - 8, P.W["wood1"])                 # 빗자루 자루
    PX.rect(img, 3, hh - 10, 11, hh - 5, P.W["olive2"])         # 빗자루 솔
    PX.rect(img, ww - 9, 8, ww - 8, hh - 8, P.W["metal0"])      # 대걸레 자루
    PX.rect(img, ww - 14, hh - 10, ww - 5, hh - 5, P.W["snow0"])  # 대걸레 천
    return img


def _toolbox(w: int, h: int):
    """공구함 = 붉은 뚜껑 + 금속 몸통 + 걸쇠 + 안쪽 트레이 칸."""
    img = parts.slab(w, h, "metal2")
    ww, hh = img.size
    parts.grain(img, "metal", "metal2", seed=36)
    PX.rect(img, 2, 2, ww - 3, hh // 2 - 1, P.W["alert"])
    parts.seam(img, 2, hh // 2, ww - 3, hh // 2 + 1, "metal2")
    parts.handle(img, ww // 2, hh // 2, "latch")
    PX.rect(img, ww // 4, hh * 3 // 4, ww * 3 // 4, hh * 3 // 4 + 1, P.W["metal1"])
    return img


# ═══════════════════════════════════════════════════════════════════ 게시판류

def _board(w: int, h: int):
    """게시판 = 코르크 판 + 압정 + 붙은 종이 여러 장."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    PX.rect(img, 3, 4, ww - 4, hh - 4, P.neighbor("wood2", -1))
    parts.grain(img, "wood", "wood2", seed=13)
    cols = max(1, (ww - 6) // 16)
    rows = max(1, (hh - 8) // 22)
    _papers(img, 4, 5, ww - 4, hh - 4, cols, rows, seed=13)
    parts.rim(img, "wood2")
    return img


def _statusBoard(w: int, h: int):
    """상황판 = 좁고 긴 코르크 판 + 세로로 쌓인 종이."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    PX.rect(img, 3, 4, ww - 4, hh - 4, P.neighbor("wood2", -1))
    parts.grain(img, "wood", "wood2", seed=22)
    rows = max(1, (hh - 8) // 24)
    _papers(img, 4, 5, ww - 4, hh - 4, 1, rows, seed=22)
    parts.rim(img, "wood2")
    return img


def _logBoard(w: int, h: int):
    """일지 보드 = 코르크 판 + 칸수만큼 붙은 일지 종이."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    PX.rect(img, 3, 4, ww - 4, hh - 4, P.neighbor("wood2", -1))
    parts.grain(img, "wood", "wood2", seed=32)
    _papers(img, 4, 5, ww - 4, hh - 4, w, h, seed=32)
    parts.rim(img, "wood2")
    return img


def _scheduleBoard(w: int, h: int):
    """일과표 게시판 = 큰 코르크 판 + 칸(요일×교시)마다 붙은 표."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    PX.rect(img, 3, 4, ww - 4, hh - 4, P.neighbor("wood2", -1))
    parts.grain(img, "wood", "wood2", seed=26)
    _papers(img, 4, 5, ww - 4, hh - 4, w, h, seed=26)
    parts.rim(img, "wood2")
    return img


def _shadowBoard(w: int, h: int):
    """섀도보드 = 페그보드 결 + 공구 실루엣(드라이버·렌치·망치)."""
    img = parts.slab(w, h, "metal1")
    ww, hh = img.size
    parts.grain(img, "mesh", "metal1", seed=30)
    dark = P.neighbor("metal1", -2)
    PX.rect(img, ww // 5 - 2, hh // 3, ww // 5 + 2, hh * 2 // 3, dark)          # 드라이버
    PX.rect(img, ww // 2 - 6, hh // 4, ww // 2 + 6, hh // 4 + 3, dark)          # 렌치 머리
    PX.rect(img, ww // 2 - 2, hh // 4 + 3, ww // 2 + 2, hh * 3 // 4, dark)      # 렌치 자루
    PX.rect(img, ww * 4 // 5 - 5, hh // 3, ww * 4 // 5 + 5, hh // 3 + 4, dark)  # 망치 머리
    PX.rect(img, ww * 4 // 5 - 1, hh // 3 + 4, ww * 4 // 5 + 1, hh * 3 // 4, dark)  # 망치 자루
    parts.rim(img, "metal1")
    return img


# ═══════════════════════════════════════════════════════════════════ 책상류

def _table(w: int, h: int):
    """식탁 = 나무 결 + 상판 널빤지 이음매 + 다리."""
    img = parts.slab(w, h, "wood0")
    ww, hh = img.size
    parts.grain(img, "wood", "wood0", seed=17)
    parts.panelize(img, max(1, w), 1, "wood0")
    parts.rim(img, "wood0")
    parts.feet(img, "wood0")
    return img


def _adminDesk(w: int, h: int):
    """행정 책상 = 서류 더미 + 사무기기(장치) + 서랍 손잡이 + 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=19)
    split = ww * 2 // 5
    cols = max(1, (split - 4) // 10)
    _papers(img, 4, 4, split, hh - 6, cols, 2, seed=19)
    PX.rect(img, ww * 3 // 5, 6, ww - 6, hh // 2, P.W["device"])
    PX.rect(img, ww * 3 // 5 + 2, 8, ww - 8, hh // 2 - 2, P.neighbor("device", 1))
    parts.seam(img, 2, hh - 8, ww - 3, hh - 8, "wood1")
    parts.handle(img, ww // 2, hh - 4, "knob")
    parts.feet(img, "wood1")
    return img


def _ledgerDesk(w: int, h: int):
    """재물 대장 = 책상 + 두꺼운 장부(종이 겹 + 책등) + 다리."""
    img = parts.slab(w, h, "wood0")
    ww, hh = img.size
    parts.grain(img, "wood", "wood0", seed=25)
    bx0, by0, bx1, by1 = ww // 2 - 12, 5, ww // 2 + 12, hh - 6
    PX.rect(img, bx0, by0, bx1, by1, P.W["paper"])
    for i in range(2, by1 - by0, 3):
        PX.rect(img, bx0 + 1, by0 + i, bx1 - 1, by0 + i, P.W["paperLine"])
    PX.rect(img, bx0 - 1, by0, bx0, by1, P.neighbor("wood0", -1))
    parts.feet(img, "wood0")
    return img


def _personnelDesk(w: int, h: int):
    """인원 대장 = 책상 + 명부 바인더 여러 권(세로 책등) + 서류."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=27)
    n = 4
    bw = 6
    for i in range(n):
        x = 4 + i * (bw + 2)
        key = "olive1" if i % 2 == 0 else "cold"
        PX.rect(img, x, 4, x + bw, hh - 6, P.W[key])
    PX.rect(img, ww // 2 + 4, 6, ww - 6, hh - 8, P.W["paper"])
    parts.feet(img, "wood1")
    return img


def _requisitionDesk(w: int, h: int):
    """청구 데스크 = 카운터 턱(상판) + 접수 서류 + 걸쇠(창구 잠금) + 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=29)
    PX.rect(img, 3, 3, ww - 4, hh // 2 - 2, P.neighbor("wood1", 1))
    _papers(img, 4, hh // 2, ww * 3 // 5, hh - 6, 2, 1, seed=29)
    parts.handle(img, ww - 8, hh - 6, "latch")
    parts.feet(img, "wood1")
    return img


def _pcDesk(w: int, h: int):
    """PC 책상 = 모니터 2대(장치+화면) + 서류 한 장 + 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=31)
    mon_w = 14
    for i in range(2):
        x0 = 8 + i * (mon_w + 10)
        PX.rect(img, x0, 3, x0 + mon_w, 16, P.W["device"])
        PX.rect(img, x0 + 2, 5, x0 + mon_w - 2, 14, P.W["cold"])
    PX.rect(img, ww - 22, 6, ww - 6, 20, P.W["paper"])
    parts.feet(img, "wood1")
    return img


# ═══════════════════════════════════════════════════════════════════ 그 밖의 정체성

def _lectern(w: int, h: int):
    """강단 = 경사진 상판(밝은 톤) + 원고 + 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=20)
    PX.rect(img, 2, 2, ww - 3, 10, P.neighbor("wood1", 1))
    PX.rect(img, 6, 4, ww - 7, 8, P.W["paper"])
    parts.feet(img, "wood1")
    return img


def _seat(w: int, h: int):
    """좌석 = 등받이 쿠션 + 좌판 + 다리."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    PX.rect(img, 4, 3, ww - 5, hh // 2, P.W["olive1"])
    parts.grain(img, "fabric", "olive1", seed=21)
    PX.rect(img, 3, hh // 2 + 2, ww - 4, hh - 6, P.neighbor("olive1", -1))
    parts.feet(img, "wood1")
    return img


def _assemblySeating(w: int, h: int):
    """집합 좌석 = 여러 줄의 좌석 띠(교대 명암) — 대형 정렬석."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    parts.grain(img, "wood", "wood2", seed=24)
    rows = max(1, h * 2)
    for r in range(rows):
        y0 = hh * r // rows + 2
        y1 = hh * (r + 1) // rows - 4
        if y1 <= y0:
            continue
        # `wood2`는 wood 계열의 가장 어두운 끝이라 `P.neighbor(wood2, -1)`은
        # 더 갈 곳이 없어 자기 자신으로 클램프된다(§4.2 계열 클램프) — 그러면
        # 줄무늬가 안 보여 그냥 네모가 된다(실측: 렌더 확인). 계열 안에서
        # 이미 등록된 다른 톤(`wood1`)을 직접 골라 확실히 갈라 보이게 한다.
        color = P.W["wood2"] if r % 2 == 0 else P.W["wood1"]
        PX.rect(img, 3, y0, ww - 4, y1, color)
    parts.rim(img, "wood2")
    return img


def _shelf(w: int, h: int):
    """물자 선반 = 선반 단(이음매) + 칸마다 보급품 상자."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    parts.grain(img, "wood", "wood2", seed=14)
    shelves = 5
    for i in range(1, shelves):
        # `parts.seam(..., "wood2")`는 몸통과 같은(계열 가장 어두운 끝) 색이라
        # 클램프로 안 보인다 — 계열 안의 다른 톤(`wood1`)을 직접 찍어 단을 낸다.
        y = hh * i // shelves
        PX.rect(img, 1, y, ww - 2, y + 1, P.W["wood1"])
    for i in range(shelves):
        y0 = hh * i // shelves + 3
        y1 = hh * (i + 1) // shelves - 3
        if y1 <= y0:
            continue
        for c in range(max(1, w)):
            x0 = ww * c // w + 3
            x1 = ww * (c + 1) // w - 3
            if x1 <= x0:
                continue
            key = "olive2" if (i + c) % 2 == 0 else "metal1"
            PX.rect(img, x0, y0, x1, y1, P.W[key])
    parts.rim(img, "wood2")
    parts.feet(img, "wood2")
    return img


def _rack(w: int, h: int):
    """총기 거치대 = 세로 칸막이 + 거치된 총열 실루엣(총열+개머리판)."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    parts.grain(img, "wood", "wood2", seed=15)
    cols = max(2, w * 2)
    for i in range(1, cols):
        # 몸통이 `wood2`(계열 가장 어두운 끝)라 `parts.seam`의 클램프로 칸막이가
        # 안 보인다 — 직접 다른 톤을 찍어 세로 칸막이를 낸다.
        x = ww * i // cols
        PX.rect(img, x, 1, x, hh - 2, P.W["wood1"])
    for i in range(cols):
        cx = ww * (2 * i + 1) // (2 * cols)
        PX.rect(img, cx - 1, 3, cx + 1, hh - 5, P.W["metal0"])       # 총열
        PX.rect(img, cx - 2, hh - 10, cx + 2, hh - 5, P.W["wood0"])  # 개머리판
    parts.rim(img, "wood2")
    return img


def _slipperShelf(w: int, h: int):
    """슬리퍼 선반 = 칸막이 큐비클 + 놓인 슬리퍼(번갈아 색)."""
    img = parts.slab(w, h, "wood1")
    ww, hh = img.size
    parts.grain(img, "wood", "wood1", seed=23)
    cubbies = max(2, w * 2)
    for i in range(1, cubbies):
        x = ww * i // cubbies
        parts.seam(img, x, 4, x, hh - 3, "wood1")
    for i in range(cubbies):
        if i % 2:
            continue
        x0 = ww * i // cubbies + 3
        x1 = ww * (i + 1) // cubbies - 3
        if x1 <= x0:
            continue
        key = "accent" if i % 4 == 0 else "cold"
        PX.ellipse(img, (x0 + x1) / 2, hh - 8, (x1 - x0) / 2, 3, P.W[key])
    return img


def _dumbbellRack(w: int, h: int):
    """덤벨 거치대 = 선반 단 + 단마다 늘어선 덤벨(막대 손잡이 + 양끝 원판)."""
    img = parts.slab(w, h, "wood2")
    ww, hh = img.size
    parts.grain(img, "wood", "wood2", seed=34)
    shelves = 2
    n = max(2, w)
    for s in range(shelves):
        y = hh * (s + 1) // (shelves + 1)
        # 몸통(`wood2`)과 같은 색으로 이음매를 찍으면 클램프로 안 보이므로
        # 계열 안 다른 톤을 직접 찍어 선반 단을 낸다.
        PX.rect(img, 1, y - 1, ww - 2, y, P.W["wood1"])
        r = 3
        half = r + 4
        cy = max(r + 2, y - half - 2)
        for i in range(n):
            cx = ww * (2 * i + 1) // (2 * n)
            # 손잡이 바를 원판 지름보다 길게 그려야 원판 두 개 사이에서 보인다
            # (전에는 원판 반지름과 바 길이가 같아서 원판이 바를 통째로 덮었다).
            PX.rect(img, cx - half, cy - 1, cx + half, cy + 1, P.W["metal0"])
            _plate(img, cx - half, cy, r, "metal1")
            _plate(img, cx + half, cy, r, "metal1")
    return img


def _dryingRack(w: int, h: int):
    """건조대 = 상단 바 + 널린 옷(번갈아 색) + 지지대 다리."""
    img = PX.blank(w * TILE, h * TILE)
    ww, hh = img.size
    PX.rect(img, 2, 2, ww - 3, 4, P.W["metal1"])
    xs = [max(6, ww * (i + 1) // 6) for i in range(4)]
    for i, x in enumerate(xs):
        key = "olive1" if i % 2 == 0 else "conc1"
        PX.rect(img, x - 3, 5, x + 3, hh - 4, P.W[key])
    PX.rect(img, 3, hh - 3, 6, hh - 1, P.W["metal2"])
    PX.rect(img, ww - 7, hh - 3, ww - 4, hh - 1, P.W["metal2"])
    return img


#: 소품 이름 → 그리는 함수 `f(w, h) -> Image`. 비어 있으면 기존 그림이 그대로 쓰인다
BUILDERS: dict[str, object] = {
    "관물대": _locker,
    "침상": _bunk,
    "게시판": _board,
    "물자 선반": _shelf,
    "총기 거치대": _rack,
    "처치대": _cot,
    "식탁": _table,
    "서류함": _cabinet,
    "행정 책상": _adminDesk,
    "강단": _lectern,
    "좌석": _seat,
    "상황판": _statusBoard,
    "슬리퍼 선반": _slipperShelf,
    "집합 좌석": _assemblySeating,
    "재물 대장": _ledgerDesk,
    "일과표 게시판": _scheduleBoard,
    "인원 대장": _personnelDesk,
    "공용장비함": _commonCabinet,
    "청구 데스크": _requisitionDesk,
    "섀도보드": _shadowBoard,
    "PC 책상": _pcDesk,
    "일지 보드": _logBoard,
    "들것 거치대": _stretcherRack,
    "덤벨 거치대": _dumbbellRack,
    "건조대": _dryingRack,
    "청소도구함": _cleaningLocker,
    "공구함": _toolbox,
}
