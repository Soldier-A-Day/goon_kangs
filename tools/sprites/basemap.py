"""
부대 본영 맵 오소링 (SAD-ART-002 PLAN 01~04).

도면이 배치를 확정했으므로 **손좌표로 깐다.** 이전 판은 구역 크기만 주고 흐름
배치로 자리를 잡았는데, 그건 도면이 없을 때의 방법이다. PLAN 01이 어느 동이
어디에 서는지 그려버린 이상 그 그림이 곧 진실이고, 자동 배치는 그것과 어긋날
자유만 만든다.

**단위: 1타일 = 0.5m** (PLAN 01 축척 규칙). 캐릭터 어깨너비가 한 타일이고,
최장 대각 110타일을 이동 10타일/초로 약 11초에 지난다 — 그게 §6.1의 동선 비용을
실제 걷기로 치르게 하는 예산이다.

구조가 세 겹이다.

  **동(棟)**   외벽으로 둘러싸인 덩어리. 야외로 통하는 출입구를 갖는다
  **복도**     동 안에서 방들을 잇는 이동 허브 (PLAN 02-D · 폭 4타일)
  **방**       복도로 통하는 문을 하나씩 갖는다. 방 하나가 곧 구역이다

문은 장식이 아니라 **유일한 통로**다. 벽을 두르고 문만 비우면, 플레이어는
복도를 지나 걸어가는 것 말고는 다른 방에 갈 방법이 없다.
"""

from __future__ import annotations

import json
import os

import tiles as T
import trainmap as TR

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))

TILE = T.TILE

#: 부대 본영. 훈련 맵은 이 오른쪽에 이어 붙는다(`trainmap.py`)
BASE_W = 110
BASE_H = 96

#: 월드 전체. 훈련 맵까지 **같은 심리스 월드**에 둔다 — 씬을 가르면 카메라·
#: HUD·파티클·시야 차단을 전부 두 번 세워야 하고, 그 배선이 훈련 맵 그리는
#: 일보다 크다. §6.4가 씬을 나눈 것은 3D 시절 메모리 예산 때문이었다
WIDTH = 244
HEIGHT = 224

#: PLAN 02-D — 복도 폭 4타일(2m)
CORRIDOR = 4
#: 문 폭. 2타일이면 두 사람이 스치듯 지나간다
DOOR_W = 2

#: 정문 — 남쪽 철조망(`build()`)에서 걷어내는 구간. 위병소(Z18)의 문 등록
#: (`_outdoor`)도 이 값을 그대로 쓴다 — 두 곳에 각자 적으면 반드시 어긋난다
GATE_X0, GATE_X1 = 52, 56          # 정문 폭 5칸 — 4인이 함께 지나갈 만큼


# ════════════════════════════════════════════════════════════════════ 방 정의
#
# (id, 이름, 바닥, 문이 난 방향, 소품)
#
# **방 하나가 곧 서버 구역이다.** 예전에는 도면이 방 25개를 들고 sim은 8구역만
# 알아서, 둘을 잇는 매핑을 여기 적어뒀다. 같은 사실이 두 곳에 살면 반드시
# 어긋나고 — 실제로 퀘스트 17건이 어긋나 있었다 — 그래서 sim 쪽을 방 단위로
# 쪼갰다. 이제 여기 `id`가 곧 `packages/sim/data/zones.json`의 구역 id이고,
# 아래 `_assert_matches_sim`이 두 파일이 같은 세계를 말하는지 매번 검사한다.
#
# 규칙이 먼저고 그림이 따른다(ARCH-02) — 구역이 있고 없고는 sim이 정하며,
# 여기서 하는 일은 그 구역이 실제로 걸어 다닐 수 있는 모양을 갖추는 것뿐이다.


def room(rid, name, x, y, w, h, floor, door, props):
    return dict(id=rid, name=name, x=x, y=y, w=w, h=h,
                floor=floor, door=door, props=props)


def row(x0, x1, y, h, cells):
    """
    동 안의 한 줄을 방으로 나눈다.

    **이웃한 방은 벽 한 장을 공유하고, 양 끝 방은 동 외벽을 제 벽으로 쓴다.**
    이게 이 함수가 존재하는 이유다 — 방마다 좌표를 손으로 주면 각자 제 벽을
    두르게 되고, 동 외벽 안쪽에 벽이 한 겹 더 생겨 배치도가 "벽 안에 또 벽"이
    된다. 실제 건물은 그렇게 지어지지 않는다.

    `x0`·`x1`은 **동 외벽의 좌표**다(안쪽이 아니라). 방 사각형은 자기 벽을
    포함하므로 이웃과 경계 타일에서 만난다.

    `cells`는 (id, 이름, 폭 가중치, 바닥, 문 방향, 소품) 튜플이다.
    """
    total = sum(c[2] for c in cells)
    span = x1 - x0
    out, at = [], x0

    for index, (rid, name, weight, floor, door, props) in enumerate(cells):
        # 마지막 칸은 남은 곳을 다 먹는다 — 반올림 오차가 동 끝에 틈을 남기면
        # 그 자리가 벽도 방도 아닌 칸이 된다
        end = x1 if index == len(cells) - 1 else at + max(2, round(span * weight / total))
        out.append(room(rid, name, at, y, end - at + 1, h, floor, door, props))
        at = end

    return out


# ── 생활관동 (PLAN 01: 54×24 · PLAN 02) ──
#
# 복도가 가운데를 가로지르고 위아래로 방이 붙는다. PLAN 02-D의 그림 그대로다.
# 생활관은 16×12(실척 8m×6m)이고 그 방 하나가 곧 구역 Z01 — "카메라가 여기서 고정된다".

BARRACKS_X, BARRACKS_Y = 16, 5
BARRACKS_W, BARRACKS_H = 56, 30
_CORR_Y = BARRACKS_Y + 13          # 복도 상단 (동 로컬 13)

_BARRACKS_X1 = BARRACKS_X + BARRACKS_W - 1
_BARRACKS_Y1 = BARRACKS_Y + BARRACKS_H - 1

BARRACKS_ROOMS = [
    # 북쪽 열 — 생활관 3실. 우리 분대 방이 첫 칸이다.
    # 위 벽은 동 외벽, 아래 벽은 복도와 공유한다
    *row(BARRACKS_X, _BARRACKS_X1, BARRACKS_Y, _CORR_Y - BARRACKS_Y, [
        ("Z01", "생활관 (1분대)", 1, "wood", "south",
         ["침상", "침상", "침상", "침상", "관물대", "관물대", "관물대", "수입 깔개", "상황판"]),
        # 2·3분대는 **우리 일과가 없는 방**이다. 한때 `locked`로 잠갔다고
        # 적어뒀지만 문을 뚫는 코드가 그 값을 보지도 않아 실제로는 늘 열려
        # 있었고, 그 플래그를 믿고 "잠겼다"고 말하는 곳만 늘었다.
        # 안 쓰는 플래그는 거짓말이 되므로 지웠다 — 들어갈 수 있고, 할 일이 없다.
        ("Z01b", "생활관 (2분대)", 1, "wood", "south",
         ["관물대", "관물대", "침상", "침상"]),
        ("Z01c", "생활관 (3분대)", 1, "wood", "south",
         ["관물대", "관물대", "침상", "침상"]),
    ]),

    # 남쪽 열 — 2004년 이후 생활관동 내부로 통합된 편의시설 (PLAN 04 "흡수 3").
    # PLAN 04 "이동 1" — 무기고는 생활관동 안, 탄약고는 외곽. 총기와 탄약은 분리 보관
    *row(BARRACKS_X, _BARRACKS_X1, _CORR_Y + CORRIDOR,
         _BARRACKS_Y1 - (_CORR_Y + CORRIDOR) + 1, [
        ("Z03", "공용 세면장 · 샤워장", 12, "tile", "north",
         ["거울", "세면대", "세면대", "샤워 칸", "샤워 칸", "변기칸", "변기칸", "변기칸", "약제함", "바닥 배수구"]),
        ("Z13", "세탁 · 건조실", 11, "tile", "north",
         ["세탁기", "세탁기", "건조대", "건조대", "세탁물 수령대"]),
        ("Z16", "사지방", 12, "tile", "north",
         ["좌석", "좌석", "좌석", "좌석", "집합 좌석"]),
        ("Z17", "체력단련장", 9, "concrete", "north", ["러닝머신", "덤벨 거치대", "케이블 머신"]),
        ("Z09", "무기고 (통제구역)", 8, "concrete", "north", ["총기 거치대", "총기 거치대", "재물 대장"]),
    ]),
]

# ── 급양동 (PLAN 03) ──
#
# 배식 동선은 단방향이다 — 입장 → 배식대 → 지정석 → 퇴식구 → 퇴장.
# 되돌아가는 경로가 없다는 것이 도면의 설계 의도라, 식당과 취사장을 따로 둔다.

MESS_X, MESS_Y = 78, 5
MESS_W, MESS_H = 26, 34

_MESS_CORR = MESS_Y + 17

MESS_ROOMS = [
    *row(MESS_X, MESS_X + MESS_W - 1, MESS_Y, _MESS_CORR - MESS_Y, [
        ("Z07", "병사식당", 1, "tile", "south",
         ["배식대", "식탁", "식탁", "식탁", "퇴식구", "잔반통", "분리수거함"]),
    ]),
    *row(MESS_X, MESS_X + MESS_W - 1, _MESS_CORR + CORRIDOR,
         (MESS_Y + MESS_H - 1) - (_MESS_CORR + CORRIDOR) + 1, [
        ("Z07b", "취사장", 1, "tile", "north",
         ["취반기", "세척 컨베이어", "부식고", "급수통", "시약대"]),
    ]),
]

# ── 지원동 (PLAN 04-A) — 보직 전용 구역 3종 ──

# 급양동 아래로 **다섯 칸 띄운다.** 예전에는 y가 한 칸 겹쳐서 두 동이 맞붙었고,
# 지도에서 동 이름이 서로 위에 찍혔다 — 이름은 상자 위에 얹히므로 동 사이에
# 글자 한 줄이 들어갈 자리가 없으면 반드시 겹친다
SUPPORT_X, SUPPORT_Y = 78, 44
SUPPORT_W, SUPPORT_H = 26, 27

_SUPPORT_CORR = SUPPORT_Y + 12

SUPPORT_ROOMS = [
    *row(SUPPORT_X, SUPPORT_X + SUPPORT_W - 1, SUPPORT_Y,
         _SUPPORT_CORR - SUPPORT_Y, [
        ("Z05", "의무실", 1, "tile", "south",
         ["약품장", "처치대", "처치대", "들것 거치대", "폐기함", "위생 검수대"]),
        ("Z06", "통신실", 1, "tile", "south", ["무전 콘솔", "일지 보드", "배터리함"]),
    ]),
    *row(SUPPORT_X, SUPPORT_X + SUPPORT_W - 1, _SUPPORT_CORR + CORRIDOR,
         (SUPPORT_Y + SUPPORT_H - 1) - (_SUPPORT_CORR + CORRIDOR) + 1, [
        ("Z04", "행정반 · CP", 1, "tile", "north",
         ["행정 책상", "서류함", "일과표 게시판", "인원 대장"]),
    ]),
]

# ── 보급 · 정비동 (PLAN 03-C) ──

SUPPLY_X, SUPPLY_Y = 5, 51
SUPPLY_W, SUPPLY_H = 22, 27

_SUPPLY_CORR = SUPPLY_Y + 12

SUPPLY_ROOMS = [
    *row(SUPPLY_X, SUPPLY_X + SUPPLY_W - 1, SUPPLY_Y,
         _SUPPLY_CORR - SUPPLY_Y, [
        ("Z08", "보급 창고", 1, "concrete", "south",
         ["물자 선반", "물자 선반", "물자 선반", "공용장비함", "유류통", "유류통", "청구 데스크", "하역 출입구", "배터리함"]),
    ]),
    *row(SUPPLY_X, SUPPLY_X + SUPPLY_W - 1, _SUPPLY_CORR + CORRIDOR,
         (SUPPLY_Y + SUPPLY_H - 1) - (_SUPPLY_CORR + CORRIDOR) + 1, [
        ("Z10", "정비고 · 차량", 1, "concrete", "north",
         ["차량", "공구함", "섀도보드", "발전기", "유량계", "고압 세척기"]),
    ]),
]

#: 동 목록 — (id, 이름, x, y, w, h, 복도 y(로컬, 없으면 None), 방들, 출입구 변)
BUILDINGS = [
    dict(id="B1", name="생활관동", x=BARRACKS_X, y=BARRACKS_Y, w=BARRACKS_W, h=BARRACKS_H,
         # PLAN 01은 정문을 남쪽 중앙에 그렸지만, 그 자리에서 복도까지 가려면
         # 남쪽 방을 관통해야 한다. 복도 끝으로 옮기고 도로를 그쪽으로 돌린다
         # 복도 양끝이 다 열린다 — 한쪽만 열면 반대편 방에서 나가는 데
         # 복도를 통째로 가로질러야 한다
         corridor=13, rooms=BARRACKS_ROOMS, entrances=("west", "east"), wall="interior",
         corridorZone="Z02",
         exitLabel="밖으로"),
    dict(id="B2", name="급양동", x=MESS_X, y=MESS_Y, w=MESS_W, h=MESS_H,
         corridor=17, rooms=MESS_ROOMS, entrances=("west",), wall="interior",
         corridorZone="Z20",
         exitLabel="밖으로"),
    dict(id="B3", name="지원동", x=SUPPORT_X, y=SUPPORT_Y, w=SUPPORT_W, h=SUPPORT_H,
         corridor=12, rooms=SUPPORT_ROOMS, entrances=("west",), wall="interior",
         corridorZone="Z21",
         exitLabel="밖으로"),
    dict(id="B4", name="보급 · 정비동", x=SUPPLY_X, y=SUPPLY_Y, w=SUPPLY_W, h=SUPPLY_H,
         corridor=12, rooms=SUPPLY_ROOMS, entrances=("east",), wall="utility",
         corridorZone="Z22",
         exitLabel="밖으로"),
]

#: 복도에 놓이는 물건 (files-6 FIELD 01). 복도는 방 목록에 없어서 따로 든다
CORRIDOR_PROPS = {
    "Z02": ["청소도구함", "게시판", "슬리퍼 선반"],
}

#: 야외 구역 — 벽이 없다. 경계만 있고 걸어서 그냥 들어간다
OUTDOOR = [
    dict(id="Z11", name="연병장", x=30, y=51, w=46, h=30,
         floor="drill", props=["국기 게양대", "나무", "나무", "나무", "배수로", "배수로", "모래함", "안테나 마스트", "급전선 단자", "관측 지점",
                "대열 정렬선"]),
    dict(id="Z12", name="초소 1 (북서)", x=5, y=8, w=10, h=8,
         floor="concrete", props=["초소 벽", "감시창", "인수인계대", "초소 전화", "모래주머니", "모래주머니"], fenced=True),
    dict(id="Z12b", name="초소 2 (남서)", x=5, y=84, w=10, h=8,
         floor="concrete", props=["초소 벽", "철조망", "철조망", "모래주머니"], fenced=True),
    # PLAN 01 — 위병소는 연병장 쪽으로 뚫려 있다. 정문이 부대 안으로 이어지는 곳이다
    #
    # **위병소가 정문을 덮는다.** h를 7→9로 늘려 위병소 구역의 남쪽 끝이 정문
    # 철조망 행(y=94, `GATE_X0..GATE_X1`)과 정확히 맞물리게 한다. `open_fence(1)`이
    # 두르는 바깥 담장(패딩 1)의 남쪽 변도 같은 y=94에 서게 되고, 그 담장의
    # 서·동 벽(x=45·x=62)이 북쪽 문(연병장 쪽)부터 정문까지 통째로 막아 정문
    # 옆을 돌아가는 길을 끊는다 — 밖으로 나가려면 위병소 북쪽 문으로 들어와
    # 안을 가로질러 정문으로 나가는 것 말고는 방법이 없다.
    dict(id="Z18", name="정문 위병소", x=46, y=85, w=16, h=9,
         floor="concrete", props=["초소 벽", "차단기", "검문대", "출입 대장", "물자 상자"],
         fenced=True, gate="north", openGate=8),
    # PLAN 04-B — 생활관동에서 가장 먼 지점. 편도 이동만 약 8초
    dict(id="Z19", name="탄약고 (외곽 격리)", x=88, y=82, w=16, h=8,
         floor="concrete", props=["탄약함", "탄약함", "재물 대장", "운반 적재대", "초소 벽"], fenced=True, double=True,
         gate="west"),
    # 벽으로 둘러싸인 단독 건물이라 야외 목록에 있어도 **실내**다 —
    # §5.0 열원 판정이 이 값에 걸려 있고, 극혹한에 여기로 뛰어드는 것이
    # 서쪽 끝에서 얼지 않는 유일한 방법이다
    dict(id="Z14", name="보일러실", x=4, y=28, w=12, h=10,
         floor="concrete", props=["보일러 본체", "압력계", "비상 발전기", "유류통", "유류통", "차단 밸브", "차단기", "배관 분기", "배관 분기",
                "배관 분기", "난로"],
         walled=True, indoor=True),
    # 훈련장 — 도면(PLAN 01~04)에는 없지만 sim이 아는 구역이다. 일과 6건이
    # 여기서 벌어지므로 없으면 그 하루가 성립하지 않는다.
    #
    # 생활관동과 연병장 사이의 긴 띠에 놓는다. 남은 땅 중 연병장에 맞닿은
    # 가장 큰 자리이기도 하고, 장애물 코스는 원래 길고 좁다.
    #
    # 위아래로 **네 칸씩 띄운다.** 붙여두면 지도에서 연병장과 한 덩어리로
    # 보이고, 이름 둘이 같은 줄에 찍힌다.
    dict(id="Z50", name="훈련장", x=18, y=39, w=54, h=8,
         floor="dirt", props=["텐트", "텐트", "야전 취사도구", "모탕", "그늘막 팩", "장애물", "장애물", "모래주머니", "강단"]),
]


class Grid:
    """타일 격자 하나. 레이어별로 하나씩 둔다."""

    def __init__(self, w: int, h: int):
        self.w, self.h = w, h
        self.cells: dict[tuple[int, int], str] = {}

    def fill(self, x0: int, y0: int, w: int, h: int, value: str) -> None:
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                if 0 <= x < self.w and 0 <= y < self.h:
                    self.cells[(x, y)] = value

    def fill_floor(self, x0: int, y0: int, w: int, h: int, kind: str) -> None:
        """
        바닥 전용 채우기 — 칸마다 좌표 해시로 variant를 고른다 (D-2 반복 깨기).

        `fill()`로 사각형을 통째로 같은 문자열로 채우면 Unity 타일맵에 같은
        32×32 비트맵이 그대로 반복돼 복사기로 찍은 것처럼 보인다
        (BENCHMARK §3). variant가 없는 종류(`kind`가 `tiles.FLOOR_VARIANTS`에
        없음)는 `T.floor_variant()`가 그대로 돌려주므로 `fill()`과 동작이
        같다 — 안전하게 모든 바닥 채우기에 대신 쓸 수 있다.
        """
        for y in range(y0, y0 + h):
            for x in range(x0, x0 + w):
                if 0 <= x < self.w and 0 <= y < self.h:
                    self.cells[(x, y)] = f"floor:{T.floor_variant(kind, x, y)}"

    def set(self, x: int, y: int, value: str) -> None:
        if 0 <= x < self.w and 0 <= y < self.h:
            self.cells[(x, y)] = value

    def set_floor(self, x: int, y: int, kind: str) -> None:
        """`fill_floor()`의 칸 하나짜리 버전 — 문지방·복도 이음매처럼 낱개로 깔 때"""
        self.set(x, y, f"floor:{T.floor_variant(kind, x, y)}")

    def get(self, x: int, y: int) -> str | None:
        return self.cells.get((x, y))

    def clear(self, x: int, y: int) -> None:
        self.cells.pop((x, y), None)

    def outline(self, x0: int, y0: int, w: int, h: int, value: str) -> None:
        for x in range(x0, x0 + w):
            self.set(x, y0, value)
            self.set(x, y0 + h - 1, value)
        for y in range(y0, y0 + h):
            self.set(x0, y, value)
            self.set(x0 + w - 1, y, value)

    def dump(self) -> list[dict]:
        """(값 → 좌표 목록)으로 접는다. 셀마다 한 줄씩 쓰면 JSON이 수 MB가 된다."""
        packed: dict[str, list[int]] = {}
        for (x, y), value in sorted(self.cells.items(), key=lambda kv: (kv[0][1], kv[0][0])):
            packed.setdefault(value, []).extend((x, y))
        return [{"tile": k, "cells": v} for k, v in sorted(packed.items())]


# ═══════════════════════════════════════════════════════════════════════ 문

def door_span(rect: dict, side: str) -> list[tuple[int, int]]:
    """
    문이 뚫릴 칸들. 변의 한가운데를 `DOOR_W`만큼 비운다.

    문은 **유일한 통로**다. 여기서 비운 칸이 곧 그 방에 들어갈 수 있는 유일한
    자리이고, 나머지는 벽으로 막혀 있다.
    """
    x, y, w, h = rect["x"], rect["y"], rect["w"], rect["h"]
    cx = x + w // 2 - DOOR_W // 2
    cy = y + h // 2 - DOOR_W // 2

    if side == "north":
        return [(cx + i, y) for i in range(DOOR_W)]
    if side == "south":
        return [(cx + i, y + h - 1) for i in range(DOOR_W)]
    if side == "west":
        return [(x, cy + i) for i in range(DOOR_W)]
    return [(x + w - 1, cy + i) for i in range(DOOR_W)]


def _outward(side: str) -> tuple[int, int]:
    return {"north": (0, -1), "south": (0, 1), "west": (-1, 0), "east": (1, 0)}[side]


# ═══════════════════════════════════════════════════════════════════ 맵 조립

def _check_spots(zones: list[dict], props: list[dict]) -> None:
    """
    일과가 붙을 물건이 그 구역에 실제로 놓였는가.

    `quests.json`이 일과마다 **어느 물건 앞에서 하는가**(`spot`)를 들고 있다.
    예전에는 클라가 일과 **이름**을 보고 방 안의 아무 물건을 골랐고, 그래서
    관물대 정돈과 복도 정돈이 같은 자리에서 벌어졌다. 지금은 데이터가 정하는데,
    그러면 **없는 물건을 가리킬 수 있다** — 그때 표식은 조용히 방 한가운데로
    떨어지고 아무도 원인을 못 찾는다. 그래서 굽는 자리에서 막는다.
    """
    placed: dict[str, set[str]] = {}
    for prop in props:
        placed.setdefault(prop["zone"], set()).add(prop["name"])

    quests = json.load(open(os.path.join(ROOT, "packages/sim/data/quests.json"),
                            encoding="utf-8"))
    rows = [q for lst in list(quests["role"].values())
            + [quests["chores"], quests["surprise"]] for q in lst]

    broken = [f'{q["id"]}: {q["zone"]}에 "{q["spot"]}"이 없다'
              for q in rows
              if q.get("spot") and q["spot"] not in placed.get(q["zone"], set())]
    if broken:
        raise SystemExit("[맵] 수행 지점이 놓이지 않았다:\n  " + "\n  ".join(broken))


def build() -> dict:
    ground = Grid(WIDTH, HEIGHT)
    deco = Grid(WIDTH, HEIGHT)
    walls = Grid(WIDTH, HEIGHT)

    # 바탕 — 부대 안은 전부 흙이고 그 위에 건물과 도로가 얹힌다
    ground.fill_floor(0, 0, WIDTH, HEIGHT, "dirt")

    zones: list[dict] = []
    props: list[dict] = []
    doors: list[dict] = []
    # 문·출입구·게이트가 뚫어놓은 칸 — C1 남향 벽 승격에서 반드시 제외한다.
    # 실제로 벽을 지우는 자리마다 여기 적립하고, 승격 단계는 이 표만 본다
    # (문 목록 `doors`는 통로 한쪽만 기록하는 곳이 있어 그것만으론 못 믿는다)
    protected: set[tuple[int, int]] = set()

    _outdoor(ground, walls, zones, props, doors, protected)

    for building in BUILDINGS:
        _building(building, ground, walls, zones, props, doors, protected)

    # 보일러실은 어느 동에도 속하지 않는 단독 건물이라 위 루프가 문을 안 낸다.
    # 두 동(여기선 야외 소품과 B1)이 다 그려진 뒤에 불러야 벽이 안 덮인다
    _connect_boiler_to_washroom(ground, walls, doors, protected)

    # 외곽 철조망 — PLAN 01. **부대만** 두른다. 훈련장은 그 밖이고,
    # 정문(Z18)으로 나가서 간다 — 사격장도 숙영지도 원래 위병소를 지나 간다
    walls.outline(1, 1, BASE_W - 2, BASE_H - 2, "wall:fence")

    # **정문을 실제로 뚫는다.** 위 주석이 "정문(Z18)으로 나가서 간다"고 적어 놓고도
    # 철조망에 구멍이 없었다 — 그래서 훈련장 10곳(사격장·유격장·행군로·숙영지 …)이
    # 걸어서 도달 불가였고, D-03 사격훈련부터 진행이 막혔다(사용자 신고).
    # 위병소 Z18(x46~62)의 가운데를 남쪽 철조망에서 걷어낸다.
    gate_y = BASE_H - 2                # `outline`이 그린 남쪽 변
    for gx in range(GATE_X0, GATE_X1 + 1):
        walls.clear(gx, gate_y)
        protected.add((gx, gate_y))    # 남향 벽 두껍게 등 후속 처리가 다시 막지 않게

    _training(ground, walls, zones, props, doors, protected)

    # 위병소 → 훈련장 7곳 전부를 잇는 길. `_training` 뒤에 불러야 한다 —
    # 각 훈련장의 남쪽 문(door_span)이 그 안에서 뚫리고, 길이 그 문턱까지
    # 덮어써야 이어진다
    roads = _road(ground)
    road_cells = _road_cells(roads)

    # **벽은 한 줄로 둔다.** 남향 벽을 2타일로 두껍게 해 본 적이 있다(캐릭터가
    # 1타일이라 벽 앞에 서면 벽을 통째로 가리는 문제 때문). 통행 폭이 줄고 화면이
    # 답답해져 사용자가 되돌리라고 했다. 구현은 git 이력에 있다
    # (`_thicken_south_walls`, "남향 벽을 2타일로" 커밋).

    snow = _snow(zones, walls)

    _check_spots(zones, props)

    _assert_no_overlap(zones)
    _assert_reachable(BUILDINGS)
    _assert_doors_meet_corridor(BUILDINGS)
    _assert_matches_sim(zones)
    _assert_road_clear(road_cells, walls, props, zones)
    _assert_road_connected(roads, road_cells)
    _assert_training_reachable(walls, zones, props)

    return {
        "tile": TILE,
        "width": WIDTH,
        "height": HEIGHT,
        "layers": {
            "ground": ground.dump(),
            "groundDeco": deco.dump(),
            "wall": walls.dump(),
        },
        "zones": zones,
        # 지도를 동 단위로 묶어 그리기 위한 것 — 방 25개를 낱개로 늘어놓으면
        # 어느 것이 한 건물인지 읽히지 않는다
        "buildings": [
            {"id": b["id"], "name": b["name"],
             "x": b["x"], "y": b["y"], "w": b["w"], "h": b["h"]}
            for b in BUILDINGS
        ],
        "props": props,
        "doors": doors,
        # 길. 바닥 타일로도 깔리지만(`asphalt`) 미니맵은 바닥을 그리지 않아
        # 사각형 목록으로도 함께 내보낸다 — 지도에 길이 보여야 훈련장을 찾아간다
        "roads": roads,
        "snow": snow,
    }


def _snow(zones: list[dict], walls: Grid) -> list[dict]:
    """
    §6.3 `TS_Snow` — 야외에 쌓이는 눈.

    **별도 맵을 만들지 않는다.** 바닥 위에 오버레이 한 겹을 얹고 한랭 이하
    밴드에서만 켠다. 그래야 제설 일과(`chore-snow`)가 이 레이어만 지워서
    **일한 결과가 눈에 보인다** — 게이지가 차는 것 말고 화면에 남는 것이
    생기는 유일한 일과다.

    구역별로 묶어 내보낸다. 제설은 연병장(Z11)에서 하는 일이고, 끝났을 때
    지울 칸이 어디인지 클라이언트가 알아야 한다.

    두께는 좌표 해시로 정한다 — 고르게 깔면 종이를 덮어놓은 것처럼 보이고,
    난수를 쓰면 다시 뽑을 때마다 달라져 diff가 통째로 바뀐다.
    """
    # 어느 구역에 속한 칸인가. 구역 밖 공터도 눈은 쌓이지만 제설 대상은 아니다 —
    # 연병장을 치우는 것이지 부대 전체를 치우는 것이 아니다
    owner: dict[tuple[int, int], str] = {}
    for zone in zones:
        if zone["indoor"]:
            continue
        for y in range(zone["y"], zone["y"] + zone["h"]):
            for x in range(zone["x"], zone["x"] + zone["w"]):
                owner[(x, y)] = zone["id"]

    # 훈련 맵 위에는 이 오버레이를 얹지 않는다 — TR05가 이미 눈바닥이고,
    # 나머지는 제설 대상이 아니라 영영 안 지워지는 눈이 남는다
    training = set()
    for spec in TR.build()["topdown"]:
        for ty in range(spec["y"], spec["y"] + spec["h"]):
            for tx in range(spec["x"], spec["x"] + spec["w"]):
                training.add((tx, ty))

    inside_building = set()
    for b in BUILDINGS:
        for y in range(b["y"], b["y"] + b["h"]):
            for x in range(b["x"], b["x"] + b["w"]):
                inside_building.add((x, y))

    # (구역, 두께) → 칸들
    buckets: dict[tuple[str, int], list[int]] = {}
    for y in range(BASE_H):
        for x in range(BASE_W):
            # 지붕 아래에는 눈이 안 쌓인다
            if (x, y) in inside_building or (x, y) in training:
                continue
            # 벽·철조망 위에도 안 쌓는다. 걸어 들어갈 수 없는 칸이라 제설도
            # 못 하고, 영영 안 지워지는 눈이 남는다
            if walls.get(x, y) is not None:
                continue

            key = (owner.get((x, y), ""), _snow_level(x, y))
            buckets.setdefault(key, []).extend((x, y))

    return [
        {"zone": zone, "level": level, "cells": cells}
        for (zone, level), cells in sorted(buckets.items())
    ]


#: 눈더미 격자. 이 칸수마다 두께가 한 번 정해지고 그 사이는 이어 붙인다
SNOW_LATTICE = 7


def _snow_noise(x: int, y: int) -> float:
    """
    0~1 사이로 **부드럽게** 변하는 값.

    처음에는 4타일 블록마다 해시를 하나씩 뽑았는데, 그러면 두께가 사각형으로
    뚝뚝 끊겨 연병장이 타일 카펫처럼 보였다. 격자에서 값을 뽑고 그 사이를
    이어 붙이면 바람에 쓸린 눈처럼 두께가 흐른다.
    """
    gx, gy = x / SNOW_LATTICE, y / SNOW_LATTICE
    ix, iy = int(gx // 1), int(gy // 1)
    fx, fy = gx - ix, gy - iy

    # 부드럽게 — 선형으로 이으면 격자선이 그대로 보인다
    fx = fx * fx * (3 - 2 * fx)
    fy = fy * fy * (3 - 2 * fy)

    def at(cx: int, cy: int) -> float:
        return T._hash(cx, cy, 4242) % 1000 / 1000.0

    top = at(ix, iy) * (1 - fx) + at(ix + 1, iy) * fx
    bottom = at(ix, iy + 1) * (1 - fx) + at(ix + 1, iy + 1) * fx
    return top * (1 - fy) + bottom * fy


def _snow_level(x: int, y: int) -> int:
    """쌓인 두께 0~3"""
    v = _snow_noise(x, y)
    if v < 0.30:
        return 0
    if v < 0.52:
        return 1
    if v < 0.74:
        return 2
    return 3


def _assert_reachable(buildings: list[dict]) -> None:
    """
    출입구가 복도와 만나는가.

    복도는 동을 가로지르므로 **서·동 끝**에 문을 내야 바로 이어진다. 남·북 벽에
    내면 그 사이 방을 관통해야 하고, 관통시키면 방 벽에 구멍이 뚫린다.
    나갈 수 없는 동은 게임을 멈추게 하므로 여기서 잡는다 — 실제로 생활관동에서
    밖으로 나갈 수 없었다.
    """
    bad = []
    for b in buildings:
        if b["corridor"] is None:
            bad.append(f"{b['name']}: 복도가 없다 — 출입구를 낼 곳이 없다")
            continue
        if not b["entrances"]:
            bad.append(f"{b['name']}: 출입구가 없다")
        for side in b["entrances"]:
            if side not in ("west", "east"):
                bad.append(f"{b['name']}: 출입구가 {side} — 복도 끝이 아니다")
    if bad:
        raise SystemExit("동에서 나갈 수 없다:\n  " + "\n  ".join(bad))


def _assert_doors_meet_corridor(buildings: list[dict]) -> None:
    """
    방 문이 복도에 닿는가.

    방 둘레 한 겹은 벽이므로, 방 끝과 복도 사이에 한 칸이라도 비면 그 칸이
    벽으로 남아 **문을 열고도 벽에 막힌다.** 급양동에서 실제로 그랬다.
    """
    bad = []
    for b in buildings:
        if b["corridor"] is None:
            continue
        cy = b["y"] + b["corridor"]
        for r in b["rooms"]:
            # 방의 바깥 변이 곧 복도와 공유하는 벽이고, 문은 그 벽을 뚫는다.
            # 복도 **안쪽**은 cy..cy+CORRIDOR-1 이므로 방은 그 바로 바깥에 선다
            if r["door"] == "south":
                touching = r["y"] + r["h"] - 1 == cy - 1
            elif r["door"] == "north":
                touching = r["y"] == cy + CORRIDOR
            else:
                touching = True
            if not touching:
                bad.append(f"{r['name']}: 문({r['door']})이 복도({cy}~{cy + CORRIDOR - 1})에 닿지 않는다")
    if bad:
        raise SystemExit("문이 복도에 닿지 않는다:\n  " + "\n  ".join(bad))


def _sim_zones() -> dict:
    """sim이 소유한 구역 표. 없으면 그냥 멈춘다 — 추측해서 그리면 안 된다"""
    with open(os.path.join(ROOT, "packages", "sim", "data", "zones.json"),
              encoding="utf-8") as f:
        return json.load(f)


def _layout_links() -> set[frozenset[str]]:
    """
    **도면이 실제로 뚫어놓은 통로**를 그대로 모은다.

    두 종류뿐이다.

      **방 ↔ 그 동의 복도**  — 방에는 문이 하나뿐이고 그 문은 복도로만 난다.
      **열린 땅끼리**        — 동 출입구와 야외 구역은 전부 흙바닥으로 나온다.

    두 번째가 중요하다. 처음에는 야외를 "전부 연병장을 거친다"로 적었는데,
    그건 그림과 다르다 — 북서 초소에서 정문 위병소까지는 연병장을 밟지 않고
    그냥 걸어갈 수 있다. 그렇게 적어두면 걸어갈 수 있는데 서버가 이동을
    거절하고, 플레이어에게는 그게 그냥 고장으로 보인다. 벽이 없으면 이어져
    있는 것이고, 표도 그렇게 적혀 있어야 한다.

    **단, 벽 두른 단독 건물(`walled`)은 "열린 땅"이 아니다.** 보일러실(Z14)이
    그 예다 — 벽으로 막혀 있으니 문이 없으면 못 들어가고, 문이 있으면 그
    문으로만 들어간다. 여기서 빼고 실제 문이 낸 통로만 따로 잇는다
    (`_connect_boiler_to_washroom`) — 안 그러면 "벽이 있는데 인접하다"는
    거짓말이 다시 생긴다.

    부대 밖으로는 못 나간다 — 외곽 철조망이 둘러 있고 그 밖은 게임이 없는 땅이다.
    """
    links: set[frozenset[str]] = set()
    for b in BUILDINGS:
        for r in b["rooms"]:
            links.add(frozenset((r["id"], b["corridorZone"])))

    # 흙바닥에 발을 딛는 것들 — 서로 벽이 없다. 벽 두른 단독 건물은 뺀다
    open_ground = [b["corridorZone"] for b in BUILDINGS] + \
        [o["id"] for o in OUTDOOR if not o.get("walled")]
    for i, a in enumerate(open_ground):
        for b in open_ground[i + 1:]:
            links.add(frozenset((a, b)))

    # 보일러실 ↔ 세면장 — `_connect_boiler_to_washroom`이 실제로 뚫은 문
    links.add(frozenset(("Z03", "Z14")))

    # 훈련장은 철조망 **밖**이다. 정문 위병소를 지나야 나갈 수 있고, 그게
    # "훈련은 부대를 나가서 한다"를 규칙으로 만든다 — 마당 패거리에 넣으면
    # 생활관 복도에서 사격장으로 바로 걸어가진다
    for spec in TR.build()["topdown"]:
        links.add(frozenset((spec["id"], "Z18")))
    for lane in TR.build()["lanes"]:
        links.add(frozenset((lane["id"], "Z18")))
    return links


def _assert_matches_sim(zones: list[dict]) -> None:
    """
    맵이 sim의 구역 표를 실제로 실현하는가.

    **규칙이 먼저고 그림이 따른다**(ARCH-02). sim은 "Z03은 Z02와 이어져 있다"고
    선언하고, 이동 허가(`isAdjacent`)와 이동 소요(`travelMs`)를 그 표로 판정한다.
    그 선언이 그림과 어긋나면 둘 중 하나가 벌어진다 — 걸어서 갈 수 있는데 서버가
    막거나, 서버는 허락하는데 벽에 막혀 못 간다. 어느 쪽이든 플레이어에게는
    게임이 고장난 것으로 보인다.

    이전 판에는 이 검사가 있을 수 없었다. 서버가 8구역만 알고 아트가 25개를
    들고 있어서 비교할 대상 자체가 없었고, 그래서 퀘스트 17건이 조용히 어긋난
    채 굴러갔다. 하나로 합치고 나서야 "같은 세계인가"를 물을 수 있게 됐다.

    실내 여부까지 같이 본다 — §5.0 지형보정과 열원 판정이 그 값에 걸려 있다.
    """
    table = _sim_zones()["zones"]
    declared = {z["id"] for z in table}
    drawn = {z["id"] for z in zones}

    bad = []
    for missing in sorted(declared - drawn):
        bad.append(f"{missing}: sim이 아는 구역인데 맵에 없다 — 갈 수 없는 일과가 생긴다")
    for extra in sorted(drawn - declared):
        bad.append(f"{extra}: 맵에만 있다 — 서버가 모르는 곳이라 들어가면 보고가 거절된다")

    indoor = {z["id"]: z["indoor"] for z in zones}
    for z in table:
        if z["id"] in indoor and indoor[z["id"]] != z["indoor"]:
            bad.append(f"{z['id']}: 실내 여부가 다르다 (sim {z['indoor']} / 맵 {indoor[z['id']]})")

    want = {frozenset((z["id"], a)) for z in table for a in z["adjacent"]}
    have = _layout_links()
    for pair in sorted(want - have, key=sorted):
        a, b = sorted(pair)
        bad.append(f"{a} ↔ {b}: sim은 이어져 있다는데 맵에 통로가 없다")
    for pair in sorted(have - want, key=sorted):
        a, b = sorted(pair)
        bad.append(f"{a} ↔ {b}: 맵은 뚫려 있는데 sim이 모른다 — 걸어가도 이동이 거절된다")

    if bad:
        raise SystemExit(
            "맵과 sim 구역 표가 어긋난다 (packages/sim/data/zones.json):\n  "
            + "\n  ".join(bad))


def _assert_no_overlap(zones: list[dict]) -> None:
    """
    구역이 겹치면 그 자리에서 멈춘다.

    도면대로 손좌표를 까는 이상 겹침은 반드시 생기고, 겹치면 바닥 타일이 나중에
    칠한 쪽으로 덮여 **어느 구역인지 코드와 화면이 달라진다.**
    """
    # **벽 한 장은 겹쳐도 된다.** 이웃한 방은 경계 타일을 공유하므로(`row`)
    # 사각형이 1타일 겹치는 것이 정상이다 — 그보다 많이 겹치면 방이 방을 먹은 것이다
    bad = []
    for i, a in enumerate(zones):
        for b in zones[i + 1:]:
            ox = min(a["x"] + a["w"], b["x"] + b["w"]) - max(a["x"], b["x"])
            oy = min(a["y"] + a["h"], b["y"] + b["h"]) - max(a["y"], b["y"])
            if ox > 1 and oy > 1:
                bad.append(f"{a['id']}({a['name']}) ↔ {b['id']}({b['name']}) "
                           f"— {ox}×{oy}타일")
    if bad:
        raise SystemExit("구역이 겹친다:\n  " + "\n  ".join(bad))


def _building(b: dict, ground: Grid, walls: Grid,
              zones: list[dict], props: list[dict], doors: list[dict],
              protected: set[tuple[int, int]]) -> None:
    """
    동 하나 — 외벽 · 복도 · 방들 · 출입구.

    **동 내부를 통째로 벽으로 채운 뒤 방과 복도를 파낸다.** 방 둘레만 벽을
    그리면 방과 방 사이 한 칸이 벽도 방도 아닌 틈으로 남고, 거기 들어간
    플레이어는 어느 구역에도 속하지 않은 채 갇힌다 — 실제로 그랬다.

    출입구는 **복도의 끝**에 낸다. 복도가 동을 가로지르므로 서·동 끝에 문을
    내면 바로 이어지고, 남쪽 벽에 내면 그 사이에 있는 방을 관통해야 한다.
    """
    ground.fill_floor(b["x"], b["y"], b["w"], b["h"], "concreteLight")
    walls.fill(b["x"], b["y"], b["w"], b["h"], f"wall:{b['wall']}")

    def carve(rect: dict, floor_kind: str) -> None:
        """벽을 파내 방(또는 복도) 안쪽을 만든다. 둘레 한 겹은 벽으로 남긴다"""
        for y in range(rect["y"] + 1, rect["y"] + rect["h"] - 1):
            for x in range(rect["x"] + 1, rect["x"] + rect["w"] - 1):
                walls.clear(x, y)
                ground.set_floor(x, y, floor_kind)

    corridor_rect = None
    if b["corridor"] is not None:
        cy = b["y"] + b["corridor"]
        corridor_rect = dict(x=b["x"], y=cy, w=b["w"], h=CORRIDOR)
        # 복도는 동 폭을 가로지른다 — 외벽 안쪽까지 전부
        for y in range(cy, cy + CORRIDOR):
            for x in range(b["x"] + 1, b["x"] + b["w"] - 1):
                walls.clear(x, y)
                ground.set_floor(x, y, "concreteLight")

        # 복도도 **동 외벽까지 닿는다.** 안쪽으로 한 칸 들여놓으면 그 자리가
        # 또 벽이 되어 동 안에 벽이 겹겹이 생긴다
        # 복도 사각형은 **공유 벽 두 줄까지 감싼다**(위·아래 각 한 줄).
        # `cy..cy+CORRIDOR-1`만 잡으면 그 두 줄이 복도 안쪽을 파먹어
        # 폭 4칸 복도가 2칸이 된다 — 실제로 그렇게 좁아졌다
        corridor_zone = dict(id=b["corridorZone"], name=f"{b['name']} 복도",
                             x=b["x"], y=cy - 1, w=b["w"], h=CORRIDOR + 2,
                             props=CORRIDOR_PROPS.get(b["corridorZone"], []))
        zones.append(_zone_entry(corridor_zone, indoor=True, kind="corridor"))
        # 복도도 일과가 벌어지는 곳이다 — `복도 정돈`이 여기 있다
        props.extend(_place_props(corridor_zone))

    for r in b["rooms"]:
        carve(r, r["floor"])

        span = door_span(r, r["door"])
        for (dx, dy) in span:
            walls.clear(dx, dy)
            ground.set_floor(dx, dy, "concreteLight")
            protected.add((dx, dy))

        ox, oy = _outward(r["door"])
        head = span[0]
        doors.append({
            "zone": r["id"], "name": r["name"],
            "x": head[0], "y": head[1],
            "w": DOOR_W if ox == 0 else 1,
            "h": DOOR_W if oy == 0 else 1,
            "side": r["door"],
        })

        zones.append(_zone_entry(r, indoor=True, kind="room"))
        props.extend(_place_props(r))

    # 동 출입구 — **복도 양끝을 복도 폭만큼 통째로 연다.**
    #
    # 2칸짜리 문으로 내면 복도(4칸)에서 문으로 들어설 때 좌우로 비켜서야 하고,
    # 4인이 한꺼번에 나갈 때 병목이 된다. 복도가 곧 통로이므로 그 폭 그대로
    # 벽을 걷어내는 편이 도면(PLAN 02-D "이동 허브")에 맞다.
    inside = b["corridorZone"] if corridor_rect else b["rooms"][0]["id"]
    if corridor_rect is None:
        return

    cy = corridor_rect["y"]
    for side in b["entrances"]:
        wall_x = b["x"] if side == "west" else b["x"] + b["w"] - 1
        for y in range(cy, cy + CORRIDOR):
            walls.clear(wall_x, y)
            ground.set_floor(wall_x, y, "concreteLight")
            protected.add((wall_x, y))

        ox = -1 if side == "west" else 1
        doors.append({
            "zone": inside,
            "name": b["name"],
            "x": wall_x, "y": cy, "w": 1, "h": CORRIDOR,
            "side": side,
            "exitLabel": b["exitLabel"],
            "exit": True,
        })

        # 문 밖 앞마당 — 콘크리트 한 뼘. 흙바닥에서 문으로 바로 이어지면
        # 어디가 입구인지 밖에서 안 보인다
        for i in range(1, 3):
            for y in range(cy, cy + CORRIDOR):
                ground.set_floor(wall_x + ox * i, y, "concrete")


def _training(ground: Grid, walls: Grid, zones: list[dict], props: list[dict],
              doors: list[dict], protected: set[tuple[int, int]]) -> None:
    """
    훈련 맵 7종을 월드에 얹는다 (§6.4 TR01~TR10 중 탑다운).

    사이드뷰 3종(TR03·TR07·TR08)은 여기 없다 — 중력 방향이 달라 같은 격자에
    둘 수 없고, `train_maps.json`의 `lanes`로 따로 나가 전용 무대가 그린다.
    """
    for spec in TR.build()["topdown"]:
        ground.fill_floor(spec["x"], spec["y"], spec["w"], spec["h"], spec["floor"])
        walls.outline(spec["x"], spec["y"], spec["w"], spec["h"], f"wall:{spec['wall']}")

        # 남쪽 가운데를 연다 — 부대에서 걸어 들어가는 입구
        rect = dict(x=spec["x"], y=spec["y"], w=spec["w"], h=spec["h"])
        span = door_span(rect, "south")
        for (dx, dy) in span:
            walls.clear(dx, dy)
            ground.set_floor(dx, dy, "concrete")
            protected.add((dx, dy))

        # **입구를 문 목록에 올린다.** 벽만 뚫어 두면 지도에 아무 표시가
        # 없어서, 길을 따라와도 40타일짜리 담장 어디가 입구인지 알 수 없다.
        # 여기 올리면 미니맵·부대 지도가 주황색 문선으로 그리고, 가까이 가면
        # HUD가 이름을 띄운다(`ZoneWorld.NearestDoor`). `exit`인 이유는 이
        # 문이 방문이 아니라 **바깥에서 들어오는 출입구**이기 때문이다
        doors.append({
            "zone": spec["id"],
            "name": spec["name"],
            "exitLabel": "부대로",
            "x": span[0][0], "y": span[0][1],
            "w": len(span), "h": 1,
            "side": "south",
            "exit": True,
        })

        zones.append(_zone_entry(spec, indoor=False, kind="outdoor"))
        props.extend(_place_props(spec))

    _lanes(ground, walls, zones, props)


def _lanes(ground: Grid, walls: Grid, zones: list[dict], props: list[dict]) -> None:
    """
    사이드뷰 무대 3종 (§9.0 재설계 "횡스크롤 레인 미니게임").

    같은 타일맵에 깐다. 옆에서 본 화면이지만 타일은 그냥 사각형이라, **위를
    비우고 아래를 채우면** 그 자체로 지면이 된다 — 2D 횡스크롤이 원래 그렇게
    만들어진다. 씬을 따로 세우지 않아도 되는 이유가 이것이다.

    높이가 x마다 다르므로 열 단위로 채운다. 그 높이 프로파일이 곧 오르막이고,
    §9.0의 "페이스 게이지"가 겨루는 지형이다.
    """
    for lane in TR.build()["lanes"]:
        x0, y0 = lane["x"], lane["y"]
        w, h = lane["w"], lane["h"]

        # 하늘 — 밟을 수 없다는 것이 색으로 보여야 한다
        ground.fill_floor(x0, y0, w, h, lane["sky"])

        for column, height in enumerate(lane["ground"]):
            top = y0 + h - height
            for y in range(top, y0 + h):
                ground.set_floor(x0 + column, y, lane["floor"])

        for obstacle in lane["obstacles"]:
            column = obstacle["x"]
            if column >= len(lane["ground"]):
                continue
            top = y0 + h - lane["ground"][column]
            props.append({
                "name": "장애물" if obstacle["kind"] == "hurdle" else "모래주머니",
                "zone": lane["id"],
                "x": x0 + column, "y": top - 1,
                "w": 1, "h": 1,
                "walkable": True,
            })

        zones.append(_zone_entry(
            dict(id=lane["id"], name=lane["name"], x=x0, y=y0, w=w, h=h),
            indoor=False, kind="lane"))


#: 길 폭(타일). 문(`DOOR_W`=2)보다 한 칸 넓게 잡아 길이라는 게 한눈에 보이게 한다
ROAD_W = 3


def _road(ground: Grid) -> list[dict]:
    """
    정문 밖에서 시작해 **탑다운 훈련장 7곳 전부**의 문턱까지 아스팔트를 깐다.

    **`_training` 뒤에 불러야 한다.** 각 훈련장의 남쪽 문(`door_span`)이 그
    함수 안에서 뚫리고 문턱 바닥이 콘크리트로 깔리는데, 길이 그 위까지
    덮어써야 문턱에서 끊기지 않는다 — 순서를 바꾸면 문턱 두 칸만 다시
    콘크리트로 되돌아간다.

    ── 좌표를 적지 않는다 ────────────────────────────────────────────────
    직전 판은 훈련장 한 곳(TR04)까지만 가는 길이었고, 꺾이는 자리를 전부
    숫자로 적어 두고 "`trainmap.py`가 바뀌면 이 상수도 다시 맞춰야 한다"는
    주석을 달았다. 훈련장이 7곳이 되면 그런 상수가 20개가 넘고, 그중 하나만
    묵으면 길이 조용히 벽을 뚫거나 문 앞에서 한 칸 못 미쳐 끊긴다.

    그래서 **훈련 맵 사각형에서 계산한다.** `trainmap.build()`가 맵을 세로로
    쌓고 넘치면 오른쪽 열로 옮기는데(`x += column_w + 4`), 그 배치가 그대로
    길의 뼈대가 된다:

      간선(trunk)  열마다 하나. 그 열 왼쪽 담장 바로 앞을 세로로 달린다.
                   열 사이 간격이 4칸이라 폭 3이 딱 들어가고 한 칸이 남는다
      지선(spur)   맵마다 하나. 남쪽 변 **바로 아래 빈 띠**를 타고 간선에서
                   그 맵의 문 앞까지 간다. 맵을 4칸 띄워 쌓으므로(`h + 4`)
                   그 띠는 언제나 비어 있다
      문턱(stub)   지선에서 문 두 칸까지 올라붙는 마지막 조각
      남측 연결로  제일 아래 맵보다 더 아래를 가로질러 열의 간선끼리 잇는다.
                   이것이 없으면 두 번째 열은 첫 열과 따로 놀아 못 간다
      정문 진입로  정문 밖 → 동쪽 → 첫 열 간선

    폭은 `ROAD_W`. 반환값은 사각형 목록이고, 그대로 `base_map.json`의
    `roads`가 되어 미니맵이 길을 그리는 데 쓴다.
    """
    maps = TR.build()["topdown"]

    # 열 = 같은 x에서 시작하는 맵들. `trainmap.build()`가 세로로 쌓은 그 단위다
    columns: dict[int, list[dict]] = {}
    for spec in maps:
        columns.setdefault(spec["x"], []).append(spec)
    column_x = sorted(columns)

    # 간선이 설 자리 — 그 열 왼쪽 담장 바로 앞 `ROAD_W`칸
    trunk_x = {cx: cx - ROAD_W for cx in column_x}

    # 왼쪽 열의 가장 넓은 맵이 오른쪽 열 간선 자리를 침범하지 않는가.
    # `trainmap.build()`의 `+ 4` 간격에 기대는 부분이라 어긋나면 여기서 죽는다
    for left, right in zip(column_x, column_x[1:]):
        widest = max(s["x"] + s["w"] for s in columns[left])
        assert widest <= trunk_x[right], (
            f"훈련 맵 열 간격이 좁아 길이 들어갈 자리가 없다: "
            f"x={left} 열이 {widest}까지 오는데 간선은 {trunk_x[right]}부터다. "
            f"trainmap.build()의 열 간격(+4)을 ROAD_W({ROAD_W})보다 넓게 유지해라")

    segments: list[dict] = []

    def lay(x, y, w, h):
        segments.append({"x": x, "y": y, "w": w, "h": h})

    # 남측 연결로가 달릴 높이 — 모든 맵보다 아래. 여기서 열의 간선이 만난다
    link_y = max(s["y"] + s["h"] for s in maps) + 1

    for cx in column_x:
        tx = trunk_x[cx]
        spur_ys = []

        for spec in columns[cx]:
            rect = dict(x=spec["x"], y=spec["y"], w=spec["w"], h=spec["h"])
            span = door_span(rect, "south")
            door_x0, door_y = span[0][0], span[0][1]
            door_x1 = span[-1][0]

            # 지선은 맵 남쪽 변 바로 아래 띠. 맵을 `h + 4`로 띄워 쌓으므로 빈 띠다
            spur_y = spec["y"] + spec["h"]
            spur_ys.append(spur_y)
            lay(tx, spur_y, door_x1 + 2 - tx, ROAD_W)

            # 문턱 — 문 두 칸에서 지선까지. 문이 남쪽 변(y+h-1)이라 두 줄이면 닿는다
            lay(door_x0, door_y, door_x1 - door_x0 + 1, spur_y - door_y + 1)

        # 간선 — 그 열 맨 위 지선부터 남측 연결로까지 한 줄로
        top = min(spur_ys)
        lay(tx, top, ROAD_W, link_y + ROAD_W - top)

    # 남측 연결로 — 열의 간선을 전부 꿴다
    first, last = trunk_x[column_x[0]], trunk_x[column_x[-1]]
    lay(first, link_y, last + ROAD_W - first, ROAD_W)

    # 정문 진입로 — 정문 폭 5칸의 가운데 `ROAD_W`칸으로 나가 동쪽으로 달린다.
    # 철조망 남쪽 변(y = BASE_H - 2)에서 시작해야 문턱이 흙으로 남지 않는다
    gate_x = GATE_X0 + (GATE_X1 - GATE_X0 + 1 - ROAD_W) // 2
    approach_y = BASE_H + 1
    lay(gate_x, BASE_H - 2, ROAD_W, approach_y + ROAD_W - (BASE_H - 2))
    lay(gate_x, approach_y, first + ROAD_W - gate_x, ROAD_W)

    for seg in segments:
        ground.fill_floor(seg["x"], seg["y"], seg["w"], seg["h"], "asphalt")

    return segments


def _road_cells(roads: list[dict]) -> list[tuple[int, int]]:
    return [(x, y)
            for r in roads
            for y in range(r["y"], r["y"] + r["h"])
            for x in range(r["x"], r["x"] + r["w"])]


def _assert_road_clear(road_cells: list[tuple[int, int]], walls: Grid,
                        props: list[dict], zones: list[dict]) -> None:
    """
    길이 벽·소품·구역 스폰을 침범하지 않는가.

    길은 `ground` 레이어에만 칠하므로 다른 레이어를 지우지는 않지만, 벽 밑에
    깔리거나 소품·스폰과 겹치면 그 자리는 걸어도 길처럼 안 보이거나(벽 밑)
    소품에 막혀 실제로는 못 지나가는 자리가 된다. 여기서 잡는다.
    """
    bad = []
    walled = [(x, y) for (x, y) in road_cells if walls.get(x, y) is not None]
    if walled:
        bad.append(f"벽과 겹친다: {len(walled)}칸 (예: {walled[0]})")

    road_set = set(road_cells)
    for prop in props:
        overlap = [(x, y) for x in range(prop["x"], prop["x"] + prop["w"])
                   for y in range(prop["y"], prop["y"] + prop["h"])
                   if (x, y) in road_set]
        if overlap and not prop.get("walkable"):
            bad.append(f"소품과 겹친다: {prop['name']}({prop['zone']}) @ {overlap[0]}")

    for zone in zones:
        spawn = (zone["spawn"]["x"], zone["spawn"]["y"])
        if spawn in road_set:
            bad.append(f"구역 스폰과 겹친다: {zone['id']} @ {spawn}")

    if bad:
        raise SystemExit("위병소-훈련장 길이 침범했다:\n  " + "\n  ".join(bad))


def _assert_road_connected(roads: list[dict], road_cells: list[tuple[int, int]]) -> None:
    """
    길이 정문에서 훈련장 입구까지 **한 덩어리로** 이어지는가.

    바로 아래 `_assert_training_reachable`과 목적이 다르다. 부대 밖은 벽이
    없는 트인 땅이라 길을 한 칸도 안 깔아도 도달성 검사는 통과한다 — 즉
    그 검사는 "갈 수 있는가"를 보증할 뿐 **길이 이어졌는가**는 못 본다.
    지선 하나가 간선에 한 칸 못 미쳐 떠 있어도 조용히 넘어간다는 뜻이다.
    길은 길로 증명한다.
    """
    road_set = set(road_cells)
    start = (GATE_X0 + 2, BASE_H - 2)
    assert start in road_set, f"정문 칸 {start}이 길 위에 없다"

    seen = {start}
    stack = [start]
    while stack:
        (x, y) = stack.pop()
        for step in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if step in seen or step not in road_set: continue
            seen.add(step)
            stack.append(step)

    orphan = len(road_set) - len(seen)
    if orphan:
        example = next(c for c in sorted(road_set) if c not in seen)
        raise SystemExit(
            f"길이 끊겼다 — 정문에서 못 닿는 길 {orphan}칸 (예: {example})")

    for spec in TR.build()["topdown"]:
        rect = dict(x=spec["x"], y=spec["y"], w=spec["w"], h=spec["h"])
        missing = [c for c in door_span(rect, "south") if c not in seen]
        if missing:
            raise SystemExit(
                f"{spec['id']} {spec['name']} 입구가 길에 안 닿는다: {missing}")


def _assert_training_reachable(walls: Grid, zones: list[dict],
                               props: list[dict]) -> None:
    """
    생활관에서 **걸어서** 훈련장 전부에 닿는가 — 실제 플러드필.

    이 검사는 뒤늦게 넣었다. 그 전에는 "정문으로 나가서 간다"는 주석만 있고
    철조망에 구멍이 없었는데, 코드 어디에도 그걸 잡는 장치가 없어서 훈련장
    10곳이 통째로 도달 불가인 채 배포됐다 — `curriculum.json`이 D-03부터
    사격장을 배정하므로 3일차에 게임이 멈추는 상태였다. 사용자가 플레이해서
    발견했다. 통행은 사람 눈이 아니라 여기서 증명한다.

    **소품도 막는 것으로 센다.** 벽만 보면 길 한복판에 물자 상자가 놓여도
    통과로 나오는데, 실제로는 콜라이더가 있어 못 지나간다.
    """
    blocked = {(x, y) for (x, y) in walls.cells}
    for prop in props:
        if prop.get("walkable"):
            continue
        blocked.update((x, y)
                       for x in range(prop["x"], prop["x"] + prop["w"])
                       for y in range(prop["y"], prop["y"] + prop["h"]))

    start_zone = next(z for z in zones if z["id"] == "Z01")
    start = (start_zone["spawn"]["x"], start_zone["spawn"]["y"])

    seen = {start}
    stack = [start]
    while stack:
        (x, y) = stack.pop()
        for (nx, ny) in ((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)):
            if not (0 <= nx < WIDTH and 0 <= ny < HEIGHT): continue
            if (nx, ny) in seen or (nx, ny) in blocked: continue
            seen.add((nx, ny))
            stack.append((nx, ny))

    # 사이드뷰 코스(`kind == "lane"`)는 제외한다 — 걸어가는 곳이 아니라
    # 훈련 시작 시 전용 무대로 넘어가는 화면이다
    unreachable = [
        f"{z['id']} {z['name']}"
        for z in zones
        if z["id"].startswith("TR") and z["kind"] != "lane"
        and (z["spawn"]["x"], z["spawn"]["y"]) not in seen
    ]
    if unreachable:
        raise SystemExit(
            f"생활관({start})에서 걸어서 못 가는 훈련장이 있다:\n  "
            + "\n  ".join(unreachable))


def _is_outer(b: dict, x: int, y: int) -> bool:
    return x in (b["x"], b["x"] + b["w"] - 1) or y in (b["y"], b["y"] + b["h"] - 1)


def _connect_boiler_to_washroom(ground: Grid, walls: Grid, doors: list[dict],
                                 protected: set[tuple[int, int]]) -> None:
    """
    보일러실(Z14) ↔ 세면장(Z03) — 보일러실의 유일한 문.

    보일러실은 벽을 두른 단독 건물이다(플레이어 보고: "보일러실에 들어갈 수가
    없다"). 두 방은 x=16 선을 y=32~33 구간에서 맞대지만 그 벽은 **두 겹**이다 —
    보일러실 자기 동쪽 벽(x=15)과 생활관동 서쪽 외벽(x=16)이 나란히 선다.
    한 겹만 뚫으면 문을 열고도 다른 벽에 막힌다. 반드시 이 함수가 두 동을 다
    그린 **뒤에** 불려야 한다 — `_building`이 동 내부를 벽으로 다시 채우고
    시작하므로, 먼저 뚫으면 그 위에 벽이 덮인다.

    자리는 보일러실 자신의 동쪽 문 자리(`door_span`)를 그대로 쓴다 — 소품이
    보일러 본체 · 배관 쪽에 몰려 있어(`LAYOUTS["Z14"]`) 동쪽 벽 앞은 원래
    비어 있고, 세면장 쪽도 같은 y에는 샤워 칸이 한 칸 못 미쳐 끝난다(y28~30).
    """
    boiler = next(o for o in OUTDOOR if o["id"] == "Z14")
    span = door_span(boiler, "east")
    washroom_wall_x = BARRACKS_X  # 16 — Z03는 동 서쪽 외벽을 제 벽으로 쓴다(row())

    for (bx, by) in span:
        walls.clear(bx, by)
        walls.clear(washroom_wall_x, by)
        ground.set_floor(bx, by, "concreteLight")
        ground.set_floor(washroom_wall_x, by, "concreteLight")
        protected.add((bx, by))
        protected.add((washroom_wall_x, by))

    # 세면장에서 볼 때와 보일러실에서 볼 때 할 말이 다르다 — 동 출입구가
    # `name`(밖에서 볼 때) · `exitLabel`(안에서 볼 때)을 나누는 것과 같은 규칙.
    # `zone`을 Z14로 두면: Z14 안에서는 exitLabel("세면장")을, 밖(Z03)에서는
    # name("보일러실")을 보여준다(`ZoneWorld.NearestDoor`).
    doors.append({
        "zone": "Z14", "name": "보일러실", "exitLabel": "세면장",
        "x": span[0][0], "y": span[0][1], "w": 1, "h": DOOR_W,
        "side": "east", "exit": True,
    })


def _outdoor(ground: Grid, walls: Grid, zones: list[dict], props: list[dict],
             doors: list[dict], protected: set[tuple[int, int]]) -> None:
    for o in OUTDOOR:
        ground.fill_floor(o["x"], o["y"], o["w"], o["h"], o["floor"])

        if o.get("walled"):
            # 벽만 두른다. 문은 여기서 내지 않는다 — 예전에는 동쪽 벽에 틈만
            # 뚫어놓고 그 틈이 어디로도 안 이어져서 "문처럼 보이는 막다른 벽"이
            # 됐다(§보일러실 버그). 실제 통로는 `_connect_boiler_to_washroom`이
            # 이웃 건물 벽까지 같이 뚫어야 완성되므로 그쪽에서 낸다
            walls.outline(o["x"], o["y"], o["w"], o["h"], "wall:utility")
        elif o.get("fenced"):
            # 초소·위병소·탄약고는 철조망으로 두른다. 탄약고는 **이중** (PLAN 04-B).
            # 문이 나는 방향은 도면이 정한다 — 위병소는 연병장 쪽(북)이다
            side = o.get("gate", "west")
            # 철조망 문은 넓게. 차량이 드나드는 곳이고, 위병소는 정문이라 더 넓다
            gap = o.get("openGate", 4)

            def open_fence(pad: int) -> None:
                rect = dict(x=o["x"] - pad, y=o["y"] - pad,
                            w=o["w"] + pad * 2, h=o["h"] + pad * 2)
                walls.outline(rect["x"], rect["y"], rect["w"], rect["h"], "wall:fence")
                cx = rect["x"] + rect["w"] // 2 - gap // 2
                cy = rect["y"] + rect["h"] // 2 - gap // 2
                for i in range(gap):
                    if side == "north":
                        walls.clear(cx + i, rect["y"])
                        ground.set_floor(cx + i, rect["y"], "concrete")
                        protected.add((cx + i, rect["y"]))
                    elif side == "south":
                        walls.clear(cx + i, rect["y"] + rect["h"] - 1)
                        ground.set_floor(cx + i, rect["y"] + rect["h"] - 1, "concrete")
                        protected.add((cx + i, rect["y"] + rect["h"] - 1))
                    elif side == "east":
                        walls.clear(rect["x"] + rect["w"] - 1, cy + i)
                        ground.set_floor(rect["x"] + rect["w"] - 1, cy + i, "concrete")
                        protected.add((rect["x"] + rect["w"] - 1, cy + i))
                    else:
                        walls.clear(rect["x"], cy + i)
                        ground.set_floor(rect["x"], cy + i, "concrete")
                        protected.add((rect["x"], cy + i))
                return rect

            fence_rect = open_fence(1)
            if o.get("double"):
                open_fence(3)

            # 위병소(Z18)만 — 미니맵(§7.9)이 벽·문만 그리고 바닥 종류는
            # 안 본다. 문 목록에 없으면 이 담장은 사방이 막힌 상자로만
            # 보인다. 북쪽 문(연병장에서 들어오는 입구)은 여기서 낸 것이라
            # `doors`에 없었고, 남쪽 정문(길로 나가는 유일한 출구)은 담장
            # 완성 뒤 `build()`가 따로 걷어내 역시 `doors`에 없었다 — 둘 다
            # 여기서 등록해야 지도에 "여기가 열려 있다"는 주황선이 뜬다
            if o["id"] == "Z18":
                north_gap_x0 = fence_rect["x"] + fence_rect["w"] // 2 - gap // 2
                doors.append({
                    "zone": o["id"], "name": o["name"],
                    "x": north_gap_x0, "y": fence_rect["y"], "w": gap, "h": 1,
                    "side": "north",
                })
                doors.append({
                    "zone": o["id"], "name": o["name"], "exitLabel": "훈련장",
                    "x": GATE_X0, "y": BASE_H - 2, "w": GATE_X1 - GATE_X0 + 1, "h": 1,
                    "side": "south", "exit": True,
                })

        zones.append(_zone_entry(o, indoor=o.get("indoor", False),
                                 kind="room" if o.get("indoor") else "outdoor"))
        props.extend(_place_props(o))


def _zone_entry(r: dict, indoor: bool, kind: str) -> dict:
    return {
        "id": r["id"],
        "name": r["name"],
        "x": r["x"], "y": r["y"], "w": r["w"], "h": r["h"],
        "indoor": indoor,
        "kind": kind,
        "door": None,
        "spawn": {"x": r["x"] + r["w"] // 2, "y": r["y"] + r["h"] // 2},
    }



# ══════════════════════════════════════════════════════════ 방별 배치 (files-6)
#
# **목업이 규칙이고, 개수는 방이 정한다.**
#
# 예전에는 소품을 방 둘레를 따라 3칸 간격으로 흩었다. 그러면 목업의 그림이
# 전혀 안 나온다 — 침상 옆에 관물대가 붙어 있고, 세면대가 거울 아래 일렬로
# 서고, 러닝머신이 벽면을 통째로 차지하는 것이 그 방을 그 방으로 만든다.
#
# 그래서 자리를 **목업의 정규화 좌표**로 잡고, 반복은 **타일 간격**으로 준다.
# 우리 방이 목업보다 크면(복도 2.6배 · 식당 2.1배) 같은 간격으로 더 많이
# 들어차고, 작으면 덜 들어찬다. 개수를 손으로 세어 박지 않는 이유다.
#
#   at(이름, nx, ny)                 한 개
#   row(이름, nx, ny, 간격, 축, 끝)   그 방향으로 끝까지 반복
#   grid(이름, nx, ny, dx, dy, 끝x, 끝y)  격자로 채운다

def at(name, nx, ny):
    return dict(name=name, nx=nx, ny=ny)


def row(name, nx, ny, pitch, axis="x", until=0.96):
    return dict(name=name, nx=nx, ny=ny, pitch=pitch, axis=axis, until=until)


def grid(name, nx, ny, dx, dy, untilX=0.96, untilY=0.96):
    return dict(name=name, nx=nx, ny=ny, pitch=dx, axis="x", until=untilX,
                pitchY=dy, untilY=untilY)


#: 구역 → 배치. 여기 없는 구역은 예전처럼 둘레에 흩는다
LAYOUTS = {
    # 침상과 관물대가 **쌍으로** 상·하 벽면에 늘어선다. 가운데는 통로로 비운다
    "Z01": [row("침상", 0.04, 0.06, 6, "x"), row("관물대", 0.04, 0.24, 6, "x"),
            row("침상", 0.04, 0.72, 6, "x"), row("관물대", 0.04, 0.90, 6, "x"),
            at("화장실 칸", 0.84, 0.06), at("수입 깔개", 0.30, 0.46),
            at("상황판", 0.02, 0.44)],
    # 복도 — 방문이 한쪽 벽에 늘어서고 반대쪽에 슬리퍼가 선을 맞춘다
    "Z02": [row("슬리퍼", 0.06, 0.80, 4, "x"),
            at("청소도구함", 0.02, 0.55), at("게시판", 0.94, 0.10)],
    # 거울이 상단 벽 전폭, 그 아래 세면대. 하단 좌측 샤워 · 우측 변기칸
    "Z03": [at("거울", 0.04, 0.02), row("세면대", 0.06, 0.20, 3, "x", 0.62),
            row("샤워 칸", 0.04, 0.52, 4, "x", 0.52),
            row("변기칸", 0.70, 0.16, 4, "x"), at("약제함", 0.84, 0.62),
            at("바닥 배수구", 0.55, 0.72)],
    "Z13": [row("세탁기", 0.05, 0.08, 4, "x", 0.58), row("건조대", 0.08, 0.56, 5, "x", 0.58),
            at("세탁물 수령대", 0.66, 0.10)],
    # 단말이 얹힌 책상이 줄로 반복되고, 우측이 집합 좌석 구역이다
    "Z16": [row("PC 책상", 0.05, 0.14, 4, "y", 0.86), row("단말", 0.10, 0.10, 4, "x", 0.46),
            row("단말", 0.10, 0.38, 4, "x", 0.46), row("단말", 0.10, 0.66, 4, "x", 0.46),
            at("집합 좌석", 0.58, 0.20)],
    "Z17": [at("덤벨 거치대", 0.06, 0.08), at("케이블 머신", 0.06, 0.48),
            at("러닝머신", 0.64, 0.10)],
    "Z09": [row("총기 거치대", 0.10, 0.06, 3, "x"), at("재물 대장", 0.10, 0.80)],
    # 배식대 위에 배식통, 식탁은 2열로 반복, 우측이 퇴식 구역
    "Z07": [at("배식대", 0.02, 0.04), row("배식통", 0.06, 0.10, 3, "x", 0.44),
            row("식탁", 0.05, 0.40, 5, "x", 0.72), row("식탁", 0.05, 0.62, 5, "x", 0.72),
            row("식탁", 0.05, 0.84, 5, "x", 0.72),
            at("퇴식구", 0.78, 0.06), at("잔반통", 0.62, 0.10),
            row("분리수거함", 0.80, 0.42, 4, "y")],
    "Z07b": [row("조리솥", 0.04, 0.06, 4, "x", 0.36), at("취반기", 0.40, 0.06),
             at("세척 컨베이어", 0.04, 0.44), at("세척기", 0.52, 0.44),
             at("급수 정수 설비", 0.72, 0.06), at("부식고", 0.72, 0.46),
             at("잔반장", 0.88, 0.46), at("시약대", 0.90, 0.34)],
    "Z08": [row("물자 선반", 0.04, 0.06, 4, "x", 0.42), at("공용장비함", 0.46, 0.06),
            row("유류통", 0.46, 0.46, 3, "x", 0.68), at("배터리함", 0.74, 0.08),
            at("청구 데스크", 0.74, 0.46), at("하역 출입구", 0.30, 0.94)],
    "Z10": [at("차량", 0.04, 0.06), at("발전기", 0.62, 0.06), at("유량계", 0.92, 0.34),
            at("섀도보드", 0.62, 0.46), at("공구함", 0.06, 0.70),
            at("고압 세척기", 0.30, 0.72)],
    "Z05": [row("처치대", 0.04, 0.10, 5, "y", 0.70), at("약품장", 0.42, 0.08),
            at("위생 검수대", 0.42, 0.54), at("폐기함", 0.44, 0.80),
            at("들것 거치대", 0.82, 0.10)],
    "Z06": [at("무전 콘솔", 0.04, 0.08), at("일지 보드", 0.56, 0.08),
            at("배터리함", 0.56, 0.56), at("접속부", 0.80, 0.56)],
    "Z04": [at("인원 대장", 0.04, 0.06), at("일과표 게시판", 0.50, 0.06),
            at("행정 책상", 0.04, 0.58), row("서류함", 0.42, 0.62, 3, "x")],
    "Z14": [at("보일러 본체", 0.04, 0.06), at("압력계", 0.30, 0.20),
            at("비상 발전기", 0.40, 0.06), row("유류통", 0.40, 0.50, 3, "x", 0.60),
            row("배관 분기", 0.72, 0.20, 4, "y"), at("차단 밸브", 0.56, 0.74),
            at("차단기", 0.88, 0.68), at("난로", 0.04, 0.72)],
    "Z11": [at("국기 게양대", 0.10, 0.16), row("나무", 0.04, 0.04, 9, "x"),
            row("배수로", 0.03, 0.20, 3, "y", 0.86), row("토사 퇴적", 0.28, 0.12, 12, "x", 0.66),
            at("대열 정렬선", 0.16, 0.36), at("안테나 마스트", 0.64, 0.28),
            at("급전선 단자", 0.64, 0.58), at("모래함", 0.62, 0.80),
            at("관측 지점", 0.88, 0.74)],
    "Z12": [at("감시창", 0.16, 0.08), at("인수인계대", 0.20, 0.44),
            at("초소 전화", 0.48, 0.42), grid("모래주머니", 0.08, 0.72, 2, 2, 0.44, 0.90),
            at("초소 벽", 0.66, 0.08)],
    "Z12b": [at("초소 벽", 0.08, 0.08), row("철조망", 0.04, 0.62, 3, "x"),
             at("모래주머니", 0.60, 0.20)],
    "Z18": [at("출입 대장", 0.0, 0.0), at("차단기", 0.62, 0.0),
            at("검문대", 0.0, 1.0), at("초소 벽", 0.30, 0.0),
            at("물자 상자", 0.34, 1.0)],
    "Z19": [grid("탄약함", 0.06, 0.08, 3, 3, 0.52, 0.56), at("상하키 수령대", 0.64, 0.08),
            at("재물 대장", 0.64, 0.42), at("운반 적재대", 0.20, 0.74),
            at("초소 벽", 0.86, 0.70)],
    "Z50": [at("야전 취사장", 0.36, 0.08), at("야전 취사도구", 0.38, 0.52),
            row("모탕", 0.56, 0.08, 3, "x", 0.66), row("텐트", 0.04, 0.10, 6, "x", 0.30),
            row("그늘막 팩", 0.74, 0.36, 3, "x"), row("장애물", 0.06, 0.62, 6, "x", 0.34),
            at("모래주머니", 0.50, 0.66), at("강단", 0.86, 0.62)],
}


def _layout_props(zone: dict) -> list[dict] | None:
    """
    배치표대로 찍는다. 표가 없으면 None을 돌려 예전 방식으로 넘긴다.

    자리는 정규화 좌표라 방 크기와 무관하고, 반복은 타일 간격이라 방이 크면
    더 들어찬다 — 그게 "목업을 규칙으로 삼는다"의 뜻이다.
    """
    plan = LAYOUTS.get(zone["id"])
    if not plan:
        return None

    x0, y0 = zone["x"] + 1, zone["y"] + 1
    x1, y1 = zone["x"] + zone["w"] - 2, zone["y"] + zone["h"] - 2
    if x1 <= x0 or y1 <= y0:
        return None
    span_x, span_y = x1 - x0, y1 - y0

    out: list[dict] = []

    def free(px, py, w, h):
        if px < x0 or py < y0 or px + w > x1 + 1 or py + h > y1 + 1:
            return False
        return not any(px < p["x"] + p["w"] and px + w > p["x"] and
                       py < p["y"] + p["h"] and py + h > p["y"] for p in out)

    def put(name, nx, ny):
        spec = T.PROPS.get(name)
        if not spec:
            return False
        w, h = spec[0], spec[1]
        px = x0 + int(round(nx * span_x))
        py = y0 + int(round(ny * span_y))
        px = max(x0, min(px, x1 - w + 1))
        py = max(y0, min(py, y1 - h + 1))
        if not free(px, py, w, h):
            return False
        out.append({"name": name, "zone": zone["id"], "x": px, "y": py,
                    "w": w, "h": h, "walkable": spec[3]})
        return True

    for item in plan:
        name = item["name"]
        if "pitch" not in item:
            put(name, item["nx"], item["ny"])
            continue

        # 반복 — 간격은 타일이고, 방 끝까지 채운다
        axis = item["axis"]
        step = item["pitch"] / (span_x if axis == "x" else span_y)
        rows = [item["ny"]]
        if "pitchY" in item:
            dy = item["pitchY"] / span_y
            rows = []
            v = item["ny"]
            while v <= item.get("untilY", 0.96) and len(rows) < 12:
                rows.append(v)
                v += dy

        for ny in rows:
            v = item["nx"] if axis == "x" else item["ny"]
            guard = 0
            while v <= item["until"] and guard < 24:
                put(name, v if axis == "x" else item["nx"],
                    ny if axis == "x" else v)
                v += step
                guard += 1

    return out


def _place_props(zone: dict) -> list[dict]:
    """
    소품을 방 둘레를 따라 세운다.

    한가운데 흩뿌리면 걸어 다닐 공간이 사라지고, 무엇보다 **어디에 무엇이 있는지
    익힐 수가 없다.** 벽을 따라 두면 방에 들어서는 순간 다 보인다.
    """
    designed = _layout_props(zone)
    if designed is not None:
        return designed

    out: list[dict] = []
    items = [(n, T.PROPS[n]) for n in zone.get("props", []) if n in T.PROPS]
    if not items:
        return out

    x0, y0 = zone["x"] + 1, zone["y"] + 1
    x1, y1 = zone["x"] + zone["w"] - 2, zone["y"] + zone["h"] - 2
    if x1 <= x0 or y1 <= y0:
        return out

    perimeter: list[tuple[int, int]] = []
    step = 3
    for x in range(x0, x1, step):
        perimeter.append((x, y0))
    for y in range(y0 + step, y1, step):
        perimeter.append((x1 - 1, y))
    for x in range(x1 - 1, x0, -step):
        perimeter.append((x, y1 - 1))
    for y in range(y1 - step, y0, -step):
        perimeter.append((x0, y))
    if not perimeter:
        perimeter = [(x0, y0)]

    stride = max(1, len(perimeter) // len(items))

    for i, (name, spec) in enumerate(items):
        w, h = spec[0], spec[1]
        px, py = perimeter[(i * stride) % len(perimeter)]
        px = max(x0, min(px, x1 - w + 1))
        py = max(y0, min(py, y1 - h + 1))

        # 둘레 자리가 겹치면 **방 안을 훑어** 빈 자리를 찾는다.
        #
        # 예전에는 여덟 번 밀어보고 포기했다. 목업 배치(files-6)로 물건이 방마다
        # 늘어나자 뒤쪽 물건이 통째로 사라졌고, 그게 하필 일과가 붙는 물건이면
        # 표식이 방 한가운데로 떨어졌다 — 원인이 안 보이는 종류의 고장이다.
        def free(ax: int, ay: int) -> bool:
            return not any(ax < p["x"] + p["w"] and ax + w > p["x"] and
                           ay < p["y"] + p["h"] and ay + h > p["y"] for p in out)

        if not free(px, py):
            px = py = -1
            for sy in range(y0, y1 - h + 2):
                for sx in range(x0, x1 - w + 2):
                    if free(sx, sy):
                        px, py = sx, sy
                        break
                if px >= 0:
                    break
        if px < 0:
            continue

        out.append({"name": name, "zone": zone["id"], "x": px, "y": py, "w": w, "h": h,
                    "walkable": spec[3]})

    return out


# 도로는 걷어냈다.
#
# PLAN 01의 초록 점선은 **동선**이지 포장도로가 아니다. 노면으로 깔아보니
# 부대가 아스팔트로 갈린 주차장처럼 보였고, 길을 따라가야 한다는 압박만 생겼다.
# 부대 바탕은 마사토 하나로 두고, 어디로 갈지는 지도와 HUD 방향 표시가 말한다.
