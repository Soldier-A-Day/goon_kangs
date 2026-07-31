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
        private void Awake()
        {
            var url = Application.absoluteURL ?? "";
            var heapMode = url.Contains("mode=heap");

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
            if (heap != null) heap.showOverlay = true;

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

            Debug.Log("[M0] 힙 모드 — 목표 부하 고정 · 10Hz 스냅샷 파싱");
        }
    }
}
