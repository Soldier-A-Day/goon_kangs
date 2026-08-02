using System.IO;
using System.Reflection;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 눈이 깔리고 제설로 지워지는지 본다 (SAD-ART-001 §6.3).
    ///
    /// 이 연출의 요점은 **일한 결과가 화면에 남는 것**이라, 지워지는지를
    /// 눈으로 확인하지 않으면 만든 의미가 없다. 카메라를 연병장 위로 올려
    /// 세 장을 찍는다 — 눈 없음 · 쌓임 · 제설 후.
    ///
    ///     Unity -batchmode -projectPath unity \
    ///       -executeMethod SoldierADay.EditorTools.SnowProbe.Run
    /// </summary>
    public static class SnowProbe
    {
        private const string Flag = "sad.snowprobe";
        private const string OutDir = "/tmp/sad_snow";

        private static double _entered;
        private static int _shot = -1;

        private static readonly string[] Shots = { "0_없음", "1_쌓임", "2_제설후" };

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Base.unity");
            SessionState.SetBool(Flag, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            if (!SessionState.GetBool(Flag, false)) return;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return;

            _entered = EditorApplication.timeSinceStartup;
            _shot = -1;
            Directory.CreateDirectory(OutDir);
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            var cover = Object.FindFirstObjectByType<SnowCover>();
            var rig = Object.FindFirstObjectByType<CameraRig>();
            if (cover == null || rig == null) return;

            // 스냅샷이 밴드로 눈을 다시 깔아버린다 — 검사 중에는 끊는다
            cover.enabled = false;

            // 연병장(Z11) 한가운데를 본다. 부대 좌표계에서 대략 여기다
            rig.target = null;
            rig.ClearBounds();
            rig.transform.position = new Vector3(53f, 26f, -10f);

            var elapsed = EditorApplication.timeSinceStartup - _entered;
            var index = Mathf.FloorToInt((float)((elapsed - 3.0) / 1.2f));
            if (index < 0 || index == _shot) return;
            if (index >= Shots.Length)
            {
                SessionState.SetBool(Flag, false);
                Debug.Log($"[snow] {Shots.Length}장 → {OutDir}");
                EditorApplication.Exit(0);
                return;
            }

            _shot = index;
            switch (index)
            {
                case 0: Invoke(cover, "Show", false); break;
                case 1: Invoke(cover, "Show", true); break;
                case 2: Invoke(cover, "Clear", "Z11"); break;
            }

            EditorApplication.delayCall += () => Capture($"{OutDir}/{Shots[index]}.png");
        }

        private static void Invoke(SnowCover cover, string method, object arg)
        {
            var info = cover.GetType().GetMethod(method,
                BindingFlags.Instance | BindingFlags.NonPublic);
            info?.Invoke(cover, new[] { arg });
        }

        private static void Capture(string path)
        {
            var camera = Camera.main;
            if (camera == null) return;

            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
