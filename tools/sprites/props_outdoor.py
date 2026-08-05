"""
소품 야외·잡화 계열 그림 (C3 — 조각 합성).

`parts.py`의 조각 어휘로 소품을 **여러 조각을 겹쳐** 만든다. 단색 사각형이던
`tiles._box()`를 대신한다.

**타일 크기(w,h)는 절대 바꾸지 마라** — `base_map.json`이 그 크기로 배치돼 있어
바꾸면 맵이 어긋난다. 이 파일은 **그리는 함수만** 갈아 끼운다.

`BUILDERS`에 등록된 이름은 `tiles.PROPS`의 같은 이름 항목의 그리는 함수를 대체한다.
"""

from __future__ import annotations

import parts
import palette as P
import pixel as PX

TILE = parts.TILE

#: 소품 이름 → 그리는 함수 `f(w, h) -> Image`. 비어 있으면 기존 그림이 그대로 쓰인다
BUILDERS: dict[str, object] = {}
