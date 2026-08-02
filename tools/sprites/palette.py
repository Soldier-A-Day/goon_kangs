"""
SOLDIER : A DAY — 팔레트 (SAD-ART-001 §4).

색이 두 벌인 것이 이 게임의 아트 디렉션 그 자체다(§3.3 "UI는 종이, 월드는 픽셀").

  UI  — 기획서 문서의 CSS 변수를 그대로 승격시킨 것. 갱지색 바탕에 잉크색 글자.
  월드 — 저채도 32색 픽셀 병영.

**팔레트 밖 색을 쓰지 않는다**는 것이 §4.2의 지시다. 온도 밴드 그레이딩(§4.3)이
팔레트를 통째로 밀어내는 방식이라, 원본에 팔레트 밖 색이 섞이면 6밴드가 전부
어긋난다. 그래서 생성기는 전부 여기서만 색을 가져오고, `check()`가 산출물에
팔레트 밖 색이 없는지 검사한다.
"""

from __future__ import annotations


def _hex(code: str) -> tuple[int, int, int]:
    code = code.lstrip("#")
    return (int(code[0:2], 16), int(code[2:4], 16), int(code[4:6], 16))


# ────────────────────────────────────────────────────────────── §4.1 UI 팔레트

# 라이트/다크 두 벌. §14 미결정 3은 "다크 기본 + 옵션"이 유력하다고 적혀 있고,
# 야간 시간대가 많다는 근거가 붙어 있다. 그 권고를 따라 다크를 기본으로 둔다.
UI_LIGHT = {
    "paper": "#E3E4DD", "paper2": "#D8DAD0", "paper3": "#EEEFE9",
    "ink": "#191C16", "ink2": "#5A6053",
    "rule": "#B7BAAD", "rule2": "#CBCDC2",
    "accent": "#5E6E42", "accentW": "#EAEDDF",
    "cold": "#3F6E90", "heat": "#B25E2C", "alert": "#9E3226",
}

UI_DARK = {
    "paper": "#12150F", "paper2": "#1B1F17", "paper3": "#191D14",
    "ink": "#DFE2D6", "ink2": "#99A18D",
    "rule": "#333A2B", "rule2": "#262C1F",
    "accent": "#9AB06F", "accentW": "#21281A",
    "cold": "#7FAECF", "heat": "#DB8B55", "alert": "#D2594B",
}

#: 아이콘·UI 스프라이트를 그릴 때 쓰는 기본 벌. 런타임에 색을 다시 입히므로
#: (§3.2 "색은 정보다") 여기서는 형태만 정확하면 된다.
UI = {k: _hex(v) for k, v in UI_DARK.items()}
UI_L = {k: _hex(v) for k, v in UI_LIGHT.items()}


# ──────────────────────────────────────────────────────────── §4.2 월드 팔레트

#: 계열별 명→암. 이름이 곧 의미이며, 생성기는 이 이름으로만 색을 부른다.
WORLD = {
    # 콘크리트
    "conc0": "#C9C7BC", "conc1": "#A5A399", "conc2": "#807E76", "conc3": "#5C5B55",
    # 흙 · 연병장
    "dirt0": "#B09A76", "dirt1": "#8E7A5B", "dirt2": "#6B5C45", "dirt3": "#4A4131",
    # 잔디 · 초목
    "grass0": "#7E9155", "grass1": "#617040", "grass2": "#45512D", "grass3": "#2E381F",
    # 목재 · 관물대
    "wood0": "#A88254", "wood1": "#84643E", "wood2": "#5F482C",
    # 피복 올리브
    "olive0": "#6E7A50", "olive1": "#55603C", "olive2": "#3D4629",
    # 금속 · 장비
    "metal0": "#8E958F", "metal1": "#6A716C", "metal2": "#484D49",
    # 눈
    "snow0": "#EFF2F4", "snow1": "#D2DAE0", "snow2": "#AFBAC4",
    # 물 · 배수로
    "water0": "#5A8296", "water1": "#436374", "water2": "#2E4652",
    # 피부 3톤
    "skin0": "#E0B48C", "skin1": "#C29268", "skin2": "#9A7048",
    # 야간 하늘
    "night0": "#1B2130", "night1": "#111624",
    # 등화 (전구)
    "lamp0": "#FFD98A", "lamp1": "#E0A85C",
    # 강조 4색 — UI와 **같은 값**을 쓴다. 화면 안팎이 따로 놀면 안 된다
    "accent": UI_LIGHT["accent"], "cold": UI_LIGHT["cold"],
    "heat": UI_LIGHT["heat"], "alert": UI_LIGHT["alert"],
    # §5.1 아웃라인 — 배경 대비가 낮은 구간에서만 켠다
    "outline": "#2A2E24",
}

#: 목업(files-3)이 실제로 쓴 부속 색.
#:
#: §4.2는 "팔레트 외 색 사용 금지"라고 못박았지만, 확정 목업의 캐릭터 시트는
#: 32색 표에 없는 색을 몇 개 쓴다 — 군화·눈·머리카락처럼 **팔레트 계열 사이에
#: 끼어야 하는 어두운 톤**이다. 목업이 최종 디자인 기준이므로 여기에 명시적으로
#: 등록해 팔레트의 일부로 만든다. 등록하지 않고 검사만 통과시키면 다음 사람이
#: 아무 색이나 더하게 된다.
WORLD.update({
    "boot": "#33352E",      # 전투화
    "bootDark": "#2A2C26",  # 전투화 그늘 (측면 안쪽 발)
    "eye": "#3A2E22",       # 눈 1px
    "hair": "#4A3B2A",      # 뒤통수 (후면·측면에서만 보인다)
    "white": "#EEF1E8",     # 의무병 전용 — §5.3 "유일하게 흰색이 들어감"
    "antenna": "#8A9080",   # 통신병 안테나 1px
    "device": "#3A3F38",    # 무전기 · 장비 몸체
    "webbing": "#4A4A40",   # 탄띠 · 군장 벨트
    "coat0": "#4A5636", "coat1": "#3F4A2E", "coat2": "#2E3722",  # 방상외피
    "paper": "#D8DAD0", "paperLine": "#B7BAAD",  # 행정병 서류판
    "cross": "#9E3226",     # 적십자
    "rankHi": "#E0D8B0",    # 상병 계급장
})

W = {k: _hex(v) for k, v in WORLD.items()}


# ─────────────────────────────────────────────────────── §5.3 보직 시각 식별

#: 완장 색. 색만으로 구분되지 않도록 실루엣도 함께 다르다(§5.3 색각 이상 대응).
ROLE_BAND = {
    "rifle": W["olive0"],     # 올리브
    "comms": W["cold"],       # 청
    "medic": W["white"],      # 백 + 적십자
    "admin": _hex("#B08A2C"), # 황
}

#: 머리 위 이름표에 항상 붙는 3글자. 색각 이상 대응의 본체다.
ROLE_TAG = {"rifle": "RFL", "comms": "COM", "medic": "MED", "admin": "ADM"}

#: §5.4 계급장 색 (가슴 3×5px)
RANK_COLOR = {
    "private": W["conc0"], "pfc": W["conc0"],
    "corporal": W["rankHi"], "sergeant": W["lamp0"],
}
#: 계급 → (가로선 수, 상단 점 여부)
RANK_MARK = {
    "private": (1, False), "pfc": (2, False),
    "corporal": (3, False), "sergeant": (3, True),
}

#: 팔레트에 계열로 넣기 어려운 단발 색. 행정병 완장(§5.3),
#: 야시장비 단색화(§4.3 `#6BFF7A`).
#: 마커는 런타임에 색을 입힌다 — 원본은 순백이어야 틴트가 정확하다
EXCEPTIONS = {_hex("#B08A2C"), _hex("#6BFF7A"), (255, 255, 255)}

#: 팔레트 밖 색 검사용 집합.
ALLOWED = set(W.values()) | set(UI.values()) | set(UI_L.values()) | EXCEPTIONS


def check(image) -> list[tuple[int, int, int]]:
    """이미지에 팔레트 밖 색이 있으면 그 목록을 돌려준다. 없으면 빈 목록."""
    out = []
    for _, (r, g, b, a) in image.convert("RGBA").getcolors(1 << 16) or []:
        if a == 0:
            continue
        if (r, g, b) not in ALLOWED:
            out.append((r, g, b))
    return out


def swatch(path: str, size: int = 16) -> None:
    """§4.2가 픽셀 아티스트에게 지급하라고 한 스와치 파일을 뽑는다."""
    from PIL import Image, ImageDraw

    keys = list(WORLD)
    cols = 8
    rows = (len(keys) + cols - 1) // cols
    img = Image.new("RGBA", (cols * size, rows * size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(img)
    for i, key in enumerate(keys):
        x, y = (i % cols) * size, (i // cols) * size
        draw.rectangle([x, y, x + size - 1, y + size - 1], fill=W[key])
    img.save(path)
