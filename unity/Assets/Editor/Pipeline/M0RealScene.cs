using System.Collections.Generic;
using System.IO;
using SoldierADay.M0;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// `M0_Real` — 실제 에셋 대조 씬 (docs/M0_SCENE.md §0 · §4).
    ///
    /// **이 씬은 게이트가 아니다.** M0 에셋 범위는 목표 부하의 1/4이라 60fps가
    /// 나오는 게 당연하다. §4가 정한 목적은 하나다 —
    /// **같은 폴리에서 합성 프록시보다 무겁게 나오는 항목을 찾는 것.**
    ///
    /// 그래서 카메라·태양·URP 설정을 `M0_Synthetic`과 **똑같이** 맞춘다.
    /// 렌더 설정이 하나라도 다르면 차이가 에셋 때문인지 설정 때문인지 구분되지 않고,
    /// 그러면 씬을 두 개 만든 이유가 사라진다.
    /// </summary>
    public static class M0RealScene
    {
        private const string SceneDir = "Assets/Scenes";
        private const string ScenePath = SceneDir + "/M0_Real.unity";
        private const string ArtRoot = "Assets/Art";

        /// <summary>3.0의 4인 편성. 보직마다 한 명씩이다</summary>
        private static readonly string[] Roles = { "rifle", "comms", "medic", "admin" };

        [MenuItem("SOLDIER/M0_Real 씬 생성")]
        public static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            M0Pipeline.EnsureRenderPipeline();
            var material = M0Pipeline.EnsureProxyMaterial();

            // --- 합성 씬과 동일해야 하는 부분 ---
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
            camera.farClipPlane = 300f;
            // --- 여기까지 ---

            var placed = new List<string>();

            // 부대 맵 둘을 함께 놓는다. §1이 부대 맵과 훈련 맵을 더하는 이유와 같다 —
            // 전환 구간에서 양쪽이 메모리에 함께 있고, 그때가 최악이다.
            placed.Add(PlaceStatic("baseMap", "base.drillGround", Vector3.zero, material));
            placed.Add(PlaceStatic("baseMap", "base.barracks", new Vector3(0f, 0f, 60f), material));

            // 4인 편성. 연병장에 도열한 간격으로 놓는다(6.0 제식·집합)
            for (var i = 0; i < Roles.Length; i += 1)
            {
                var at = new Vector3((i - 1.5f) * 1.6f, 0f, -10f);
                placed.Add(BuildSoldier(Roles[i], i, at, material));
            }

            var probeGo = new GameObject("M0_Real");
            probeGo.AddComponent<RealProbe>();

            Directory.CreateDirectory(SceneDir);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[M0_Real] 씬 생성 완료: {ScenePath}\n" + string.Join("\n", placed));
        }

        /// <summary>
        /// 정적 지오메트리 배치.
        ///
        /// `isStatic`을 켜야 정적 배칭이 걸린다. 켜지 않으면 모듈 하나하나가
        /// 드로우콜이 되는데, 생활관 모듈 24종이 수십 벌씩 놓이므로 그대로면
        /// §0의 드로우콜 800을 이 씬 하나로 넘긴다.
        ///
        /// 임포트 규칙이 `isReadable = false`로 두는 것과 충돌하지 않는다 —
        /// 정적 배칭은 빌드 시점에 구워지므로 런타임 읽기가 필요 없다.
        /// M0에서 코드로 만든 프록시 메시는 반대로 읽기를 켜야 했는데, 그건
        /// 런타임에 생성한 메시라 빌드 시점에 존재하지 않았기 때문이다.
        /// </summary>
        private static string PlaceStatic(string category, string id, Vector3 at, Material material)
        {
            var path = FindModel($"{ArtRoot}/{category}/{id}");
            if (path == null) return $"  ✗ {id} — 에셋 없음. `npm run start -w @sad/blockout` 먼저";

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            instance.name = id;
            instance.transform.position = at;

            var tris = 0;
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) tris += filter.sharedMesh.triangles.Length / 3;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.sharedMaterial = material;
            }

            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(
                    t.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
            }

            return $"  ✓ {id,-20} {tris,8:N0} tris · 정적 배칭";
        }

        /// <summary>
        /// 병사 한 명 = 베이스 + 상의 + 하의 (+ 소총).
        ///
        /// **피복을 베이스의 본에 다시 묶는 것이 이 함수의 요점이다.** 16.0이
        /// "베이스 메시 1종 + 피복 스왑"으로 4보직을 표현한다고 정했으므로,
        /// 피복은 자기 골격이 아니라 몸의 골격을 따라가야 한다. 다시 묶지 않으면
        /// 옷이 몸과 따로 논다 — 그리고 그건 **애니메이션을 붙이기 전까지 안 보인다.**
        /// 가만히 서 있는 T포즈에서는 겹쳐 있어 멀쩡해 보인다.
        ///
        /// 실제 피복 스왑 시스템이 하는 일과 같다. 여기서 성립하는 것을 확인해두면
        /// 실제 에셋이 왔을 때 리그만 맞추면 된다.
        /// </summary>
        private static string BuildSoldier(string role, int index, Vector3 at, Material material)
        {
            var soldier = new GameObject($"soldier_{role}");
            soldier.transform.position = at;

            var body = Spawn("character", "char.base.player", soldier.transform, material);
            if (body == null) return $"  ✗ soldier_{role} — 캐릭터 에셋 없음";

            var bones = CollectBones(body);
            var tris = TrianglesOf(body);
            var parts = 1;

            // 피복 변형은 보직마다 다른 것을 입힌다 — 4종을 다 쓰는지 확인하려는 것이다
            foreach (var slot in new[] { "cloth.top", "cloth.bottom" })
            {
                var garment = Spawn("clothing", $"{slot}_{index}", soldier.transform, material, slot);
                if (garment == null) continue;

                RebindTo(garment, bones);
                tris += TrianglesOf(garment);
                parts += 1;
            }

            // 소총은 전원 지급이다(표 11-1). 오른손에 붙인다
            var riflePath = FindModel($"{ArtRoot}/equipment/equip.rifle");
            if (riflePath != null && bones.TryGetValue("RightHand", out var hand))
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(riflePath);
                var rifle = (GameObject)PrefabUtility.InstantiatePrefab(source, hand);
                rifle.name = "equip.rifle";
                // 손 본은 손목 위치이고 총은 그보다 앞·아래로 나가야 한다. 0으로 두면
                // 몸에 파묻혀 **화면에 없는데 계측기는 세는** 상태가 된다 — 그러면
                // 프레임 값이 실제 게임과 어긋난다. 파지 자세에 맞춰 옮긴다.
                rifle.transform.localPosition = new Vector3(0.06f, -0.02f, 0.14f);
                rifle.transform.localRotation = Quaternion.Euler(0f, 90f, -8f);
                foreach (var renderer in rifle.GetComponentsInChildren<MeshRenderer>(true))
                {
                    renderer.sharedMaterial = material;
                }
                tris += TrianglesOf(rifle);
                parts += 1;
            }

            return $"  ✓ soldier_{role,-8} {tris,8:N0} tris · 파츠 {parts} · 본 {bones.Count}";
        }

        /// <summary>
        /// 피복을 몸의 본에 다시 묶는다.
        ///
        /// 이름으로 맞춘다. 블록아웃 생성기가 Unity Humanoid 규약 이름을 쓰므로
        /// 실제 캐릭터가 들어와도 같은 방식이 통한다 — 그러라고 이름을 맞춘 것이다.
        /// </summary>
        private static void RebindTo(GameObject garment, Dictionary<string, Transform> bones)
        {
            var renderer = garment.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null) return;

            var own = renderer.bones;
            var rebound = new Transform[own.Length];
            var missing = 0;

            for (var i = 0; i < own.Length; i += 1)
            {
                if (own[i] != null && bones.TryGetValue(own[i].name, out var target)) rebound[i] = target;
                else { rebound[i] = own[i]; missing += 1; }
            }

            if (missing > 0)
            {
                Debug.LogWarning(
                    $"[M0_Real] {garment.name}: 본 {missing}개가 몸에 없다 — 그 부분만 따로 움직인다");
            }

            var rootName = renderer.rootBone != null ? renderer.rootBone.name : "Hips";
            renderer.bones = rebound;
            if (bones.TryGetValue(rootName, out var newRoot)) renderer.rootBone = newRoot;

            // 자기 골격은 더 이상 참조되지 않는다. 남겨두면 트랜스폼이 계속 갱신되어
            // 아무도 쓰지 않는 계층을 매 프레임 계산하게 된다.
            foreach (Transform child in garment.transform)
            {
                if (child.name == "Hips") Object.DestroyImmediate(child.gameObject);
            }
        }

        private static Dictionary<string, Transform> CollectBones(GameObject root)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;
            return bones;
        }

        private static GameObject Spawn(
            string category, string file, Transform parent, Material material, string dirId = null)
        {
            var dir = $"{ArtRoot}/{category}/{dirId ?? file}";
            var path = File.Exists($"{dir}/{file}.prefab") ? $"{dir}/{file}.prefab" : FindModel(dir, file);
            if (path == null) return null;

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.transform.localPosition = Vector3.zero;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = material;
            }

            return instance;
        }

        private static string FindModel(string dir, string nameHint = null)
        {
            if (!Directory.Exists(dir)) return null;

            foreach (var guid in AssetDatabase.FindAssets("t:Model t:Prefab", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("_LOD")) continue;
                if (nameHint != null && !Path.GetFileNameWithoutExtension(path).StartsWith(nameHint)) continue;
                return path;
            }
            return null;
        }

        private static int TrianglesOf(GameObject go)
        {
            var total = 0;
            foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) total += filter.sharedMesh.triangles.Length / 3;
            }
            foreach (var skinned in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null) total += skinned.sharedMesh.triangles.Length / 3;
            }
            return total;
        }

        [MenuItem("SOLDIER/M0_Real WebGL 빌드")]
        public static void BuildWebGL()
        {
            CreateScene();

            var outputDir = Path.GetFullPath(
                Path.Combine(Application.dataPath, "..", "Build", "M0Real"));
            Directory.CreateDirectory(outputDir);

            // 합성 씬 빌드와 같은 설정. 하나라도 다르면 비교가 성립하지 않는다.
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.memorySize = 1024;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);
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
                $"[M0_Real] 빌드 {report.summary.result} · " +
                $"{report.summary.totalSize / (1024f * 1024f):F1}MB · " +
                $"{report.summary.totalTime.TotalMinutes:F1}분 · {outputDir}");

            if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
