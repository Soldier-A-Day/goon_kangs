using System.Collections.Generic;
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

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;

            var importer = (TextureImporter)assetImporter;
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
