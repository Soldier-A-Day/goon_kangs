# 에셋 배치 규약

```
Assets/Art/{카테고리}/{에셋 id}/...
```

`에셋 id`는 `packages/assets/data/manifest.json`의 `id`를 **그대로** 쓴다.
폴더 이름이 곧 카탈로그 키다.

| 카테고리 | 예시 경로 |
|---|---|
| `character` | `Assets/Art/character/char.base.player/player.fbx` |
| `clothing` | `Assets/Art/clothing/cloth.top/combat_top.fbx` |
| `baseMap` | `Assets/Art/baseMap/base.drillGround/drill_ground.fbx` |
| `trainingMap` | `Assets/Art/trainingMap/train.range/range.fbx` |
| `prop` | `Assets/Art/prop/prop.small/broom.fbx` |
| `equipment` | `Assets/Art/equipment/equip.rifle/rifle.fbx` |

이름으로 잇는 것이 취약해 보이지만, 대안인 ScriptableObject 매핑은 파일을 옮길 때
조용히 끊어지고 아무도 모른다. 규약을 벗어난 경로는 임포트할 때 경고가 뜨고
예산 검사에서 빠진다 — **틀리면 알 수 있다**는 것이 이 방식의 값어치다.

## 텍스처 접미사

임포트 시점에는 텍스처 내용을 볼 수 없으므로 이름으로 구분한다.

| 접미사 | 용도 | sRGB |
|---|---|---|
| `_A` | 알베도 | O |
| `_N` | 노멀 | X (NormalMap 타입으로 전환) |
| `_M`, `_MRA` | 마스크·러프니스·AO | X |

마스크를 sRGB로 두면 값이 감마를 타서 셰이더 계산이 틀어지는데,
화면에서는 "조금 이상한 재질"로만 보여 원인을 찾기 어렵다.

## LOD

`_LOD1` / `_LOD2` / `_LOD3` 접미사를 쓴다. 예산 검사는 **LOD0만** 센다 —
LOD는 감산율로 파생되는 것이라 함께 세면 같은 메시를 두 번 세게 된다.

감산율은 카탈로그의 `importRules.lodRatios`에 있다 (50% / 20% / 5%).

## 자동으로 강제되는 것

`SoldierImportRules`가 임포트할 때마다 적용한다. 인스펙터에서 되돌려도
다시 임포트하면 규칙으로 돌아온다.

- 텍스처: DXT1/DXT5, 최대 2048, 밉맵 + 스트리밍 (ARCH-03은 DXT 단일 — ASTC를 함께 구우면 번들이 두 벌이 된다)
- 메시: 압축 Medium, **읽기 끔** (켜두면 CPU 사본이 남아 메모리가 두 배가 된다)
- 리그: 카탈로그가 `rig: humanoid`라 적은 것만 Humanoid. 4보직이 애니메이션을 공유하므로(16.0) 리그가 한 벌만 어긋나도 그 캐릭터만 리타게팅에서 빠진다

## 검사

```bash
npm test -w @sad/assets          # 카탈로그가 스스로 모순되지 않는지
npm run assets:check             # 파일이 그 기준을 지키는지 (M0 범위)
```

순서가 있다. 기준이 틀린 채로 파일을 검사하면 아무 의미가 없다.
