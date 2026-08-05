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

    W1 §4가 만드는 `_n.png` 노멀맵은 여기서 뺀다 — RGB가 색이 아니라 탄젠트
    공간 표면 방향을 인코딩한 것이라(예: 평평한 면 `(128,128,255)`) 애초에
    §4.2 팔레트 대상이 아니다. 걸러내지 않으면 노멀맵을 뽑을 때마다 감사가
    "팔레트 밖 색"으로 오탐해 빌드가 실패한다.
    """
    bad: list[str] = []
    for root, _, files in os.walk(directory):
        for name in files:
            if not name.endswith(".png") or name.endswith("_n.png"):
                continue
            path = os.path.join(root, name)
            strays = P.check(Image.open(path))
            if strays:
                rel = os.path.relpath(path, directory)
                bad.append(f"{rel}: {sorted(set(strays))[:4]}")
    return bad


def _with_normals(names: set[str]) -> set[str]:
    """
    `keep` 집합에 `_n.png` 노멀맵 파일명도 더한다(W1 §4 계약 — 원본 옆
    `_n` 접미사, 같은 이름 규칙).

    `tiles.generate()`는 노멀맵을 **`index`에 등록하지 않는다**(아틀라스·
    매니페스트에서 빼야 하므로, `tiles._save_pair()` 참고) — 그래서
    `_prune_orphans()`에 넘기는 `keep` 집합도 원본 파일명만 갖고 있다.
    등록하지 않은 파일은 이번 실행이 안 만든 것으로 보여 그대로 두면
    `_prune_orphans()`가 고아로 오인해 지워버린다. 원본 이름에서 기계적으로
    유도할 수 있으므로(항상 `{stem}_n.png`) 여기서 파생시켜 지키는 편이,
    `tiles.py` 쪽에 별도 반환값을 만들어 매니페스트와 뒤섞는 것보다 낫다.
    """
    out = set(names)
    for name in names:
        if name.endswith(".png"):
            out.add(f"{name[:-4]}_n.png")
    return out


def _prune_orphans(directory: str, keep: set[str]) -> list[str]:
    """
    `directory` 안에서 이번 실행이 만들지 않은 파일(과 그 `.meta`)을 지운다.

    이름(→ 해시 파일명)이 바뀌거나 없어진 소품이 있으면 옛 파일이 지워지지
    않고 **고아**로 남는다 — 겉보기엔 무해하지만 `_audit()`가 그 파일도 검사
    대상에 넣어서, 팔레트 값이 바뀔 때마다(D-1 §2 `ramp()` 교체처럼) 아무도
    안 만든 옛 파일이 "팔레트 밖 색"으로 걸린다(`props/prop_00023.png`가
    실제로 그랬다). 디렉터리를 통째로 비우고 다시 굽지 않는 이유는 Unity
    `.meta`가 스프라이트 GUID를 들고 있어서다 — 이름이 그대로인 파일까지
    지웠다 다시 만들면 참조가 다 끊긴다. **고아만** 지운다.
    """
    if not os.path.isdir(directory):
        return []
    removed = []
    for name in os.listdir(directory):
        if name in keep or name.endswith(".meta") and name[:-5] in keep:
            continue
        if not (name.endswith(".png") or name.endswith(".png.meta")):
            continue
        os.remove(os.path.join(directory, name))
        removed.append(name)
    return removed


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
    orphans = _prune_orphans(
        os.path.join(OUT, "chars"),
        {os.path.basename(s["file"]) for s in char_manifest["sheets"]})

    # ── §6.3 타일 · 소품 ──
    tile_index = tiles.generate(OUT)
    print(f"타일    바닥 {len(tile_index['floors'])}종 · "
          f"벽 {len(tile_index['walls'])}종×16 · 벽 정면 {len(tile_index.get('wallFaces', []))}종 · "
          f"소품 {len(tile_index['props'])}종")
    orphans += _prune_orphans(
        os.path.join(OUT, "props"),
        _with_normals({os.path.basename(p["file"]) for p in tile_index["props"]}))
    tiles_keep = {os.path.basename(f["file"]) for f in tile_index["floors"]}
    tiles_keep |= {os.path.basename(f) for f in tile_index.get("snow", [])}
    for w in tile_index["walls"]:
        tiles_keep |= {os.path.basename(f) for f in w["files"]}
    tiles_keep |= {os.path.basename(f["file"]) for f in tile_index.get("wallFaces", [])}
    orphans += _prune_orphans(os.path.join(OUT, "tiles"), _with_normals(tiles_keep))
    orphans += _prune_orphans(
        os.path.join(OUT, "markers"),
        {os.path.basename(m["file"]) for m in tile_index["markers"]})

    # ── §9.1 파티클 ──
    vfx_index = vfx.generate(os.path.join(OUT, "vfx"))
    print(f"파티클  알갱이 {len(vfx_index['sprites'])}종")
    orphans += _prune_orphans(
        os.path.join(OUT, "vfx"),
        {f"{s['name']}.png" for s in vfx_index["sprites"]})

    if orphans:
        print(f"고아 파일 정리: {len(orphans)}개 ({', '.join(sorted(orphans)[:6])}"
              f"{' ...' if len(orphans) > 6 else ''})")

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
