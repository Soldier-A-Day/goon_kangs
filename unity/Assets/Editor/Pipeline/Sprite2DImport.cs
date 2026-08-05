using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// `Assets/Art/2d` 아래 PNG의 임포트 규칙 (SAD-ART-001 §2.1 · §5.1).
    ///
    /// 손으로 맞추지 않는다 — 스프라이트가 수백 장이고, 하나라도 필터가 Bilinear로
    /// 남으면 그 그림만 뿌옇게 떠서 "누가 안 맞췄는지"를 눈으로 찾게 된다.
    /// 규칙이 코드에 있으면 새로 뽑은 그림도 자동으로 맞는다.
    ///
    /// 픽셀아트 3원칙: Point 필터(뭉개지 않는다) · 압축 없음(픽셀이 곧 데이터다) ·
    /// 밉맵 없음(직교 카메라 한 배율뿐이다). 목업 §E 하단 주석이 요구한 그대로다.
    ///
    /// **PPU는 전부 32다.** §2.1이 못박은 값이고, 캐릭터 셀(32×48)도 타일(32×32)도
    /// 같은 자로 재야 사람이 타일 한 칸을 정확히 채운다.
    /// </summary>
    public sealed class Sprite2DImport : AssetPostprocessor
    {
        private const string Root = "Assets/Art/2d/";

        /// <summary>§2.1 Pixels Per Unit</summary>
        private const float PPU = 32f;

        /// <summary>§5.1 캐릭터 셀과 피벗 — 하단 중앙 (16, 2), 발 접지점</summary>
        private const int CellW = 32;
        private const int CellH = 48;
        private static readonly Vector2 FootPivot = new Vector2(16f / CellW, 2f / CellH);

        /// <summary>
        /// W1 노멀맵 산출 계약(W3) — 원본과 같은 폴더에 `_n` 접미사, 크기는 원본과 같다
        /// (예: `props/prop_13032_n.png`, `tiles/floor_concrete_n.png`).
        /// </summary>
        private const string NormalSuffix = "_n";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;

            var importer = (TextureImporter)assetImporter;

            if (IsNormalMapPath(assetPath))
            {
                // 노멀맵 자체는 **스프라이트가 아니다** — Sprite 타입이 아니므로
                // `LoadSprite`(BaseScene.cs)에도, `BuildSpriteAtlas`의 폴더 단위
                // 패킹에도 안 잡힌다(둘 다 `Sprite` 서브에셋만 줍는다. 이 텍스처는
                // NormalMap 타입이라 그 서브에셋 자체가 안 생긴다).
                // 피벗·슬라이스는 여기서는 의미가 없어 나머지 로직을 건너뛴다
                importer.textureType = TextureImporterType.NormalMap;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 4096;
                // `NormalMap` 타입은 엔진이 내부적으로 선형으로 읽지만, 인스펙터에
                // sRGB 체크박스가 아예 숨겨지는 버전 차이가 있어 값도 명시해 둔다
                importer.sRGBTexture = false;
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.spritePixelsPerUnit = PPU;
            importer.maxTextureSize = 4096;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            // Tiled drawMode와 Tilemap이 FullRect를 요구한다. 전부 FullRect로 두는
            // 편이 규칙 하나로 끝난다 — Tight 메시가 아끼는 픽셀은 이 해상도에서 없다시피 하다
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteExtrude = 0;

            if (assetPath.Contains("/chars/"))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = FootPivot;
                importer.SetTextureSettings(settings);
                SliceSheet(importer);
                LinkNormalMapIfPresent(importer, assetPath);
                return;
            }

            if (assetPath.Contains("/props/") || assetPath.Contains("/markers/"))
            {
                // 소품은 **아랫변**이 깊이 기준이다. 캐릭터의 발과 같은 선에서
                // 정렬되어야 침상 위쪽을 지나면 가려지고 아래쪽을 지나면 가린다
                settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            }
            else
            {
                // 타일은 Tilemap이 중앙 기준으로 놓는다
                settings.spriteAlignment = (int)SpriteAlignment.Center;
            }

            // `spriteMode`를 settings 쪽에도 넣어야 한다. `ReadTextureSettings`가
            // **기존 값을 담아 오므로** `SetTextureSettings`가 그걸 그대로 되돌려
            // 놓는다 — `importer.spriteImportMode`만 바꿔두면 조용히 덮어써지고,
            // 그러면 Multiple로 남은 타일에 스프라이트가 하나도 안 생긴다.
            settings.spriteMode = (int)SpriteImportMode.Single;
            importer.SetTextureSettings(settings);
            importer.spriteImportMode = SpriteImportMode.Single;
            LinkNormalMapIfPresent(importer, assetPath);
        }

        /// <summary>`internal` — `BuildSpriteAtlas`도 같은 판단 기준으로 노멀맵을 걸러낸다.
        /// 접미사 규칙을 두 곳에 따로 적으면 언젠가 어긋난다</summary>
        internal static bool IsNormalMapPath(string path) =>
            Path.GetFileNameWithoutExtension(path).EndsWith(NormalSuffix);

        private static string OriginalPathFor(string normalPath)
        {
            var dir = Path.GetDirectoryName(normalPath)?.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(normalPath);
            var ext = Path.GetExtension(normalPath);
            var baseName = name.Substring(0, name.Length - NormalSuffix.Length);
            return string.IsNullOrEmpty(dir) ? $"{baseName}{ext}" : $"{dir}/{baseName}{ext}";
        }

        private static string NormalPathFor(string originalPath)
        {
            var dir = Path.GetDirectoryName(originalPath)?.Replace('\\', '/');
            var name = Path.GetFileNameWithoutExtension(originalPath);
            var ext = Path.GetExtension(originalPath);
            return string.IsNullOrEmpty(dir) ? $"{name}{NormalSuffix}{ext}" : $"{dir}/{name}{NormalSuffix}{ext}";
        }

        /// <summary>
        /// W1이 낸 노멀맵을 `_NormalMap` 보조 텍스처로 잇는다.
        ///
        /// **없어도 아무 일도 없다** — `File.Exists`가 거짓이면 그냥 돌아간다.
        /// 지금은 노멀맵이 하나도 없으므로 이 함수는 전부 조용히 스킵된다. 나중에
        /// 생겨도 씬 빌더·머티리얼 쪽을 하나도 안 건드리고 자동으로 붙는다 —
        /// Sprite-Lit-Default가 스프라이트의 보조 텍스처를 알아서 읽는다.
        /// </summary>
        private static void LinkNormalMapIfPresent(TextureImporter importer, string assetPath)
        {
            var normalPath = NormalPathFor(assetPath);
            if (!File.Exists(normalPath)) return;

            // 아직 임포트되지 않았으면 null이 온다 — `OnPostprocessAllAssets`가
            // 노멀맵이 (재)임포트될 때마다 원본을 강제로 다시 돌려 잡아준다,
            // 그러니 여기서 못 잡아도 다음 패스에서 잡힌다(순서 무관)
            var normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normalTexture == null) return;

            importer.secondarySpriteTextures = new[]
            {
                new SecondarySpriteTexture { name = "_NormalMap", texture = normalTexture },
            };
        }

        /// <summary>
        /// 노멀맵 파일이 (재)임포트되면 원본 스프라이트를 강제로 다시 돌린다.
        ///
        /// 임포트 배치 안에서 어느 파일이 먼저 처리될지는 보장되지 않는다 — 원본이
        /// 노멀맵보다 먼저 돌면 `LinkNormalMapIfPresent`가 아직 없는 텍스처를 찾다
        /// 조용히 실패한다. 노멀맵 쪽에서 한 번 더 밀어주면 순서에 상관없이 붙는다.
        /// </summary>
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
            {
                if (!path.StartsWith(Root) || !IsNormalMapPath(path)) continue;

                var original = OriginalPathFor(path);
                if (File.Exists(original)) AssetDatabase.ImportAsset(original, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>
        /// 캐릭터 시트를 32×48 격자로 자른다 (목업 §E "행 = 클립×방향, 열 = 프레임").
        ///
        /// 이름을 `walk_S_2` 꼴이 아니라 **행·열 번호**로 붙인다. 시트의 행 이름은
        /// `art2d.json`이 들고 있고, 씬 빌더가 그 순서대로 읽어 색인을 만든다 —
        /// 이름 규칙을 두 곳에 두면 클립을 하나 더할 때마다 양쪽이 어긋난다.
        /// </summary>
        private void SliceSheet(TextureImporter importer)
        {
            importer.spriteImportMode = SpriteImportMode.Multiple;

            // 임포트 전이라 텍스처 크기를 에셋에서 읽을 수 없다. 파일 헤더에서 본다
            if (!TryReadPngSize(assetPath, out var width, out var height)) return;

            var cols = Mathf.Max(1, width / CellW);
            var rows = Mathf.Max(1, height / CellH);
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            var metas = new List<SpriteMetaData>(cols * rows);
            for (var row = 0; row < rows; row += 1)
            {
                for (var col = 0; col < cols; col += 1)
                {
                    metas.Add(new SpriteMetaData
                    {
                        name = $"{name}_{row}_{col}",
                        // Unity 텍스처 좌표는 아래가 0이고 시트는 위에서부터 채웠다
                        rect = new Rect(col * CellW, height - (row + 1) * CellH, CellW, CellH),
                        alignment = (int)SpriteAlignment.Custom,
                        pivot = FootPivot,
                    });
                }
            }

            importer.spritesheet = metas.ToArray();
        }

        /// <summary>
        /// PNG 헤더에서 크기만 읽는다.
        ///
        /// `AssetDatabase`로 텍스처를 불러오면 지금 임포트 중인 에셋을 다시 임포트하게
        /// 되어 재귀에 빠진다. IHDR은 파일 앞 24바이트 안에 있으므로 직접 읽는 편이
        /// 안전하고 빠르다.
        /// </summary>
        private static bool TryReadPngSize(string path, out int width, out int height)
        {
            width = height = 0;
            try
            {
                using var stream = System.IO.File.OpenRead(path);
                var header = new byte[24];
                if (stream.Read(header, 0, 24) < 24) return false;
                if (header[0] != 0x89 || header[1] != 'P' || header[2] != 'N' || header[3] != 'G')
                    return false;

                width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
                height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
                return width > 0 && height > 0;
            }
            catch (System.IO.IOException)
            {
                return false;
            }
        }
    }
}
