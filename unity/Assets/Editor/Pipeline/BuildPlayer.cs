using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 플레이어 빌드.
    ///
    /// 배치모드에서 도는 것이 요점이다 — 에디터를 띄우지 않고도 "지금 상태가 실제로
    /// 실행되는가"를 확인할 수 있어야 한다. 씬 목록도 여기서 정한다. 손으로
    /// Build Settings에 끌어다 놓으면 씬이 하나 늘 때마다 누군가 잊는다.
    ///
    ///     Unity -batchmode -quit -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.BuildPlayer.Mac -out build/mac
    ///
    /// 타깃은 둘이다. **웹이 배포 타깃**이지만(ARCH-03) 빌드가 몇 분 걸리므로,
    /// 화면을 눈으로 볼 때는 맥 네이티브가 빠르다.
    /// </summary>
    public static class BuildPlayer
    {
        private const string MainScene = "Assets/Scenes/Base.unity";

        public static void Mac() => Run(BuildTarget.StandaloneOSX, "build/mac/SoldierADay.app");

        public static void Web() => Run(BuildTarget.WebGL, "build/web");

        private static void Run(BuildTarget target, string fallback)
        {
            var output = Arg("-out") ?? fallback;

            if (!File.Exists(MainScene))
            {
                Fail($"씬이 없다: {MainScene} — SOLDIER/부대 본영 2D 씬 생성 을 먼저 돌려라");
                return;
            }

            // 빌드에 들어갈 씬은 여기서 정한다. M0 측정 씬은 뺀다 —
            // 게임이 아니라 숫자를 재는 씬이라 플레이어에 들어갈 이유가 없다
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };

            if (target == BuildTarget.WebGL)
            {
                // §1.2 — 2D 전환으로 초기 다운로드 예산이 25MB로 돌아왔다.
                // Brotli + 스트리핑이 그 예산을 지키는 최소 조건이다
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                // `PROJECT:` 접두사는 `Assets/WebGLTemplates/` 아래를 가리킨다.
                // `Default`로 두면 그 이름의 프로젝트 템플릿을 찾다가 빌드가
                // 통째로 실패한다 — 내장 템플릿을 쓰려면 `APPLICATION:`이다.
                // 여기서는 로딩 화면까지 §11 팔레트로 맞춘 전용 템플릿을 쓴다
                PlayerSettings.WebGL.template = "PROJECT:SAD";
                PlayerSettings.SetManagedStrippingLevel(
                    UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
            }

            PlayerSettings.colorSpace = ColorSpace.Linear;

            var group = BuildPipeline.GetBuildTargetGroup(target);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
                locationPathName = output,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"빌드 실패: {summary.result} · 에러 {summary.totalErrors}");
                return;
            }

            Debug.Log($"[빌드] {target} → {output} · " +
                      $"{summary.totalSize / 1024 / 1024}MB · {summary.totalTime.TotalSeconds:0}초");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[빌드] {message}");
            EditorApplication.Exit(1);
        }

        private static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
