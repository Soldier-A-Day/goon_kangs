using System.Collections.Generic;
using System.IO;
using System.Linq;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 부대 본영 씬 빌더 (SAD-ART-001 §6.4).
    ///
    /// `base_map.json`과 `art2d.json`을 읽어 **단일 심리스 맵**을 세운다. 씬 파일을
    /// 손으로 만들지 않는 이유는 3D 시절과 같다 — 배치가 바뀌면 다시 만들면 되고,
    /// 왜 그 모양인지가 코드에 남는다.
    ///
    /// 좌표 규약: 타일 (tx, ty)의 셀은 Unity 셀 (tx, H − ty − 1). 타일 좌표는 아래로
    /// 증가하고 Unity y는 위로 증가하므로 뒤집는다. 뒤집고 나면 **화면 아래가 앞**이라는
    /// 정렬 규칙(§6.2 Y-sort)이 y 하나로 성립한다.
    /// </summary>
    public static class BaseScene
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/Base.unity";
        private const string Art2DDir = "Assets/Art/2d";
        /// <summary>생성된 `Tile` 에셋이 사는 곳. 씬이 참조하므로 디스크에 있어야 한다</summary>
        private const string TileDir = "Assets/Art/2d/TileAssets";
        /// <summary>§12.2 폴더 구조의 `Settings/Volumes/` — 밴드별 프로파일 7종</summary>
        private const string VolumeDir = "Assets/Settings/Volumes";
        private const string MaterialDir = "Assets/Settings/Materials";
        /// <summary>§W2 절차적 그림자·AO 스프라이트가 사는 곳. Art/2d 밖에 둔다 — 손으로 그린 자산이 아니다</summary>
        private const string DepthDir = "Assets/Settings/WorldDepth";

        /// <summary>정적 소품과 캐릭터가 **같은 공식**을 써야 서로 가린다</summary>
        private const float SortScale = CharacterRig.SortScale;

        [MenuItem("SOLDIER/부대 본영 2D 씬 생성")]
        public static void CreateScene()
        {
            var map = LoadMap();
            var art = LoadArt();
            if (map == null || art == null) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("부대");
            var height = map.height;

            var library = BuildLibrary(art);
            var world = root.AddComponent<ZoneWorld>();

            var snow = BuildTilemaps(root.transform, map, art, height);
            var steamAt = new List<Transform>();
            var zones = BuildZones(root.transform, map, art, height, steamAt);
            var camera = BuildCamera(root.transform);
            var grading = BuildGrading(root.transform);
            var screenFx = BuildScreenEffects(root.transform, grading);
            var vfx = BuildParticles(root.transform, camera, steamAt, art);
            BuildRuntime(root.transform, library, world, camera, zones, grading, screenFx, vfx, snow, map, height);

            // §9.3 W3 — 조명·머티리얼. `BuildRuntime` 다음에 두는 것은 `grading`·
            // `camera`가 그때서야 배선할 준비가 끝나 있어서다(둘 다 그 앞에서 만들어짐)
            ApplyWorldLighting(root.transform, map, zones, grading, camera);

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);

            Debug.Log($"[부대] {map.width}×{map.height} 타일 · 구역 {map.zones.Length} · " +
                      $"소품 {map.props.Length} · 시트 {library.sheets.Length} → {ScenePath}");
        }

        /* ══════════════════════════════════════════════════════ 타일맵 */

        /// <summary>
        /// §6.2 타일맵 레이어 스택.
        ///
        /// Sorting Layer를 쓰지 않고 order로만 가른다 — Sorting Layer는 프로젝트
        /// 설정에 있어서 씬 빌더가 만들 수 없고, 없는 레이어 이름을 쓰면 조용히
        /// Default로 떨어진다. order는 씬 안에서 완결된다.
        /// </summary>
        private static SnowCover BuildTilemaps(Transform parent, BaseMap map, Art2D art, int height)
        {
            var grid = new GameObject("Grid");
            grid.transform.SetParent(parent, false);
            var component = grid.AddComponent<Grid>();
            component.cellSize = Vector3.one;   // 타일 32px = PPU 32 = 1유닛

            var tiles = BuildTileAssets(art);

            // Ground(0) · GroundDeco(1) — 밟고 지나가는 것. 캐릭터보다 항상 뒤
            Paint(grid.transform, "TM_Ground", map.layers.ground, tiles, height, -32000);
            Paint(grid.transform, "TM_GroundDeco", map.layers.groundDeco, tiles, height, -31000);

            // Wall — 몸이 걸린다. §6.1 "Tilemap Collider 2D + Composite Collider 2D"
            var wall = Paint(grid.transform, "TM_Wall", map.layers.wall, tiles, height, -30000);
            if (wall != null)
            {
                var collider = wall.gameObject.AddComponent<TilemapCollider2D>();
                collider.compositeOperation = Collider2D.CompositeOperation.Merge;

                var body = wall.gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Static;

                // 합치지 않으면 벽 한 칸마다 콜라이더가 하나씩 생기고, 8,800칸짜리
                // 맵에서 그건 물리 갱신만으로 프레임을 먹는다
                wall.gameObject.AddComponent<CompositeCollider2D>().geometryType =
                    CompositeCollider2D.GeometryType.Polygons;
            }

            // §W2 벽 높이(오블리크) 배치 · 바닥 AO — 콜라이더는 위에서 이미 끝났다.
            // 둘 다 시각 전용이라 걷기 판정에는 영향이 없다. 벽·바닥 칸 집합은
            // 둘이 같이 쓰므로 한 번만 모은다(8,800칸을 두 번 훑지 않는다)
            var cells = CollectMapCells(map);
            BuildWallFaces(grid.transform, map, cells, height);
            BuildFloorAo(grid.transform, cells, height);

            // §W2 ② 캐스트 섀도우(캐릭터, 런타임). 플레이어는 씬 빌드 시점에 이미
            // 있지만 분대원은 스냅샷 이후 `SquadView`가 늦게 만든다 — 그래서 소품처럼
            // 미리 구워둘 수 없고, 낮은 주기로 찾아 붙이는 매니저 하나를 심어둔다
            var depth = new GameObject("WorldDepth");
            depth.transform.SetParent(parent, false);
            depth.AddComponent<WorldDepthShadowManager>();

            return BuildSnow(grid.transform, map, tiles, height);
        }

        /* ══════════════════════════════════════ §W2 벽 오블리크 정면 · 바닥 AO */

        /// <summary>계약(§W2)상 정면 스프라이트가 있는 kind. `fence`는 없다 — 조용히 건너뛴다</summary>
        private static readonly HashSet<string> WallFaceKinds =
            new HashSet<string> { "interior", "utility", "outdoor", "wood" };

        /// <summary>
        /// §W2 ① 벽 정면(오블리크) 배치.
        ///
        /// 남쪽 이웃이 벽이 아니고 남쪽에 바닥이 있는 벽 칸 아래에, 그 칸 바로
        /// 아래 26px를 덮는 정면 스프라이트를 얹는다(바닥 위에 그려진다). 콜라이더는
        /// 건드리지 않는다 — 걷기 판정은 위 `TM_Wall`이 그대로 맡는다.
        ///
        /// W1이 `wall_{kind}_face.png`를 아직 안 냈으면 `LoadSprite`가 null을 돌려주고
        /// 그 kind만 건너뛴다 — 예외로 씬 생성이 멈추지 않는다.
        /// </summary>
        /// <summary>벽 정면(오블리크) 낱개 스프라이트를 담는 컨테이너. 조명 스윕이
        /// 이 이름으로 찾아 Lit을 입힌다 — 이름이 바뀌면 벽 위/아래 밝기가 어긋난다</summary>
        private const string WallFaceLayerName = "TM_WallFace";

        /// <summary>바닥 AO 타일맵. 조명 스윕이 이 이름으로 **제외**한다 — 어둡게
        /// 덮는 오버레이라 Lit이 되면 등 아래에서 스스로 밝아진다</summary>
        private const string FloorAoLayerName = "TM_FloorAO";

        private static void BuildWallFaces(Transform grid, BaseMap map, MapCells cells, int height)
        {
            if (map.layers?.wall == null || map.layers.wall.Length == 0) return;

            var wallCells = cells.Wall;
            var floorCells = cells.Floor;

            var container = new GameObject(WallFaceLayerName);
            container.transform.SetParent(grid, false);

            // kind별로 한 번만 로드 — 8,800칸을 훑어도 `AssetDatabase` 조회는 kind 수만큼만
            var spriteByKind = new Dictionary<string, Sprite>();

            foreach (var run in map.layers.wall)
            {
                if (string.IsNullOrEmpty(run.tile) || run.cells == null) continue;
                var kind = run.tile.StartsWith("wall:") ? run.tile.Substring(5) : run.tile;

                for (var i = 0; i + 1 < run.cells.Length; i += 2)
                {
                    var tx = run.cells[i];
                    var ty = run.cells[i + 1];

                    // 타일 좌표는 아래로 증가한다(§ 클래스 주석) — 남쪽은 ty + 1
                    var south = CellKey(tx, ty + 1);
                    if (wallCells.Contains(south)) continue;    // 남쪽도 벽 — 정면 없음
                    if (!floorCells.Contains(south)) continue;  // 남쪽에 바닥이 없다

                    if (!spriteByKind.TryGetValue(kind, out var sprite))
                    {
                        sprite = LoadSprite($"tiles/wall_{kind}_face.png");
                        spriteByKind[kind] = sprite;   // null도 캐시한다 — 재조회 방지
                        if (sprite == null && WallFaceKinds.Contains(kind))
                            Debug.Log($"[부대] 벽 정면 아직 없음(건너뜀): wall_{kind}_face.png");
                    }
                    if (sprite == null) continue;

                    PlaceWallFace(container.transform, sprite, tx, ty, height);
                }
            }
        }

        /// <summary>
        /// 정면 스프라이트 하나를 놓는다. 피벗이 무엇이든(Sprite2DImport는 `tiles/`
        /// 아래를 전부 Center로 놓는다 — 실측해 확인했다) `sprite.bounds`로 실측해
        /// 자리를 잡는다 — 위 변이 벽 밑변에 맞닿고, 가로 중심이 칸 중앙에 오도록.
        ///
        /// **좌표 검증(§W4)**: `wall_interior_face.png`(32×26, Center 피벗)를 (tx=25,
        /// ty=5, height=224)에 넣으면 `wallBottomY=218`, `bounds.max.y=13/32=0.40625`
        /// → position.y = 218 − 0.40625 = 217.59375, 스프라이트가 실제로 차지하는
        /// 구간은 [217.59375−0.40625, 217.59375+0.40625) = **[217.1875, 218)**.
        /// 정확히 과제가 요구한 [223−ty−0.8125, 223−ty) = [217.1875, 218)와 일치한다.
        /// 즉 자리는 원래도 맞았다(씬 파일에 구운 실제 좌표로 재확인함).
        /// </summary>
        private static void PlaceWallFace(Transform parent, Sprite sprite, int tx, int ty, int height)
        {
            var wallBottomY = height - ty - 1;   // 벽 칸의 아랫변 = 남쪽 바닥 칸의 윗변
            var bounds = sprite.bounds;

            var go = new GameObject($"WallFace_{tx}_{ty}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(
                (tx + 0.5f) - bounds.center.x,
                wallBottomY - bounds.max.y,
                0f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            // §W4 실측: 벽 타일(`wall_*_15.png`) 자체가 이미 32px 안에 밑단
            // 어두운 톤(예: RGB 103,104,71)을 굽고 있고, 정면 PNG의 **끝 쪽**(파일
            // 기준 아래쪽 행)이 정확히 같은 톤이다 — 두 톤이 이어지도록 만든 자산이다.
            // 하지만 위치 계산과 무관하게 스프라이트를 "제 위" 그대로 그리면 벽의
            // 어두운 끝단 바로 아래에 정면의 밝은 시작 행이 와서 톤이 다시 밝아졌다
            // 어두워지는 이음매가 생기고, 이게 "벽 무늬가 두 번 나온다"는 밝은 띠로
            // 보인다. 세로로 뒤집으면(FlipY) 같은 톤끼리 맞닿아 이음매가 사라지고
            // 정면 전체가 벽→바닥으로 이어지는 단일 그라디언트로 읽힌다. 위치(좌표)는
            // 그대로다 — bounds는 FlipY 영향을 받지 않는다
            renderer.flipY = true;

            // 정면의 "밑변"이 Y소트 기준선이다 — 소품·캐릭터와 같은 공식(§6.2).
            // 캐릭터가 그보다 남쪽(작은 y)이면 앞, 북쪽(큰 y)이면 정면에 가려진다
            var splitY = wallBottomY - bounds.size.y;
            renderer.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-splitY * SortScale), -29000, 32000);
        }

        /// <summary>
        /// §W4 바닥 AO 방향별 회전각. 텍스처(`WorldDepth.BuildAoEdgeTexture`)는 "북쪽에
        /// 벽이 있다"(위가 진하다) 방향 하나만 굽는다 — 나머지는 90°씩 돌려 재사용한다.
        /// 남쪽은 180°(위→아래), 서쪽은 +90°(위→왼쪽), 동쪽은 −90°(위→오른쪽)다.
        /// </summary>
        private static readonly (int dx, int dy, float rotZ)[] AoEdgeDirs =
        {
            (0, -1, 0f),     // 북쪽(ty-1)에 벽 — 진한 쪽이 위(회전 없음)
            (0, 1, 180f),    // 남쪽(ty+1)에 벽 — 진한 쪽이 아래
            (-1, 0, 90f),    // 서쪽(tx-1)에 벽 — 진한 쪽이 왼쪽
            (1, 0, -90f),    // 동쪽(tx+1)에 벽 — 진한 쪽이 오른쪽
        };

        /// <summary>
        /// §W2/§W4 바닥 AO. 벽에 붙은 바닥 칸마다, 벽이 있는 쪽으로 회전한 그라디언트
        /// 스프라이트를 얹는다 — 벽 쪽이 진하고 칸 안쪽으로 갈수록 0까지 부드럽게
        /// 빠진다. **타일 단위 균일 알파(예전 방식)는 계단 현상이 났다** — 벽 정면과
        /// 같은 "낱개 스프라이트" 방식으로 바꿔 일관되게 만들었다. 강도는
        /// `WorldDepth.AoAlpha` 하나 — 0으로 두면 완전히 꺼진다.
        /// </summary>
        private static void BuildFloorAo(Transform grid, MapCells cells, int height)
        {
            var wallCells = cells.Wall;
            var floorCells = cells.Floor;
            if (wallCells.Count == 0 || floorCells.Count == 0) return;
            if (WorldDepth.AoAlpha <= 0f) return;   // 상수 하나로 끌 수 있다

            var sprite = AoEdgeSprite();
            if (sprite == null) return;

            // 일반 GameObject(타일맵이 아니다) — 조명 스윕(`ApplyWorldLighting`)은
            // "Grid" 밑의 `TilemapRenderer`와, 이름이 `TM_WallFace`인 컨테이너만
            // 콕 집어 Lit을 입힌다. 이 컨테이너는 둘 중 어디에도 안 걸리므로 손대지
            // 않아도 자동으로 unlit으로 남는다(AO는 빛을 받으면 안 된다 — §W2)
            var container = new GameObject(FloorAoLayerName);
            container.transform.SetParent(grid, false);

            foreach (var key in floorCells)
            {
                var tx = (int)(key >> 32);
                var ty = (int)(key & 0xFFFFFFFFL);

                foreach (var (dx, dy, rotZ) in AoEdgeDirs)
                {
                    if (!wallCells.Contains(CellKey(tx + dx, ty + dy))) continue;
                    PlaceAoEdge(container.transform, sprite, tx, height - ty - 1, rotZ);
                }
            }
        }

        /// <summary>AO 그라디언트 한 장을 셀 중앙에 놓고 벽 쪽으로 회전시킨다</summary>
        private static void PlaceAoEdge(Transform parent, Sprite sprite, int tx, int worldY, float rotZ)
        {
            var go = new GameObject($"AO_{tx}_{worldY}_{rotZ}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(tx + 0.5f, worldY + 0.5f, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, rotZ);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(0f, 0f, 0f, WorldDepth.AoAlpha);
            // 눈(-30500) 위, 벽(-30000) 아래 — 예전 타일맵과 같은 자리
            renderer.sortingOrder = -30200;
        }

        /// <summary>벽 칸 · 바닥 칸 집합. `BuildWallFaces`와 `BuildFloorAo`가 같이 쓰므로 한 번만 모은다</summary>
        private readonly struct MapCells
        {
            public readonly HashSet<long> Wall;
            public readonly HashSet<long> Floor;

            public MapCells(HashSet<long> wall, HashSet<long> floor)
            {
                Wall = wall;
                Floor = floor;
            }
        }

        private static MapCells CollectMapCells(BaseMap map)
        {
            var wall = new HashSet<long>();
            var floor = new HashSet<long>();
            if (map.layers != null)
            {
                CollectCells(wall, map.layers.wall);
                CollectCells(floor, map.layers.ground);
                CollectCells(floor, map.layers.groundDeco);
            }
            return new MapCells(wall, floor);
        }

        /// <summary>배열 전체를 모은다. `runs`가 null이어도(레이어가 비어 있어도) 안전하다</summary>
        private static void CollectCells(HashSet<long> set, BaseMap.TileRun[] runs)
        {
            if (runs == null) return;
            foreach (var run in runs) CollectCells(set, run);
        }

        private static void CollectCells(HashSet<long> set, BaseMap.TileRun run)
        {
            if (run?.cells == null) return;
            for (var i = 0; i + 1 < run.cells.Length; i += 2)
                set.Add(CellKey(run.cells[i], run.cells[i + 1]));
        }

        /// <summary>타일 좌표 (x, y) → 해시 키. 맵 크기(≪2^31)에서 충돌 없이 하나로 접는다</summary>
        private static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>
        /// §6.3 눈 오버레이.
        ///
        /// **`TM_GroundDeco` 위, 벽 아래**다. 바닥은 덮어야 하고 벽은 안 덮어야
        /// 한다 — 눈이 벽 위로 올라오면 어디까지가 걸을 수 있는 땅인지 안 읽힌다.
        ///
        /// 처음에는 아무 타일도 안 깐 채로 둔다. 한랭 이하 밴드가 오면
        /// `SnowCover`가 깔고, 제설이 끝나면 그 구역만 지운다.
        /// </summary>
        private static SnowCover BuildSnow(Transform grid, BaseMap map,
                                           Dictionary<string, TileBase> tiles, int height)
        {
            if (map.snow == null || map.snow.Length == 0) return null;

            var go = new GameObject("TM_Snow");
            go.transform.SetParent(grid, false);
            go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = -30500;   // 바닥장식(-31000)과 벽(-30000) 사이
            renderer.mode = TilemapRenderer.Mode.Chunk;

            var cover = go.AddComponent<SnowCover>();
            cover.tilemap = go.GetComponent<Tilemap>();

            var patches = new List<SnowCover.Patch>();
            foreach (var patch in map.snow)
            {
                if (patch.cells == null || patch.cells.Length == 0) continue;
                if (!tiles.TryGetValue($"snow:{patch.level}", out var tile)) continue;

                // 타일 좌표는 y가 뒤집혀 있다(맵은 위에서부터, Unity는 아래에서부터)
                var flipped = new int[patch.cells.Length];
                for (var i = 0; i + 1 < patch.cells.Length; i += 2)
                {
                    flipped[i] = patch.cells[i];
                    flipped[i + 1] = height - patch.cells[i + 1] - 1;
                }

                patches.Add(new SnowCover.Patch
                {
                    zone = patch.zone,
                    cells = flipped,
                    tile = tile,
                });
            }

            cover.patches = patches.ToArray();
            return cover;
        }

        private static Tilemap Paint(Transform grid, string name, BaseMap.TileRun[] runs,
                                     Dictionary<string, TileBase> tiles, int height, int order)
        {
            if (runs == null || runs.Length == 0) return null;

            var go = new GameObject(name);
            go.transform.SetParent(grid, false);
            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            renderer.sortingOrder = order;
            renderer.mode = TilemapRenderer.Mode.Chunk;

            var positions = new List<Vector3Int>();
            var assets = new List<TileBase>();

            foreach (var run in runs)
            {
                if (string.IsNullOrEmpty(run.tile) || run.cells == null) continue;
                if (!tiles.TryGetValue(run.tile, out var tile) || tile == null) continue;

                for (var i = 0; i + 1 < run.cells.Length; i += 2)
                {
                    positions.Add(new Vector3Int(run.cells[i], height - run.cells[i + 1] - 1, 0));
                    assets.Add(tile);
                }
            }

            // 한 칸씩 `SetTile`하면 8,800번 호출이 되고 에디터가 몇 초씩 멈춘다
            tilemap.SetTiles(positions.ToArray(), assets.ToArray());
            return tilemap;
        }

        /// <summary>
        /// 스프라이트를 `Tile` 에셋으로 감싼다.
        ///
        /// 벽은 오토타일 16장이지만 **여기서는 마스크를 계산하지 않는다** — 오소링
        /// 쪽이 `wall:interior` 하나로만 내보내고, 이웃을 보고 고르는 것은 이 아래
        /// `Autotile`이 한다. 두 곳에서 마스크를 만들면 반드시 어긋난다.
        /// </summary>
        private static Dictionary<string, TileBase> BuildTileAssets(Art2D art)
        {
            var tiles = new Dictionary<string, TileBase>();
            Directory.CreateDirectory(TileDir);

            foreach (var floor in art.tiles.floors)
            {
                var sprite = LoadSprite(floor.file);
                if (sprite == null)
                {
                    Debug.LogWarning($"[부대] 바닥 타일 없음: {floor.file}");
                    continue;
                }
                tiles["floor:" + floor.kind] = MakeTile($"floor_{floor.kind}", sprite, false);
            }

            foreach (var wall in art.tiles.walls)
            {
                // 오토타일 16장 중 "사방이 이어진" 마스크 15를 대표로 쓴다.
                // 벽을 사각형으로 두르는 현재 오소링에서는 안쪽 면이 대부분이고,
                // 모서리 마감은 §6 다음 개정에서 RuleTile로 옮긴다
                var sprite = LoadSprite(wall.files[15]);
                if (sprite == null)
                {
                    Debug.LogWarning($"[부대] 벽 타일 없음: {wall.files[15]}");
                    continue;
                }
                tiles["wall:" + wall.kind] = MakeTile($"wall_{wall.kind}", sprite, true);
            }

            // §6.3 `TS_Snow` 오버레이 — 두께 4단계
            for (var level = 0; level < (art.tiles.snow?.Length ?? 0); level += 1)
            {
                var sprite = LoadSprite(art.tiles.snow[level]);
                if (sprite == null) continue;
                tiles[$"snow:{level}"] = MakeTile($"snow_{level}", sprite, false);
            }

            return tiles;
        }

        /// <summary>
        /// `Tile`을 **에셋으로 저장한다.**
        ///
        /// 메모리에만 만든 `ScriptableObject`는 씬에 참조를 남길 수 없어서, 씬을
        /// 저장하는 순간 `m_TileAssetArray`가 통째로 비어버린다 — 타일맵이 있는데
        /// 아무것도 안 그려지는 상태가 되고, 로그에는 아무 말도 안 나온다.
        /// </summary>
        private static Tile MakeTile(string name, Sprite sprite, bool solid)
        {
            var path = $"{TileDir}/{name}.asset";
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            tile.sprite = sprite;
            tile.colliderType = solid ? Tile.ColliderType.Grid : Tile.ColliderType.None;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static Sprite _shadowSpriteCache;
        private static Sprite _aoSpriteCache;

        /// <summary>§W2 소품 그림자용 절차적 스프라이트. 캐릭터(런타임)와 같은 픽셀을 `WorldDepth`에서 만든다</summary>
        private static Sprite ShadowSprite() =>
            _shadowSpriteCache ??= BakeProceduralSprite(
                "WorldDepth_Shadow", WorldDepth.BuildShadowTexture, WorldDepth.BuildShadowSprite);

        /// <summary>§W4 바닥 AO 그라디언트 스프라이트. 렌더러 색(alpha)이 강도를 준다(상수 하나)</summary>
        private static Sprite AoEdgeSprite() =>
            _aoSpriteCache ??= BakeProceduralSprite(
                "WorldDepth_AOEdge", WorldDepth.BuildAoEdgeTexture, WorldDepth.BuildAoEdgeSprite);

        /// <summary>
        /// 절차적으로 만든 스프라이트를 **에셋으로 저장한다.** `MakeTile`과 같은 이유다 —
        /// `Sprite.Create`로만 만든 것은 메모리에 있어 씬 저장 시 참조가 끊긴다.
        /// 텍스처를 메인 자산으로, 스프라이트를 그 서브 자산으로 같이 묶는다.
        /// </summary>
        private static Sprite BakeProceduralSprite(string name, System.Func<Texture2D> makeTexture,
                                                   System.Func<Texture2D, Sprite> makeSprite)
        {
            var path = $"{DepthDir}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) return existing;

            Directory.CreateDirectory(DepthDir);
            var texture = makeTexture();
            AssetDatabase.CreateAsset(texture, path);

            var sprite = makeSprite(texture);
            sprite.name = name;
            AssetDatabase.AddObjectToAsset(sprite, texture);

            EditorUtility.SetDirty(texture);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /* ══════════════════════════════════════════════════════ 구역 */

        private static List<ZoneMap> BuildZones(Transform parent, BaseMap map, Art2D art, int height,
                                                List<Transform> steamAt)
        {
            var props = art.tiles.props.ToDictionary(p => p.name, p => p);
            var result = new List<ZoneMap>();
            var byId = new Dictionary<string, ZoneMap>();

            var container = new GameObject("구역");
            container.transform.SetParent(parent, false);

            foreach (var zone in map.zones)
            {
                var go = new GameObject($"{zone.id} {zone.name}");
                go.transform.SetParent(container.transform, false);

                var component = go.AddComponent<ZoneMap>();
                component.id = zone.id;
                component.zoneName = zone.name;
                component.indoor = zone.indoor;
                component.kind = string.IsNullOrEmpty(zone.kind) ? "room" : zone.kind;
                component.area = new Rect(zone.x, height - zone.y - zone.h, zone.w, zone.h);
                component.door = zone.door != null
                    ? Cell(zone.door.x, zone.door.y, height)
                    : component.area.center;
                component.spawn = zone.spawn != null
                    ? Cell(zone.spawn.x, zone.spawn.y, height)
                    : component.area.center;

                result.Add(component);
                byId[zone.id] = component;
            }

            foreach (var placement in map.props)
            {
                if (!byId.TryGetValue(placement.zone, out var zone)) continue;
                if (!props.TryGetValue(placement.name, out var def)) continue;

                var sprite = LoadSprite(def.file);
                if (sprite == null) continue;

                var go = new GameObject(placement.name);
                go.transform.SetParent(zone.transform, false);

                // 피벗이 하단 중앙이라(Sprite2DImport) 아랫변을 놓으면 된다
                var x = placement.x + placement.w * 0.5f;
                var y = height - placement.y - placement.h;
                go.transform.position = new Vector3(x, y, 0f);

                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                // 캐릭터와 같은 공식. 아랫변이 화면 아래일수록 앞이므로,
                // 침상 위쪽을 지나면 가려지고 아래쪽을 지나면 가린다
                renderer.sortingOrder = Mathf.Clamp(Mathf.RoundToInt(-y * SortScale), -29000, 32000);

                // §W2 ② 캐스트 섀도우(정적) — 우하단 고정, 소품 높이(타일)에 비례.
                // 소품은 움직이지 않으니 절대 sortingOrder를 한 번만 계산하면 계속 맞는다
                WorldDepth.AttachStaticShadow(go.transform, ShadowSprite(), placement.h, renderer.sortingOrder);

                if (!placement.walkable)
                {
                    var box = go.AddComponent<BoxCollider2D>();
                    box.size = new Vector2(placement.w, placement.h);
                    box.offset = new Vector2(0f, placement.h * 0.5f);
                }

                zone.Register(placement.name, go.transform);

                // §9.1 `VFX_Steam` "난로 · 취사 · 보일러 — 상승 루프".
                // 열원이 눈에 보여야 §5.0 90초 규칙에서 어디로 뛸지 알 수 있다
                if (SteamSources.Contains(placement.name)) steamAt.Add(go.transform);
            }

            return result;
        }

        /// <summary>§9.1 수증기가 피어오르는 소품</summary>
        private static readonly HashSet<string> SteamSources = new HashSet<string>
        {
            "난로", "보일러 본체", "세척대", "배식대",
        };

        /// <summary>
        /// §9.0 사이드뷰 코스를 런타임 형태로 옮긴다.
        ///
        /// 타일은 이미 `basemap._lanes`가 월드에 깔아뒀다. 여기서 옮기는 것은
        /// **지면 높이 프로파일**이다 — 타일에서 되읽으면 8,000칸을 훑어야 하고,
        /// 그건 이미 JSON에 있는 것을 다시 계산하는 일이다.
        /// </summary>
        private static LaneRun.Lane[] BuildLanes(int height)
        {
            var train = LoadTrain();
            if (train?.lanes == null) return System.Array.Empty<LaneRun.Lane>();

            var lanes = new List<LaneRun.Lane>();
            foreach (var lane in train.lanes)
            {
                lanes.Add(new LaneRun.Lane
                {
                    zone = lane.id,
                    name = lane.name,
                    area = new Rect(lane.x, height - lane.y - lane.h, lane.w, lane.h),
                    ground = lane.ground,
                    segments = lane.segments,
                    legs = lane.legs == null
                        ? System.Array.Empty<string>()
                        : System.Array.ConvertAll(lane.legs, l => l.name),
                });
            }
            return lanes.ToArray();
        }

        private static Vector2 Cell(int tx, int ty, int height) =>
            new Vector2(tx + 0.5f, height - ty - 0.5f);

        /* ══════════════════════════════════════════════════════ 카메라 */

        private static CameraRig BuildCamera(Transform parent)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);

            var camera = go.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = CameraRig.OrthoSize;
            camera.backgroundColor = HudTheme.Hex("111624");   // §4.2 야간 하늘
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var index = EnsureRenderer2D();
            if (index >= 0)
            {
                // 3D용 UniversalRenderer로 스프라이트를 그리면 정렬이 깊이·배치에
                // 뒤섞인다. 실제로 겪었다
                camera.GetUniversalAdditionalCameraData().SetRenderer(index);
            }

            return go.AddComponent<CameraRig>();
        }

        /// <summary>
        /// URP 에셋에 2D 렌더러를 더하고 그 인덱스를 돌려준다.
        ///
        /// 기존 3D 렌더러(index 0)는 건드리지 않는다 — M0 측정 씬이 그걸 쓴다.
        /// 카메라별로 렌더러를 고를 수 있으므로 이 씬의 카메라만 2D로 간다.
        /// </summary>
        private static int EnsureRenderer2D()
        {
            const string urpAssetPath = "Assets/M0/URP_Asset.asset";
            const string renderer2DPath = "Assets/M0/URP_Renderer2D.asset";

            M0Pipeline.EnsureRenderPipeline();
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(urpAssetPath);
            if (urp == null)
            {
                Debug.LogWarning("[부대] URP 에셋이 없다 — 기본 렌더러로 간다");
                return -1;
            }

            var data = AssetDatabase.LoadAssetAtPath<Renderer2DData>(renderer2DPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(data, renderer2DPath);
            }

            // 공개 API가 없어 직렬화 필드로 넣는다. 이미 있으면 그 인덱스를 쓴다
            var serialized = new SerializedObject(urp);
            var list = serialized.FindProperty("m_RendererDataList");
            for (var i = 0; i < list.arraySize; i += 1)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) return i;
            }

            list.arraySize += 1;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return list.arraySize - 1;
        }

        /* ══════════════════════════════════════════════ §4.3 온도 밴드 */

        /// <summary>
        /// 밴드별 Volume Profile 7종 + Global Light 2D.
        ///
        /// 프로파일을 **에셋으로 저장한다** — 타일과 같은 이유로, 메모리에만 만든
        /// `VolumeProfile`은 씬 저장 시 참조가 끊긴다.
        ///
        /// §12.2가 "밴드별 Volume Profile 참조는 JSON의 밴드 ID로 조회"하라고 적었다.
        /// 여기서는 `WeatherGrading.BandOrder`가 그 ID 순서를 들고 있고, 배열 인덱스가
        /// 곧 밴드다 — Unity에 밴드 이름 문자열이 흩어지지 않는다.
        /// </summary>
        private static WeatherGrading BuildGrading(Transform parent)
        {
            Directory.CreateDirectory(VolumeDir);

            var go = new GameObject("그레이딩");
            go.transform.SetParent(parent, false);
            var grading = go.AddComponent<WeatherGrading>();

            var volumes = new List<Volume>();
            for (var i = 0; i < BandProfiles.Bands.Length; i += 1)
            {
                volumes.Add(MakeVolume(go.transform,
                    $"VOL_Band_{BandProfiles.Names[i]}", BandProfiles.Bands[i],
                    // 평시에서 시작한다. 나머지는 weight 0
                    i == 2 ? 1f : 0f));
            }

            grading.bands = volumes.ToArray();
            grading.nightVolume = MakeVolume(go.transform, "VOL_Band_Night", BandProfiles.Night, 0f);
            grading.stateVolume = MakeVolume(go.transform, "VOL_State_Dehydration",
                BandProfiles.Dehydration, 0f);

            // §9.3 Global — 온도 밴드별 색·강도(§4.3 표)
            var lightGo = new GameObject("Global Light 2D");
            lightGo.transform.SetParent(go.transform, false);
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = HudTheme.Hex("FFF6E4");
            light.intensity = 1f;
            grading.globalLight = light;

            return grading;
        }

        /* ══════════════════════════════════════════════ §9.3 로컬 조명 (W3) */

        /// <summary>URP 2D 스프라이트·타일맵 Lit 셰이더 — 월드만 이걸로 간다(HUD는 IMGUI라 무관하다)</summary>
        private const string WorldLitShaderName = "Universal Render Pipeline/2D/Sprite-Lit-Default";

        /// <summary>§9.3 방 천장등 격자 간격(월드 유닛 = 타일 1개)</summary>
        private const float RoomLightSpacing = 7f;
        /// <summary>§9.3 복도 등 간격</summary>
        private const float CorridorLightSpacing = 6f;

        /// <summary>실내 방·복도 등 — 따뜻한 색(§9.3). 밤에 `WorldLighting`이 이 값까지 밝힌다</summary>
        private static readonly Color IndoorLightColor = HudTheme.Hex("FFE3B0");
        /// <summary>창 등 — 찬 색(§9.3). `HudTheme.Cold`를 그대로 써 온도 밴드 색과 어휘를 맞춘다</summary>
        private static readonly Color WindowLightColor = HudTheme.Cold;

        /// <summary>맵 데이터에 창(구조)이 있다고 나오는 유일한 소품. §9.3 "창이 있다면"의 근거</summary>
        private const string WindowPropName = "감시창";

        /// <summary>
        /// 월드 스프라이트를 Lit으로 바꾸고, 맵 데이터 기반 규칙으로 로컬 광원을 놓는다.
        ///
        /// **HUD·VFX·캐릭터는 건드리지 않는다** — 블랭킷 순회 대신 "Grid"(타일맵)와
        /// "구역"(소품) 두 컨테이너만 이름으로 콕 집는다(둘 다 `BuildTilemaps`·
        /// `BuildZones`가 이미 그렇게 지어 둔 이름이다). HUD는 IMGUI라 SpriteRenderer가
        /// 없고, 캐릭터(`CharacterRig`)와 VFX(`파티클`)는 이 두 컨테이너 밖에 있으므로
        /// `GetComponentsInChildren`이 애초에 닿지 않는다.
        ///
        /// 셰이더를 못 찾으면(패키지 문제 등) **아무것도 바꾸지 않고 돌아간다** — 지금
        /// (unlit) 그대로 남는 것이 이 작업이 깨질 때의 유일하게 안전한 실패 방식이다.
        /// </summary>
        private static void ApplyWorldLighting(Transform root, BaseMap map, List<ZoneMap> zones,
                                               WeatherGrading grading, CameraRig camera)
        {
            var material = EnsureWorldLitMaterial();
            if (material == null) return;

            var grid = root.Find("Grid");
            if (grid != null)
            {
                foreach (var renderer in grid.GetComponentsInChildren<TilemapRenderer>(true))
                {
                    // 바닥 AO는 **빛을 받으면 안 된다.** 어둡게 덮는 오버레이라서
                    // Lit이 되면 등 아래에서 스스로 밝아져 접지 그늘이 사라진다
                    if (renderer.name == FloorAoLayerName) continue;
                    renderer.sharedMaterial = material;
                }

                // 벽 정면(오블리크)은 타일맵이 아니라 낱개 스프라이트다. 여기서
                // 안 잡으면 벽 윗면만 Lit이 되어 같은 벽의 위/아래 밝기가 어긋난다
                var wallFace = grid.Find(WallFaceLayerName);
                if (wallFace != null)
                {
                    foreach (var renderer in wallFace.GetComponentsInChildren<SpriteRenderer>(true))
                        renderer.sharedMaterial = material;
                }
            }

            var zoneContainer = root.Find("구역");
            if (zoneContainer != null)
            {
                foreach (var renderer in zoneContainer.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // 소품 그림자도 같은 이유로 unlit으로 남긴다(AO와 같은 성격)
                    if (renderer.name == WorldDepth.ShadowChildName) continue;
                    renderer.sharedMaterial = material;
                }
            }

            var placed = new List<Light2D>();
            var boost = new List<bool>();

            foreach (var zone in zones)
            {
                switch (zone.kind)
                {
                    case "room":
                        PlaceRoomLights(zone, placed, boost);
                        break;
                    case "corridor":
                        PlaceCorridorLights(zone, placed, boost);
                        break;
                    // "outdoor" — 로컬 광원 없음. 야외는 Global이 주야를 전담한다(§9.3
                    // "로컬 광원 최소"). "lane"(사이드뷰 훈련 코스)은 이 발주 범위 밖이다
                }
            }

            var height = map.height;
            foreach (var placement in map.props ?? System.Array.Empty<BaseMap.PropPlacement>())
            {
                if (placement.name != WindowPropName) continue;

                // BuildZones의 소품 배치 공식과 동일하다(§6.2 좌표 규약)
                var x = placement.x + placement.w * 0.5f;
                var y = height - placement.y - placement.h;
                placed.Add(MakeLight("조명_창", new Vector2(x, y), WindowLightColor,
                                     intensity: 0.6f, inner: 0.5f, outer: 3f));
                boost.Add(false);
            }

            var container = new GameObject("조명");
            container.transform.SetParent(root, false);
            foreach (var light in placed) light.transform.SetParent(container.transform, true);

            var lighting = container.AddComponent<WorldLighting>();
            lighting.lights = placed.ToArray();
            lighting.nightBoost = boost.ToArray();
            lighting.grading = grading;
            lighting.worldCamera = camera != null ? camera.GetComponent<Camera>() : null;
        }

        /// <summary>§9.3 "천장등을 방 크기에 따라 격자로" — 작은 방은 자연히 1개로 접힌다</summary>
        private static void PlaceRoomLights(ZoneMap zone, List<Light2D> placed, List<bool> boost)
        {
            var area = zone.area;
            var cols = Mathf.Clamp(Mathf.RoundToInt(area.width / RoomLightSpacing), 1, 6);
            var rows = Mathf.Clamp(Mathf.RoundToInt(area.height / RoomLightSpacing), 1, 6);

            for (var r = 0; r < rows; r += 1)
            {
                for (var c = 0; c < cols; c += 1)
                {
                    var x = area.xMin + (c + 0.5f) * area.width / cols;
                    var y = area.yMin + (r + 0.5f) * area.height / rows;
                    // §9.3 반경 4~6타일 — 중간값 5로 고정한다(격자 위치가 이미
                    // 방마다 달라지므로 반경까지 무작위로 흔들면 재현성만 잃는다)
                    placed.Add(MakeLight($"조명_{zone.id}_{r}_{c}", new Vector2(x, y),
                                        IndoorLightColor, intensity: 1f, inner: 1.5f, outer: 5f));
                    boost.Add(true);
                }
            }
        }

        /// <summary>§9.3 "복도: 일정 간격 등" — 긴 축을 따라 등간격으로</summary>
        private static void PlaceCorridorLights(ZoneMap zone, List<Light2D> placed, List<bool> boost)
        {
            var area = zone.area;
            var horizontal = area.width >= area.height;
            var length = horizontal ? area.width : area.height;
            var count = Mathf.Clamp(Mathf.RoundToInt(length / CorridorLightSpacing), 1, 12);

            for (var i = 0; i < count; i += 1)
            {
                var t = (i + 0.5f) / count;
                var at = horizontal
                    ? new Vector2(area.xMin + t * area.width, area.center.y)
                    : new Vector2(area.center.x, area.yMin + t * area.height);
                placed.Add(MakeLight($"조명_{zone.id}_{i}", at,
                                    IndoorLightColor, intensity: 0.85f, inner: 1f, outer: 3.5f));
                boost.Add(true);
            }
        }

        private static Light2D MakeLight(string name, Vector2 position, Color color,
                                         float intensity, float inner, float outer)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(position.x, position.y, 0f);

            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.pointLightInnerRadius = inner;
            light.pointLightOuterRadius = outer;

            // 그림자 캐스터는 이번 발주 제외다(W2가 가짜 그림자를 담당) — 굳이
            // 계산을 태우지 않게 명시적으로 끈다. 캐스터가 없어도 기본값은
            // true라 계산 자체는 시도되므로, 꺼 두는 편이 안전하다
            light.shadowsEnabled = false;

            // 노멀맵이 나중에 연결되면(`Sprite2DImport`) 여기서 방향감이 생긴다.
            // 지금처럼 노멀맵이 없어도 Disabled였을 때와 렌더 결과가 같아 사고가 나지 않는다
            EnableNormalMapSampling(light);

            return light;
        }

        /// <summary>
        /// `Light2D.normalMapQuality`는 공개 setter가 없다 — 인스펙터 전용 필드다.
        /// `URP_Renderer2D` 렌더러 피처를 얹을 때(`EnsureScreenEffectsPass`)와 같은
        /// 이유로 `SerializedObject`를 직접 쓴다.
        /// </summary>
        private static void EnableNormalMapSampling(Light2D light)
        {
            var serialized = new SerializedObject(light);
            var property = serialized.FindProperty("m_NormalMapQuality");
            if (property == null) return;   // 엔진 버전이 달라 필드가 없으면 조용히 넘어간다

            // `enumValueIndex`는 인스펙터 표시 순서를 가리키고 실제 저장값과 다르다
            // (`Fast`는 선언 순서로 둘째지만 값은 0이다) — 헷갈리지 않게 `intValue`로
            // 실제 저장값을 바로 넣는다
            property.intValue = (int)Light2D.NormalMapQuality.Fast;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 월드 스프라이트·타일맵 공용 Lit 머티리얼 — 하나만 만들어 전부 공유한다.
        ///
        /// 스프라이트별 그림은 재질이 아니라 자기 텍스처(타일이면 `Tile.sprite`,
        /// 소품이면 `SpriteRenderer.sprite`)에서 오므로 재질 하나로 충분하다.
        /// 노멀맵은 여기 안 걸린다 — `Sprite2DImport`가 스프라이트별 보조 텍스처로
        /// 실어 두면 Sprite-Lit-Default가 렌더러 쪽에서 자동으로 읽는다.
        ///
        /// 다른 머티리얼(`ParticleMaterial` 등)과 같은 이유로 **에셋으로 저장한다**
        /// — 메모리에만 있으면 씬 저장 시 참조가 끊긴다.
        /// </summary>
        private static Material EnsureWorldLitMaterial()
        {
            var shader = Shader.Find(WorldLitShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[부대] 셰이더가 없다: {WorldLitShaderName} — 월드가 unlit로 남는다");
                return null;
            }
            EnsureAlwaysIncluded(shader);

            Directory.CreateDirectory(MaterialDir);
            var path = $"{MaterialDir}/SAD_WorldLit.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Lit 셰이더를 Always Included Shaders에 박아 둔다.
        ///
        /// 씬에 참조된 머티리얼이 있으면(있다, 위에서 막 만들었다) 보통 스트리핑에서
        /// 살아남지만, "에디터에선 되는데 WebGL 빌드에서 분홍색"이 이 저장소의 전형적
        /// 사고다(발주서 §2). 이중 안전장치로 명시적으로도 넣는다. 이미 있으면 스킵한다.
        /// </summary>
        private static void EnsureAlwaysIncluded(Shader shader)
        {
            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) return;

            var serialized = new SerializedObject(settings);
            var list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null) return;

            for (var i = 0; i < list.arraySize; i += 1)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            }

            list.arraySize += 1;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        /* ══════════════════════════════════════ §9.2 풀스크린 셰이더 6종 */

        /// <summary>§9.2 표 순서. 배열 인덱스가 곧 `ScreenEffectsFeature.Slot`이다</summary>
        private static readonly string[] ScreenShaders =
        {
            "SH_HeatDistort", "SH_FrostFrame", "SH_MaskFrame",
            "SH_NightVision", "SH_Vignette_Pulse", "SH_Grayscale_Fade",
        };

        /// <summary>
        /// 셰이더 6종을 머티리얼로 굳히고 렌더러에 패스를 얹는다.
        ///
        /// 머티리얼을 **에셋으로 저장한다.** 타일·VolumeProfile과 같은 이유다 —
        /// 메모리에만 만든 것은 씬 저장 시 참조가 끊기고, 화면 효과가 통째로
        /// 사라진 채 로그에는 아무 말도 안 나온다.
        /// </summary>
        private static ScreenEffects BuildScreenEffects(Transform parent, WeatherGrading grading)
        {
            Directory.CreateDirectory(MaterialDir);

            var go = new GameObject("화면 효과");
            go.transform.SetParent(parent, false);
            var effects = go.AddComponent<ScreenEffects>();
            effects.grading = grading;
            effects.materials = new Material[ScreenShaders.Length];

            for (var i = 0; i < ScreenShaders.Length; i += 1)
            {
                var name = ScreenShaders[i];
                var shader = Shader.Find($"SAD/{name}");
                if (shader == null)
                {
                    Debug.LogWarning($"[부대] 셰이더가 없다: SAD/{name}");
                    continue;
                }

                var path = $"{MaterialDir}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                EditorUtility.SetDirty(material);
                effects.materials[i] = material;
            }

            EnsureScreenEffectsPass();
            return effects;
        }

        /// <summary>
        /// 2D 렌더러에 `ScreenEffectsFeature`를 한 번만 얹는다.
        ///
        /// 렌더러 피처를 더하는 공개 API가 없어 직렬화 필드를 직접 만진다.
        /// `m_RendererFeatures`와 `m_RendererFeatureMap`은 **짝이다** — 맵은
        /// 피처의 GUID 목록이고, 이걸 안 맞추면 에디터를 다시 열 때 피처가
        /// 사라진 것처럼 보인다.
        /// </summary>
        private static void EnsureScreenEffectsPass()
        {
            const string rendererPath = "Assets/M0/URP_Renderer2D.asset";
            var data = AssetDatabase.LoadAssetAtPath<Renderer2DData>(rendererPath);
            if (data == null) return;

            foreach (var existing in AssetDatabase.LoadAllAssetsAtPath(rendererPath))
            {
                if (existing is ScreenEffectsFeature) return;
            }

            var feature = ScriptableObject.CreateInstance<ScreenEffectsFeature>();
            feature.name = "SAD 화면 효과";
            AssetDatabase.AddObjectToAsset(feature, data);

            var serialized = new SerializedObject(data);
            var list = serialized.FindProperty("m_RendererFeatures");
            list.arraySize += 1;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;

            var map = serialized.FindProperty("m_RendererFeatureMap");
            map.arraySize += 1;
            // 맵에 들어가는 것은 피처 에셋의 **로컬 파일 ID**다
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }


        /* ═══════════════════════════════════════════ §9.1 파티클 12종 */

        /// <summary>
        /// 파티클 12종을 세운다.
        ///
        /// 날씨 계열은 **카메라 자식**이다(§9.1 `VFX_Snow_Light` "카메라 자식").
        /// 월드에 뿌리면 110×85 타일을 다 덮어야 하고, 화면 밖 눈송이 수만 개를
        /// 시뮬레이션하게 된다. 카메라에 붙이면 언제나 화면만큼만 돈다.
        ///
        /// 머티리얼은 `Sprites/Default`를 쓴다. 파티클 전용 셰이더도 있지만
        /// 2D 렌더러에서 Sorting Layer·Order를 그대로 따르는 쪽이 이 게임의
        /// 정렬 규칙(§6.2 Y-sort)과 어긋나지 않는다.
        /// </summary>
        /// <summary>알갱이 이름 → 픽셀 크기. `BuildParticles`가 채운다</summary>
        private static readonly Dictionary<string, Vector2Int> VfxSize =
            new Dictionary<string, Vector2Int>();

        private static Vfx BuildParticles(Transform parent, CameraRig camera,
                                          List<Transform> steamAt, Art2D art)
        {
            VfxSize.Clear();
            foreach (var sprite in art.vfx?.sprites ?? System.Array.Empty<Art2D.VfxSprite>())
            {
                VfxSize[sprite.name] = new Vector2Int(sprite.cellW, sprite.cellH);
            }

            Directory.CreateDirectory(MaterialDir);

            var go = new GameObject("파티클");
            go.transform.SetParent(parent, false);
            var vfx = go.AddComponent<Vfx>();

            // ── 날씨 — 카메라 위에서 화면을 덮는다 ──
            // 세로 12유닛(화면 11.25)보다 조금 위에서 뿌려 화면 밖에서 들어오게 한다
            vfx.snowLight = Weather(camera.transform, "VFX_Snow_Light", "flake", 40f, 3.2f, 0.7f);
            vfx.snowHeavy = Weather(camera.transform, "VFX_Snow_Heavy", "speck", 120f, 7.5f, 2.6f);
            vfx.rain = Weather(camera.transform, "VFX_Rain", "drop", 80f, 14f, 1.2f);
            vfx.heatHaze = Ground(camera.transform, "VFX_HeatHaze", "haze");

            // 먼지는 발밑이라 카메라가 아니라 사람을 따라간다 — 아래에서 붙인다.
            // **한 번 터지는 것이 아니라 걷는 동안 계속 인다**, 그래서 Burst가 아니다.
            // 간격(0.07초)과 수명(0.55초)을 따로 준다 — 수명을 간격에 묶어두면
            // 알갱이가 한 알씩만 살아 있어 먼지가 아니라 점 하나가 된다
            vfx.dust = Body(go.transform, "VFX_Dust", "dust", 1, 0.07f, 0.55f);

            // ── 몸 ──
            vfx.breath = Body(go.transform, "VFX_Breath", "breath", 3, 2f);
            vfx.sweat = Body(go.transform, "VFX_SweatDrop", "sweat", 1, 1.1f);

            // ── 사건 ──
            vfx.questComplete = Burst(go.transform, "VFX_QuestComplete", "ring", 1, 0.4f);
            vfx.muzzleFlash = Burst(go.transform, "VFX_MuzzleFlash", "muzzle", 1, 0.08f);
            vfx.collapse = Burst(go.transform, "VFX_Collapse", "ash", 16, 1.4f);
            vfx.decon = Burst(go.transform, "VFX_Decon", "spray", 30, 2.5f);

            // 수증기는 난로·보일러·취사장에서 **늘** 난다(§9.1). 상태가 없으므로
            // 런타임이 켜고 끌 일도 없다 — 아래 `PlaceSteam`이 소품 자리에 세운다
            vfx.steam = Body(go.transform, "VFX_Steam", "steam", 1, 0.5f);

            // 열원마다 한 벌씩 세운다. 상태가 없으므로 런타임이 켜고 끌 일이 없다
            foreach (var at in steamAt)
            {
                var copy = Object.Instantiate(vfx.steam.gameObject, at, false);
                copy.name = "VFX_Steam";
                copy.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                var emission = copy.GetComponent<ParticleSystem>().emission;
                emission.enabled = true;
            }

            vfx.grading = null;   // BuildRuntime이 채운다
            return vfx;
        }

        /// <summary>화면을 덮는 낙하물 — 눈 · 비</summary>
        private static ParticleSystem Weather(Transform parent, string name, string sprite,
                                              float rate, float fall, float drift)
        {
            var system = MakeSystem(parent, name, sprite, lifetime: 4f, size: 1f);
            system.transform.localPosition = new Vector3(0f, 7f, 1f);

            var main = system.main;
            main.startSpeed = fall;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // 화면보다 넓게 — 카메라가 옆으로 움직여도 가장자리가 비지 않는다
            shape.scale = new Vector3(28f, 0.1f, 1f);

            // **셰이프를 돌려서 아래로 쏜다.** Box는 자기 +Z 방향으로 뿌리는데,
            // 2D 직교 카메라에서 +Z는 화면 속으로 들어가는 방향이라 눈이 화면
            // 위에 붙박인 채 깊이로만 멀어진다. X로 90° 돌리면 +Z가 −Y가 된다.
            //
            // `velocityOverLifetime`으로 떨어뜨려도 될 것 같지만 그쪽은 안 먹었다 —
            // 모듈은 켜지고 값도 저장되는데 알갱이가 스폰 지점에서 꼼짝을 안 했다.
            // 셰이프 방향은 확실하게 듣는다.
            shape.rotation = new Vector3(90f, 0f, 0f);

            var emission = system.emission;
            emission.rateOverTime = rate;
            emission.enabled = false;

            // 바람은 힘으로 준다 — 눈보라는 옆으로 분다(§9.1 "밀도 120 + 바람 벡터")
            var force = system.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.World;
            force.x = new ParticleSystem.MinMaxCurve(-drift, drift);

            return system;
        }

        /// <summary>지면에서 피어오르는 것 — 아지랑이</summary>
        private static ParticleSystem Ground(Transform parent, string name, string sprite)
        {
            var system = MakeSystem(parent, name, sprite, lifetime: 1.6f, size: 1f);
            system.transform.localPosition = new Vector3(0f, -4f, 1f);

            var main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(24f, 0.5f, 1f);
            // 아지랑이는 **올라간다**. 지면의 열이 뜨는 것이라 눈과 방향이 반대다
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var emission = system.emission;
            emission.rateOverTime = 22f;
            emission.enabled = false;
            return system;
        }

        /// <summary>캐릭터에 붙어 반복되는 것 — 입김 · 땀</summary>
        private static ParticleSystem Body(Transform parent, string name, string sprite,
                                           int frames, float period, float lifetime = 0f)
        {
            var system = MakeSystem(parent, name, sprite,
                                    lifetime: lifetime > 0f ? lifetime : period * 0.8f, size: 1f);

            var main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.5f);
            main.maxParticles = 8;

            var emission = system.emission;
            emission.rateOverTime = 1f / period;
            emission.enabled = false;

            // 위로 퍼진다. 셰이프를 안 돌리면 화면 깊이로만 움직여 제자리에 뜬다
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.12f;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            if (frames > 1)
            {
                var sheet = system.textureSheetAnimation;
                sheet.enabled = true;
                sheet.numTilesX = frames;
                sheet.numTilesY = 1;
            }
            return system;
        }

        /// <summary>한 번 터지고 마는 것 — 완료 링 · 격발 · 쓰러짐</summary>
        private static ParticleSystem Burst(Transform parent, string name, string sprite,
                                            int count, float lifetime)
        {
            var system = MakeSystem(parent, name, sprite, lifetime, size: 1f);

            var main = system.main;
            main.startSpeed = 0f;
            main.maxParticles = Mathf.Max(count * 2, 8);
            main.duration = Mathf.Max(lifetime, 0.1f);
            main.loop = false;
            main.playOnAwake = false;

            var emission = system.emission;
            // **방출 모듈은 켜둔다.** `MakeSystem`이 꺼둔 채로 두면 버스트가
            // 등록돼 있어도 한 알도 안 나온다 — 재생 여부는 `playOnAwake = false`와
            // `Play()`가 가르므로 여기서 또 막을 이유가 없다
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            var shape = system.shape;
            shape.enabled = count > 1;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.2f;

            // Circle은 **돌리지 않는다.** Box·Cone과 달리 원래 XY 평면에서
            // 바깥으로 퍼지는 셰이프라, 눕히면 오히려 화면 깊이로 흩어진다
            if (count > 1) main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
            return system;
        }

        /// <summary>공통 뼈대 — 크기 · 머티리얼 · 정렬</summary>
        private static ParticleSystem MakeSystem(Transform parent, string name, string sprite,
                                                 float lifetime, float size)
        {
            // **크기는 알갱이 그림이 정한다.** 파티클 크기 1은 1월드유닛 = 32px인데
            // (§2.1 PPU 32) 알갱이는 3~24px로 그려져 있다. 1로 두면 3px짜리 눈송이가
            // 32px 흰 사각형으로 부풀어 화면이 종잇조각으로 뒤덮인다 — 실제로 그랬다.
            if (VfxSize.TryGetValue(sprite, out var pixels))
            {
                size *= Mathf.Max(pixels.x, pixels.y) / 32f;
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();

            var main = system.main;
            main.startLifetime = lifetime;
            main.startSize = size;
            main.startColor = Color.white;
            main.gravityModifier = 0f;
            main.playOnAwake = true;

            // 알갱이가 PPU 32로 임포트되므로 크기 1 = 32px. 스프라이트 자체가
            // 이미 작게 그려져 있어(§9.1 대부분 4~12px) 배율은 1로 둔다
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.material = ParticleMaterial(sprite);
            // 캐릭터(±29000)보다 앞. 파티클이 사람 뒤로 가면 눈이 사람을 통과한다
            renderer.sortingOrder = 30000;

            var emission = system.emission;
            emission.enabled = false;
            return system;
        }

        /// <summary>알갱이 머티리얼. 스프라이트 하나당 하나만 만든다</summary>
        private static Material ParticleMaterial(string sprite)
        {
            var path = $"{MaterialDir}/VFX_{sprite}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, path);
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art2DDir}/vfx/{sprite}.png");
            if (texture != null) material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Volume MakeVolume(Transform parent, string name,
                                         BandProfiles.Grading spec, float weight)
        {
            var path = $"{VolumeDir}/{name}.asset";
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            if (!profile.TryGet<ColorAdjustments>(out var color))
                color = profile.Add<ColorAdjustments>(true);
            color.colorFilter.overrideState = true;
            color.colorFilter.value = HudTheme.Hex(spec.filter);
            color.saturation.overrideState = true;
            color.saturation.value = spec.saturation;
            color.contrast.overrideState = true;
            color.contrast.value = spec.contrast;
            color.postExposure.overrideState = true;
            color.postExposure.value = spec.exposure;

            if (!profile.TryGet<Vignette>(out var vignette))
                vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = spec.vignette;
            vignette.color.overrideState = true;
            vignette.color.value = HudTheme.Hex(spec.vignetteColor);

            EditorUtility.SetDirty(profile);

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;
            volume.weight = weight;
            // 밴드끼리 섞여야 하므로 우선순위를 순서대로 준다.
            // 상태 오버라이드(탈수)는 밴드 위에 얹혀야 하므로 더 높다
            volume.priority = name.Contains("State") ? 20f : name.Contains("Night") ? 10f : 0f;
            return volume;
        }

        /* ══════════════════════════════════════════════ 스프라이트 색인 */

        /// <summary>
        /// §5.2 시트를 잘라 `SpriteLibrary`에 굽는다.
        ///
        /// 런타임에 파일을 읽지 않기 위해서다 — WebGL에서 파일을 읽으려면 통째로
        /// 다른 경로가 필요해지고, 그러면 에디터와 빌드가 다르게 돈다.
        /// </summary>
        private static SpriteLibrary BuildLibrary(Art2D art)
        {
            var go = new GameObject("스프라이트");
            var library = go.AddComponent<SpriteLibrary>();

            var chars = art.chars;
            var sheets = new List<SpriteLibrary.Sheet>();

            foreach (var def in chars.sheets)
            {
                var cells = LoadSheet(def.file, chars.rows.Length, chars.cols);
                if (cells == null) continue;
                sheets.Add(new SpriteLibrary.Sheet
                {
                    layer = def.layer,
                    variant = def.variant,
                    cols = chars.cols,
                    cells = cells,
                });
            }

            var clips = new List<SpriteLibrary.Clip>();
            foreach (var def in chars.clips)
            {
                var clip = new SpriteLibrary.Clip
                {
                    name = def.name,
                    frames = def.frames,
                    fps = def.fps,
                    loop = def.loop,
                };
                foreach (var row in def.rows)
                {
                    var index = System.Array.IndexOf(chars.rows, row);
                    if (index < 0) continue;
                    if (row.EndsWith("_S")) clip.rowS = index;
                    else if (row.EndsWith("_N")) clip.rowN = index;
                    else if (row.EndsWith("_E")) clip.rowE = index;
                }
                clips.Add(clip);
            }

            library.sheets = sheets.ToArray();
            library.clips = clips.ToArray();

            // §7.1.5 표식 — 없으면 어디로 가야 하는지 알 방법이 사라진다.
            // 조용히 null로 두면 마커 오브젝트는 생기는데 아무것도 안 그려진다
            foreach (var marker in art.tiles.markers ?? System.Array.Empty<Art2D.MarkerDef>())
            {
                var sprite = LoadSprite(marker.file);
                if (sprite == null)
                {
                    Debug.LogWarning($"[부대] 표식 없음: {marker.file}");
                    continue;
                }
                if (marker.name == "quest") library.markerQuest = sprite;
                else if (marker.name == "door") library.markerDoor = sprite;
            }

            return library;
        }

        /// <summary>
        /// 잘린 스프라이트를 행·열 순서로 편다.
        ///
        /// `LoadAllAssetsAtPath`가 돌려주는 순서는 보장되지 않으므로 이름
        /// (`{시트}_{행}_{열}`)으로 자리를 잡는다. 순서에 기대면 어느 날 조용히
        /// 팔이 다리 자리에 들어간다.
        /// </summary>
        private static Sprite[] LoadSheet(string file, int rows, int cols)
        {
            var path = $"{Art2DDir}/{file}";
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0) return null;

            var name = Path.GetFileNameWithoutExtension(file);
            var cells = new Sprite[rows * cols];
            var found = 0;

            foreach (var asset in assets)
            {
                if (asset is not Sprite sprite) continue;
                var parts = sprite.name.Split('_');
                if (parts.Length < 3) continue;
                if (!int.TryParse(parts[^2], out var row) || !int.TryParse(parts[^1], out var col))
                    continue;
                if (row >= rows || col >= cols) continue;

                cells[row * cols + col] = sprite;
                found += 1;
            }

            if (found == 0)
            {
                Debug.LogWarning($"[부대] {name} 시트가 잘리지 않았다 — 임포터가 돌았는지 확인");
                return null;
            }
            return cells;
        }

        /* ══════════════════════════════════════════════════ 런타임 배선 */

        private static void BuildRuntime(Transform parent, SpriteLibrary library, ZoneWorld world,
                                         CameraRig camera, List<ZoneMap> zones,
                                         WeatherGrading grading, ScreenEffects screenFx,
                                         Vfx vfx, SnowCover snow, BaseMap map, int height)
        {
            library.transform.SetParent(parent, false);
            foreach (var zone in zones) zone.transform.SetParent(zone.transform.parent, true);

            // ── 네트워크 ──
            var net = new GameObject("Net");
            net.transform.SetParent(parent, false);
            // `GameClient`가 `[RequireComponent(typeof(GameSocket))]`이라 소켓은 함께 붙는다
            net.AddComponent<GameSocket>();
            var client = net.AddComponent<GameClient>();
            var boot = net.AddComponent<NetBootstrap>();
            boot.client = client;
            if (screenFx != null) screenFx.client = client;
            if (vfx != null) { vfx.client = client; vfx.grading = grading; }
            if (snow != null) snow.client = client;

            // ── 플레이어 ──
            var player = new GameObject("플레이어");
            player.transform.SetParent(parent, false);
            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            // §5.1 실제 점유는 약 14×34px이지만 **충돌은 발 근처만** 잡는다.
            // 몸 전체를 콜라이더로 두면 머리가 벽 위쪽에 걸려 문을 못 지나간다
            var capsule = player.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.5f, 0.4f);
            capsule.offset = new Vector2(0f, 0.2f);

            var rig = player.AddComponent<CharacterRig>();
            var local = player.AddComponent<LocalPlayer>();
            var interactor = player.AddComponent<Interactor>();
            interactor.origin = player.transform;

            camera.target = player.transform;
            if (vfx != null)
            {
                vfx.follow = player.transform;
                // 먼지는 발밑에서 인다 — 사람에 붙여야 걸을 때 따라온다
                if (vfx.dust != null) vfx.dust.transform.SetParent(player.transform, false);
            }

            // ── §6.1 일과 수행 — E로 열리는 판 ──
            var play = net.AddComponent<QuestPlay>();
            play.client = client;
            play.interactor = interactor;
            play.player = local;
            // 판이 열리고 닫힐 때 짧은 페이드(A-2). 안 물리면 조용히 컷으로 돌아간다
            play.effects = screenFx;

            // ── §9.0 훈련 ──
            var lane = net.AddComponent<LaneRun>();
            lane.client = client;
            lane.world = world;
            lane.player = local;
            lane.camera = camera;
            lane.lanes = BuildLanes(height);

            var mask = net.AddComponent<MaskDrill>();
            mask.client = client;
            mask.effects = screenFx;
            mask.player = local;

            // ── §10.0 후송 ──
            var evac = net.AddComponent<Evacuation>();
            evac.client = client;
            evac.effects = screenFx;
            evac.player = local;
            evac.vfx = vfx;

            // ── §15.0 접근성 — 웹 셸이 고른 값을 받아온다 ──
            var access = net.AddComponent<Accessibility>();
            access.grading = grading;

            // ── 시야 · 분대 · 월드 ──
            var visibility = net.AddComponent<ZoneVisibility>();
            visibility.client = client;
            grading.client = client;
            camera.grading = grading;

            var squad = new GameObject("분대");
            squad.transform.SetParent(parent, false);
            var squadView = squad.AddComponent<SquadView>();
            squadView.client = client;
            squadView.library = library;
            squadView.visibility = visibility;
            squadView.world = world;

            world.client = client;
            world.player = local;
            world.squad = squadView;
            world.library = library;
            world.camera = camera;
            world.visibility = visibility;
            world.zones = zones.ToArray();
            // 지도와 근처 안내가 읽는다 — 걸어서 이동하므로 문이 어디에 있는지가
            // 곧 길 정보다
            world.buildings = System.Array.ConvertAll(map.buildings ?? System.Array.Empty<BaseMap.BuildingDef>(),
                b => new ZoneWorld.Building
                {
                    id = b.id, name = b.name,
                    area = new Rect(b.x, height - b.y - b.h, b.w, b.h),
                });
            world.doors = System.Array.ConvertAll(map.doors ?? System.Array.Empty<BaseMap.DoorDef>(),
                d => new ZoneWorld.Door
                {
                    zone = d.zone, name = d.name, exitLabel = d.exitLabel, isExit = d.exit,
                    area = new Rect(d.x, height - d.y - d.h, d.w, d.h),
                });

            boot.squad = squadView;
            boot.world = world;

            // ── HUD ──
            var hud = new GameObject("HUD");
            hud.transform.SetParent(parent, false);
            var view = hud.AddComponent<Hud>();
            view.client = client;
            view.boot = boot;
            view.interactor = interactor;
            view.world = world;
            view.visibility = visibility;
            view.grading = grading;
            view.lane = lane;
            view.mask = mask;
            view.evacuation = evac;
            view.play = play;
            // §11 타이포그래피. 한글이 들어가므로 이 폰트가 없으면 라벨이 전부 □가 된다
            view.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SoldierKR.otf");
            if (view.font == null) Debug.LogWarning("[부대] 한글 폰트를 못 찾았다 — Assets/Fonts/SoldierKR.otf");

            local.client = client;
            local.Bind(rig);
            // 시트 색인은 씬을 세울 때 붙는다. 보직은 스냅샷이 정하므로 기본 외형으로
            // 시작하고, 첫 스냅샷에서 `ZoneWorld`가 갈아입힌다
            rig.Bind(library);
            rig.SetLook("rifle", "private");
            rig.Play("idle");
        }

        /* ═══════════════════════════════════════════════════════ 로드 */

        private static Sprite LoadSprite(string file) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"{Art2DDir}/{file}");

        /// <summary>§6.4 훈련 맵 색인 — 사이드뷰 코스 데이터가 여기 있다</summary>
        private static TrainMaps LoadTrain()
        {
            var path = $"{Art2DDir}/train_maps.json";
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[부대] {path} 없음 — 사이드뷰 코스가 비어 있다");
                return null;
            }
            return JsonUtility.FromJson<TrainMaps>(File.ReadAllText(path));
        }

        private static BaseMap LoadMap()
        {
            var path = $"{Art2DDir}/base_map.json";
            if (!File.Exists(path))
            {
                Debug.LogError($"[부대] {path} 없음 — python3 tools/sprites/generate.py");
                return null;
            }
            return JsonUtility.FromJson<BaseMap>(File.ReadAllText(path));
        }

        private static Art2D LoadArt()
        {
            var path = $"{Art2DDir}/art2d.json";
            if (!File.Exists(path))
            {
                Debug.LogError($"[부대] {path} 없음 — python3 tools/sprites/generate.py");
                return null;
            }

            var art = JsonUtility.FromJson<Art2D>(File.ReadAllText(path));

            // 임포터(Sprite2DImport)가 생기기 전에 들어온 그림이 있으면 스프라이트가
            // 아니라 텍스처로 임포트돼 있다. 한 번 강제로 다시 읽힌다
            if (art.tiles.floors.Length > 0 && LoadSprite(art.tiles.floors[0].file) == null)
            {
                AssetDatabase.ImportAsset(Art2DDir,
                    ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            return art;
        }
    }
}
