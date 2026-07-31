using System;
using System.IO;
using System.Linq;
using SoldierADay.M0;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// M0 씬 생성과 WebGL 빌드를 배치모드로 돌리기 위한 진입점.
    ///
    /// 씬을 손으로 만들지 않는 이유는 재현성 때문이다. M0의 산출물은 "어디서부터 무너지는가"이고
    /// 그건 값을 바꿔가며 여러 번 빌드해야 나온다 — 그때마다 사람이 씬을 다시 조립하면
    /// 무엇이 달라졌는지 추적이 안 된다.
    /// </summary>
    public static class M0Pipeline
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/M0_Synthetic.unity";

        [MenuItem("SOLDIER/M0 씬 생성")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 태양 하나. 나머지는 베이크 가정이며, 실시간 광원이 늘면 캐스터 패스가 그만큼 반복된다.
            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.transform.position = new Vector3(0f, 25f, -70f);
            camera.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
            // 야외 최대 부하를 재는 씬이라 컬링으로 가려지지 않게 멀리 본다
            camera.farClipPlane = 300f;

            var probe = new GameObject("M0");
            var builder = probe.AddComponent<LoadBuilder>();
            // 씬이 머티리얼을 참조해야 빌드에 셰이더가 들어간다
            builder.baseMaterial = EnsureProxyMaterial();
            // 스윕이 부하를 정하므로 Start의 자동 빌드는 끈다
            builder.buildOnStart = false;
            probe.AddComponent<HeavyAxes>().baseMaterial = builder.baseMaterial;
            probe.AddComponent<AutoSweep>();
            // 힙 오버레이는 스윕 표와 겹치므로 기본은 숫자만 모은다.
            // ?mode=heap 이면 M0Mode가 켠다.
            probe.AddComponent<HeapProbe>().showOverlay = false;
            probe.AddComponent<SnapshotChurn>();
            probe.AddComponent<M0Mode>();

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[M0] 씬 생성 완료: {ScenePath}");
        }

        private const string MaterialPath = "Assets/M0/M0_Proxy.mat";
        private const string UrpAssetPath = "Assets/M0/URP_Asset.asset";
        private const string UrpRendererPath = "Assets/M0/URP_Renderer.asset";

        /// <summary>
        /// URP 파이프라인 에셋을 만들어 프로젝트에 지정한다.
        ///
        /// 없으면 Unity가 Built-in 파이프라인으로 돌고, URP Lit 셰이더는 렌더되지 못해
        /// **전부 마젠타**로 나온다. 패키지를 넣는 것과 파이프라인을 지정하는 것은 별개다.
        ///
        /// 기획서 ARCH-01이 Unity를 쓰는 이유로 든 것 중 하나가 URP Volume 프로파일
        /// (온도 6밴드 그레이딩)이므로, 어차피 실제 게임도 URP로 가야 한다.
        /// </summary>
        private static void EnsureRenderPipeline()
        {
            var existing = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (existing == null)
            {
                Directory.CreateDirectory("Assets/M0");

                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, UrpRendererPath);

                existing = UniversalRenderPipelineAsset.Create(rendererData);
                // 실시간 그림자 1광원 — 표 18-2의 측정 조건을 에셋에 박아둔다
                existing.shadowDistance = 150f;
                existing.supportsHDR = true;
                AssetDatabase.CreateAsset(existing, UrpAssetPath);
                AssetDatabase.SaveAssets();

                Debug.Log($"[M0] URP 파이프라인 에셋 생성: {UrpAssetPath}");
            }

            GraphicsSettings.defaultRenderPipeline = existing;
            QualitySettings.renderPipeline = existing;
            AssetDatabase.SaveAssets();

            Debug.Log("[M0] 렌더 파이프라인 지정 완료 — URP");
        }

        /// <summary>
        /// 프록시가 쓸 머티리얼을 **에셋으로** 만든다.
        ///
        /// 처음에는 Shader.Find + Always Included Shaders 로 해결하려 했는데 그게 함정이었다.
        /// URP Lit을 Always Included에 넣으면 그 셰이더의 **모든 변형**을 컴파일한다 —
        /// 수천 개라 빌드가 16분이 지나도 끝나지 않았다.
        ///
        /// 머티리얼 에셋을 씬이 참조하면 Unity가 **실제로 쓰이는 변형만** 골라 넣는다.
        /// 빌드에 셰이더를 포함시키는 정석이 이쪽이다.
        /// </summary>
        private static Material EnsureProxyMaterial()
        {
            EnsureRenderPipeline();

            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogError("[M0] 쓸 셰이더를 찾지 못했다");
                return null;
            }

            Directory.CreateDirectory("Assets/M0");
            var material = new Material(shader) { name = "M0_Proxy", enableInstancing = true };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[M0] 프록시 머티리얼 생성: {MaterialPath} ({shader.name})");
            return material;
        }

        [MenuItem("SOLDIER/M0 WebGL 빌드")]
        public static void BuildWebGL()
        {
            if (!File.Exists(ScenePath)) CreateScene();

            var outputDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Build", "M0"));
            Directory.CreateDirectory(outputDir);

            // ARCH-03 확정 사항을 빌드 설정에 반영한다
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.memorySize = 1024; // 표 18-2: 힙 ≤ 1GB
            // 예외를 끄면 빌드에서 죽었을 때 wasm 함수 번호만 남고 메시지가 사라진다.
            // M0는 측정이 목적이고 원인 추적이 더 급하므로 켜둔다.
            PlayerSettings.WebGL.exceptionSupport =
                WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetManagedStrippingLevel(
                NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            // 측정 중 프레임 제한이 걸리면 상한을 못 본다
            PlayerSettings.runInBackground = true;

            var stamp = System.DateTime.Now.ToString("MMdd-HHmm");
            StampBuild(stamp);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log(
                $"[M0] 빌드 {summary.result} · {summary.totalSize / (1024f * 1024f):F1}MB · " +
                $"{summary.totalTime.TotalMinutes:F1}분 · {outputDir}");

            if (summary.result != BuildResult.Succeeded)
            {
                var errors = report.steps
                    .SelectMany(step => step.messages)
                    .Where(m => m.type == LogType.Error || m.type == LogType.Exception)
                    .Select(m => m.content)
                    .Take(10);
                foreach (var error in errors) Debug.LogError($"[M0] {error}");
                EditorApplication.Exit(1);
                return;
            }

            BustLoaderCache(outputDir, stamp);
        }

        /// <summary>
        /// 빌드 시각을 런타임이 읽을 수 있는 곳에 박는다.
        ///
        /// 화면에 뜨는 값이 방금 빌드한 시각과 다르면 **옛 빌드가 돌고 있는 것**이다.
        /// 이걸 눈으로 확인할 방법이 없어서, 코드를 고치고 빌드했는데도 바뀌지 않는
        /// 상황을 두 번 겪었다. 측정 결과보다 먼저 확인해야 하는 값이다.
        /// </summary>
        private static void StampBuild(string stamp)
        {
            const string dir = "Assets/Resources";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "m0_build.txt"), stamp);
            AssetDatabase.ImportAsset($"{dir}/m0_build.txt");
        }

        /// <summary>
        /// 로더가 받아오는 URL에 빌드 도장을 붙인다.
        ///
        /// Unity WebGL 로더는 받은 파일을 **IndexedDB에 캐시**하는데, 키가 URL이라
        /// 파일 내용이 바뀌어도 경로가 같으면 옛것을 그대로 쓴다. 서버가 보내는
        /// `no-store`로는 막을 수 없다 — 브라우저 캐시가 아니라 Unity의 캐시다.
        /// 페이지 주소에 `?v=` 를 붙여도 소용없다. 바뀌어야 하는 건 **데이터 URL**이다.
        /// </summary>
        private static void BustLoaderCache(string outputDir, string stamp)
        {
            var indexPath = Path.Combine(outputDir, "index.html");
            if (!File.Exists(indexPath))
            {
                Debug.LogWarning($"[M0] index.html 없음 — 캐시 도장 생략: {indexPath}");
                return;
            }

            var html = File.ReadAllText(indexPath);
            // index.html은 `buildUrl + "/M0.data.br"` 꼴로 이어 붙이므로
            // 파일명이 아니라 그 조각을 그대로 갈아끼운다
            foreach (var name in new[] { "M0.data.br", "M0.framework.js.br", "M0.wasm.br" })
            {
                html = html.Replace($"\"/{name}\"", $"\"/{name}?b={stamp}\"");
            }

            File.WriteAllText(indexPath, html);
            Debug.Log($"[M0] 빌드 도장 {stamp} — 로더 URL에 부착 완료");
        }
    }
}
