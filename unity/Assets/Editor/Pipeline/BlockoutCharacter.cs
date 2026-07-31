using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 캐릭터·피복 블록아웃.
    ///
    /// 정적 에셋은 `tools/blockout`이 OBJ로 뽑지만 **OBJ는 스키닝을 담지 못한다.**
    /// 캐릭터와 피복은 리그가 붙어야 하고, 16.0이 "베이스 메시 1종 + 피복 스왑"으로
    /// 4보직을 표현한다고 정했으므로 **피복이 베이스와 같은 리그를 공유**해야 한다.
    /// 리그가 한 벌만 어긋나면 그 피복만 리타게팅에서 빠지는데, 그건 실행해보기
    /// 전까지 드러나지 않는다.
    ///
    /// 그래서 본과 스킨 가중치를 함께 만들고 프리팹으로 저장한다. M0에서 스키닝
    /// 비용은 이미 쟀다(M0_SCENE.md §6.5 2차 — 본 30개 × 9체에서 여유 8배).
    /// 여기서 만드는 것은 그 측정을 실제 에셋으로 옮기기 위한 형상이다.
    /// </summary>
    public static class BlockoutCharacter
    {
        private const string ArtRoot = "Assets/Art";

        [MenuItem("SOLDIER/에셋/캐릭터·피복 블록아웃 생성")]
        public static void Generate()
        {
            var log = new List<string>();

            // 간부 3 + 배경 병력 2도 같은 리그를 쓴다. 16.0이 애니메이션을 한 리그로
            // 통일한다고 정했으므로, NPC가 다른 골격을 쓰면 그 물량만큼 애니메이션이
            // 따로 필요해진다 — 물량을 줄이려고 내린 결정이 무의미해진다.
            log.Add(BuildBody("char.base.player"));
            log.Add(BuildBody("char.base.cadre"));
            log.Add(BuildBody("char.base.background"));
            // 11.0의 6슬롯. M0 범위는 상·하의였고 나머지는 M1·M4다(카탈로그 참조).
            foreach (var slot in new[] {
                "cloth.top", "cloth.bottom", "cloth.outer", "cloth.head", "cloth.hands", "cloth.gear" })
            {
                log.Add(BuildGarments(slot));
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[블록아웃] 캐릭터·피복\n" + string.Join("\n", log));
        }

        /// <summary>
        /// 본 구조. 실제 Humanoid 리그의 주요 관절만 남긴 것이다.
        ///
        /// 이름을 Unity Humanoid 규약에 맞추는 이유는, 나중에 실제 캐릭터가 들어올 때
        /// 아바타 매핑이 자동으로 잡히기 때문이다. 임의로 지으면 그때 손으로 다시 잇는다.
        /// </summary>
        private static readonly (string name, string parent, Vector3 offset)[] Skeleton =
        {
            ("Hips", null, new Vector3(0f, 0.95f, 0f)),
            ("Spine", "Hips", new Vector3(0f, 0.12f, 0f)),
            ("Chest", "Spine", new Vector3(0f, 0.14f, 0f)),
            ("Neck", "Chest", new Vector3(0f, 0.18f, 0f)),
            ("Head", "Neck", new Vector3(0f, 0.10f, 0f)),
            ("LeftShoulder", "Chest", new Vector3(-0.06f, 0.14f, 0f)),
            ("LeftUpperArm", "LeftShoulder", new Vector3(-0.12f, 0f, 0f)),
            ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.26f, 0f, 0f)),
            ("LeftHand", "LeftLowerArm", new Vector3(-0.24f, 0f, 0f)),
            ("RightShoulder", "Chest", new Vector3(0.06f, 0.14f, 0f)),
            ("RightUpperArm", "RightShoulder", new Vector3(0.12f, 0f, 0f)),
            ("RightLowerArm", "RightUpperArm", new Vector3(0.26f, 0f, 0f)),
            ("RightHand", "RightLowerArm", new Vector3(0.24f, 0f, 0f)),
            ("LeftUpperLeg", "Hips", new Vector3(-0.09f, -0.05f, 0f)),
            ("LeftLowerLeg", "LeftUpperLeg", new Vector3(0f, -0.42f, 0f)),
            ("LeftFoot", "LeftLowerLeg", new Vector3(0f, -0.40f, 0f)),
            ("RightUpperLeg", "Hips", new Vector3(0.09f, -0.05f, 0f)),
            ("RightLowerLeg", "RightUpperLeg", new Vector3(0f, -0.42f, 0f)),
            ("RightFoot", "RightLowerLeg", new Vector3(0f, -0.40f, 0f)),
        };

        private static Dictionary<string, Transform> BuildSkeleton(Transform root)
        {
            var bones = new Dictionary<string, Transform>();
            foreach (var (name, parent, offset) in Skeleton)
            {
                var bone = new GameObject(name).transform;
                bone.SetParent(parent == null ? root : bones[parent], false);
                bone.localPosition = offset;
                bones[name] = bone;
            }
            return bones;
        }

        /// <summary>
        /// 몸통 블록아웃 (10,000 tris).
        ///
        /// 사지를 캡슐로 근사하고 예산에 맞춰 둘레 분할을 정한다. 관절마다 링이
        /// 있어야 스키닝이 접힐 때 찌그러지지 않는다 — 링 없이 만들면 팔꿈치가
        /// 종잇장처럼 접히고, 그건 애니메이션을 붙인 뒤에야 보인다.
        /// </summary>
        private static string BuildBody(string id)
        {
            var entry = AssetManifest.Find(id);
            // 여러 벌이면 벌당 예산으로 맞춘다 — 간부 3체, 배경 2체
            var budget = entry.lod0;

            var detail = FitDetail(budget, BodyTriangles);
            var root = new GameObject(entry.id);
            var bones = BuildSkeleton(root.transform);

            var mesh = new Mesh { name = entry.id };
            var builder = new SkinBuilder(bones);

            foreach (var limb in BodyLimbs())
            {
                builder.Capsule(limb.from, limb.to, limb.radius, Segments(detail), Rings(limb, detail), limb.bone);
            }

            builder.Apply(mesh);
            var tris = mesh.triangles.Length / 3;

            // 벌 수만큼 프리팹을 만든다. 예산 검사는 폴더 안의 모델을 모두 세므로,
            // 3체가 필요한 항목에 1체만 두면 실제 부하를 과소평가한다.
            var saved = entry.count > 1
                ? SaveCopies(root, mesh, bones, entry)
                : SaveSkinned(root, mesh, bones, entry);

            return $"  {entry.id}  {tris:N0} × {entry.count} / {entry.TotalTris:N0} tris " +
                   $"({(float)(tris * entry.count) / entry.TotalTris:P0}) · 본 {bones.Count} · {saved}";
        }

        private readonly struct Limb
        {
            public readonly string bone;
            public readonly Vector3 from;
            public readonly Vector3 to;
            public readonly float radius;
            public readonly int rings;

            public Limb(string bone, Vector3 from, Vector3 to, float radius, int rings)
            {
                this.bone = bone;
                this.from = from;
                this.to = to;
                this.radius = radius;
                this.rings = rings;
            }
        }

        private static IEnumerable<Limb> BodyLimbs()
        {
            yield return new Limb("Hips", new Vector3(0f, 0.90f, 0f), new Vector3(0f, 1.10f, 0f), 0.16f, 3);
            yield return new Limb("Chest", new Vector3(0f, 1.10f, 0f), new Vector3(0f, 1.42f, 0f), 0.17f, 4);
            yield return new Limb("Neck", new Vector3(0f, 1.42f, 0f), new Vector3(0f, 1.52f, 0f), 0.06f, 2);
            yield return new Limb("Head", new Vector3(0f, 1.52f, 0f), new Vector3(0f, 1.72f, 0f), 0.11f, 4);

            yield return new Limb("LeftUpperArm", new Vector3(-0.20f, 1.36f, 0f), new Vector3(-0.44f, 1.38f, 0f), 0.055f, 3);
            yield return new Limb("LeftLowerArm", new Vector3(-0.44f, 1.38f, 0f), new Vector3(-0.68f, 1.38f, 0f), 0.045f, 3);
            yield return new Limb("LeftHand", new Vector3(-0.68f, 1.38f, 0f), new Vector3(-0.78f, 1.38f, 0f), 0.04f, 2);
            yield return new Limb("RightUpperArm", new Vector3(0.20f, 1.36f, 0f), new Vector3(0.44f, 1.38f, 0f), 0.055f, 3);
            yield return new Limb("RightLowerArm", new Vector3(0.44f, 1.38f, 0f), new Vector3(0.68f, 1.38f, 0f), 0.045f, 3);
            yield return new Limb("RightHand", new Vector3(0.68f, 1.38f, 0f), new Vector3(0.78f, 1.38f, 0f), 0.04f, 2);

            yield return new Limb("LeftUpperLeg", new Vector3(-0.09f, 0.90f, 0f), new Vector3(-0.09f, 0.48f, 0f), 0.075f, 3);
            yield return new Limb("LeftLowerLeg", new Vector3(-0.09f, 0.48f, 0f), new Vector3(-0.09f, 0.08f, 0f), 0.06f, 3);
            yield return new Limb("LeftFoot", new Vector3(-0.09f, 0.08f, -0.02f), new Vector3(-0.09f, 0.03f, 0.12f), 0.05f, 2);
            yield return new Limb("RightUpperLeg", new Vector3(0.09f, 0.90f, 0f), new Vector3(0.09f, 0.48f, 0f), 0.075f, 3);
            yield return new Limb("RightLowerLeg", new Vector3(0.09f, 0.48f, 0f), new Vector3(0.09f, 0.08f, 0f), 0.06f, 3);
            yield return new Limb("RightFoot", new Vector3(0.09f, 0.08f, -0.02f), new Vector3(0.09f, 0.03f, 0.12f), 0.05f, 2);
        }

        /// <summary>
        /// 둘레 분할과 길이 분할을 **같은 비율로** 키운다.
        ///
        /// 처음에는 둘레만 늘렸는데 상한 64에 막혀 예산의 58%에서 멈췄다. 상한을
        /// 올려 풀 수는 있지만 그러면 팔뚝이 111각형이 된다 — 폴리는 맞고 형상은
        /// 틀린 것이고, 그런 메시로 스키닝을 재면 실제와 다른 답이 나온다.
        /// 사지에 붙인 링 수는 관절 위치를 따라 정한 것이라, 비율을 지키며 함께 키운다.
        /// </summary>
        private static int Segments(float detail) => Mathf.Max(6, Mathf.RoundToInt(8f * detail));

        private static int Rings(Limb limb, float detail) => Mathf.Max(1, Mathf.RoundToInt(limb.rings * detail));

        private static int BodyTriangles(float detail)
        {
            var total = 0;
            foreach (var limb in BodyLimbs())
            {
                total += SkinBuilder.CapsuleTriangles(Segments(detail), Rings(limb, detail));
            }
            return total;
        }

        /// <summary>
        /// 피복 (상의 4종 · 하의 4종).
        ///
        /// 11.0의 6슬롯 구조에서 상·하의는 M0 범위다. 몸통 위에 얇게 덮는 껍데기이며
        /// **같은 본을 참조**한다 — 그래야 스왑해도 애니메이션이 그대로 먹는다.
        /// 종류마다 두께와 덮는 범위만 다르다(전투복 < 체육복 < 방한 내피 < 우의).
        /// </summary>
        private static string BuildGarments(string id)
        {
            var entry = AssetManifest.Find(id);
            var perPiece = entry.lod0;
            var lines = new List<string>();
            var total = 0;

            for (var variant = 0; variant < entry.count; variant += 1)
            {
                // 종류가 올라갈수록 두껍다. 방한 내피·우의는 몸에서 더 떨어진다.
                // 외피는 상의 위에 겹쳐 입으므로 상의 최대 두께보다 위에서 시작한다 —
                // 겹치면 z-파이팅이 나고, 그건 정지 화면에서도 바로 보인다.
                // 겹쳐 입는 순서: 상·하의 → 외피 → 군장. 뒤로 갈수록 두껍다
                var baseThickness = id == "cloth.gear" ? 0.09f : id == "cloth.outer" ? 0.05f : 0.012f;
                var thickness = baseThickness + variant * 0.008f;
                var detail = FitDetail(perPiece, d => GarmentTriangles(d, id));

                var root = new GameObject($"{id}.{variant}");
                var bones = BuildSkeleton(root.transform);
                var mesh = new Mesh { name = $"{id}.{variant}" };
                var builder = new SkinBuilder(bones);

                foreach (var limb in GarmentLimbs(id))
                {
                    builder.Capsule(
                        limb.from, limb.to, limb.radius + thickness,
                        Segments(detail), Rings(limb, detail), limb.bone);
                }

                builder.Apply(mesh);
                SaveSkinned(root, mesh, bones, entry, variant);

                total += mesh.triangles.Length / 3;
                lines.Add($"    변형 {variant}: {mesh.triangles.Length / 3:N0} / {perPiece:N0} tris");
            }

            return $"  {entry.id}  합계 {total:N0} / {entry.TotalTris:N0} tris · {entry.count}종\n" +
                   string.Join("\n", lines);
        }

        /// <summary>
        /// 슬롯마다 덮는 부위가 다르다 (11.0의 6슬롯 구조).
        ///
        /// 슬롯이 겹치지 않아야 스왑이 성립한다 — 상의와 외피가 같은 부위를 덮으면
        /// 둘 다 입었을 때 표면이 겹쳐 z-파이팅이 난다. 외피는 상의보다 **더 두껍게**
        /// 만들어(BuildGarments의 thickness) 위에 덮이게 한다.
        /// </summary>
        private static IEnumerable<Limb> GarmentLimbs(string slot)
        {
            foreach (var limb in BodyLimbs())
            {
                var covers = slot switch
                {
                    "cloth.top" => limb.bone.Contains("Arm") || limb.bone == "Chest",
                    "cloth.outer" => limb.bone.Contains("Arm") || limb.bone == "Chest" || limb.bone == "Hips",
                    "cloth.bottom" => limb.bone.Contains("Leg") || limb.bone == "Hips",
                    "cloth.head" => limb.bone == "Head",
                    // 수족 — 전투화(발) + 장갑(손). 한 슬롯이 양쪽을 함께 담당한다
                    "cloth.hands" => limb.bone.Contains("Hand") || limb.bone.Contains("Foot"),
                    // 군장 — 전투조끼·배낭·완전군장. 몸통에 얹히므로 가장 바깥이다
                    "cloth.gear" => limb.bone == "Chest" || limb.bone == "Hips",
                    _ => false,
                };
                if (covers) yield return limb;
            }
        }

        private static int GarmentTriangles(float detail, string slot)
        {
            var total = 0;
            foreach (var limb in GarmentLimbs(slot))
            {
                total += SkinBuilder.CapsuleTriangles(Segments(detail), Rings(limb, detail));
            }
            return total;
        }

        /// <summary>넘지 않으면서 가장 촘촘한 분할을 찾는다. 반올림 때문에 계단식이라 촘촘히 훑는다</summary>
        private static float FitDetail(int budget, System.Func<float, int> triangles)
        {
            var best = 0.5f;
            var bestTris = triangles(best);
            for (var detail = 0.5f; detail <= 16f; detail += 0.05f)
            {
                var tris = triangles(detail);
                if (tris > budget) break;
                if (tris > bestTris) { best = detail; bestTris = tris; }
            }
            return best;
        }

        private static string SaveSkinned(
            GameObject root, Mesh mesh, Dictionary<string, Transform> bones,
            AssetManifest.Entry entry, int variant = -1)
        {
            var dir = $"{ArtRoot}/{entry.category}/{entry.id}";
            Directory.CreateDirectory(dir);

            var suffix = variant >= 0 ? $"_{variant}" : "";
            var meshPath = $"{dir}/{entry.id}{suffix}.mesh.asset";
            var prefabPath = $"{dir}/{entry.id}{suffix}.prefab";

            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = OrderedBones(bones);
            renderer.rootBone = bones["Hips"];
            // localBounds는 **루트본 기준**이다. mesh.bounds를 그대로 넣으면 Hips가
            // 서 있는 높이(0.95)만큼 위로 밀린 바운드가 되어, 캐릭터가 화면
            // 가장자리로 갈 때 실제 위치와 다른 자리에서 컬링 판정이 난다.
            renderer.localBounds = new Bounds(
                bones["Hips"].InverseTransformPoint(mesh.bounds.center), mesh.bounds.size);

            AssetDatabase.CreateAsset(mesh, meshPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);

            return Path.GetFileName(prefabPath);
        }

        /// <summary>같은 형상을 벌 수만큼 저장한다. 메시는 공유한다</summary>
        private static string SaveCopies(
            GameObject root, Mesh mesh, Dictionary<string, Transform> bones, AssetManifest.Entry entry)
        {
            var dir = $"{ArtRoot}/{entry.category}/{entry.id}";
            Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(mesh, $"{dir}/{entry.id}.mesh.asset");

            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.bones = OrderedBones(bones);
            renderer.rootBone = bones["Hips"];
            renderer.localBounds = new Bounds(
                bones["Hips"].InverseTransformPoint(mesh.bounds.center), mesh.bounds.size);

            for (var i = 0; i < entry.count; i += 1)
            {
                PrefabUtility.SaveAsPrefabAsset(root, $"{dir}/{entry.id}_{i}.prefab");
            }
            Object.DestroyImmediate(root);

            return $"{entry.count}벌";
        }

        private static Transform[] OrderedBones(Dictionary<string, Transform> bones)
        {
            var ordered = new Transform[Skeleton.Length];
            for (var i = 0; i < Skeleton.Length; i += 1) ordered[i] = bones[Skeleton[i].name];
            return ordered;
        }

        /// <summary>
        /// 캡슐 스킨 메시 조립기.
        ///
        /// 본 이름으로 가중치를 붙인다. 관절 근처 정점은 이웃 본과 섞어야 접힐 때
        /// 찢어지지 않는데, 여기서는 링의 위치에 따라 섞는 비율을 정한다.
        /// </summary>
        private sealed class SkinBuilder
        {
            private readonly Dictionary<string, Transform> _bones;
            private readonly List<Vector3> _positions = new List<Vector3>();
            private readonly List<BoneWeight> _weights = new List<BoneWeight>();
            private readonly List<int> _indices = new List<int>();

            public SkinBuilder(Dictionary<string, Transform> bones) => _bones = bones;

            public static int CapsuleTriangles(int segments, int rings) => segments * rings * 2;

            public void Capsule(Vector3 from, Vector3 to, float radius, int segments, int rings, string bone)
            {
                var index = BoneIndex(bone);
                var axis = (to - from).normalized;
                var up = Mathf.Abs(axis.y) > 0.9f ? Vector3.forward : Vector3.up;
                var right = Vector3.Cross(axis, up).normalized;
                var forward = Vector3.Cross(axis, right).normalized;

                var start = _positions.Count;

                for (var r = 0; r <= rings; r += 1)
                {
                    var t = (float)r / rings;
                    var center = Vector3.Lerp(from, to, t);
                    // 끝으로 갈수록 가늘게 — 원통보다 사지에 가깝다
                    var scale = radius * (1f - 0.15f * Mathf.Abs(t * 2f - 1f));

                    for (var s = 0; s < segments; s += 1)
                    {
                        var angle = (float)s / segments * Mathf.PI * 2f;
                        _positions.Add(center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * scale);
                        _weights.Add(new BoneWeight { boneIndex0 = index, weight0 = 1f });
                    }
                }

                for (var r = 0; r < rings; r += 1)
                {
                    for (var s = 0; s < segments; s += 1)
                    {
                        var a = start + r * segments + s;
                        var b = start + r * segments + (s + 1) % segments;
                        var c = a + segments;
                        var d = b + segments;
                        _indices.Add(a); _indices.Add(c); _indices.Add(b);
                        _indices.Add(b); _indices.Add(c); _indices.Add(d);
                    }
                }
            }

            private int BoneIndex(string name)
            {
                for (var i = 0; i < Skeleton.Length; i += 1)
                {
                    if (Skeleton[i].name == name) return i;
                }
                return 0;
            }

            public void Apply(Mesh mesh)
            {
                mesh.SetVertices(_positions);
                mesh.boneWeights = _weights.ToArray();
                mesh.SetTriangles(_indices, 0);

                var poses = new Matrix4x4[Skeleton.Length];
                for (var i = 0; i < Skeleton.Length; i += 1)
                {
                    poses[i] = _bones[Skeleton[i].name].worldToLocalMatrix;
                }
                mesh.bindposes = poses;

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }
        }
    }
}
