using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 소품 Sprite Atlas 생성 (H-1 — "소품 Sprite Atlas 묶기, 지금 0건").
    ///
    /// **왜 손으로 `.spriteatlas`를 안 쓰는가.** 그 파일은 YAML이지만 사람이 읽으라고
    /// 만든 포맷이 아니다 — 패킹 해시, 패커 버전, 내부 fileID 참조가 뒤섞여 있고
    /// 필드 하나를 밀리면 에디터가 조용히 무시하거나 다음에 연 사람이 "패킹 다시
    /// 해야 함" 배지만 보고 원인을 못 찾는다. 여기서는 Unity가 아는 API로 만들고
    /// 저장은 Unity에게 맡긴다 — 포맷이 버전마다 바뀌어도 이 스크립트는 안 깨진다.
    ///
    /// **왜 비압축·Point인가.** `Sprite2DImport.cs`가 낱장 스프라이트에 강제하는
    /// 규칙(픽셀아트 3원칙 — Point 필터·압축 없음·밉맵 없음)을 아틀라스도 그대로
    /// 따라야 한다. 낱장이 맞아도 패킹된 아틀라스 텍스처가 기본값(Bilinear·압축)
    /// 으로 구워지면 화면에는 뭉개진 그림만 남는다 — 아틀라스는 낱장 임포트
    /// 설정을 상속하지 않고 **자기 텍스처 설정을 따로** 가진다는 게 함정이다.
    ///
    ///     Unity -batchmode -quit -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.BuildSpriteAtlas.Props
    ///
    /// 재실행해도 안전하다 — 기존 아틀라스가 있으면 설정만 다시 맞추고 패커블을
    /// 새로 잡는다. 소품이 늘어도 스크립트를 다시 돌리기만 하면 된다.
    /// </summary>
    public static class BuildSpriteAtlas
    {
        private const string PropsFolder = "Assets/Art/2d/props";
        private const string PropsAtlasPath = "Assets/Art/2d/props/props.spriteatlas";

        /// <summary>§2.1 아틀라스 텍스처 상한. 소품 105장 총 면적이 이 절반에도
        /// 못 미치므로(≈0.5MP) 실제로 구워지는 텍스처는 이보다 훨씬 작다 —
        /// 이건 여유를 둔 상한일 뿐이다</summary>
        private const int MaxTextureSize = 2048;

        public static void Props() => Build(PropsFolder, PropsAtlasPath);

        private static void Build(string folder, string atlasPath)
        {
            if (!AssetDatabase.IsValidFolder(folder))
            {
                Fail($"폴더가 없다: {folder}");
                return;
            }

            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
            var isNew = atlas == null;
            if (isNew)
            {
                atlas = new SpriteAtlas();
            }
            else
            {
                // 재실행 — 이전 패커블을 비우고 폴더를 다시 넣는다. 그대로 Add만
                // 반복하면 같은 폴더가 중복으로 쌓여도 티가 안 나다가 나중에
                // 못 찾을 스프라이트가 생긴다
                var existing = SpriteAtlasExtensions.GetPackables(atlas);
                if (existing.Length > 0) SpriteAtlasExtensions.Remove(atlas, existing);
            }

            SpriteAtlasExtensions.SetPackingSettings(atlas, new SpriteAtlasPackingSettings
            {
                // 픽셀아트에서 회전은 이득이 없다 — Point 필터는 축에 맞은 텍셀만
                // 정확히 집는데, 회전된 UV는 그 전제를 깨고 가장자리를 계단으로 만든다
                enableRotation = false,
                // Sprite2DImport.cs가 낱장 메시를 FullRect로 강제한 것과 같은 이유 —
                // 이 해상도(최대 192×160)에서 타이트 패킹이 아끼는 픽셀은 없다시피 하다
                enableTightPacking = false,
                padding = 2,
            });

            SpriteAtlasExtensions.SetTextureSettings(atlas, new SpriteAtlasTextureSettings
            {
                filterMode = FilterMode.Point,
                generateMipMaps = false,
                sRGB = true,
                readable = false,
            });

            // Default·Standalone(눈으로 볼 때)·WebGL(배포 타깃) 세 곳 다 명시한다.
            // 하나라도 비워두면 그 플랫폼은 Unity 기본값(압축)으로 구워지고,
            // 그 실수는 빌드 크기로만 드러나 원인을 찾기 어렵다
            foreach (var platform in new[] { "DefaultTexturePlatform", "Standalone", "WebGL" })
            {
                SpriteAtlasExtensions.SetPlatformSettings(atlas, new TextureImporterPlatformSettings
                {
                    name = platform,
                    overridden = true,
                    maxTextureSize = MaxTextureSize,
                    format = TextureImporterFormat.RGBA32,
                    textureCompression = TextureImporterCompression.Uncompressed,
                    crunchedCompression = false,
                });
            }

            if (isNew)
            {
                AssetDatabase.CreateAsset(atlas, atlasPath);
            }

            var folderAsset = AssetDatabase.LoadAssetAtPath<DefaultAsset>(folder);
            SpriteAtlasExtensions.Add(atlas, new Object[] { folderAsset });

            EditorUtility.SetDirty(atlas);
            AssetDatabase.SaveAssets();

            Debug.Log($"[아틀라스] {atlasPath} ← {folder}");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[아틀라스] {message}");
            EditorApplication.Exit(1);
        }
    }
}
