using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// M0 합성 부하 생성기 (docs/M0_SCENE.md §3).
    ///
    /// M0 에셋 범위(캐릭터 1세트 + 야외 맵 1)만 배치하면 실제 최대 부하의 1/4에 그친다.
    /// 거기서 60fps가 나와도 완성된 게임이 60fps라는 근거가 되지 못하므로,
    /// 프록시로 목표 부하 436,000 tris를 **합성**해서 잰다.
    ///
    /// 손으로 배치한 씬이 아니라 코드인 이유는 스윕 때문이다 — 재질 수·캐스케이드·파티클을
    /// 바꿔가며 "어디서부터 무너지는가"를 찾는 것이 M0의 산출물이고, 그건 인스펙터에서
    /// 값 하나 바꿔 다시 만드는 편이 빠르다.
    /// </summary>
    public sealed class LoadBuilder : MonoBehaviour
    {
        [Header("목표 — ASSETS.md §9 최대 부하")]
        [Tooltip("합성할 총 삼각형 수. 700K가 화면 예산이고 436K가 실제 최대 부하다.")]
        public int targetTriangles = 489_500;

        [Header("스킨드 프록시 — 캐릭터·NPC 대역")]
        [Tooltip("정적 배칭이 불가능한 렌더러. 드로우콜에 그대로 얹힌다.")]
        public int skinnedCount = 9;
        public int skinnedTriangles = 15_500;

        [Header("환경 — 정적 배칭 대상")]
        public int staticBlockCount = 240;
        public int staticBlockTriangles = 500;

        [Tooltip("재질 수가 곧 배칭 그룹 수다. 8 → 16 → 32 로 올리며 배칭이 깨지는 지점을 찾는다.")]
        public int materialCount = 8;

        [Header("훈련 맵 프록시 — 전환 구간의 최악 지점")]
        public int trainingBlockCount = 200;
        public int trainingBlockTriangles = 1_000;

        [Header("소품 — GPU 인스턴싱")]
        public int propKinds = 20;
        public int propsPerKind = 5;
        public int propTriangles = 300;

        [Header("머티리얼")]
        [Tooltip("프록시가 쓸 기준 머티리얼. 씬이 참조해야 빌드에 셰이더가 포함된다 — " +
                 "Shader.Find는 문자열 조회라 참조로 잡히지 않는다.")]
        public Material baseMaterial;

        [Tooltip("AutoSweep이 부하를 직접 정할 때는 끈다")]
        public bool buildOnStart = true;

        [Header("스윕 — 런타임 토글")]
        [Tooltip("1키: 그림자. 캐스터 패스는 드로우콜을 두 배로 만든다")]
        public bool shadowsEnabled = true;

        [Tooltip("2키: 정적 배칭. 끄면 오브젝트 수만큼 드로우콜이 그대로 나간다")]
        public bool staticBatching = true;

        [Header("배치 범위")]
        public float fieldRadius = 60f;

        private readonly List<Material> _materials = new List<Material>();
        private readonly List<GameObject> _spawned = new List<GameObject>();

        /// <summary>마지막으로 만든 부하의 집계. HUD가 읽어 화면에 띄운다.</summary>
        public LoadReport Report { get; private set; }

        private void Start()
        {
            if (buildOnStart) Build();
        }

        /// <summary>
        /// 런타임 스윕. M0의 산출물은 "어디서부터 무너지는가"인데, 축을 바꿀 때마다
        /// 4분씩 재빌드하면 원인을 좁힐 수 없다. 키로 켜고 끄며 즉시 본다.
        ///
        ///   1  그림자 on/off      — 캐스터 패스가 드로우콜을 두 배로 만드는 몫
        ///   2  정적 배칭 on/off   — 배칭이 실제로 먹는지
        ///   3/4  오브젝트 절반/두 배 — 어느 수에서 30fps가 깨지는지
        /// </summary>
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                shadowsEnabled = !shadowsEnabled;
                ApplyShadows();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                staticBatching = !staticBatching;
                Build();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                Scale(0.5f);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                Scale(2f);
            }
        }

        private void Scale(float factor)
        {
            skinnedCount = Mathf.Max(1, Mathf.RoundToInt(skinnedCount * factor));
            staticBlockCount = Mathf.Max(1, Mathf.RoundToInt(staticBlockCount * factor));
            trainingBlockCount = Mathf.Max(1, Mathf.RoundToInt(trainingBlockCount * factor));
            propsPerKind = Mathf.Max(1, Mathf.RoundToInt(propsPerKind * factor));
            Build();
        }

        private void ApplyShadows()
        {
            var mode = shadowsEnabled
                ? UnityEngine.Rendering.ShadowCastingMode.On
                : UnityEngine.Rendering.ShadowCastingMode.Off;
            foreach (var go in _spawned)
            {
                if (go == null) continue;
                var renderer = go.GetComponent<MeshRenderer>();
                if (renderer != null) renderer.shadowCastingMode = mode;
            }
            Debug.Log($"[M0] 그림자 {(shadowsEnabled ? "on" : "off")}");
        }

        public void Build()
        {
            Clear();
            BuildMaterials();
            if (_materials.Count == 0) return;

            var triangles = 0;
            var renderers = 0;

            // 스킨드 프록시 — 배칭이 되지 않으므로 드로우콜을 그대로 먹는다
            for (var i = 0; i < skinnedCount; i += 1)
            {
                Spawn($"Skinned_{i}", skinnedTriangles, batchable: false);
                triangles += skinnedTriangles;
                renderers += 1;
            }

            // 환경 — 정적 배칭 대상
            for (var i = 0; i < staticBlockCount; i += 1)
            {
                Spawn($"Env_{i}", staticBlockTriangles, batchable: true);
                triangles += staticBlockTriangles;
            }

            // 훈련 맵 프록시 — 부대 맵과 함께 메모리에 있는 최악 상태를 재현한다
            for (var i = 0; i < trainingBlockCount; i += 1)
            {
                Spawn($"Training_{i}", trainingBlockTriangles, batchable: true);
                triangles += trainingBlockTriangles;
            }

            // 소품 — 인스턴싱이 실제로 먹는지 본다
            for (var kind = 0; kind < propKinds; kind += 1)
            {
                for (var n = 0; n < propsPerKind; n += 1)
                {
                    Spawn($"Prop_{kind}_{n}", propTriangles, batchable: false);
                    triangles += propTriangles;
                }
            }

            // 정적 배칭은 씬에 오브젝트가 다 올라온 뒤 한 번에 건다.
            // 메시가 읽기 가능해야 한다 — 아니면 조용히 실패하고 드로우콜이 그대로 남는다.
            if (staticBatching) StaticBatchingUtility.Combine(gameObject);
            ApplyShadows();

            Report = new LoadReport
            {
                triangles = triangles,
                targetTriangles = targetTriangles,
                spawnedObjects = _spawned.Count,
                unbatchedRenderers = renderers,
                materialCount = _materials.Count,
            };

            Debug.Log(
                $"[M0] 합성 부하 {triangles:N0} tris / 목표 {targetTriangles:N0} · " +
                $"오브젝트 {_spawned.Count} · 재질 {_materials.Count} · " +
                $"배칭 {(staticBatching ? "on" : "off")} · 그림자 {(shadowsEnabled ? "on" : "off")}");
        }

        private void BuildMaterials()
        {
            if (baseMaterial == null)
            {
                Debug.LogError("[M0] baseMaterial이 비어 있다 — 씬에서 머티리얼을 지정해야 한다");
                return;
            }

            for (var i = 0; i < materialCount; i += 1)
            {
                // 기준 머티리얼을 복제한다. 같은 셰이더라 변형이 늘지 않는다.
                var material = new Material(baseMaterial) { name = $"M0_Mat_{i}" };
                material.enableInstancing = true;
                // 색을 조금씩 다르게 해 배칭이 색만 보고 합쳐버리지 않게 한다
                material.color = Color.HSVToRGB(i / (float)Mathf.Max(1, materialCount), 0.35f, 0.7f);
                _materials.Add(material);
            }
        }

        /// <summary>
        /// 지정한 삼각형 수에 근접한 구를 만든다.
        /// 구를 쓰는 이유는 세그먼트 수로 폴리를 정밀하게 조절할 수 있어서다.
        /// </summary>
        private void Spawn(string label, int triangles, bool batchable)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            go.transform.position = RandomPoint();
            go.isStatic = batchable;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = ProxyMesh.Sphere(triangles);

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _materials[_spawned.Count % _materials.Count];
            // 전부 그림자를 켠다 — 캐스터 패스가 드로우콜을 두 배로 만드는 것이
            // M0에서 봐야 할 지점이라 여기서 빼면 안 된다 (docs/M0_SCENE.md §2)
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

            _spawned.Add(go);
        }

        private Vector3 RandomPoint()
        {
            var circle = Random.insideUnitCircle * fieldRadius;
            return new Vector3(circle.x, Random.Range(0f, 3f), circle.y);
        }

        public void Clear()
        {
            foreach (var go in _spawned)
            {
                if (go != null) DestroyImmediate(go);
            }
            _spawned.Clear();
            _materials.Clear();
        }

        public struct LoadReport
        {
            public int triangles;
            public int targetTriangles;
            public int spawnedObjects;
            public int unbatchedRenderers;
            public int materialCount;
        }
    }
}
