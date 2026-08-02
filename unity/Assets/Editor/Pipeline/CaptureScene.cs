using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 씬을 실제로 렌더해 PNG로 남긴다.
    ///
    /// "안 보인다"의 원인은 컬링·위치·크기·재질 어느 쪽이든 될 수 있고,
    /// 로그로는 구분이 안 된다. 그림 한 장이면 어느 쪽인지 바로 갈린다.
    /// </summary>
    public static class CaptureScene
    {
        public static void Run()
        {
            var scenePath = Arg("-scene") ?? "Assets/Scenes/M0_Real.unity";
            var outPath = Arg("-out") ?? "/tmp/m0real.png";
            var width = int.Parse(Arg("-width") ?? "1280");
            var height = int.Parse(Arg("-height") ?? "800");

            EditorSceneManager.OpenScene(scenePath);
            var camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null) { Debug.LogError("[캡처] 카메라 없음"); EditorApplication.Exit(1); return; }

            // 측정용 카메라는 씬에 박혀 있지만, 무엇이 서 있는지 보려면 가까이 가야 한다.
            // 측정과 확인은 다른 일이므로 씬을 고치지 않고 이 자리에서만 옮긴다.
            var pos = Arg("-pos");
            var look = Arg("-look");
            if (pos != null) camera.transform.position = ParseVec(pos);
            if (look != null) camera.transform.rotation = Quaternion.LookRotation(ParseVec(look) - camera.transform.position);

            // 2D 씬용. 직교 반높이를 키우면 부대 전체가 한 장에 들어온다
            var ortho = Arg("-ortho");
            if (ortho != null) camera.orthographicSize = float.Parse(ortho);

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);

            // SRP에서 Camera.Render()는 지원되지 않는 레거시 경로라 결과가 실행마다
            // 흔들린다 — 정렬이 뒤집히고 색이 이중 감마로 어두워지는 "유령 버그"를
            // 이걸로 몇 시간 쫓았다. 실제 파이프라인을 태우는 정식 API로 그린다.
            var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request))
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
            }
            else
            {
                camera.targetTexture = rt;
                camera.Render();
            }

            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, image.EncodeToPNG());
            Debug.Log($"[캡처] {scenePath} → {outPath} ({width}x{height})");
            EditorApplication.Exit(0);
        }

        private static Vector3 ParseVec(string text)
        {
            var parts = text.Split(',');
            return new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
        }

        private static string Arg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1) if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
