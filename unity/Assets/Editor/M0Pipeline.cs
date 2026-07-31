using System;
using System.IO;
using System.Linq;
using SoldierADay.M0;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            probe.AddComponent<LoadBuilder>();
            probe.AddComponent<HeapProbe>();

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[M0] 씬 생성 완료: {ScenePath}");
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
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.SetManagedStrippingLevel(
                NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            // 측정 중 프레임 제한이 걸리면 상한을 못 본다
            PlayerSettings.runInBackground = true;

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
            }
        }
    }
}
