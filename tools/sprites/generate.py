"""
SOLDIER : A DAY — 2D 에셋 생성기 (SAD-ART-001).

에셋 파일 대신 **에셋을 만드는 코드**가 저장소에 있다. 배치가 바뀌면 다시 뽑으면
되고, 왜 그 모양인지가 코드에 남는다.

    python3 tools/sprites/generate.py

산출은 전부 `unity/Assets/Art/2d/` 아래로 간다.

    palettes/   §4.2 32색 스와치 (픽셀 아티스트 지급용)
    chars/      §5 캐릭터 8레이어 × 변형 시트 (셀 32×48)
    tiles/      §6 바닥 · 벽 오토타일 (32×32)
    props/      §6.2 TM_Object — Y-sort 대상이라 Tilemap이 아니라 개별 스프라이트
    vfx/        §9.1 파티클 알갱이 (시스템 자체는 씬 빌더가 세운다)
    art2d.json  Unity 씬 빌더가 읽는 색인
    base_map.json  §6.4 부대 본영 — 구역 15종 + 타일 레이어 + 상호작용 지점
"""

from __future__ import annotations

import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from PIL import Image

import basemap
import chars
import palette as P
import tiles
import trainmap
import vfx

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "unity", "Assets", "Art", "2d")


def _write_json(path: str, data) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)


def _audit(directory: str) -> list[str]:
    """
    §4.2 "팔레트 외 색 사용 금지"를 산출물에 대해 실제로 검사한다.

    규칙을 문서에만 적어두면 지켜지지 않는다. 온도 밴드 그레이딩(§4.3)이 팔레트를
    통째로 밀어내는 방식이라 원본이 흔들리면 6밴드가 전부 어긋난다.
    """
    bad: list[str] = []
    for root, _, files in os.walk(directory):
        for name in files:
            if not name.endswith(".png"):
                continue
            path = os.path.join(root, name)
            strays = P.check(Image.open(path))
            if strays:
                rel = os.path.relpath(path, directory)
                bad.append(f"{rel}: {sorted(set(strays))[:4]}")
    return bad


def main() -> int:
    os.makedirs(OUT, exist_ok=True)

    # ── §4.2 팔레트 스와치 ──
    palettes = os.path.join(OUT, "palettes")
    os.makedirs(palettes, exist_ok=True)
    P.swatch(os.path.join(palettes, "world32.png"))

    # ── §5 캐릭터 ──
    char_manifest = chars.generate(os.path.join(OUT, "chars"))
    sheets = len(char_manifest["sheets"])
    rows = len(char_manifest["rows"])
    print(f"캐릭터  시트 {sheets}장 · 행 {rows} · 열 {char_manifest['cols']} "
          f"· 클립 {len(char_manifest['clips'])}종")

    # ── §6.3 타일 · 소품 ──
    tile_index = tiles.generate(OUT)
    print(f"타일    바닥 {len(tile_index['floors'])}종 · "
          f"벽 {len(tile_index['walls'])}종×16 · 소품 {len(tile_index['props'])}종")

    # ── §9.1 파티클 ──
    vfx_index = vfx.generate(os.path.join(OUT, "vfx"))
    print(f"파티클  알갱이 {len(vfx_index['sprites'])}종")

    # ── §6.4 부대 본영 ──
    base = basemap.build()

    _write_json(os.path.join(OUT, "base_map.json"), base)
    print(f"부대 맵 {base['width']}×{base['height']} 타일 "
          f"({base['width'] * base['tile']}×{base['height'] * base['tile']} px) · "
          f"구역 {len(base['zones'])} · 소품 {len(base['props'])} · "
          f"문 {len(base['doors'])}")

    # ── §6.4 훈련 맵 ──
    #
    # 탑다운 7종은 이미 부대 맵에 얹혀 있다(`basemap._training`). 여기서 따로
    # 내보내는 것은 **사이드뷰 코스 데이터**와 훈련 종류 → 맵 대응표다 —
    # 지면 높이와 구간 이름은 타일에 담기지 않는다
    train = trainmap.build()
    _write_json(os.path.join(OUT, "train_maps.json"), train)
    print(f"훈련 맵 탑다운 {len(train['topdown'])}종 · 사이드뷰 {len(train['lanes'])}종")

    _write_json(os.path.join(OUT, "art2d.json"), {
        "chars": char_manifest,
        "tiles": tile_index,
        "vfx": vfx_index,
    })

    strays = _audit(OUT)
    if strays:
        print("\n팔레트 밖 색이 섞였다 (§4.2 위반):", file=sys.stderr)
        for line in strays[:12]:
            print(f"  {line}", file=sys.stderr)
        return 1

    print("팔레트 검사 통과 — 산출물에 §4 팔레트 밖 색 없음")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
