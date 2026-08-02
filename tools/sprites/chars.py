"""
캐릭터 스프라이트 생성기 (SAD-ART-001 §5 · APPENDIX A).

좌표는 **확정 목업 `files-3/MOCKUP_03_캐릭터시트.svg`를 6으로 나눈 실측값**이다.
목업이 6배 스케일로 그린 32×48 픽셀아트라서, ÷6이 곧 픽셀 좌표가 된다.
눈대중으로 다시 그리지 않는 이유는 그 시트가 이미 디자인 결정이기 때문이다 —
어깨가 14px인지 16px인지는 32px 캐릭터에서 실루엣을 바꾼다.

구조가 두 겹이다.

  **파츠**  방향(S/N/E)별로 어느 사각형이 어느 레이어에 속하는지. 정적이다.
  **포즈**  클립의 프레임마다 파츠 그룹을 얼마나 밀지. 여기서 애니메이션이 나온다.

프레임을 한 장씩 그리지 않고 포즈로 만드는 이유는 §5.5의 물량 계산에 있다.
베이스만 263프레임이고 스왑 변형까지 하면 6,300프레임이 나온다 — 손으로 그릴 수
없는 숫자다. 파츠를 밀어서 만들면 변형 하나를 더해도 프레임이 늘지 않는다.
"""

from __future__ import annotations

import json
import os

from PIL import Image

import palette as P
import pixel as PX

CELL_W, CELL_H = 32, 48

#: §5.1 피벗 — 하단 중앙 (16, 2). 발 접지점이다.
PIVOT = (16, 2)


# ══════════════════════════════════════════════════════════════════════ 파츠
#
# (x, y, w, h, 색키, 그룹)
# 그룹은 포즈가 밀 단위다: legL/legR/armL/armR/head/torso/none

S_PARTS = {
    "head": [
        (12, 12, 8, 1, "olive2", "head"),
        (11, 13, 10, 2, "olive1", "head"),
        (11, 15, 10, 1, "olive2", "head"),
    ],
    "body": [
        (12, 16, 8, 1, "skin1", "head"),
        (12, 17, 8, 4, "skin0", "head"),
        (14, 19, 1, 1, "eye", "head"),
        (17, 19, 1, 1, "eye", "head"),
        (13, 21, 6, 1, "skin1", "head"),
        (9, 34, 2, 1, "skin0", "armL"),
        (21, 34, 2, 1, "skin0", "armR"),
    ],
    "torso": [
        (13, 22, 6, 1, "olive1", "torso"),
        (9, 23, 14, 2, "olive0", "torso"),
        (11, 25, 10, 9, "olive0", "torso"),
        (9, 25, 2, 9, "olive1", "armL"),
        (21, 25, 2, 9, "olive1", "armR"),
        (11, 32, 10, 2, "olive2", "torso"),
    ],
    "legs": [
        (12, 35, 4, 7, "olive1", "legL"),
        (16, 35, 1, 7, "olive2", "torso"),
        (17, 35, 4, 7, "olive1", "legR"),
        (12, 42, 4, 4, "boot", "legL"),
        (17, 42, 4, 4, "boot", "legR"),
    ],
}

N_PARTS = {
    "head": list(S_PARTS["head"]),
    "body": [
        (12, 16, 8, 5, "hair", "head"),
        (13, 21, 6, 1, "skin1", "head"),
        (9, 34, 2, 1, "skin0", "armL"),
        (21, 34, 2, 1, "skin0", "armR"),
    ],
    "torso": list(S_PARTS["torso"]),
    "legs": list(S_PARTS["legs"]),
}

# §5.1 측면은 폭을 줄인다 — 목업 주석 "폭 7px로 축소"
E_PARTS = {
    "head": [
        (13, 12, 7, 1, "olive2", "head"),
        (12, 13, 9, 2, "olive1", "head"),
        (12, 15, 10, 1, "olive2", "head"),
    ],
    "body": [
        (13, 16, 4, 5, "hair", "head"),
        (17, 16, 3, 1, "skin1", "head"),
        (17, 17, 3, 4, "skin0", "head"),
        (18, 19, 1, 1, "eye", "head"),
        (15, 21, 4, 1, "skin1", "head"),
        (18, 34, 2, 1, "skin0", "armR"),
    ],
    "torso": [
        (14, 22, 5, 1, "olive1", "torso"),
        (13, 23, 7, 2, "olive0", "torso"),
        (13, 25, 6, 9, "olive0", "torso"),
        (18, 25, 2, 9, "olive1", "armR"),
        (13, 32, 7, 2, "olive2", "torso"),
    ],
    "legs": [
        (13, 35, 2, 7, "olive2", "legL"),
        (15, 35, 4, 7, "olive1", "legR"),
        (13, 42, 2, 4, "bootDark", "legL"),
        (15, 42, 4, 4, "boot", "legR"),
    ],
}

PARTS = {"S": S_PARTS, "N": N_PARTS, "E": E_PARTS}


# ─────────────────────────────────────────────────── 누운 자세 (전용 파츠)
#
# 포즈 오프셋으로는 만들 수 없다 — 서 있는 파츠를 아무리 밀어도 누운 사람이
# 되지 않는다. §5.5의 collapse / down_idle / carried / sleep 4종이 여기 해당한다.

DOWN_PARTS = {
    "head": [(6, 38, 1, 8, "olive2", "torso"), (7, 37, 2, 10, "olive1", "torso")],
    "body": [
        (9, 38, 4, 8, "skin1", "torso"),
        (10, 39, 3, 6, "skin0", "torso"),
        (22, 40, 2, 2, "skin0", "torso"),
    ],
    "torso": [
        (13, 36, 9, 11, "olive0", "torso"),
        (13, 35, 9, 2, "olive1", "torso"),
        (21, 37, 2, 9, "olive1", "torso"),
    ],
    "legs": [
        (22, 38, 6, 4, "olive1", "torso"),
        (22, 43, 6, 4, "olive1", "torso"),
        (27, 38, 3, 4, "boot", "torso"),
        (27, 43, 3, 4, "boot", "torso"),
    ],
}


# ═══════════════════════════════════════════════════════════════════════ 포즈
#
# 프레임 하나 = {그룹: (dx, dy)}. 적지 않은 그룹은 (0, 0).

def _walk_cycle(amp: int, bob: int) -> list[dict]:
    """
    6프레임 걷기. 다리를 번갈아 **위로 든다**.

    탑다운 픽셀에서 다리를 앞뒤로 미는 것은 읽히지 않는다 — 32px에서 x로 2px
    움직여봐야 몸 폭 안이라 안 보인다. 발이 지면에서 떨어지는 것은 보인다.
    """
    return [
        {},
        {"legL": (0, -amp), "armL": (0, amp), "armR": (0, -amp), "head": (0, -bob), "torso": (0, -bob)},
        {"legL": (0, -amp * 2), "armL": (0, amp), "armR": (0, -amp)},
        {},
        {"legR": (0, -amp), "armR": (0, amp), "armL": (0, -amp), "head": (0, -bob), "torso": (0, -bob)},
        {"legR": (0, -amp * 2), "armR": (0, amp), "armL": (0, -amp)},
    ]


def _arm_swing(frames: list[int], groups: tuple[str, ...] = ("armL", "armR")) -> list[dict]:
    return [{g: (0, dy) for g in groups} for dy in frames]


def _shift(frames: list[tuple[int, int]]) -> list[dict]:
    """캐릭터 전체를 민다. 떨림·흔들림용."""
    keys = ("head", "torso", "armL", "armR", "legL", "legR")
    return [{k: (dx, dy) for k in keys} for dx, dy in frames]


#: §5.5 애니메이션 리스트. (프레임 포즈, fps, 방향, 루프)
#: 방향 "4" = S/N/E 전부(W는 E 미러), "1" = S만, "SIDE" = 사이드뷰 씬 전용
CLIPS: dict[str, dict] = {
    # 01 대기 — 호흡만. 32px에서 이 이상은 안 읽힌다
    "idle": dict(poses=[{}, {}, {"head": (0, 1), "torso": (0, 1)}, {}], fps=6, dirs="4", loop=True),
    # 02~03 이동
    "walk": dict(poses=_walk_cycle(1, 1), fps=10, dirs="4", loop=True),
    "run": dict(poses=_walk_cycle(2, 1), fps=14, dirs="4", loop=True),
    # 04 단독 운반 — 두 팔을 앞으로 내린 채 (속도 −20%)
    "walk_carry": dict(
        poses=[{**p, "armL": (0, 2), "armR": (0, 2)} for p in _walk_cycle(1, 0)],
        fps=8, dirs="4", loop=True),
    # 05 2인 1조 운반 — 한 팔만 옆으로. 링크 상대 쪽 팔이 뻗는다
    "walk_coop": dict(
        poses=[{**p, "armR": (1, 1)} for p in _walk_cycle(1, 0)],
        fps=7, dirs="4", loop=True),
    # 06~08 작업 3종
    "work_generic": dict(poses=_arm_swing([0, -2, -3, -2]), fps=8, dirs="4", loop=True),
    "work_ground": dict(
        poses=[{"head": (0, 2), "torso": (0, 1), "armL": (0, dy), "armR": (0, dy)}
               for dy in (2, 3, 4, 3)],
        fps=8, dirs="4", loop=True),
    "work_panel": dict(poses=_arm_swing([0, -1, -2, -1], ("armR",)), fps=8, dirs="4", loop=True),
    # 09~10 정렬 동작 — 정면만
    "salute": dict(
        poses=[{"armR": (0, d)} for d in (0, -3, -6, -8, -8)], fps=8, dirs="1", loop=False),
    "attention": dict(poses=[{}, {"head": (0, 1)}], fps=4, dirs="1", loop=True),
    # 11~12 사격
    "aim": dict(poses=[{"armR": (0, -3)}, {"armR": (0, -3)}, {"armR": (0, -4)}],
                fps=8, dirs="4", loop=True),
    "fire": dict(poses=[{"armR": (0, -4)}, {"armR": (-1, -4), "torso": (-1, 0)}, {"armR": (0, -3)}],
                 fps=20, dirs="4", loop=False),
    # 13 방독면 착용 — 컷인과 병행하므로 월드 쪽은 팔 동작만
    "mask_on": dict(
        poses=[{"armR": (0, d), "armL": (0, d)} for d in (0, -3, -6, -8, -8, -6, -3, 0)],
        fps=10, dirs="1", loop=False),
    # 14~16 후송 파이프라인 — 전용 파츠
    "collapse": dict(poses=[{}] * 6, fps=8, dirs="1", loop=False, parts="down", fade=True),
    "down_idle": dict(poses=[{}, {"torso": (0, -1)}], fps=3, dirs="1", loop=True, parts="down"),
    "carried": dict(poses=[{}, {"torso": (0, -1)}], fps=4, dirs="1", loop=True, parts="down"),
    # 17 낙오자 부축 — 한쪽으로 기운다
    "assist": dict(
        poses=[{**p, "head": (-1, 0), "torso": (-1, 0), "armL": (-2, 1)}
               for p in _walk_cycle(1, 0)],
        fps=8, dirs="4", loop=True),
    # 18 취침
    "sleep": dict(poses=[{}, {"torso": (0, -1)}], fps=2, dirs="1", loop=True, parts="down"),
    # 19~20 상태 이상 — idle을 대체한다
    "shiver": dict(poses=_shift([(0, 0), (1, 0), (0, 0), (-1, 0)]), fps=10, dirs="4", loop=True),
    "pant": dict(
        poses=[{"head": (0, 1), "torso": (0, 1)}, {}, {"head": (0, 1), "torso": (0, 1)}, {}],
        fps=12, dirs="4", loop=True),
    # 21~22 사이드뷰 씬 전용 (행군 · 유격)
    "march": dict(poses=_walk_cycle(1, 1) + _walk_cycle(2, 1)[:2], fps=10, dirs="SIDE", loop=True),
    "climb": dict(
        poses=[{"armL": (0, -d), "armR": (0, -d), "legL": (0, -d // 2), "legR": (0, -d // 2)}
               for d in (0, 2, 4, 6, 4, 2)],
        fps=10, dirs="SIDE", loop=True),
    # 23 퀵 커맨드 발신 — 팔 들기. 8종 전부 같은 동작이고 말풍선이 내용을 말한다
    "emote_cmd": dict(poses=[{"armR": (0, -4)}, {"armR": (0, -8)}, {"armR": (0, -8)}],
                      fps=8, dirs="1", loop=False),
}


# ═════════════════════════════════════════════════════════════════════ 변형
#
# §5.2 레이어별 변형 목록. legs/torso는 **팔레트 스왑**으로 처리한다 —
# §5.5의 추가 축소안이며, 실루엣이 같고 색만 다르므로 2,100장이 530장이 된다.

#: 피복별 색 치환. (olive0, olive1, olive2) → 그 피복의 3톤
CLOTH_SWAPS = {
    "field": None,  # 전투복 — 기준색 그대로
    "winter": {"olive0": "coat0", "olive1": "coat1", "olive2": "coat2"},
    "pt": {"olive0": "conc1", "olive1": "conc2", "olive2": "conc3"},
    "rain": {"olive0": "metal0", "olive1": "metal1", "olive2": "metal2"},
}

#: §5.2 head 5종. **보직 식별 1순위**라 방향당 1프레임 고정이다(§5.5 축소 규칙)
HEADS = ("cap", "winterCap", "mask", "helmet", "bare")

#: §5.2 outer 4종 / gear 5종 / hand 7종
OUTERS = ("none", "parka", "raincoat", "fieldCoat")
GEARS = ("none", "belt", "pack", "medicBag", "toolbox")
HANDS = ("none", "rifle", "shovel", "broom", "radio", "clipboard", "stretcher", "fuelCan")

SKINS = ("skin0", "skin1", "skin2")


def _skin_swap(tone: str) -> dict[str, str] | None:
    """피부 3톤. skin0이 기준이고 나머지는 한 단계씩 어둡다."""
    if tone == "skin0":
        return None
    if tone == "skin1":
        return {"skin0": "skin1", "skin1": "skin2"}
    return {"skin0": "skin2", "skin1": "skin2"}


# ══════════════════════════════════════════════════════ 레이어별 추가 파츠

def head_parts(kind: str, direction: str) -> list[tuple]:
    """
    §5.3 보직 식별은 여기서 갈린다. 머리 실루엣만으로 4보직이 구분되어야 한다.
    """
    if kind == "cap":
        return PARTS[direction]["head"]
    if kind == "winterCap":
        # 방한모 — 귀덮개가 옆으로 나온다. 실루엣이 넓어진다
        base = PARTS[direction]["head"]
        if direction == "E":
            return base + [(12, 15, 2, 4, "coat1", "head")]
        return base + [(10, 15, 2, 4, "coat1", "head"), (20, 15, 2, 4, "coat1", "head")]
    if kind == "mask":
        # 방독면 — 얼굴을 통째로 덮고 정화통이 튀어나온다
        if direction == "E":
            return [(12, 12, 9, 2, "metal2", "head"), (12, 14, 9, 7, "metal1", "head"),
                    (20, 17, 2, 3, "device", "head")]
        return [(11, 12, 10, 2, "metal2", "head"), (11, 14, 10, 7, "metal1", "head"),
                (14, 18, 2, 2, "metal2", "head"), (17, 18, 2, 2, "metal2", "head")]
    if kind == "helmet":
        base = [(11, 11, 10, 4, "olive2", "head"), (10, 14, 12, 2, "olive2", "head")]
        return base if direction != "E" else [(12, 11, 9, 4, "olive2", "head"),
                                              (11, 14, 11, 2, "olive2", "head")]
    # bare — 무모/작업모. §5.3 "행정병은 유일하게 모자가 다르다"
    if direction == "E":
        return [(13, 13, 8, 2, "olive1", "head"), (13, 15, 8, 1, "olive2", "head")]
    return [(12, 13, 8, 2, "olive1", "head"), (11, 15, 10, 1, "olive2", "head")]


def outer_parts(kind: str, direction: str) -> list[tuple]:
    """§5.2 outer — 보온치 시각화. 몸통 위에 한 겹 덮는다."""
    if kind == "none":
        return []
    color = {"parka": "coat1", "raincoat": "metal1", "fieldCoat": "olive2"}[kind]
    hem = {"parka": "coat2", "raincoat": "metal2", "fieldCoat": "olive2"}[kind]
    if direction == "E":
        return [(12, 23, 9, 12, color, "torso"), (12, 34, 9, 2, hem, "torso"),
                (12, 25, 2, 9, hem, "armR")]
    return [(8, 23, 16, 12, color, "torso"), (8, 34, 16, 2, hem, "torso"),
            (8, 25, 2, 9, hem, "armL"), (22, 25, 2, 9, hem, "armR")]


def gear_parts(kind: str, direction: str) -> list[tuple]:
    """§5.2 gear — 무게가 이동속도와 연동된다. 실루엣으로 무게가 보여야 한다."""
    if kind == "none":
        return []
    if kind == "belt":
        return ([(13, 31, 7, 2, "webbing", "torso")] if direction == "E"
                else [(10, 31, 12, 2, "webbing", "torso"),
                      (12, 32, 2, 2, "boot", "torso"), (18, 32, 2, 2, "boot", "torso")])
    if kind == "pack":
        # 군장 — 후면에서 가장 크게 보인다
        if direction == "N":
            return [(11, 24, 10, 10, "webbing", "torso"), (12, 26, 8, 2, "boot", "torso")]
        if direction == "E":
            return [(11, 25, 3, 9, "webbing", "torso")]
        return [(9, 26, 2, 7, "webbing", "armL"), (21, 26, 2, 7, "webbing", "armR")]
    if kind == "medicBag":
        side = [(21, 28, 3, 5, "white", "armR"), (22, 29, 1, 3, "cross", "armR"),
                (21, 30, 3, 1, "cross", "armR")]
        return side if direction != "E" else [(11, 28, 3, 5, "white", "torso")]
    # toolbox — 공구함. 손이 아니라 옆구리에 매단다
    return [(21, 29, 3, 4, "device", "armR")] if direction != "E" else [(11, 29, 3, 4, "device", "torso")]


def mark_parts(role: str, rank: str, direction: str) -> list[tuple]:
    """
    §5.3 완장 + §5.4 계급장 3×5px.

    후면(N)에서는 계급장이 안 보인다 — 가슴에 달려 있다. 완장은 팔에 있으니 보인다.
    """
    band = {"rifle": "olive0", "comms": "cold", "medic": "white", "admin": "adminBand"}[role]
    out: list[tuple] = []

    # 완장 — 왼팔 위쪽.
    #
    # 팔은 2px인데 완장을 3px로 그려 1px 삐져나오게 한다. §3.2가 실루엣을
    # 1순위로 두었고, 의무병 완장에는 적십자가 들어가야 하는데(§5.3) 2px 안에
    # 십자를 넣으면 흰색이 한 픽셀도 안 남아 "유일하게 흰색"이라는 식별
    # 근거 자체가 사라진다.
    if direction == "E":
        out.append((12, 26, 3, 4, band, "torso"))
        if role == "medic":
            out.append((13, 27, 1, 2, "cross", "torso"))
    else:
        out.append((8, 26, 3, 4, band, "armL"))
        if role == "medic":
            out.append((9, 26, 1, 4, "cross", "armL"))   # 세로
            out.append((8, 27, 3, 1, "cross", "armL"))   # 가로

    if direction == "N":
        return out

    # 계급장 — 가슴. 가로선 n개 + 병장은 상단 점
    lines, dot = P.RANK_MARK[rank]
    color = {"private": "conc0", "pfc": "conc0", "corporal": "rankHi", "sergeant": "lamp0"}[rank]
    x = 16 if direction == "S" else 15
    top = 27
    if dot:
        out.append((x + 1, top - 2, 1, 1, color, "torso"))
    for i in range(lines):
        out.append((x, top + i * 2, 3, 1, color, "torso"))
    return out


def hand_parts(kind: str, direction: str) -> list[tuple]:
    """
    §5.2 hand — 상호작용 중에만 켠다.

    §5.5 축소 규칙대로 **정지 스프라이트 1장**이고, 흔드는 것은 팔 그룹을 따라간다.
    """
    if kind == "none":
        return []
    if kind == "rifle":
        # §5.3 소총수 — "등에 총열 실루엣이 튀어나온다"
        if direction == "N":
            return [(19, 22, 2, 12, "boot", "torso"), (18, 21, 4, 2, "boot", "torso")]
        if direction == "E":
            return [(19, 26, 2, 10, "boot", "armR")]
        return [(22, 22, 2, 12, "boot", "armR"), (21, 21, 4, 2, "boot", "armR")]
    if kind == "shovel":
        return [(22, 28, 1, 8, "wood1", "armR"), (21, 35, 3, 3, "metal1", "armR")]
    if kind == "broom":
        return [(22, 28, 1, 9, "wood0", "armR"), (21, 36, 3, 2, "dirt1", "armR")]
    if kind == "clipboard":
        # §5.3 행정병 — "겨드랑이 사각 실루엣". 모자와 함께 유일한 식별 근거다
        if direction == "E":
            return [(11, 27, 3, 5, "paper", "torso"), (11, 27, 3, 1, "metal0", "torso")]
        return [(22, 27, 3, 5, "paper", "armR"), (22, 27, 3, 1, "metal0", "armR"),
                (23, 29, 2, 1, "paperLine", "armR")]
    if kind == "radio":
        # §5.3 통신병 — 허리 무전기 + 등 안테나 1px
        base = [(21, 29, 3, 5, "device", "armR"), (22, 30, 1, 2, "cold", "armR")]
        return base + [(24, 22, 1, 8, "antenna", "armR")]
    if kind == "stretcher":
        return [(8, 33, 16, 1, "wood1", "torso"), (8, 32, 1, 3, "metal1", "armL"),
                (23, 32, 1, 3, "metal1", "armR")]
    # fuelCan — 유류통. 2인 운반 대상이라 크게 보여야 한다
    return [(21, 30, 4, 6, "olive2", "armR"), (22, 29, 2, 1, "metal2", "armR")]


#: 행정병 완장은 팔레트 계열에 없는 단발 색이라 여기서 이름을 붙인다
EXTRA_COLORS = {"adminBand": P.ROLE_BAND["admin"]}


# ═══════════════════════════════════════════════════════════════════ 렌더링

def _color(key: str) -> tuple:
    if key in EXTRA_COLORS:
        return EXTRA_COLORS[key]
    return P.W[key]


#: 발밑 그림자 (D-1) — 캐릭터 조립 최하위 레이어(`body`, CharacterRig sortingOrder
#: 0)에 굽는다. 8레이어가 각각 별도 시트라 파이썬에서 하나로 합칠 수 없고,
#: Unity `CharacterRig.Layers` 배열(스크립트 — 다른 에이전트 소관)에 새 레이어를
#: 추가하지 않고도 그림자가 항상 최하위에 깔리게 하는 방법이 이것뿐이다.
#: 반투명 금지 원칙대로 팔레트의 어두운 단색(`night1`)으로 찍는다.
_SHADOW_CX, _SHADOW_CY, _SHADOW_RX, _SHADOW_RY = CELL_W / 2, CELL_H - 2, 7, 2


def render(parts: list[tuple], pose: dict, swap: dict[str, str] | None = None,
           shadow: bool = False) -> Image.Image:
    """파츠 목록 + 포즈 → 32×48 한 프레임."""
    img = PX.blank(CELL_W, CELL_H)
    if shadow:
        PX.ellipse(img, _SHADOW_CX, _SHADOW_CY, _SHADOW_RX, _SHADOW_RY, P.W["night1"])
    for x, y, w, h, key, group in parts:
        if swap and key in swap:
            key = swap[key]
        dx, dy = pose.get(group, (0, 0))
        PX.rect(img, x + dx, y + dy, x + dx + w - 1, y + dy + h - 1, _color(key))
    return img


def clip_rows(clip: str) -> list[str]:
    """
    이 클립이 만드는 시트 행 목록.

    §5.1대로 W는 만들지 않는다 — E를 scaleX −1로 뒤집는 것이 Unity 쪽 일이다.
    """
    spec = CLIPS[clip]
    if spec["dirs"] == "4":
        return [f"{clip}_S", f"{clip}_N", f"{clip}_E"]
    if spec["dirs"] == "SIDE":
        return [f"{clip}_E"]
    return [f"{clip}_S"]


def build_layer(layer: str, variant: str, role: str = "rifle", rank: str = "private"):
    """
    레이어 하나의 시트를 만든다.

    반환: (이미지, 행 이름 목록, 최대 프레임 수)
    """
    rows: list[str] = []
    grid: list[list[Image.Image]] = []
    swap = None

    if layer in ("legs", "torso"):
        name = CLOTH_SWAPS.get(variant)
        swap = {k: v for k, v in name.items()} if name else None
    elif layer == "body":
        swap = _skin_swap(variant)

    for clip, spec in CLIPS.items():
        source = DOWN_PARTS if spec.get("parts") == "down" else None
        for row in clip_rows(clip):
            direction = row.rsplit("_", 1)[1]

            if source is not None:
                # 누운 자세는 전용 파츠뿐이다. 장구·완장·손 소지품은 그리지 않는다 —
                # 후송되는 사람에게 소총과 계급장을 붙여두면 §3.3의 "고통 묘사를
                # 넣지 않는다"가 아니라 그냥 어색한 그림이 된다
                parts = source.get(layer, [])
            elif layer == "head":
                parts = head_parts(variant, direction)
            elif layer == "outer":
                parts = outer_parts(variant, direction)
            elif layer == "gear":
                parts = gear_parts(variant, direction)
            elif layer == "mark":
                parts = mark_parts(role, rank, direction)
            elif layer == "hand":
                parts = hand_parts(variant, direction)
            else:
                parts = PARTS[direction][layer]

            # `body`가 최하위 레이어(sortingOrder 0)다 — 그림자를 여기 굽으면
            # `legs`(발) 등 위 레이어가 자동으로 그 위에 그려진다. 누운 자세는
            # 발밑 개념이 없어 제외한다(`source`가 DOWN_PARTS일 때)
            want_shadow = layer == "body" and source is None

            frames = []
            for i, pose in enumerate(spec["poses"]):
                frame = render(parts, pose, swap, shadow=want_shadow)
                if spec.get("fade"):
                    # collapse — 서 있던 사람이 사라지고 누운 사람이 남는다.
                    # 중간 프레임을 알파로 넘긴다. §3.3대로 고통 묘사는 넣지 않는다
                    alpha = int(255 * (i + 1) / len(spec["poses"]))
                    frame.putalpha(frame.getchannel("A").point(lambda v: v * alpha // 255))
                frames.append(frame)

            rows.append(row)
            grid.append(frames)

    if not grid:
        return None, [], 0

    cols = max(len(r) for r in grid)
    sheet = Image.new("RGBA", (cols * CELL_W, len(grid) * CELL_H), (0, 0, 0, 0))
    for y, frames in enumerate(grid):
        for x, frame in enumerate(frames):
            sheet.alpha_composite(frame, (x * CELL_W, y * CELL_H))

    return sheet, rows, cols


def generate(out_dir: str) -> dict:
    """
    §5.2 8레이어 × 변형을 전부 뽑는다.

    시트는 레이어·변형마다 1장이고 셀은 32×48, 여백 0이다(목업 §E 하단 주석:
    "Full Rect, Filter Point, Compression None").
    """
    os.makedirs(out_dir, exist_ok=True)

    plan = [
        ("body", SKINS),
        ("legs", tuple(CLOTH_SWAPS)),
        ("torso", tuple(CLOTH_SWAPS)),
        ("outer", OUTERS),
        ("gear", GEARS),
        ("head", HEADS),
        ("hand", HANDS),
    ]

    sheets: list[dict] = []
    rows_ref: list[str] = []
    cols_ref = 0

    for layer, variants in plan:
        for variant in variants:
            sheet, rows, cols = build_layer(layer, variant)
            if sheet is None:
                continue
            name = f"{layer}_{variant}"
            sheet.save(os.path.join(out_dir, f"{name}.png"))
            sheets.append({"layer": layer, "variant": variant, "file": f"chars/{name}.png"})
            rows_ref, cols_ref = rows, max(cols_ref, cols)

    # mark는 보직×계급 조합이다 — 완장과 계급장이 함께 붙어 있기 때문
    for role in P.ROLE_TAG:
        for rank in P.RANK_MARK:
            sheet, rows, cols = build_layer("mark", "", role, rank)
            if sheet is None:
                continue
            name = f"mark_{role}_{rank}"
            sheet.save(os.path.join(out_dir, f"{name}.png"))
            sheets.append({"layer": "mark", "variant": f"{role}_{rank}", "file": f"chars/{name}.png"})

    # Unity의 `JsonUtility`는 Dictionary를 읽지 못한다. 에디터에서 파서를 하나 더
    # 들이는 대신 **내보내는 쪽을 배열로 맞춘다** — 색인은 어차피 한 번만 돌기
    # 때문에 배열 순회로 충분하고, 의존성이 늘지 않는다.
    return {
        "cellW": CELL_W,
        "cellH": CELL_H,
        "pivotX": PIVOT[0],
        "pivotY": PIVOT[1],
        "rows": rows_ref,
        "cols": cols_ref,
        "clips": [
            {
                "name": clip,
                "frames": len(spec["poses"]),
                "fps": spec["fps"],
                "dirs": spec["dirs"],
                "loop": spec["loop"],
                "rows": clip_rows(clip),
            }
            for clip, spec in CLIPS.items()
        ],
        "sheets": sheets,
    }
