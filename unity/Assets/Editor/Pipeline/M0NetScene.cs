using System.IO;
using System.Linq;
using SoldierADay.Net;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// `M0_Net` — Unity ↔ 서버 수직 절개 씬.
    ///
    /// 8개 구역 맵을 `ZoneLayout`이 정한 자리에 놓고, 분대원은 **서버가 보낸
    /// 구역대로** 움직인다. 여기까지가 붙어야 ARCH-02(규칙은 sim에만)가 설계가
    /// 아니라 동작이 된다.
    ///
    /// 구역이 90m씩 떨어져 있는 것은 6.1의 "동선이 멀다"를 눈으로 읽히게 하려는
    /// 것이다. 붙여 놓으면 이동이 일어나도 화면에서 아무 일도 안 일어난다.
    /// </summary>
    public static class M0NetScene
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/M0_Net.unity";
        private const string ArtRoot = "Assets/Art";

        [MenuItem("SOLDIER/M0_Net 씬 생성")]
        public static void CreateScene()
        {
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
            camera.transform.position = new Vector3(0f, 20f, -30f);
            camera.farClipPlane = 2000f;

            // 8개 구역 맵을 각자의 자리에 놓는다
            var placed = 0;
            var tris = 0;
            foreach (var zone in ZoneLayout.Zones)
            {
                var entry = AssetManifest.Entries.FirstOrDefault(e => e.zone == zone);
                if (entry == null) continue;

                var path = FindModel($"{ArtRoot}/{entry.category}/{entry.id}");
                if (path == null) continue;

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
                instance.name = entry.id;
                instance.transform.position = ZoneLayout.AnchorOf(zone);

                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter?.sharedMesh != null) tris += CountTriangles(filter.sharedMesh);
                }
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.isStatic = true;
                }

                placed += 1;
            }

            // 클라이언트 묶음. 순서가 있다 — GameClient가 GameSocket을 요구한다
            var netGo = new GameObject("Net");
            netGo.AddComponent<GameSocket>();
            var client = netGo.AddComponent<GameClient>();
            netGo.AddComponent<LobbyClient>();

            var squadGo = new GameObject("Squad");
            var squad = squadGo.AddComponent<SquadView>();
            squad.material = material;
            squad.soldierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{ArtRoot}/character/char.base.player/char.base.player.prefab");

            // 한글 폰트. 없으면 서버가 보낸 라벨이 전부 빈칸으로 나온다.
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SoldierKR.otf");
            if (font == null) Debug.LogWarning("[M0_Net] 한글 폰트 없음 — tools/font/subset.py 로 만든다");

            var boot = netGo.AddComponent<NetBootstrap>();
            boot.client = client;
            boot.squad = squad;
            boot.followCamera = camera;

            var hud = netGo.AddComponent<Hud>();
            hud.client = client;
            hud.boot = boot;
            hud.font = font;

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[M0_Net] 씬 생성 완료: {ScenePath}\n" +
                $"  구역 맵 {placed}/8 배치 · {tris:N0} tris\n" +
                $"  분대원 프리팹: {(squad.soldierPrefab != null ? "있음" : "없음 — 캡슐로 대체")}");
        }

        private static int CountTriangles(Mesh mesh)
        {
            var total = 0u;
            for (var i = 0; i < mesh.subMeshCount; i += 1) total += mesh.GetIndexCount(i);
            return (int)(total / 3);
        }

        private static string FindModel(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.Contains("_LOD")) return path;
            }
            return null;
        }

        [MenuItem("SOLDIER/M0_Net WebGL 빌드")]
        public static void BuildWebGL()
        {
            CreateScene();

            var outputDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Build", "M0Net"));
            Directory.CreateDirectory(outputDir);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.memorySize = 1024;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            // 네트워크 코드는 리플렉션 없이도 스트리핑에 잘 걸린다. 여기서만 낮춰
            // UnityWebRequest 경로가 통째로 사라지는 것을 막는다.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.runInBackground = true;

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outputDir,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });

            Debug.Log(
                $"[M0_Net] 빌드 {report.summary.result} · " +
                $"{report.summary.totalSize / (1024f * 1024f):F1}MB · " +
                $"{report.summary.totalTime.TotalMinutes:F1}분 · {outputDir}");

            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
