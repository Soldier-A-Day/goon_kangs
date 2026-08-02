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
