using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// 자동 부하 스윕.
    ///
    /// 키 입력으로 스윕하려 했으나 성립하지 않았다 — 입력은 프레임당 한 번 폴링되므로
    /// **fps가 낮을수록 조작이 안 먹는다.** 성능이 나쁠 때 성능을 조사하는 도구가
    /// 성능에 의존하면 안 된다.
    ///
    /// 그래서 스스로 돈다. 아주 작은 부하에서 시작해 2배씩 올리며 각 단계를 측정하고,
    /// 30fps가 깨지는 지점을 찾으면 멈춘다. 한 번 띄우면 답이 나온다.
    /// </summary>
    [RequireComponent(typeof(LoadBuilder))]
    public sealed class AutoSweep : MonoBehaviour
    {
        [Tooltip("각 단계를 몇 초 측정할지. 처음 몇 프레임은 버린다.")]
        public float measureSeconds = 3f;

        [Tooltip("측정 전 안정화 대기(초)")]
        public float settleSeconds = 1f;

        [Tooltip("이 fps 밑으로 떨어지면 스윕을 멈춘다 — 표 18-2의 최저선")]
        public float floorFps = 30f;

        [Tooltip("첫 단계의 오브젝트 배율. 1/32에서 시작해 2배씩 올린다")]
        public int startDivisor = 32;

        [Tooltip("목표 부하를 넘어 몇 배까지 밀어붙일지. 60fps는 vsync 상한이라 " +
                 "목표에서 60이 나와도 여유가 얼마인지 알 수 없다 — 깨지는 지점을 찾아야 안다")]
        public int maxMultiplier = 8;

        [Tooltip("스킨드·파티클·후처리를 함께 켠다. 프록시가 재지 못한 축들이다")]
        public bool heavyAxes = true;

        private LoadBuilder _builder;
        private HeavyAxes _heavy;
        private readonly List<string> _rows = new List<string>();
        private string _status = "준비 중";

        private void Awake()
        {
            _builder = GetComponent<LoadBuilder>();
            _heavy = GetComponent<HeavyAxes>();
            // 스윕이 스스로 부하를 정하므로 Start의 자동 빌드는 끈다
            _builder.buildOnStart = false;
        }

        private IEnumerator Start()
        {
            // 기준값을 기억해두고 배율로 되돌려 쓴다
            var baseSkinned = _builder.skinnedCount;
            var baseStatic = _builder.staticBlockCount;
            var baseTraining = _builder.trainingBlockCount;
            var baseProps = _builder.propsPerKind;

            _rows.Add("배율\t오브젝트\t삼각형\t스킨드\t파티클\tfps");

            // 1/32 → 1/1 → 2× → 8×. 목표 부하를 넘겨야 여유가 보인다.
            var steps = new List<(string label, float scale)>();
            for (var divisor = startDivisor; divisor > 1; divisor /= 2)
                steps.Add(($"1/{divisor}", 1f / divisor));
            for (var multiplier = 1; multiplier <= maxMultiplier; multiplier *= 2)
                steps.Add((multiplier == 1 ? "목표" : $"{multiplier}배", multiplier));

            foreach (var (label, scale) in steps)
            {
                _builder.skinnedCount = Mathf.Max(1, Mathf.RoundToInt(baseSkinned * scale));
                _builder.staticBlockCount = Mathf.Max(1, Mathf.RoundToInt(baseStatic * scale));
                _builder.trainingBlockCount = Mathf.Max(1, Mathf.RoundToInt(baseTraining * scale));
                _builder.propsPerKind = Mathf.Max(1, Mathf.RoundToInt(baseProps * scale));

                _status = $"{label} 부하 생성 중";
                _builder.Build();

                if (heavyAxes && _heavy != null)
                {
                    // 스킨드는 배칭이 안 되므로 배율을 그대로 태운다
                    _heavy.skinnedCount = Mathf.Max(1, Mathf.RoundToInt(9 * scale));
                    _heavy.particleCount = Mathf.Max(0, Mathf.RoundToInt(960 * scale));
                    _heavy.baseMaterial = _builder.baseMaterial;
                    _heavy.Build();
                }

                yield return new WaitForSecondsRealtime(settleSeconds);

                _status = $"{label} 측정 중";

                // 빌드 직후 프레임은 버린다 — 메시 생성분이 섞인다
                yield return null;

                var frames = 0;
                var elapsed = 0f;
                while (elapsed < measureSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    frames += 1;
                    yield return null;
                }
                var measured = frames / Mathf.Max(0.0001f, elapsed);

                var report = _builder.Report;
                var heavy = _heavy != null ? _heavy.Report : default;
                var totalTris = report.triangles + heavy.skinnedTriangles;

                _rows.Add(
                    $"{label}\t{report.spawnedObjects + heavy.skinnedRenderers}\t" +
                    $"{totalTris:N0}\t스킨{heavy.skinnedRenderers}\t파티클{heavy.particles}\t{measured:F1}");

                Debug.Log(
                    $"[스윕] {label} · 오브젝트 {report.spawnedObjects + heavy.skinnedRenderers} · " +
                    $"{totalTris:N0} tris · 스킨드 {heavy.skinnedRenderers} · " +
                    $"파티클 {heavy.particles} · 후처리 {(heavy.postProcessing ? "on" : "off")} · " +
                    $"{measured:F1} fps");

                if (measured < floorFps)
                {
                    _status = $"{label} 에서 {floorFps}fps 미달 — 여기가 한계다";
                    Debug.LogWarning($"[스윕] {_status}");
                    yield break;
                }
            }

            _status = $"전 구간 통과 — 목표의 {maxMultiplier}배에서도 {floorFps}fps 이상";
            Debug.Log($"[스윕] {_status}\n{string.Join("\n", _rows)}");
        }

        private void OnGUI()
        {
            var text = new StringBuilder();
            text.AppendLine(M0Mode.Banner);
            text.AppendLine(_status);
            foreach (var row in _rows) text.AppendLine(row);

            GUI.color = Color.white;
            GUI.Box(new Rect(10, 10, 560, 50 + _rows.Count * 20), "");
            GUI.Label(new Rect(20, 16, 540, 44 + _rows.Count * 20), text.ToString());
        }
    }
}
