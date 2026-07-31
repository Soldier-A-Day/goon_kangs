using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace SoldierADay.M0
{
    /// <summary>
    /// 100분 힙 측정기 (docs/M0_SCENE.md §5).
    ///
    /// 기획서 표 18-2의 "100분 세션 힙 안정성 · 누수 0 · **완화 불가**"를 재는 도구다.
    /// 세션 중 브라우저 크래시 1회는 분대 4명 전원의 런을 날리므로 타협 항목이 아니다.
    ///
    /// 핵심은 **저점**이다. 힙은 GC 주기 때문에 톱니 모양으로 오르내리는데, 꼭짓점은
    /// 언제 GC가 돌았느냐에 따라 흔들린다. 누수는 톱니의 **바닥이 우상향할 때** 드러난다.
    /// 그래서 구간마다 최저값만 남긴다.
    /// </summary>
    public sealed class HeapProbe : MonoBehaviour
    {
        [Tooltip("표본 간격(초). 10분 간격 보고를 위해 기본 10초.")]
        public float sampleInterval = 10f;

        [Tooltip("이 구간마다 저점을 확정해 기록한다(초). 기본 10분.")]
        public float bucketSeconds = 600f;

        [Tooltip("화면에 현재 수치를 띄운다.")]
        public bool showOverlay = true;

        private readonly List<Bucket> _buckets = new List<Bucket>();
        private float _nextSample;
        private float _bucketStart;
        private long _bucketFloor = long.MaxValue;

        // 프레임은 평균이 아니라 최저가 중요하다 — 30fps 미달이 곧 조작 불가다
        private float _worstFrameMs;
        private float _frameAccum;
        private int _frameCount;

        // 화면 표시용 짧은 창. 구간 누적 평균은 초기 로딩 프레임에 오염돼
        // 실시간 판독에 쓸 수 없다.
        private float _recentAccum;
        private int _recentCount;
        private float _recentFps;

        public IReadOnlyList<Bucket> Buckets => _buckets;

        // 오버레이 문자열은 캐시한다. OnGUI는 프레임당 여러 번(Layout·Repaint) 불리는데,
        // 거기서 표를 새로 조립하면 **계측기가 자기가 재는 힙에 할당을 쏟는다.**
        // 1차 100분 측정에서 이걸로 평균 프레임이 25ms까지 밀렸다 — 같은 부하를
        // 스윕에서 쟀을 때는 16.9ms였다. 재는 행위가 결과를 바꾸면 측정이 아니다.
        private string _overlayText = "";
        private float _overlayNextAt;
        private long _lastSampledHeap;
        private SnapshotChurn _churn;

        // 창이 가려지면 Chrome이 rAF를 **멈춘다.** 1차 측정에서 최악 프레임
        // 531,569ms(8.9분)가 찍혔다 — 그건 프레임이 아니라 브라우저가 쉰 시간이다.
        // 한 번만 섞여도 그 구간의 최악·평균이 통째로 무의미해지므로, 오염된 구간은
        // 프레임 지표를 버린다. **힙은 버리지 않는다** — 할당은 시간이 아니라
        // 파싱 횟수를 따라가고, 멈춘 동안에는 파싱도 멈췄기 때문이다.
        private bool _bucketWasHidden;
        private bool _focused = true;

        private void OnApplicationFocus(bool focused)
        {
            _focused = focused;
            if (!focused) _bucketWasHidden = true;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) _bucketWasHidden = true;
        }

        private void Awake()
        {
            // OnGUI 안의 GetComponent도 매 이벤트 비용이다
            _churn = GetComponent<SnapshotChurn>();
        }

        private void Update()
        {
            var frameMs = Time.unscaledDeltaTime * 1000f;

            // 복귀 직후 첫 프레임은 쉰 시간을 통째로 물고 온다. 포커스 이벤트가
            // 프레임보다 먼저 오지 않는 경우도 있어 값으로도 한 번 거른다 —
            // 1초 넘는 프레임은 이 씬에서 나올 수 없다(최악 부하가 8배에서 17ms다).
            if (frameMs > 1000f) _bucketWasHidden = true;

            _frameAccum += frameMs;
            _frameCount += 1;
            if (frameMs > _worstFrameMs) _worstFrameMs = frameMs;

            _recentAccum += frameMs;
            _recentCount += 1;
            if (_recentAccum >= 500f)
            {
                _recentFps = 1000f / (_recentAccum / _recentCount);
                _recentAccum = 0f;
                _recentCount = 0;
            }

            if (showOverlay) RefreshOverlay();

            if (Time.unscaledTime < _nextSample) return;
            _nextSample = Time.unscaledTime + sampleInterval;

            // 모노 힙이 실제로 쓰는 양. 예약량(Reserved)은 반납되지 않아 누수를 가린다.
            var used = Profiler.GetMonoUsedSizeLong();
            _lastSampledHeap = used;
            if (used < _bucketFloor) _bucketFloor = used;

            if (Time.unscaledTime - _bucketStart < bucketSeconds) return;
            CloseBucket();
        }

        private void CloseBucket()
        {
            var bucket = new Bucket
            {
                minute = Mathf.RoundToInt(_bucketStart / 60f),
                heapFloorBytes = _bucketFloor,
                worstFrameMs = _worstFrameMs,
                averageFrameMs = _frameCount > 0 ? _frameAccum / _frameCount : 0f,
                framesUsable = !_bucketWasHidden,
            };
            _buckets.Add(bucket);

            Debug.Log(
                $"[M0] {bucket.minute}분 · 힙 저점 {bucket.heapFloorBytes / (1024f * 1024f):F1}MB · " +
                (bucket.framesUsable
                    ? $"최악 프레임 {bucket.worstFrameMs:F1}ms · 평균 {bucket.averageFrameMs:F1}ms"
                    : "프레임 무효 (창이 가려짐)"));

            _bucketStart = Time.unscaledTime;
            _bucketFloor = long.MaxValue;
            _worstFrameMs = 0f;
            _frameAccum = 0f;
            _frameCount = 0;
            _bucketWasHidden = !_focused;
        }

        /// <summary>
        /// 저점이 우상향하면 누수다. 첫 구간과 마지막 구간의 차이로 판정한다.
        /// 구간이 3개 미만이면 아직 판정할 수 없다 — 초기 로딩분이 섞이기 때문이다.
        /// </summary>
        public string Verdict()
        {
            if (_buckets.Count < 3) return "표본 부족 — 최소 30분";

            var first = _buckets[1].heapFloorBytes; // 0번은 로딩분이 섞여 버린다
            var last = _buckets[_buckets.Count - 1].heapFloorBytes;
            var growthMb = (last - first) / (1024f * 1024f);

            return growthMb > 5f
                ? $"누수 의심 — 저점이 {growthMb:F1}MB 상승"
                : $"누수 없음 — 저점 변화 {growthMb:+0.0;-0.0}MB";
        }

        public string Report()
        {
            var builder = new StringBuilder();
            builder.AppendLine("분\t힙저점(MB)\t최악프레임(ms)\t평균(ms)");
            foreach (var bucket in _buckets)
            {
                builder.AppendLine(
                    $"{bucket.minute}\t{bucket.heapFloorBytes / (1024f * 1024f):F1}\t" +
                    (bucket.framesUsable
                        ? $"{bucket.worstFrameMs:F1}\t{bucket.averageFrameMs:F1}"
                        : "무효\t무효"));
            }
            builder.AppendLine(Verdict());
            builder.AppendLine(FrameVerdict());
            return builder.ToString();
        }

        /// <summary>
        /// 프레임 판정. 표 18-2의 "최저 30fps"는 평균이 아니라 **최악**으로 봐야 한다 —
        /// 평균 60에 순간 200ms가 섞이면 그 순간 조작이 불가능하고, 집합 판정처럼
        /// 몇 초가 걸리는 상황에서는 그게 곧 실패다.
        /// </summary>
        private string FrameVerdict()
        {
            var usable = 0;
            var worst = 0f;
            // 0번 구간은 씬 생성(549 오브젝트 + 본 리그)이 통째로 들어가 2초짜리
            // 프레임이 찍힌다. 그건 로딩 비용이지 플레이 중 프레임이 아니다.
            for (var i = 1; i < _buckets.Count; i += 1)
            {
                if (!_buckets[i].framesUsable) continue;
                usable += 1;
                if (_buckets[i].worstFrameMs > worst) worst = _buckets[i].worstFrameMs;
            }

            if (usable == 0) return "프레임 판정 불가 — 유효 구간 없음 (창을 계속 띄워둬야 한다)";

            return worst > 33.3f
                ? $"프레임 미달 — 최악 {worst:F0}ms (유효 구간 {usable})"
                : $"프레임 통과 — 최악 {worst:F0}ms (유효 구간 {usable})";
        }

        /// <summary>
        /// 한 구간의 측정 결과. 저점만 남기는 이유는 힙이 GC 주기 때문에 톱니로 흔들려서,
        /// 꼭짓점은 GC 타이밍에 좌우되고 누수는 바닥이 우상향할 때만 드러나기 때문이다.
        /// </summary>
        public struct Bucket
        {
            /// <summary>구간 시작 시점 (분)</summary>
            public int minute;

            /// <summary>이 구간에서 관측된 Mono 힙 최저값</summary>
            public long heapFloorBytes;

            /// <summary>최악 프레임(ms). 30fps = 33.3ms 를 넘으면 조작이 불가능해진다</summary>
            public float worstFrameMs;

            public float averageFrameMs;

            /// <summary>
            /// 창이 가려진 적 없는 구간인가. 가려졌으면 프레임 값은 버린다 —
            /// 브라우저가 쉰 시간이 한 프레임으로 기록되기 때문이다. 힙은 유효하다.
            /// </summary>
            public bool framesUsable;
        }

        /// <summary>
        /// 오버레이 문자열을 초당 두 번만 다시 만든다. 100분은 사람이 지켜보는
        /// 시간이라 표가 실시간으로 차야 하지만, 사람 눈에 0.5초면 충분하다.
        /// </summary>
        private void RefreshOverlay()
        {
            if (Time.unscaledTime < _overlayNextAt) return;
            _overlayNextAt = Time.unscaledTime + 0.5f;

            var text = new StringBuilder();
            text.AppendLine(M0Mode.Banner);
            text.AppendLine(
                $"{Time.unscaledTime / 60f:F1}분 / 100분 · {_recentFps:F0} fps · " +
                $"힙 {_lastSampledHeap / (1024f * 1024f):F1}MB");

            if (_churn != null && _churn.enabled)
            {
                text.AppendLine($"스냅샷 파싱 {_churn.ParsedCount:N0}회");
            }

            text.AppendLine(Verdict());
            text.AppendLine();
            text.Append(Report());
            _overlayText = text.ToString();
        }

        private void OnGUI()
        {
            if (!showOverlay) return;

            // Layout 이벤트에서는 그릴 게 없다. 명시 Rect를 쓰므로 레이아웃 계산이
            // 필요 없고, 이벤트마다 그리면 같은 일을 프레임당 두 번 이상 한다.
            if (Event.current.type != EventType.Repaint) return;

            // 30fps 미달은 완화 불가 항목이라 눈에 띄어야 한다
            GUI.color = _recentFps < 30f ? Color.red : _recentFps < 60f ? Color.yellow : Color.green;
            GUI.Box(new Rect(10, 10, 560, 140 + _buckets.Count * 18), "");
            GUI.Label(new Rect(20, 16, 540, 130 + _buckets.Count * 18), _overlayText);
            GUI.color = Color.white;
        }
    }
}
