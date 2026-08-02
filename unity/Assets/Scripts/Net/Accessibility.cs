using System.Runtime.InteropServices;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 접근성 설정을 웹 셸에서 받아온다 (SAD-ART-001 §15.0 · 목업 §11 7.10-E).
    ///
    /// 설정 화면은 **로비(Next.js)에 있다.** Unity 안에 또 만들지 않는 이유는
    /// 두 가지다 — 게임이 뜨기 전에 고를 수 있어야 하고(멀미 나는 효과를 켜본
    /// 다음에 끄게 하면 이미 늦다), IMGUI로 만든 설정 화면은 브라우저 폼보다
    /// 언제나 나쁘다.
    ///
    /// 웹이 `window.__sadSettings`에 놓고 여기서 읽는다. **WebGL이 아니면
    /// 기본값**이다 — 에디터에서 돌 때 설정이 없다고 게임이 달라지면 안 된다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class Accessibility : MonoBehaviour
    {
        public WeatherGrading grading;

        /// <summary>색각 이상 모드 — 온도 밴드를 아이콘 + 텍스트로 이중 표기</summary>
        public static bool ColorBlind { get; private set; }
        /// <summary>UI 배율 1.0 · 1.25 · 1.5</summary>
        public static float UiScale { get; private set; } = 1f;
        /// <summary>
        /// 화면 흔들림 원본 토글. `grading.allowShake`는 `panicShake`와 AND된
        /// 값이라 결과창 흔들림(A-2) 같은 IMGUI 패널 흔들림에는 못 쓴다 — 이건
        /// 그 결합 전의 값이다. WebGL이 아니면 기본 켜짐.
        /// </summary>
        public static bool ScreenShake { get; private set; } = true;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern string SadReadSettings();
#endif

        private string _last;

        private void Update()
        {
            var json = Read();
            if (json == null || json == _last) return;
            _last = json;
            Apply(json);
        }

        private static string Read()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return SadReadSettings(); }
            catch { return null; }
#else
            return null;
#endif
        }

        [System.Serializable]
        private sealed class Payload
        {
            public bool heatDistort = true;
            public bool frostFrame = true;
            public bool panicShake;
            public bool screenShake = true;
            public bool colorBlind;
            public bool keyboardDelegation = true;
            public int uiScale = 100;
        }

        private void Apply(string json)
        {
            Payload settings;
            try { settings = JsonUtility.FromJson<Payload>(json); }
            catch { return; }
            if (settings == null) return;

            if (grading != null)
            {
                grading.allowHeatPulse = settings.heatDistort;
                grading.allowFrostFrame = settings.frostFrame;
                // 흔들림은 둘 다 켜져야 흔들린다 — "전반"이 개별 토글을 덮는다
                grading.allowShake = settings.screenShake && settings.panicShake;
            }

            ColorBlind = settings.colorBlind;
            UiScale = Mathf.Clamp(settings.uiScale, 100, 150) / 100f;
            ScreenShake = settings.screenShake;

            Debug.Log($"[접근성] 왜곡 {settings.heatDistort} · 결빙 {settings.frostFrame} · " +
                      $"진동 {settings.panicShake} · 색각 {settings.colorBlind} · " +
                      $"배율 {settings.uiScale}%");
        }
    }
}
