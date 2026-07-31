using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 임포트 규칙 강제 (ARCH-03).
    ///
    /// **왜 자동이어야 하는가.** 임포트 설정은 에셋마다 붙어 있고 기본값은 우리 제약과
    /// 다르다. 사람이 파일마다 맞추면 반드시 빠뜨리고, 빠진 것은 `.meta` 안에 조용히
    /// 남아 빌드 크기와 메모리로만 드러난다 — 그때는 어느 파일 때문인지 알기 어렵다.
    ///
    /// 그래서 임포트 시점에 강제한다. 사람이 인스펙터에서 되돌려도 다시 임포트하면
    /// 규칙으로 돌아온다. 예외가 필요하면 카탈로그에 적어야 한다.
    ///
    /// **경로 규약:** `Assets/Art/{카테고리}/{에셋 id}/...`
    /// 폴더 이름이 곧 카탈로그 키다. 이름으로 잇는 것이 취약해 보이지만, 대안인
    /// ScriptableObject 매핑은 파일을 옮길 때 조용히 끊어지고 아무도 모른다 —
    /// 규약이 깨지면 아래 검사가 경고를 낸다.
    /// </summary>
    public sealed class SoldierImportRules : AssetPostprocessor
    {
        private const string ArtRoot = "Assets/Art/";

        /// <summary>Unity가 쓰는 WebGL 플랫폼 문자열</summary>
        private const string WebGL = "WebGL";

        private bool InArt => assetPath.StartsWith(ArtRoot, System.StringComparison.Ordinal);

        /// <summary>
        /// 경로에서 카탈로그 항목을 찾는다. 규약을 벗어난 경로는 null이고,
        /// 그 경우 공통 규칙만 적용한다 — 규약 위반을 임포트 실패로 만들면
        /// 작업 중인 파일을 프로젝트에 넣지도 못한다.
        /// </summary>
        private AssetManifest.Entry ResolveEntry()
        {
            var rest = assetPath.Substring(ArtRoot.Length);
            var parts = rest.Split('/');
            return parts.Length >= 2 ? AssetManifest.Find(parts[1]) : null;
        }

        private void OnPreprocessTexture()
        {
            if (!InArt) return;

            var importer = (TextureImporter)assetImporter;
            var rules = AssetManifest.Rules;
            var name = Path.GetFileNameWithoutExtension(assetPath);

            // 노멀맵은 타입이 다르면 압축 포맷도 톤 매핑도 달라진다.
            // 접미사로 구분하는 이유는 임포트 시점에 내용을 볼 수 없기 때문이다.
            if (name.EndsWith("_N", System.StringComparison.Ordinal))
            {
                importer.textureType = TextureImporterType.NormalMap;
            }

            // 알베도만 sRGB다. 마스크·러프니스를 sRGB로 두면 값이 감마를 타서
            // 셰이더 계산이 틀어지는데, 화면에서는 "조금 이상한 재질"로만 보인다.
            var isData =
                name.EndsWith("_M", System.StringComparison.Ordinal) ||
                name.EndsWith("_MRA", System.StringComparison.Ordinal) ||
                importer.textureType == TextureImporterType.NormalMap;
            importer.sRGBTexture = !isData;

            importer.maxTextureSize = rules.maxTextureSize;
            importer.mipmapEnabled = true;
            importer.streamingMipmaps = true;

            // ARCH-03: 데스크톱 전용이므로 DXT 단일. ASTC를 함께 구우면
            // 번들이 두 벌이 되어 120MB 예산이 그냥 무너진다.
            var settings = importer.GetPlatformTextureSettings(WebGL);
            settings.overridden = true;
            settings.maxTextureSize = rules.maxTextureSize;
            settings.format = importer.DoesSourceTextureHaveAlpha()
                ? TextureImporterFormat.DXT5
                : TextureImporterFormat.DXT1;
            settings.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(settings);
        }

        private void OnPreprocessModel()
        {
            if (!InArt) return;

            var importer = (ModelImporter)assetImporter;
            var rules = AssetManifest.Rules;
            var entry = ResolveEntry();

            importer.meshCompression = ModelImporterMeshCompression.Medium;

            // 런타임에 메시를 읽을 일이 없다. 켜두면 CPU 사본이 남아 메모리가 두 배가 된다.
            // M0에서 정반대 실수를 했다 — 정적 배칭을 위해 읽기를 켜야 했는데, 그건
            // 코드로 만든 프록시 메시 얘기고 임포트한 에셋에는 해당하지 않는다.
            importer.isReadable = rules.readWriteEnabled;

            importer.importVisibility = false;
            importer.importCameras = false;
            importer.importLights = false;

            // 리깅은 카탈로그가 정한다. 4보직이 애니메이션을 공유하는 구조(16.0)라
            // 리그가 한 벌만 어긋나도 그 캐릭터만 리타게팅에서 빠진다.
            if (entry?.rig == "humanoid")
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.optimizeGameObjects = true;
            }
            else if (entry != null)
            {
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
            }
        }

        private void OnPostprocessModel(GameObject model)
        {
            if (!InArt) return;

            var entry = ResolveEntry();
            if (entry == null)
            {
                Debug.LogWarning(
                    $"[에셋] 카탈로그에 없는 경로: {assetPath}\n" +
                    "규약은 Assets/Art/{카테고리}/{에셋 id}/... 다. 예산 검사에서 빠진다.");
                return;
            }

            // 예산 초과를 **임포트 시점에** 알린다. 빌드까지 기다리면 이미
            // 다른 에셋들이 그 위에 쌓여 있어 되돌리기가 비싸다.
            var tris = CountTriangles(model);
            if (tris > entry.lod0)
            {
                Debug.LogWarning(
                    $"[에셋] {entry.id} 예산 초과 — {tris:N0} tris / 예산 {entry.lod0:N0} " +
                    $"({(float)tris / entry.lod0:P0})\n{assetPath}");
            }
        }

        private static int CountTriangles(GameObject model)
        {
            var total = 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) total += filter.sharedMesh.triangles.Length / 3;
            }
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null) total += skinned.sharedMesh.triangles.Length / 3;
            }
            return total;
        }
    }
}
