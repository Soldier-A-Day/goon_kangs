using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// HD-2D 목업 전용 배치 빌드.
    ///
    /// 본선 <c>BuildPlayer</c>(Assets/Editor/Pipeline/BuildPlayer.cs)는 건드리지
    /// 않는다 — <c>partial</c>이 아니라서 <c>BuildPlayer.Hd2dMock</c>이라는 메서드를
    /// 그 클래스에 직접 더할 방법이 없고(본선 파일 수정 금지), 그렇다고 본선 파일을
    /// partial로 바꾸는 것 자체가 이미 수정이다. 그래서 이름과 네임스페이스만
    /// 맞춘 독립 클래스로 같은 역할을 한다.
    ///
    ///     Unity -batchmode -quit -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.Hd2dMockBuildPlayer.Build
    ///
    /// 씬은 Assets/Hd2dMock/Hd2dMock.unity **하나만** 담아 unity/Build/hd2d로
    /// 낸다 — 본선 unity/Build/web은 절대 건드리지 않는다.
    /// </summary>
    public static class Hd2dMockBuildPlayer
    {
        private const string MockScene = "Assets/Hd2dMock/Hd2dMock.unity";
        private const string DefaultOutput = "Build/hd2d";

        public static void Build()
        {
            var output = Arg("-out") ?? DefaultOutput;

            if (!File.Exists(MockScene))
            {
                Fail($"목업 씬이 없다: {MockScene} — SOLDIER/HD-2D 목업 씬 생성 을 먼저 돌려라");
                return;
            }

            // 본선 EditorBuildSettings.scenes는 건드리지 않는다 — BuildPlayerOptions에
            // 씬 목록을 직접 넘기면 Build Settings 창의 상태와 무관하게 이 빌드만
            // 목업 씬 하나로 완결된다
            //
            // 압축은 **끈다.** 본선(apps/web/next.config.ts)은 `/game/Build/*.br`에만
            // `Content-Encoding: br` 헤더 규칙이 있다 — 그 파일은 본선 자산이라 손댈 수
            // 없다("본선 파일 수정 0"). Brotli를 켠 채로 apps/web/public/hd2d/에 정적
            // 파일로 올리면 헤더가 안 붙어 로더가 "Unable to parse Build/xxx.br"로 죽는다.
            // 압축을 끄면 어떤 정적 서버에서도(Next.js public/, m0serve, 그 무엇이든)
            // 헤더 설정 없이 그대로 열린다 — 목업 실험 빌드에서는 25MB 예산보다
            // "설정 없이 켜진다"가 더 중요하다
            //
            // `PlayerSettings.WebGL.*`는 빌드 타깃별이 아니라 **프로젝트 전역**이라
            // ProjectSettings/ProjectSettings.asset 한 곳에 저장된다 — 본선
            // BuildPlayer.Web도 같은 파일에 쓴다. 여기서 바꾸고 그대로 두면 이 파일이
            // 다음 git status에 본선 파일 변경으로 잡힌다("본선 파일 수정 0" 위반).
            // 그래서 원래 값을 적어 뒀다가 빌드 후 **반드시** 되돌린다.
            var savedCompression = PlayerSettings.WebGL.compressionFormat;
            var savedTemplate = PlayerSettings.WebGL.template;
            var savedStripping = PlayerSettings.GetManagedStrippingLevel(
                UnityEditor.Build.NamedBuildTarget.WebGL);
            var savedColorSpace = PlayerSettings.colorSpace;

            BuildReport report;
            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
                PlayerSettings.WebGL.template = "PROJECT:SAD";
                PlayerSettings.SetManagedStrippingLevel(
                    UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
                PlayerSettings.colorSpace = ColorSpace.Linear;

                var target = BuildTarget.WebGL;
                var group = BuildPipeline.GetBuildTargetGroup(target);
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { MockScene },
                    locationPathName = output,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.None,
                });
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = savedCompression;
                PlayerSettings.WebGL.template = savedTemplate;
                PlayerSettings.SetManagedStrippingLevel(
                    UnityEditor.Build.NamedBuildTarget.WebGL, savedStripping);
                PlayerSettings.colorSpace = savedColorSpace;
                AssetDatabase.SaveAssets();
            }

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"빌드 실패: {summary.result} · 에러 {summary.totalErrors}");
                return;
            }

            Debug.Log($"[HD2D 목업 빌드] {summary.platform} → {output} · " +
                      $"{summary.totalSize / 1024 / 1024}MB · {summary.totalTime.TotalSeconds:0}초");
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[HD2D 목업 빌드] {message}");
            EditorApplication.Exit(1);
        }

        private static string Arg(string name)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }
    }
}
