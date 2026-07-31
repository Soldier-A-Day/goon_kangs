using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 에셋 하나를 임시 씬에 띄워 PNG로 남긴다.
    ///
    /// 담장이 벽이 아니었던 것을 그림 한 장으로 잡았다. 훈련 맵 9종과 소품 45종을
    /// 눈으로 안 보고 넘기면 같은 종류의 오류가 그대로 쌓인다 — 회전이 빠졌거나,
    /// 조각이 겹쳤거나, 바닥 아래로 꺼졌거나. 전부 로그로는 안 보인다.
    ///
    /// 카메라는 대상의 바운드에 맞춰 자동으로 잡는다. 맵마다 크기가 열 배씩
    /// 차이 나서(소품 0.2m ~ 행군 코스 800m) 고정 시점으로는 아무것도 못 본다.
    /// </summary>
    public static class CaptureAsset
    {
        public static void Run()
        {
            var dir = Arg("-dir");
            var outDir = Arg("-outdir") ?? "/tmp/blockout";
            var size = int.Parse(Arg("-size") ?? "900");
            Directory.CreateDirectory(outDir);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            M0Pipeline.EnsureRenderPipeline();
            var material = M0Pipeline.EnsureProxyMaterial();

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.intensity = 1.2f;
            sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.farClipPlane = 5000f;

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (source == null) continue;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                }

                Frame(camera, instance);
                Shoot(camera, size, $"{outDir}/{Path.GetFileNameWithoutExtension(path)}.png");
                Object.DestroyImmediate(instance);
            }

            Debug.Log($"[캡처] {dir} → {outDir}");
            EditorApplication.Exit(0);
        }

        /// <summary>대상 전체가 들어오도록 카메라를 뒤로 뺀다</summary>
        private static void Frame(Camera camera, GameObject target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);

            var radius = bounds.extents.magnitude;
            var distance = radius / Mathf.Sin(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            var direction = new Vector3(0.6f, 0.55f, -1f).normalized;

            camera.transform.position = bounds.center + direction * distance;
            camera.transform.rotation = Quaternion.LookRotation(bounds.center - camera.transform.position);
            camera.nearClipPlane = Mathf.Max(0.01f, distance * 0.01f);
        }

        private static void Shoot(Camera camera, int size, string path)
        {
            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();

            RenderTexture.active = rt;
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            image.Apply();

            camera.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }

        private static string Arg(string key)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1) if (args[i] == key) return args[i + 1];
            return null;
        }
    }
}
