using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.Net
{
    /// <summary>
    /// 온도 밴드 그레이딩 (SAD-ART-001 §4.3).
    ///
    /// 이 게임에서 **색이 바뀌는 순간은 오직 온도 밴드가 바뀔 때**다(§3.1).
    /// 그래서 여기가 "기온이 바뀐 것을 말 안 해줘도 알아채나"(§13.2 M2 검증 질문)에
    /// 답하는 자리다.
    ///
    /// 밴드마다 Volume Profile 하나를 두고 weight로 섞는다. 전환은 **3초 Lerp**이며
    /// (§4.3), 즉시 바꾸면 밴드가 바뀐 것이 아니라 화면이 깜빡인 것으로 읽힌다.
    ///
    /// 야간은 밴드와 **중첩**된다(§4.3 표에서 야간만 "(중첩)"으로 적혀 있다) —
    /// 극혹한 야간은 극혹한 위에 야간이 한 겹 더 얹힌 것이지 별개 밴드가 아니다.
    /// </summary>
    public sealed class WeatherGrading : MonoBehaviour
    {
        public GameClient client;
        public Light2D globalLight;

        /// <summary>씬 빌더가 밴드 순서대로 채운다. 마지막 하나가 야간(중첩)이다</summary>
        public Volume[] bands = System.Array.Empty<Volume>();
        public Volume nightVolume;
        /// <summary>§4.3 상태 연동 오버라이드 — 탈수·열사병이 채도를 더 끌어내린다</summary>
        public Volume stateVolume;

        /// <summary>§15.0 접근성 — 흔들림·왜곡은 **개별 토글**이어야 한다</summary>
        public bool allowShake = true;
        public bool allowFrostFrame = true;
        public bool allowHeatPulse = true;

        /// <summary>§4.3 표 순서. 인덱스가 곧 `bands` 배열의 자리다</summary>
        public static readonly string[] BandOrder =
        {
            SnapshotWeatherBandValues.ExtremeCold,
            SnapshotWeatherBandValues.Cold,
            SnapshotWeatherBandValues.Normal,
            SnapshotWeatherBandValues.Warm,
            SnapshotWeatherBandValues.Hot,
            SnapshotWeatherBandValues.ExtremeHot,
        };

        /// <summary>§4.3 Global Light 2D 색·강도</summary>
        private static readonly (Color color, float intensity)[] Lights =
        {
            (HudTheme.Hex("B9CEE0"), 0.85f),
            (HudTheme.Hex("CFDDE8"), 0.95f),
            (HudTheme.Hex("FFF6E4"), 1.00f),
            (HudTheme.Hex("FFEBC4"), 1.00f),
            (HudTheme.Hex("FFDCA0"), 1.05f),
            (HudTheme.Hex("FFCC85"), 1.10f),
        };

        private static readonly (Color color, float intensity) NightLight =
            (HudTheme.Hex("6E82B4"), 0.35f);

        private int _target = 2;      // 평시에서 시작한다
        private readonly float[] _weights = new float[6];
        private float _night;
        private float _state;

        /// <summary>
        /// §5.0 열사병 2단계 · 동상 — HUD가 프레임 오버레이를 그릴지 정한다.
        ///
        /// 판정은 전부 서버에 있다. 여기 있는 것은 **그 판정을 얼마나 진하게 그릴지**뿐이다.
        /// </summary>
        public float HeatStress { get; private set; }
        public float FrostBite { get; private set; }
        public float Panic { get; private set; }

        /// <summary>
        /// 0(낮)~1(야간) 블렌드 — `WorldLighting`(W3)이 읽어서 실내등을 밤에
        /// 밝힌다. `_night`는 이미 있던 값이고 여기서는 읽기 전용으로만 연다.
        /// </summary>
        public float NightAmount => _night;

        private void Awake() => _weights[2] = 1f;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
        }

        private void Apply(Snapshot snapshot)
        {
            if (snapshot?.weather == null) return;

            var index = System.Array.IndexOf(BandOrder, snapshot.weather.band);
            if (index >= 0) _target = index;

            // 야간은 점호 시간대에 확실하고, 석식·개인정비부터 어두워진다
            var phase = snapshot.phase?.id;
            var night = phase == SnapshotPhaseIdValues.Rollcall ? 1f
                      : phase == SnapshotPhaseIdValues.Personal ? 0.5f : 0f;
            _nightTarget = night;

            var me = HudScreens.FindMember(snapshot, client.MemberId);
            if (me?.stats == null) return;

            // §4.3 상태 연동 오버라이드
            //   탈수 1단계 (수분 ≤30) — 채도 −60 추가
            //   열사병 2단계 (수분 ≤10) — 맥동 + 60초 카운트다운
            var hydration = (float)me.stats.hydration;
            _stateTarget = hydration <= 30f ? Mathf.InverseLerp(30f, 0f, hydration) : 0f;
            HeatStress = allowHeatPulse && hydration <= 10f
                ? Mathf.InverseLerp(10f, 0f, hydration) : 0f;

            // 동상은 **서버 판정**이다(`warmth.ts`). 붙으면 이동 −30% · 작업 −20%가
            // 실제로 걸리므로, 화면이 그것과 다른 말을 하면 안 된다.
            // 게이지가 줄어드는 동안 프레임이 서서히 차오르고 동상에서 가득 찬다
            var warmth = (float)me.warmthRemainingMs / 1000f;
            FrostBite = !allowFrostFrame ? 0f
                : me.frostbitten ? 1f
                : warmth > 0f ? Mathf.InverseLerp(45f, 0f, warmth)
                : 0f;

            // 패닉 — 정신력 0. §4.3이 화면 가장자리 ±1px 3Hz 진동을 요구한다
            Panic = allowShake ? Mathf.InverseLerp(20f, 0f, (float)me.stats.mental) : 0f;
        }

        private float _nightTarget;
        private float _stateTarget;

        private void Update()
        {
            // §4.3 "밴드 전환 시 3초 Lerp"
            var k = Time.deltaTime / 3f;

            for (var i = 0; i < _weights.Length; i += 1)
            {
                _weights[i] = Mathf.MoveTowards(_weights[i], i == _target ? 1f : 0f, k);
                if (i < bands.Length && bands[i] != null) bands[i].weight = _weights[i];
            }

            _night = Mathf.MoveTowards(_night, _nightTarget, k);
            if (nightVolume != null) nightVolume.weight = _night;

            // 상태 오버라이드는 §4.3이 "3초에 걸쳐"라고 못박은 탈수 기준을 따른다
            _state = Mathf.MoveTowards(_state, _stateTarget, k);
            if (stateVolume != null) stateVolume.weight = _state;

            if (globalLight == null) return;

            var (color, intensity) = Lights[Mathf.Clamp(_target, 0, Lights.Length - 1)];
            globalLight.color = Color.Lerp(color, NightLight.color, _night);
            globalLight.intensity = Mathf.Lerp(intensity, NightLight.intensity, _night);
        }

        /// <summary>
        /// §4.3 패닉 — 화면 가장자리 미세 진동 (±1px, 3Hz).
        ///
        /// 카메라를 흔든다. 픽셀 퍼펙트에서 1px은 월드 1/32유닛이며, 그보다 작게
        /// 흔들면 Pixel Perfect Camera가 스냅해버려 아무 일도 일어나지 않는다.
        /// </summary>
        public Vector2 ShakeOffset()
        {
            if (Panic <= 0f) return Vector2.zero;
            var t = Time.unscaledTime * 3f;
            return new Vector2(Mathf.Sin(t * Mathf.PI * 2f), Mathf.Cos(t * Mathf.PI * 2.6f))
                   * (Panic / CameraRig.PPU);
        }
    }

    /// <summary>
    /// §4.3 표의 밴드 수치. 에디터가 Volume Profile을 만들 때와 런타임이 같은 표를 본다.
    /// </summary>
    public static class BandProfiles
    {
        public readonly struct Grading
        {
            public readonly string filter;
            public readonly float saturation;
            public readonly float contrast;
            public readonly float exposure;
            public readonly float vignette;
            public readonly string vignetteColor;

            public Grading(string filter, float saturation, float contrast,
                           float exposure, float vignette, string vignetteColor = "000000")
            {
                this.filter = filter;
                this.saturation = saturation;
                this.contrast = contrast;
                this.exposure = exposure;
                this.vignette = vignette;
                this.vignetteColor = vignetteColor;
            }
        }

        /// <summary>§4.3 표 그대로. 순서는 `WeatherGrading.BandOrder`와 같다</summary>
        public static readonly Grading[] Bands =
        {
            new("A8C4DC", -35f, +10f, -0.20f, 0.35f, "2E4C68"),  // 극혹한
            new("C6D6E2", -20f, +5f, -0.10f, 0.20f),             // 한랭
            new("FFFFFF", 0f, 0f, 0f, 0.10f),                    // 평시
            new("FFE9C0", +8f, 0f, +0.10f, 0.10f),               // 온난
            new("FFD79A", +18f, +8f, +0.25f, 0.15f, "7A3A18"),   // 혹서
            new("FFC178", +25f, +12f, +0.40f, 0.25f),            // 극혹서
        };

        /// <summary>§4.3 야간 (중첩)</summary>
        public static readonly Grading Night = new("7E93C4", -40f, +5f, -0.90f, 0.30f);

        /// <summary>§4.3 탈수 1단계 — 채도 −60 추가. 스태미나 UI도 함께 회색이 된다</summary>
        public static readonly Grading Dehydration = new("FFFFFF", -60f, 0f, 0f, 0.20f, "7A3A18");

        public static readonly string[] Names =
        {
            "ExtremeCold", "Cold", "Normal", "Warm", "Hot", "ExtremeHot",
        };
    }
}
