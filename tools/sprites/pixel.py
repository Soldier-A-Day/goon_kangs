"""
픽셀 드로잉 헬퍼.

스프라이트를 **도트 매트릭스 문자열**로 적을 수 있게 한다. 코드로 도형을 조립하는
것보다 이쪽이 나은 이유는 리뷰가 되기 때문이다 — `.`과 `X`로 그린 눈송이는
소스에서 눈송이로 보이고, 잘못 찍힌 픽셀이 눈에 띈다. 원 그리기 함수 호출은
결과를 렌더해보기 전까지 무엇이 나올지 알 수 없다.

32×48 캐릭터에서 실루엣이 전부인 게임이므로(§3.2 "실루엣 우선") 이 차이가 크다.
"""

from __future__ import annotations

from PIL import Image

#: 도트 매트릭스에서 투명으로 읽는 문자
BLANK = ". "


def matrix(rows: list[str], colors: dict[str, tuple], size: tuple[int, int] | None = None) -> Image.Image:
    """
    도트 매트릭스를 이미지로.

    `rows`의 각 문자를 `colors`에서 찾아 칠한다. `.`과 공백은 투명이고,
    `colors`에 없는 문자는 **에러**다 — 조용히 투명으로 두면 오타가 그림에서
    사라져 알아챌 수 없다.
    """
    height = len(rows)
    width = max((len(r) for r in rows), default=0)
    if size is not None:
        width, height = size

    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    px = img.load()

    for y, row in enumerate(rows):
        if y >= height:
            break
        for x, ch in enumerate(row):
            if x >= width or ch in BLANK:
                continue
            if ch not in colors:
                raise KeyError(f"도트 매트릭스에 정의되지 않은 문자 {ch!r} (행 {y})")
            c = colors[ch]
            px[x, y] = c if len(c) == 4 else (c[0], c[1], c[2], 255)

    return img


def blank(w: int, h: int) -> Image.Image:
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))


def rect(img: Image.Image, x0: int, y0: int, x1: int, y1: int, color) -> None:
    """[x0,y0]~[x1,y1] 닫힌 구간을 칠한다. 픽셀 좌표라 끝점을 포함한다."""
    px = img.load()
    c = color if len(color) == 4 else (color[0], color[1], color[2], 255)
    for y in range(max(0, y0), min(img.height, y1 + 1)):
        for x in range(max(0, x0), min(img.width, x1 + 1)):
            px[x, y] = c


def ellipse(img: Image.Image, cx: float, cy: float, rx: float, ry: float, color) -> None:
    """
    채워진 타원을 불투명 단색으로 찍는다 (D-1 "발밑 그림자").

    반투명을 쓰지 않는다 — `tiles.py`의 원칙 그대로 픽셀아트에서 알파 중간값은
    확대하면 뿌옇게 뜬다. 그림자·바닥 얼룩·소품 밑변처럼 "여기가 바닥에
    닿았다"를 팔레트의 어두운 단색 한 장으로 찍을 때 쓴다.
    """
    px = img.load()
    c = color if len(color) == 4 else (color[0], color[1], color[2], 255)
    if rx <= 0 or ry <= 0:
        return
    y0, y1 = int(cy - ry), int(cy + ry)
    x0, x1 = int(cx - rx), int(cx + rx)
    for y in range(max(0, y0), min(img.height, y1 + 1)):
        ny = (y + 0.5 - cy) / ry
        for x in range(max(0, x0), min(img.width, x1 + 1)):
            nx = (x + 0.5 - cx) / rx
            if nx * nx + ny * ny <= 1.0:
                px[x, y] = c


def outline(img: Image.Image, color) -> None:
    """
    불투명 픽셀의 바깥 테두리를 1px 두른다 (§5.1 선택적 아웃라인).

    안쪽이 아니라 바깥에 그린다 — 안쪽에 그리면 20×34px 캐릭터에서 몸이
    2px 줄어들고, 그 정도면 실루엣이 달라진다.
    """
    src = img.copy()
    sp = src.load()
    dp = img.load()
    c = color if len(color) == 4 else (color[0], color[1], color[2], 255)

    for y in range(img.height):
        for x in range(img.width):
            if sp[x, y][3] != 0:
                continue
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < img.width and 0 <= ny < img.height and sp[nx, ny][3] != 0:
                    dp[x, y] = c
                    break


def tint(img: Image.Image, mapping: dict[tuple, tuple]) -> Image.Image:
    """
    색 치환. 팔레트 스왑으로 변형을 만드는 §5.5의 축소안이 이걸 쓴다 —
    전투복/방한/활동복은 실루엣이 같고 색만 다르므로 2,100장이 530장이 된다.
    """
    out = img.copy()
    px = out.load()
    table = {
        (k if len(k) == 4 else (k[0], k[1], k[2], 255)):
        (v if len(v) == 4 else (v[0], v[1], v[2], 255))
        for k, v in mapping.items()
    }
    for y in range(out.height):
        for x in range(out.width):
            got = table.get(px[x, y])
            if got is not None:
                px[x, y] = got
    return out


def paste(dst: Image.Image, src: Image.Image, x: int = 0, y: int = 0) -> None:
    dst.alpha_composite(src, (x, y))


def mirror(img: Image.Image) -> Image.Image:
    """X 미러링. §5.1이 `W`를 `E`의 미러로 처리하라고 지시한 그것."""
    return img.transpose(Image.FLIP_LEFT_RIGHT)


def sheet(frames: list[Image.Image], cell: tuple[int, int]) -> Image.Image:
    """프레임을 가로로 이어 붙인 스프라이트 시트. Unity가 셀 폭으로 잘라 쓴다."""
    w, h = cell
    out = Image.new("RGBA", (w * max(1, len(frames)), h), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        out.alpha_composite(f, (i * w, 0))
    return out


def scale(img: Image.Image, factor: int) -> Image.Image:
    """정수배 확대. 픽셀 폰트·아이콘은 정수배만 허용된다(§11 주의)."""
    return img.resize((img.width * factor, img.height * factor), Image.NEAREST)


def _clamp8(v: float) -> int:
    return 0 if v < 0 else (255 if v > 255 else int(round(v)))


def mul_rgb(color, factor: float):
    """RGB(A) 각 채널에 `factor`를 곱하고 0~255로 클램프한다. 알파는 그대로 둔다."""
    r, g, b = color[0], color[1], color[2]
    out = (_clamp8(r * factor), _clamp8(g * factor), _clamp8(b * factor))
    return out if len(color) < 4 else out + (color[3],)


# ─────────────────────────────────────────────────────────── W1 셰이딩 패스

#: 광원은 **좌상단 45도 고정**이다(W1 발주). 여기 상수만 바꾸면 105여 종
#: 소품·벽 정면 타일 전부의 셰이딩이 한 번에 바뀐다 — 개별 함수마다 계수를
#: 흩어두면(예전 `_box()`의 `neighbor(body, ±1)` 방식) 톤을 조절할 때마다
#: 호출부 90여 곳을 손봐야 한다.
SHADE_TOP_BAND_PX = 2       #: 각 열 첫 불투명 픽셀부터 이 폭까지 — 윗면(하이라이트)
SHADE_TOP_MUL = 1.22
SHADE_BOTTOM_BAND_PX = 3    #: 각 열 마지막 불투명 픽셀까지 이 폭 — 아랫면(그림자)
SHADE_BOTTOM_MUL = 0.70
SHADE_EDGE_MUL = 0.52       #: 알파 경계(투명과 맞닿거나 캔버스 가장자리) 1px — 외곽선


def shade(img: Image.Image, *, snap=None) -> None:
    """
    알파 마스크만 보고 좌상단 45도 광원을 자동 적용한다(W1 §1).

    도형이 무엇이든 상관없다 — `_box()` 사각 실루엣이든 `_tree()`처럼 구멍이
    숭숭 뚫린 캐노피든, 각 **열(column)**에서 첫 불투명 픽셀 쪽 `SHADE_TOP_BAND_PX`
    는 밝게, 마지막 불투명 픽셀 쪽 `SHADE_BOTTOM_BAND_PX`는 어둡게 곱하고,
    투명과 맞닿은(또는 캔버스 밖으로 나가는) 알파 경계 1px는 한 번 더 어둡게
    곱한다. 세 규칙이 겹치는 픽셀(예: 상단 하이라이트이면서 동시에 왼쪽
    가장자리인 모서리)은 **곱을 누적**한다 — 모서리에 옅은 다크림이 생겨
    "물건 귀퉁이"가 실제로 읽힌다.

    직접 RGB를 곱하면 §4.2 팔레트 밖 색이 생긴다. 호출부가 `snap`(예:
    `palette.nearest`)을 넘기면 계산한 색을 팔레트 안으로 스냅한다 —
    `pixel.py`는 팔레트를 모르는 채로 남고(이 파일은 범용 헬퍼다), 팔레트
    규칙을 아는 쪽(`tiles.py`)이 결정한다.
    """
    w, h = img.size
    px = img.load()

    tops: list[int | None] = [None] * w
    bottoms: list[int | None] = [None] * w
    for x in range(w):
        for y in range(h):
            if px[x, y][3] != 0:
                if tops[x] is None:
                    tops[x] = y
                bottoms[x] = y

    def opaque(x: int, y: int) -> bool:
        return 0 <= x < w and 0 <= y < h and px[x, y][3] != 0

    for x in range(w):
        top = tops[x]
        if top is None:
            continue
        bottom = bottoms[x]
        for y in range(top, bottom + 1):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            mul = 1.0
            if y - top < SHADE_TOP_BAND_PX:
                mul = SHADE_TOP_MUL
            elif bottom - y < SHADE_BOTTOM_BAND_PX:
                mul = SHADE_BOTTOM_MUL
            is_edge = (
                x == 0 or y == 0 or x == w - 1 or y == h - 1
                or not opaque(x - 1, y) or not opaque(x + 1, y)
                or not opaque(x, y - 1) or not opaque(x, y + 1)
            )
            if is_edge:
                mul *= SHADE_EDGE_MUL
            if mul == 1.0:
                continue
            nr, ng, nb = _clamp8(r * mul), _clamp8(g * mul), _clamp8(b * mul)
            if snap is not None:
                nr, ng, nb = snap((nr, ng, nb))
            px[x, y] = (nr, ng, nb, a)


# ────────────────────────────────────────────────────────────── W1 노멀맵

#: 소품 가장자리가 이 폭(px) 안에서 둥글게 떨어진다. 그 안쪽은 높이가 포화돼
#: 완전히 평평하다(윗면) — 알파 투명 구간이 전혀 없는 타일(바닥·벽 오토타일처럼
#: 칸 전체가 불투명)은 거리변환의 "씨앗"(투명 픽셀)이 없으므로 전체가 이
#: 포화값으로 채워져 그대로 평평한 노멀 `(128,128,255)`이 된다 — 별도 분기
#: 없이 알고리즘 자체가 그렇게 수렴한다.
NORMAL_EDGE_PLATEAU_PX = 3.0
NORMAL_STRENGTH = 1.6
NORMAL_FLAT = (128, 128, 255)


def normal_map(img: Image.Image, *, strength: float = NORMAL_STRENGTH,
               plateau: float = NORMAL_EDGE_PLATEAU_PX) -> Image.Image:
    """
    알파 마스크의 거리변환 → 높이장 → 기울기 → 탄젠트 공간 노멀(W1 §4).

    실시간 2D 조명을 켜려면 노멀맵이 있어야 한다. 손으로 그리는 대신 이미
    갖고 있는 알파 마스크에서 뽑는다 — 불투명 영역 안쪽으로 갈수록(투명과
    멀어질수록) 높다고 가정하면(§4 "가장자리가 둥글게 떨어지고 윗면은
    평평하게") 소품이 둥근 상자처럼 도드라져 보인다.

    1) 체임퍼 거리변환(2-패스, `random` 없이 정수/부동소수 연산만 — 결정적)으로
       각 불투명 픽셀에서 가장 가까운 투명 픽셀까지 거리를 구한다.
    2) `plateau`에서 포화시켜 높이장으로 쓴다(그 밖은 평평한 윗면).
    3) 중심차분으로 기울기를 구해 `(-dx, -dy, 1)`을 정규화 → RGB로 인코딩.

    투명 픽셀은 알파 0으로 그대로 두어(원본과 같은 실루엣) 노멀맵도 스프라이트
    잘라내기에 맞는다.
    """
    w, h = img.size
    a = img.getchannel("A")
    apx = a.load()

    inf = float(w + h)
    dist = [[0.0 if apx[x, y] == 0 else inf for x in range(w)] for y in range(h)]

    diag = 1.4142135623730951
    for y in range(h):
        row = dist[y]
        prev = dist[y - 1] if y > 0 else None
        for x in range(w):
            if row[x] == 0.0:
                continue
            best = row[x]
            if x > 0:
                best = min(best, row[x - 1] + 1.0)
            if prev is not None:
                best = min(best, prev[x] + 1.0)
                if x > 0:
                    best = min(best, prev[x - 1] + diag)
                if x < w - 1:
                    best = min(best, prev[x + 1] + diag)
            row[x] = best
    for y in range(h - 1, -1, -1):
        row = dist[y]
        nxt = dist[y + 1] if y < h - 1 else None
        for x in range(w - 1, -1, -1):
            if row[x] == 0.0:
                continue
            best = row[x]
            if x < w - 1:
                best = min(best, row[x + 1] + 1.0)
            if nxt is not None:
                best = min(best, nxt[x] + 1.0)
                if x < w - 1:
                    best = min(best, nxt[x + 1] + diag)
                if x > 0:
                    best = min(best, nxt[x - 1] + diag)
            row[x] = best

    height = [[dist[y][x] if dist[y][x] < plateau else plateau for x in range(w)] for y in range(h)]

    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    op = out.load()
    for y in range(h):
        hrow = height[y]
        hup = height[y - 1] if y > 0 else hrow
        hdn = height[y + 1] if y < h - 1 else hrow
        for x in range(w):
            if apx[x, y] == 0:
                continue
            hl = hrow[x - 1] if x > 0 else hrow[x]
            hr = hrow[x + 1] if x < w - 1 else hrow[x]
            hu = hup[x]
            hd = hdn[x]
            dx = (hr - hl) * 0.5 * strength
            dy = (hd - hu) * 0.5 * strength
            nx, ny, nz = -dx, -dy, 1.0
            length = (nx * nx + ny * ny + nz * nz) ** 0.5
            nx, ny, nz = nx / length, ny / length, nz / length
            op[x, y] = (
                _clamp8((nx * 0.5 + 0.5) * 255),
                _clamp8((ny * 0.5 + 0.5) * 255),
                _clamp8((nz * 0.5 + 0.5) * 255),
                255,
            )
    return out
