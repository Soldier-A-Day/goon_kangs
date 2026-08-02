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

import colorsys


def _hex(code: str) -> tuple[int, int, int]:
    code = code.lstrip("#")
    return (int(code[0:2], 16), int(code[2:4], 16), int(code[4:6], 16))


def _unhex(rgb: tuple[int, int, int]) -> str:
    return "#{:02X}{:02X}{:02X}".format(*rgb)


def ramp(base, steps: int, *, light_step: float = 13.0,
         shadow_hue: float = 10.0, shadow_sat: float = 8.0,
         highlight_hue: float = -12.0, highlight_sat: float = -4.0) -> list[tuple[int, int, int]]:
    """
    색상 이동 램프 (BENCHMARK §3 "ramp() 추가").

    지금까지 손으로 적어 넣은 계열(`conc0..conc3` 등)은 명도만 감산했다 —
    HSL로 측정해보면 어두워질수록 채도가 4%로 수렴하고 색상은 그대로다.
    회색이 짙어지는 것으로만 읽혀서 저채도 계열일수록(콘크리트·금속) 단이
    거의 안 보였다.

    이 함수는 명도와 함께 **색상·채도**도 옮긴다. 그림자 쪽(`steps<0`)은
    한랭으로(+H10°·S+8%p), 하이라이트 쪽(`steps>0`)은 온난으로(−H12°·S−4%p)
    민다 — 그림자는 하늘빛(청)을, 하이라이트는 광원(난색)을 조금 반사한다는
    통념의 픽셀 근사다. 우리 월드 팔레트 색상은 대체로 0~130° 대(황~녹)에
    몰려 있어 +H는 녹/청 쪽으로, −H는 적/주황 쪽으로 움직인다 — 부호가
    의도한 방향과 맞다(스와치로 확인).

    `base`가 인덱스 0이고, 반환값은 `[base, step1, step2, ...]` 순서다 —
    기존 `conc0..conc3` 같은 명명 규칙과 그대로 맞물린다.

    수치는 시작점이다(WORKORDER D-1 중단 기준) — `check()`가 계속 통과하는
    한 자유롭게 조정한다.
    """
    base_rgb = _hex(base) if isinstance(base, str) else tuple(base[:3])
    h, l, s = colorsys.rgb_to_hls(base_rgb[0] / 255, base_rgb[1] / 255, base_rgb[2] / 255)

    n = abs(steps)
    shadow = steps < 0
    dh = (shadow_hue if shadow else highlight_hue) / 360.0
    ds = (shadow_sat if shadow else highlight_sat) / 100.0
    dl = (-light_step if shadow else light_step) / 100.0

    out = [base_rgb]
    for i in range(1, n + 1):
        # 색상·채도는 **램프 끝에서 총량**이 shadow_hue/shadow_sat이 되도록
        # i/n 비율로 배분한다(명도는 계속 step마다 누적). i를 그대로 곱하면
        # 4단 계열(conc·dirt·grass)이 2단 계열보다 2배 더 돌아 과포화됐다 —
        # 실제로 skin2가 갈색이 아니라 겨자색이 되는 것으로 발견했다(D-1 §2)
        frac = i / n
        hh = (h + dh * frac) % 1.0
        ll = min(1.0, max(0.0, l + dl * i))
        ss = min(1.0, max(0.0, s + ds * frac))
        r, g, b = colorsys.hls_to_rgb(hh, ll, ss)
        out.append((round(r * 255), round(g * 255), round(b * 255)))
    return out


def _series(base: str, n: int, **kw) -> list[str]:
    """
    `ramp()`으로 그림자 쪽 n단계를 뽑아 WORLD 표 형식(hex 문자열)으로.

    `light_step`은 계열마다 다르게 준다 — 기본값 하나로 12계열을 다 밀면
    이미 어두운 계열(방상외피·야간)이 2~3단 만에 검정에 먹힌다. 눈으로 보고
    원래 손으로 그린 계열의 명도 낙폭에 맞춘 값이다(스와치 비교로 확정,
    WORKORDER D-1 "3회 안에 못 잡으면 후보를 뽑아 보고").
    """
    return [_unhex(c) for c in ramp(base, -(n - 1), **kw)]


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
#:
#: 12계열(conc·dirt·grass·wood·olive·metal·snow·water·skin·coat·night·lamp)은
#: 손으로 적은 명도 감산 대신 `ramp()` 산출물이다(BENCHMARK §3). 인덱스 0(가장
#: 밝은 톤)만 원래 손으로 고른 값을 그대로 시드로 쓰고, 그 아래 단은 전부
#: 한랭 이동으로 계산한다 — 그래서 `conc0` 등 기존 코드가 참조하는 "가장 밝은
#: 톤"은 픽셀 단위로 이전과 같고, 어두운 단만 색이 옮겨간다.
_conc = _series("C9C7BC", 4, light_step=14)
_dirt = _series("B09A76", 4, light_step=11)
_grass = _series("7E9155", 4, light_step=9)
_wood = _series("A88254", 3, light_step=11)
_olive = _series("6E7A50", 3, light_step=9)
_metal = _series("8E958F", 3, light_step=14)
_snow = _series("EFF2F4", 3, light_step=11)
_water = _series("5A8296", 3, light_step=11)
# 피부는 이미 채도가 높다(57%) — 기본 폭을 그대로 쓰면(심지어 절반으로 줄여도)
# 갈색이 아니라 겨자색이 된다(스와치 2회차에서도 실측: S 57→61%). 원래
# 손그림 램프도 어두워질수록 **채도가 준다**(57→42→36%) — 피부는 콘크리트와
# 달리 애초에 4%로 수렴하는 문제가 없었으므로, 채도는 살짝 낮추는 쪽으로
# 손그림 방향을 유지하고 색상만 아주 조금 옮긴다
_skin = _series("E0B48C", 3, light_step=13, shadow_hue=2, shadow_sat=-20)
_coat = _series("4A5636", 3, light_step=5)
_night = _series("1B2130", 2, light_step=4)
# 전구는 소재가 아니라 광원이다 — 어두워질 때 한랭이 아니라 **더 붉게** 죽는다
# (필라멘트가 식는 방향, 원래 손그림도 H40.5→34.5로 그렇게 갔다). `shadow_hue`에
# 음수를 줘서 어두워지는 쪽(light_step)은 유지하고 색 이동만 반대로 튼다
_lamp = _series("FFD98A", 2, light_step=15, shadow_hue=-6, shadow_sat=0)

WORLD = {
    # 콘크리트
    "conc0": _conc[0], "conc1": _conc[1], "conc2": _conc[2], "conc3": _conc[3],
    # 흙 · 연병장
    "dirt0": _dirt[0], "dirt1": _dirt[1], "dirt2": _dirt[2], "dirt3": _dirt[3],
    # 잔디 · 초목
    "grass0": _grass[0], "grass1": _grass[1], "grass2": _grass[2], "grass3": _grass[3],
    # 목재 · 관물대
    "wood0": _wood[0], "wood1": _wood[1], "wood2": _wood[2],
    # 피복 올리브
    "olive0": _olive[0], "olive1": _olive[1], "olive2": _olive[2],
    # 금속 · 장비
    "metal0": _metal[0], "metal1": _metal[1], "metal2": _metal[2],
    # 눈
    "snow0": _snow[0], "snow1": _snow[1], "snow2": _snow[2],
    # 물 · 배수로
    "water0": _water[0], "water1": _water[1], "water2": _water[2],
    # 피부 3톤
    "skin0": _skin[0], "skin1": _skin[1], "skin2": _skin[2],
    # 야간 하늘
    "night0": _night[0], "night1": _night[1],
    # 등화 (전구)
    "lamp0": _lamp[0], "lamp1": _lamp[1],
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
    "coat0": _coat[0], "coat1": _coat[1], "coat2": _coat[2],  # 방상외피 — ramp() 램프
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


#: 계열 이름 → 명→암 순서의 키 목록. `neighbor()`가 "한 단 옆"을 찾을 때 쓴다.
FAMILIES = {
    "conc": ["conc0", "conc1", "conc2", "conc3"],
    "dirt": ["dirt0", "dirt1", "dirt2", "dirt3"],
    "grass": ["grass0", "grass1", "grass2", "grass3"],
    "wood": ["wood0", "wood1", "wood2"],
    "olive": ["olive0", "olive1", "olive2"],
    "metal": ["metal0", "metal1", "metal2"],
    "snow": ["snow0", "snow1", "snow2"],
    "water": ["water0", "water1", "water2"],
    "skin": ["skin0", "skin1", "skin2"],
    "coat": ["coat0", "coat1", "coat2"],
    "night": ["night0", "night1"],
    "lamp": ["lamp0", "lamp1"],
}
_FAMILY_OF = {key: (fam, i) for fam, keys in FAMILIES.items() for i, key in enumerate(keys)}

#: 계열이 없는 단발 색의 이웃(밝게, 어둡게) 수동 지정. `_box()`가 쓰는 body
#: 값 중 계열 밖인 것(`device`·`white`)만 등록한다 — 나머지 단발 색(`alert` 등)은
#: 이웃이 없으면 `neighbor()`가 자기 자신을 돌려준다(밴드가 안 보일 뿐 안전하다).
_MANUAL_NEIGHBORS = {
    "white": ("snow0", "snow1"),
    "device": ("metal1", "metal2"),
}


def neighbor(key: str, step: int) -> tuple[int, int, int]:
    """
    팔레트 **안에서** `key`의 계열 이웃을 찾는다. `step>0`은 더 밝은(하이라이트)
    쪽, `step<0`은 더 어두운(그림자) 쪽 — 그 계열 안에 없으면(계열 끝이거나
    계열이 없는 단발 색) 있는 데까지만 움직이거나 자기 자신을 돌려준다.

    매번 `ramp()`으로 새 색을 계산하지 않고 **이미 등록된 팔레트 안에서만**
    고르는 이유는 §4.3 온도 밴드 그레이딩이 WORLD 팔레트를 통째로 밀어내는
    방식이기 때문이다 — 등록 안 된 색은 날씨가 바뀌어도 그레이딩을 안 타서
    나머지 화면이 밴드를 바꿔도 혼자 원래 색으로 남는다. `_box()`가 이 함수를
    쓰는 이유(D-1 §3)가 이것이다.
    """
    fam = _FAMILY_OF.get(key)
    if fam is not None:
        name, i = fam
        j = max(0, min(len(FAMILIES[name]) - 1, i - step))
        return W[FAMILIES[name][j]]
    manual = _MANUAL_NEIGHBORS.get(key)
    if manual is not None:
        return W[manual[0] if step > 0 else manual[1]]
    return W[key]


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
