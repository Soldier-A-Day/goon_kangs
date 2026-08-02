"""
훈련 맵 (SAD-ART-001 §6.4 "훈련 맵 별도 씬 10종" → SAD-GDD-001 §9.0).

**별도 씬을 10개 만들지 않는다.** 명세가 씬을 나눈 것은 3D 시절의 메모리
예산 때문이었고, 2D 타일맵에서 40×24짜리 맵 하나는 씬을 가를 값어치가 없다.
대신 둘로 나눈다.

  **탑다운 7종**  부대 바깥 땅에 이어 붙인다. 같은 심리스 월드라 카메라·HUD·
                 파티클·시야 차단이 전부 그대로 돈다. 훈련장까지 **걸어간다**
  **사이드뷰 3종** 씬 하나를 공유한다 (§6.4가 직접 "TR03·TR07·TR08은 사이드뷰
                 씬 1개를 공유"하라고 적었다). 중력 방향이 달라 같은 월드에
                 둘 수 없다

부대 밖에 두는 것이 오히려 맞다 — 사격장도 숙영지도 위병소를 나가서 간다.
외곽 철조망 밖이 "게임이 없는 땅"이었는데, 이제 그쪽에 갈 이유가 생긴다.
"""

from __future__ import annotations

import tiles as T

#: 훈련 맵이 시작하는 x. 부대(110타일) 오른쪽으로 한 칸 띄운다
ORIGIN_X = 120

#: 사이드뷰 코스 한 구간의 길이(타일). §9.0 "구간 4개 = 체크포인트 4건"
LANE_SPAN = 60

#: 사이드뷰 무대 하나의 높이. 아래가 지면, 위가 하늘이다
LANE_H = 24

#: 사이드뷰 무대가 시작하는 y. 탑다운 훈련 맵 아래에 나란히 깐다
LANE_Y = 140


def _map(mid, name, w, h, floor, wall, props, gate=None):
    return dict(id=mid, name=name, w=w, h=h, floor=floor, wall=wall,
                props=props, gate=gate)


# ══════════════════════════════════════════════════ 탑다운 훈련 맵 7종
#
# 크기는 §6.4 표 그대로다. 소품은 그 자리가 무엇을 하는 곳인지 한눈에
# 말해야 하는 것들만 골랐다 — 사격장의 표적, 제독소의 분무대처럼.

TOPDOWN = [
    _map("TR01", "사격장", 40, 24, "dirt", "outdoor",
         ["강단", "탄약함", "탄약함", "물자 상자", "총기 거치대", "총기 거치대",
          "모래주머니", "모래주머니", "모래주머니"]),
    _map("TR02", "화생방 제독소", 28, 20, "concrete", "utility",
         ["세척대", "세척대", "약품장", "물자 선반", "배수로", "배수로"]),
    _map("TR04", "숙영지", 44, 32, "grass", "outdoor",
         ["난로", "난로", "물자 상자", "물자 상자", "유류통", "나무", "나무",
          "나무", "배식대", "세척대"]),
    _map("TR05", "혹한기 훈련장", 48, 32, "snow", "outdoor",
         ["난로", "난로", "유류통", "장애물", "장애물", "모래주머니",
          "나무", "나무"]),
    _map("TR06", "혹서기 급수 라인", 40, 28, "dirt", "outdoor",
         ["세면대", "세면대", "세면대", "물자 상자", "물자 상자", "강단"]),
    _map("TR09", "대민지원 (마을)", 56, 40, "grass", "wood",
         ["나무", "나무", "나무", "나무", "차량", "물자 상자", "물자 선반",
          "공구함", "건조대"]),
    _map("TR10", "합동 전술훈련장", 52, 36, "dirt", "outdoor",
         ["장애물", "장애물", "모래주머니", "모래주머니", "초소 벽",
          "무전 콘솔", "탄약함", "차량"]),
]


# ══════════════════════════════════════════════════════ 사이드뷰 3종
#
# §9.0 재설계: "횡스크롤 레인 미니게임. 4명이 좌→우로 자동 전진. 구간 4개 =
# 체크포인트 4개 = 필수 퀘스트 4건."
#
# 지형은 **높이 프로파일**로 준다. 타일을 깔지 않고 높이만 내보내는 이유는
# 사이드뷰가 걸어 다니는 맵이 아니라 **달리는 레인**이기 때문이다 — 발밑
# 지면과 넘을 장애물만 있으면 되고, 그 둘이 곧 난이도다.

SIDEVIEW = [
    dict(id="TR03", name="행군로", floor="asphalt", sky="skyNight", segments=4,
         # (구간 이름, 오르막 정도 0~1, 장애물 밀도 0~1)
         legs=[("출발 — 부대 정문", 0.0, 0.05),
               ("완만한 오르막", 0.35, 0.10),
               ("고갯길", 0.75, 0.18),
               ("복귀 — 마지막 5km", 0.15, 0.25)]),
    dict(id="TR07", name="유격장 (기초)", floor="grass", sky="skyDay", segments=4,
         legs=[("외줄 다리", 0.0, 0.30),
               ("도하", 0.10, 0.35),
               ("장애물 통과", 0.20, 0.45),
               ("종합 코스", 0.30, 0.40)]),
    dict(id="TR08", name="유격장 (종합)", floor="grass", sky="skyDay", segments=4,
         # D-13은 고난도다(§14.0 "종합 코스 (고난도)") — 밀도를 올린다
         legs=[("암벽 하강", 0.55, 0.50),
               ("도하 · 완전군장", 0.25, 0.55),
               ("연속 장애물", 0.40, 0.65),
               ("각개전투", 0.20, 0.70)]),
]

#: 훈련 종류 → 맵. 서버가 보내는 `training` 값이 이 키다(`curriculum.json`)
BY_TRAINING = {
    "marksmanship": ["TR01"],
    "cbrn": ["TR02"],
    "march": ["TR03"],
    "bivouac": ["TR04"],
    "seasonal": ["TR05", "TR06"],
    "commando": ["TR07", "TR08"],
    "externalSupport": ["TR09"],
    "combinedTactics": ["TR10"],
}


def _lane_ground(leg_index: int, rise: float, x: int) -> int:
    """
    사이드뷰 지면 높이.

    좌표 해시로 흔든다 — 매끈한 선은 길이 아니라 자다. 오르막은 구간 안에서
    서서히 올라가고, 구간이 끝나면 다음 구간의 시작 높이로 이어진다.
    """
    t = (x % LANE_SPAN) / LANE_SPAN
    base = 7 + rise * 9 * t
    bump = (T._hash(x // 3, leg_index, 991) % 100) / 100.0
    return int(base + bump * 2)


def build(start_x: int = ORIGIN_X) -> dict:
    """
    훈련 맵 전부를 한 덩이로 내보낸다.

    탑다운은 부대 오른쪽에 **세로로 쌓아** 배치한다. 가로로 늘어놓으면
    월드가 600타일을 넘어가고, 그러면 타일맵 청크가 화면 밖으로 길게 늘어져
    로딩할 때 눈에 띄게 걸린다.
    """
    topdown = []
    x, y, column_w = start_x, 2, 0

    for spec in TOPDOWN:
        # 세로로 쌓다가 넘치면 오른쪽 칸으로 옮긴다
        if y + spec["h"] + 2 > 130:
            x += column_w + 4
            y, column_w = 2, 0

        topdown.append({**spec, "x": x, "y": y})
        y += spec["h"] + 4
        column_w = max(column_w, spec["w"])

    lanes = []
    for spec in SIDEVIEW:
        ground = []
        for leg_index, (_, rise, _density) in enumerate(spec["legs"]):
            for step in range(LANE_SPAN):
                ground.append(_lane_ground(leg_index, rise, leg_index * LANE_SPAN + step))

        obstacles = []
        for leg_index, (_, _rise, density) in enumerate(spec["legs"]):
            for step in range(LANE_SPAN):
                at = leg_index * LANE_SPAN + step
                # 시작 4칸은 비운다 — 출발하자마자 걸리면 배울 기회가 없다
                if step < 4:
                    continue
                if T._hash(at, leg_index, 5171) % 100 >= density * 100:
                    continue
                obstacles.append({"x": at, "kind": "hurdle" if step % 2 else "sandbag"})

        lanes.append({
            "id": spec["id"],
            "name": spec["name"],
            "x": 0,
            "y": LANE_Y + len(lanes) * (LANE_H + 4),
            "w": LANE_SPAN * spec["segments"],
            "h": LANE_H,
            "floor": spec["floor"],
            "sky": spec["sky"],
            "span": LANE_SPAN * spec["segments"],
            "segments": spec["segments"],
            "legs": [{"name": n} for (n, _r, _d) in spec["legs"]],
            "ground": ground,
            "obstacles": obstacles,
        })

    _assert_no_overlap(topdown)

    return {
        "topdown": topdown,
        "lanes": lanes,
        "byTraining": [{"training": k, "maps": v} for k, v in BY_TRAINING.items()],
        "laneSpan": LANE_SPAN,
    }


def _assert_no_overlap(maps: list[dict]) -> None:
    """부대 맵과 같은 이유로 겹침을 막는다 — 겹치면 바닥이 조용히 덮인다"""
    bad = []
    for i, a in enumerate(maps):
        for b in maps[i + 1:]:
            if (a["x"] < b["x"] + b["w"] and a["x"] + a["w"] > b["x"] and
                    a["y"] < b["y"] + b["h"] and a["y"] + a["h"] > b["y"]):
                bad.append(f"{a['id']}({a['name']}) ↔ {b['id']}({b['name']})")
    if bad:
        raise SystemExit("훈련 맵이 겹친다:\n  " + "\n  ".join(bad))
