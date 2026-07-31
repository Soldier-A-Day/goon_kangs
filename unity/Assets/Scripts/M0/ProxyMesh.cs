using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// 목표 삼각형 수에 맞춘 프록시 메시를 만든다.
    ///
    /// 구를 쓰는 이유는 세그먼트 수로 폴리를 정밀하게 조절할 수 있고, 실제 에셋처럼
    /// 모든 방향으로 면이 있어 컬링·그림자 특성이 상자보다 현실적이기 때문이다.
    ///
    /// 같은 폴리 수는 캐시해서 재사용한다 — 프록시 수백 개를 만들 때 메시를 매번 새로
    /// 생성하면 그 자체가 힙을 채워서, 측정하려는 누수와 섞인다.
    /// </summary>
    public static class ProxyMesh
    {
        private static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

        public static Mesh Sphere(int targetTriangles)
        {
            // UV 구의 삼각형 수 = segments × rings × 2. 정사각에 가깝게 나눈다.
            var segments = Mathf.Max(3, Mathf.RoundToInt(Mathf.Sqrt(targetTriangles / 2f)));
            var rings = Mathf.Max(2, Mathf.RoundToInt(targetTriangles / (2f * segments)));
            var key = segments * 10_000 + rings;

            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;

            var mesh = Build(segments, rings);
            mesh.name = $"Proxy_{mesh.triangles.Length / 3}tris";
            Cache[key] = mesh;
            return mesh;
        }

        private static Mesh Build(int segments, int rings)
        {
            var vertices = new List<Vector3>((segments + 1) * (rings + 1));
            var normals = new List<Vector3>(vertices.Capacity);
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(segments * rings * 6);

            for (var ring = 0; ring <= rings; ring += 1)
            {
                var v = ring / (float)rings;
                var phi = v * Mathf.PI;

                for (var segment = 0; segment <= segments; segment += 1)
                {
                    var u = segment / (float)segments;
                    var theta = u * Mathf.PI * 2f;

                    var point = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));

                    vertices.Add(point);
                    normals.Add(point);
                    uvs.Add(new Vector2(u, v));
                }
            }

            var stride = segments + 1;
            for (var ring = 0; ring < rings; ring += 1)
            {
                for (var segment = 0; segment < segments; segment += 1)
                {
                    var a = ring * stride + segment;
                    var b = a + stride;

                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(a + 1);

                    triangles.Add(a + 1);
                    triangles.Add(b);
                    triangles.Add(b + 1);
                }
            }

            var mesh = new Mesh();
            // 프록시는 쉽게 65,535 정점을 넘는다
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.UploadMeshData(markNoLongerReadable: true);
            return mesh;
        }
    }
}
