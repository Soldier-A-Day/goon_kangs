using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.M0
{
    /// <summary>
    /// 프록시가 재지 못한 축들.
    ///
    /// 폴리·오브젝트 수는 8배 여유가 확인됐다(docs/M0_SCENE.md §6.5). 그런데 그건
    /// **가장 쉬운 축**이었다. 실제 게임의 부하는 다른 데서 온다.
    ///
    /// - **스킨드 메시** — ASSETS.md §0이 "WebGL2는 드로우콜이 병목"이라 지목했고,
    ///   스킨드는 정적 배칭이 불가능해 개수만큼 드로우콜이 그대로 나간다.
    ///   게다가 매 프레임 본 스키닝 비용이 CPU에 얹힌다
    /// - **파티클** — 축소 순위 1번(19.0). 얼마나 여유가 있는지 알아야 무엇부터 깎을지 정해진다
    /// - **후처리** — 온도 6밴드 그레이딩이 온도 시스템 체감의 전부다(18.0 ARCH-01).
    ///   여기서 프레임을 잃으면 온도 시스템 자체가 흔들린다
    /// </summary>
    public sealed class HeavyAxes : MonoBehaviour
    {
        [Header("스킨드 메시 — 정적 배칭 불가")]
        [Tooltip("캐릭터 4인 + 간부 3 + 배경 2 = 9체 (ASSETS.md §9)")]
        public int skinnedCount = 9;

        [Tooltip("체당 폴리. 베이스 10,000 + 피복 평균 5,500")]
        public int skinnedTriangles = 15_500;

        [Tooltip("본 수. 실제 Humanoid 리그 기준")]
        public int boneCount = 30;

        [Header("파티클 — 축소 순위 1번")]
        [Tooltip("극혹한 밴드: 서리·눈 800 + 입김 40×4인 = 960 (ASSETS.md §5.2)")]
        public int particleCount = 960;

        [Header("후처리")]
        public bool postProcessing = true;

        public Material baseMaterial;

        private GameObject _root;

        public struct AxisReport
        {
            public int skinnedRenderers;
            public int skinnedTriangles;
            public int particles;
            public bool postProcessing;
        }

        public AxisReport Report { get; private set; }

        public void Build()
        {
            Clear();
            _root = new GameObject("HeavyAxes");
            _root.transform.SetParent(transform, false);

            BuildSkinned();
            BuildParticles();
            if (postProcessing) BuildVolume();

            Report = new AxisReport
            {
                skinnedRenderers = skinnedCount,
                skinnedTriangles = skinnedCount * skinnedTriangles,
                particles = particleCount,
                postProcessing = postProcessing,
            };
        }

        /// <summary>
        /// 스킨드 메시 프록시. 본을 실제로 달아 매 프레임 스키닝이 돌게 한다 —
        /// 본 없이 SkinnedMeshRenderer만 붙이면 드로우콜만 재고 스키닝 비용을 놓친다.
        /// </summary>
        private void BuildSkinned()
        {
            var mesh = ProxyMesh.Sphere(skinnedTriangles);

            for (var i = 0; i < skinnedCount; i += 1)
            {
                var go = new GameObject($"Skinned_{i}");
                go.transform.SetParent(_root.transform, false);
                go.transform.position = new Vector3(
                    (i - skinnedCount / 2f) * 3f, 1f, -20f);

                var bones = new Transform[boneCount];
                var poses = new Matrix4x4[boneCount];
                var root = new GameObject("Root").transform;
                root.SetParent(go.transform, false);

                var parent = root;
                for (var b = 0; b < boneCount; b += 1)
                {
                    var bone = new GameObject($"Bone_{b}").transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = new Vector3(0f, 2f / boneCount, 0f);
                    bones[b] = bone;
                    poses[b] = bone.worldToLocalMatrix * go.transform.localToWorldMatrix;
                    parent = bone;
                }

                // 정점을 본에 고르게 배분한다. 실제 캐릭터처럼 가중치가 퍼져야
                // 스키닝 비용이 현실적으로 나온다.
                var skinned = BuildSkinnedMesh(mesh, boneCount, poses);

                var renderer = go.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = skinned;
                renderer.bones = bones;
                renderer.rootBone = root;
                renderer.sharedMaterial = baseMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                // 화면 밖이어도 스키닝을 돌려 최악을 잰다
                renderer.updateWhenOffscreen = true;

                go.AddComponent<BoneWobble>().bones = bones;
            }
        }

        private static Mesh BuildSkinnedMesh(Mesh source, int bones, Matrix4x4[] poses)
        {
            var mesh = Object.Instantiate(source);
            var vertices = mesh.vertices;
            var weights = new BoneWeight[vertices.Length];

            for (var i = 0; i < vertices.Length; i += 1)
            {
                // 높이에 따라 본을 고르고 이웃 본과 섞는다 — 실제 스키닝과 같은 모양
                var t = Mathf.InverseLerp(-1f, 1f, vertices[i].y);
                var index = Mathf.Clamp(Mathf.FloorToInt(t * (bones - 1)), 0, bones - 2);
                weights[i] = new BoneWeight
                {
                    boneIndex0 = index,
                    weight0 = 0.6f,
                    boneIndex1 = index + 1,
                    weight1 = 0.4f,
                };
            }

            mesh.boneWeights = weights;
            mesh.bindposes = poses;
            return mesh;
        }

        /// <summary>ASSETS.md §5.2 극혹한 밴드 — 서리·눈 + 입김</summary>
        private void BuildParticles()
        {
            if (particleCount <= 0) return;

            var go = new GameObject("Particles");
            go.transform.SetParent(_root.transform, false);
            go.transform.position = new Vector3(0f, 10f, 0f);

            var particles = go.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.maxParticles = particleCount;
            main.startLifetime = 6f;
            main.startSpeed = 2f;
            main.startSize = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = particles.emission;
            emission.rateOverTime = particleCount / 6f;

            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(120f, 1f, 120f);

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = baseMaterial;
        }

        /// <summary>
        /// 온도 밴드 그레이딩 자리. Volume 프로파일을 코드로 만들어
        /// 후처리 스택이 실제로 도는 비용을 잰다.
        /// </summary>
        private void BuildVolume()
        {
            var go = new GameObject("PostFX");
            go.transform.SetParent(_root.transform, false);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1f;
            volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();

            // URP 후처리 컴포넌트는 어셈블리 참조가 필요하므로, 여기서는
            // 카메라의 후처리 플래그만 켜서 스택이 도는 비용을 잰다.
            var camera = Camera.main;
            if (camera != null)
            {
                var data = camera.GetUniversalAdditionalCameraData();
                if (data != null) data.renderPostProcessing = true;
            }
        }

        public void Clear()
        {
            if (_root != null) DestroyImmediate(_root);
        }
    }

    /// <summary>본을 흔들어 스키닝이 매 프레임 실제로 돌게 한다.</summary>
    public sealed class BoneWobble : MonoBehaviour
    {
        public Transform[] bones;

        private void Update()
        {
            if (bones == null) return;
            var t = Time.time;
            for (var i = 0; i < bones.Length; i += 1)
            {
                if (bones[i] == null) continue;
                bones[i].localRotation = Quaternion.Euler(
                    Mathf.Sin(t + i * 0.3f) * 4f, 0f, 0f);
            }
        }
    }
}
