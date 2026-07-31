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
            camera.farClipPlane = 2000f;

            // 8개 구역 맵. 한 번에 하나만 켜지므로(ZoneWorld) 겹치지 않게만 벌려둔다.
            var worldGo = new GameObject("World");
            var placed = 0;
            var tris = 0;

            foreach (var zone in ZoneLayout.Zones)
            {
                var entry = AssetManifest.Entries.FirstOrDefault(e => e.zone == zone);
                if (entry == null) continue;

                var path = FindModel($"{ArtRoot}/{entry.category}/{entry.id}");
                if (path == null) continue;

                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, worldGo.transform);
                instance.name = entry.id;
                instance.transform.position = ZoneLayout.AnchorOf(zone);

                var bounds = new Bounds(instance.transform.position, Vector3.one);
                var first = true;
                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                    var filter = renderer.GetComponent<MeshFilter>();
                    if (filter?.sharedMesh != null) tris += CountTriangles(filter.sharedMesh);

                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                {
                    t.gameObject.isStatic = true;
                }

                // 바닥 콜라이더. 블록아웃 메시마다 MeshCollider를 붙이면 구역당
                // 수백 개가 되고, 그 비용은 걷는 재미에 아무것도 보태지 않는다.
                // 지금 필요한 것은 "떨어지지 않는 것"뿐이다 — 벽 충돌은 나중 일이다.
                var floor = new GameObject("바닥 콜라이더");
                floor.transform.SetParent(instance.transform, true);
                floor.transform.position = new Vector3(bounds.center.x, -0.5f, bounds.center.z);
                var box = floor.AddComponent<BoxCollider>();
                box.size = new Vector3(bounds.size.x + 20f, 1f, bounds.size.z + 20f);

                instance.AddComponent<ZoneMap>().zone = zone;
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
            var soldierPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                $"{ArtRoot}/character/char.base.player/char.base.player.prefab");
            squad.soldierPrefab = soldierPrefab;

            // 내가 조작하는 분대원. 분대원 표시와 별개다 — 이쪽은 좌표를 스스로 갖고
            // 걷고, 저쪽은 서버가 말한 구역에 세워질 뿐이다.
            var playerGo = new GameObject("Player");
            var controller = playerGo.AddComponent<CharacterController>();
            controller.height = 1.75f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);

            if (soldierPrefab != null)
            {
                var body = (GameObject)PrefabUtility.InstantiatePrefab(soldierPrefab, playerGo.transform);
                body.transform.localPosition = Vector3.zero;
                foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                {
                    renderer.sharedMaterial = material;
                }
            }

            var player = playerGo.AddComponent<LocalPlayer>();
            player.view = camera;

            var interactor = playerGo.AddComponent<Interactor>();
            interactor.origin = playerGo.transform;

            var world = worldGo.AddComponent<ZoneWorld>();
            world.client = client;
            world.player = player;
            world.squad = squad;
            world.markerMaterial = material;

            // 한글 폰트. 없으면 서버가 보낸 라벨이 전부 빈칸으로 나온다.
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/SoldierKR.otf");
            if (font == null) Debug.LogWarning("[M0_Net] 한글 폰트 없음 — tools/font/subset.py 로 만든다");

            var boot = netGo.AddComponent<NetBootstrap>();
            boot.client = client;
            boot.squad = squad;
            boot.world = world;

            var hud = netGo.AddComponent<Hud>();
            hud.client = client;
            hud.boot = boot;
            hud.interactor = interactor;
            hud.world = world;
            hud.player = player;
            hud.font = font;

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"[M0_Net] 씬 생성 완료: {ScenePath}\n" +
                $"  구역 맵 {placed}/8 배치 · {tris:N0} tris\n" +
                $"  분대원 프리팹: {(soldierPrefab != null ? "있음" : "없음 — 캡슐로 대체")}");
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
