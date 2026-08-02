using System.IO;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 플레이모드 수직 절개 검증.
    ///
    /// 씬을 열고 플레이모드로 들어가 실제로 서버에 붙는다 — 방 생성 → 시작 →
    /// WS → 스냅샷 → 2D 월드 반영 → 이동 의도까지. 배치모드에서 돌 수 있으므로
    /// 에디터를 열지 않고도 "붙는가"를 확인한다.
    ///
    /// 진입 시 도메인 리로드로 정적 상태가 날아가므로 SessionState로 잇는다.
    /// </summary>
    public static class PlayProbe
    {
        private const string Flag = "sad.playprobe";
        private static double _entered;
        private static bool _moved;

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
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            var elapsed = EditorApplication.timeSinceStartup - _entered;
            var boot = Object.FindFirstObjectByType<NetBootstrap>();
            if (boot == null) return;

            // 연결이 서고 나서 이동을 한 번 보낸다 — 도로 주행 연출까지 밟는다
            if (!_moved && boot.Connected && elapsed > 6)
            {
                _moved = true;
                Object.FindFirstObjectByType<GameClient>().Move("Z11");
                Capture("/tmp/play2d_before.png");
                Debug.Log("[probe] 이동 의도 전송 — Z11 연병장");
            }

            if (elapsed < 16) return;

            var world = Object.FindFirstObjectByType<ZoneWorld>();
            var player = Object.FindFirstObjectByType<LocalPlayer>();
            var points = Object.FindObjectsByType<Interactable>(FindObjectsSortMode.None);

            Debug.Log($"[probe] 상태 {boot.Status} · 스냅샷 {boot.Snapshots} · 연결 {boot.Connected}");
            Debug.Log($"[probe] 플레이어 ({player.transform.position.x:F1},{player.transform.position.y:F1})" +
                      $" · 이동잔여 {world.TravelRemaining:F1}s · 상호작용 지점 {points.Length}");

            Capture("/tmp/play2d_after.png");
            SessionState.SetBool(Flag, false);
            EditorApplication.Exit(boot.Connected && boot.Snapshots > 0 ? 0 : 1);
        }

        private static void Capture(string path)
        {
            var camera = Camera.main;
            if (camera == null) return;
            var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0);
            image.Apply();
            camera.targetTexture = null;
            RenderTexture.active = null;
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
    }
}
