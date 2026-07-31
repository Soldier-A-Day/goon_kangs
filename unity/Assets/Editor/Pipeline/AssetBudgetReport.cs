using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SoldierADay.EditorTools
{
    /// <summary>
    /// 예산 검사 — 카탈로그가 정한 예산과 실제 파일을 맞춰본다.
    ///
    /// TS 쪽 `checkManifest`가 **기준이 스스로 모순되지 않는지**를 보고,
    /// 이 도구는 **파일이 그 기준을 지키는지**를 본다. 둘은 다른 질문이고
    /// 순서가 있다 — 기준이 틀린 채로 파일을 검사하면 아무 의미가 없다.
    ///
    /// 에셋이 하나도 없어도 돌아간다. 오히려 그때 가장 쓸모 있다 —
    /// **다음 마일스톤에 무엇이 없는지**가 그대로 작업 목록이 된다.
    /// </summary>
    public static class AssetBudgetReport
    {
        private const string ArtRoot = "Assets/Art";

        private sealed class Row
        {
            public AssetManifest.Entry entry;
            public int foundCount;
            public int foundTris;
            public bool Missing => foundCount == 0;
            public bool OverBudget => foundTris > entry.TotalTris;
        }

        [MenuItem("SOLDIER/에셋/예산 검사")]
        public static void Run() => Report("M5", failOnError: false);

        /// <summary>배치모드 진입점. 예산을 넘으면 0이 아닌 코드로 끝낸다</summary>
        public static void RunForCi()
        {
            var milestone = MilestoneFromArgs();
            var ok = Report(milestone, failOnError: true);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>
        /// `-milestone M0` 으로 검사 범위를 정한다. 없으면 M0 — 지금 여기까지 왔으니
        /// 기본값이 가장 좁은 범위인 편이 낫다. M5를 기본으로 두면 아직 만들 차례가
        /// 아닌 것들이 전부 '없음'으로 뜨고, 진짜 누락이 그 안에 묻힌다.
        /// </summary>
        private static string MilestoneFromArgs()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i += 1)
            {
                if (args[i] == "-milestone") return args[i + 1];
            }
            return "M0";
        }

        private static bool Report(string milestone, bool failOnError)
        {
            var rows = Scan(milestone);
            var text = new StringBuilder();

            text.AppendLine($"[에셋] 예산 검사 — {milestone} 범위 · 항목 {rows.Count}");
            text.AppendLine();
            text.AppendLine("상태\tid\t실측 tris\t예산\t비고");

            foreach (var row in rows.OrderBy(r => r.entry.category).ThenBy(r => r.entry.id))
            {
                var state = row.Missing ? "없음" : row.OverBudget ? "초과" : "통과";
                var found = row.Missing ? "—" : $"{row.foundTris:N0}";
                var note = row.Missing
                    ? $"{ArtRoot}/{row.entry.category}/{row.entry.id}/ 가 비었다"
                    : row.OverBudget
                        ? $"{(float)row.foundTris / row.entry.TotalTris:P0}"
                        : $"{row.entry.count}벌 중 {row.foundCount}";

                text.AppendLine(
                    $"{state}\t{row.entry.id}\t{found}\t{row.entry.TotalTris:N0}\t{note}");
            }

            var over = rows.Where(r => r.OverBudget).ToArray();
            var missing = rows.Where(r => r.Missing).ToArray();
            var built = rows.Where(r => !r.Missing).ToArray();

            text.AppendLine();
            text.AppendLine($"만들어진 것 {built.Length} · 없는 것 {missing.Length} · 예산 초과 {over.Length}");

            // 실제로 존재하는 에셋만으로 동시 표시 부하를 다시 계산한다.
            // 카탈로그의 추정치가 아니라 지금 파일 기준이라, 만들수록 이 값이 자란다.
            if (built.Length > 0)
            {
                var screen = AssetManifest.Limits.screenTris.value;
                var sum = built.Sum(r => r.foundTris);
                text.AppendLine(
                    $"현재 에셋 LOD0 합계 {sum:N0} tris (화면 예산 {screen:N0} 대비 {(float)sum / screen:P0})");
                text.AppendLine(
                    "  ※ 이건 전부 동시에 보일 때의 값이다. 실제 동시 표시는 LOD·오클루전 이후라 더 낮다");
            }

            if (missing.Length > 0)
            {
                text.AppendLine();
                text.AppendLine($"{milestone}에 필요한데 아직 없는 것:");
                foreach (var row in missing)
                {
                    text.AppendLine($"  - {row.entry.id} · {row.entry.label} ({row.entry.TotalTris:N0} tris)");
                }
            }

            if (over.Length > 0) Debug.LogError(text.ToString());
            else Debug.Log(text.ToString());

            // 없는 것은 실패가 아니다 — 아직 만들 차례가 오지 않았을 뿐이다.
            // 예산 초과만 실패다. 그건 이미 만든 것이 규격을 벗어났다는 뜻이라
            // 시간이 지날수록 되돌리기가 비싸진다.
            return !failOnError || over.Length == 0;
        }

        private static List<Row> Scan(string milestone)
        {
            var rows = new List<Row>();

            foreach (var entry in AssetManifest.RequiredBy(milestone))
            {
                var row = new Row { entry = entry };
                var dir = $"{ArtRoot}/{entry.category}/{entry.id}";

                if (Directory.Exists(dir))
                {
                    // Model(임포트한 fbx·obj)과 Prefab(스킨드 메시)을 함께 센다.
                    // 리그가 붙는 에셋은 OBJ로 담기지 않아 프리팹으로 만들어지므로,
                    // Model만 세면 캐릭터와 피복이 통째로 '없음'으로 잡힌다.
                    foreach (var guid in AssetDatabase.FindAssets("t:Model t:Prefab", new[] { dir }))
                    {
                        var path = AssetDatabase.GUIDToAssetPath(guid);
                        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (model == null) continue;

                        // LOD0만 센다. LOD1~3은 감산율로 파생되는 것이라
                        // 함께 세면 같은 메시를 두 번 세게 된다.
                        if (path.Contains("_LOD1") || path.Contains("_LOD2") || path.Contains("_LOD3")) continue;

                        row.foundCount += 1;
                        row.foundTris += CountTriangles(model);
                    }
                }

                rows.Add(row);
            }

            return rows;
        }

        private static int CountTriangles(GameObject model)
        {
            var total = 0;
            foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh != null) total += filter.sharedMesh.triangles.Length / 3;
            }
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh != null) total += skinned.sharedMesh.triangles.Length / 3;
            }
            return total;
        }
    }
}
