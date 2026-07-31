using System.Runtime.InteropServices;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// 한 빌드로 두 측정을 돌리기 위한 모드 스위치.
    ///
    /// 스윕(§6.5)과 100분 힙(§5)은 요구하는 씬 상태가 반대다. 스윕은 부하를 계속
    /// 바꿔야 하고, 힙은 **부하가 고정돼야** 한다 — 부하가 움직이면 힙이 오르내리는 게
    /// 누수 때문인지 오브젝트가 늘어서인지 구분되지 않는다.
    ///
    /// 빌드가 5분 걸리므로 모드마다 빌드를 나누면 측정 한 번에 왕복이 배로 든다.
    /// URL 쿼리로 가른다 — `?mode=heap` 이면 목표 부하를 한 번 세우고 그대로 둔다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class M0Mode : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern System.IntPtr M0GetQuery();
#endif

        /// <summary>
        /// 브라우저에게 쿼리 문자열을 직접 묻는다.
        ///
        /// 처음에는 `Application.absoluteURL`을 썼는데 `?mode=heap`이 잡히지 않아
        /// 힙 모드로 열어도 스윕이 돌았다. 모드 전환이 안 되면 측정 자체를 못 하므로
        /// 이 경로는 확실해야 한다.
        /// </summary>
        private static string ReadQuery()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            var pointer = M0GetQuery();
            return pointer == System.IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(pointer) ?? "";
#else
            return Application.absoluteURL ?? "";
#endif
        }

        /// <summary>
        /// 빌드 시각과 읽어낸 쿼리. **모든 오버레이의 첫 줄에 뜬다.**
        ///
        /// 고치고 빌드했는데 화면이 그대로인 상황을 겪었다. 그때 필요한 건 측정값이
        /// 아니라 "지금 도는 게 방금 빌드한 것인가"라는 답이다. 콘솔을 뒤져서 알아야
        /// 하면 늦는다 — 표 옆에 항상 떠 있어야 한다.
        /// </summary>
        public static string Banner { get; private set; } = "";

        private void Awake()
        {
            var url = ReadQuery();
            var heapMode = url.Contains("mode=heap");

            var stampAsset = Resources.Load<TextAsset>("m0_build");
            var stamp = stampAsset != null ? stampAsset.text.Trim() : "미상";
            Banner = $"빌드 {stamp} · {(heapMode ? "힙" : "스윕")} 모드 · 쿼리 \"{url}\"";

            Debug.Log($"[M0] {Banner}");

            var sweep = GetComponent<AutoSweep>();
            var heap = GetComponent<HeapProbe>();
            var churn = GetComponent<SnapshotChurn>();
            var builder = GetComponent<LoadBuilder>();
            var heavy = GetComponent<HeavyAxes>();

            if (!heapMode)
            {
                // 기본은 스윕. 스냅샷 파싱은 프레임 측정에 잡음이 되므로 끈다.
                if (churn != null) churn.enabled = false;
                return;
            }

            if (sweep != null) sweep.enabled = false;

            // §5의 유효한 단축: 누수는 시간이 아니라 **반복 횟수**에 비례하므로
            // 10Hz × 100분(6만 회)을 100Hz × 10분으로 갈음할 수 있다.
            // 시간을 그냥 줄이는 건 반대로 무효다 — 반복이 줄면 누수도 같이 줄어 안 보인다.
            var hz = ReadNumber(url, "hz=", 10f);
            if (churn != null) churn.hz = hz;

            if (heap != null)
            {
                heap.showOverlay = true;
                // 6만 회를 10구간으로 나눠 본다. 100Hz면 구간이 1분이다.
                heap.bucketSeconds = ReadNumber(url, "bucket=", 600f);
            }

            // 목표 부하를 한 번 세우고 100분 내내 그대로 둔다
            if (builder != null)
            {
                builder.buildOnStart = false;
                builder.Build();
                if (heavy != null)
                {
                    heavy.baseMaterial = builder.baseMaterial;
                    heavy.Build();
                }
            }

            Debug.Log(
                $"[M0] 힙 모드 — 목표 부하 고정 · {hz}Hz 스냅샷 파싱 · " +
                $"구간 {(heap != null ? heap.bucketSeconds : 600f) / 60f:F0}분");
        }

        /// <summary>
        /// URL 쿼리에서 숫자 하나를 읽는다. WebGL에는 커맨드라인 인자가 없어
        /// 실행 중 파라미터를 넣을 통로가 URL뿐이다.
        /// </summary>
        private static float ReadNumber(string url, string key, float fallback)
        {
            var at = url.IndexOf(key, System.StringComparison.Ordinal);
            if (at < 0) return fallback;

            var start = at + key.Length;
            var end = start;
            while (end < url.Length && (char.IsDigit(url[end]) || url[end] == '.')) end += 1;

            return float.TryParse(
                url.Substring(start, end - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) && value > 0f
                ? value
                : fallback;
        }
    }
}
