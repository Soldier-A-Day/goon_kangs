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

        /// <summary>
        /// C2 — 실내 판정을 읽기 위한 참조. `ZoneWorld`가 이미 구역 종류(방·복도·실외)를
        /// 판정해 `HereIndoor`로 들고 있으므로 여기서 새로 만들지 않는다. 씬 빌더가
        /// 배선하지 않아도 되도록(`BaseScene.cs`는 이번 작업 범위 밖) `Awake`에서
        /// 부모 쪽에서 찾아 채운다 — "그레이딩"과 `ZoneWorld`(루트 "부대")가 항상
        /// 같은 계층에 있다는 씬 빌더의 기존 구조에 기댄다.
        /// </summary>
        public ZoneWorld zoneWorld;

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
        /// 0(낮)~1(야간) 블렌드. C2 이전에는 `WorldLighting`이 이 값만 보고 실내등을
        /// 밤에만 밝혔다 — 지금은 `DarknessAmount`가 그 자리를 대신한다(아래).
        /// 이 값 자체는 "순수 밤" 신호로 남겨 둔다.
        /// </summary>
        public float NightAmount => _night;

        /// <summary>
        /// C2 — 실내(방·복도)에 있을 때 앰비언트를 얼마나 죽일지. `_night`와 같은
        /// 좌표계(0~1, "night 100%일 때의 어두움"을 1로 잡은 값)를 쓴다.
        ///
        /// **0이면 이 기능이 완전히 꺼진다** — `DarknessAmount`가 `_night` 그대로가
        /// 되어 C2 이전과 동일해진다(롤백 안전장치).
        /// </summary>
        /// 색 이동을 밤에만 주도록 바꾼 뒤(아래 `Update` 참고) 0.55는 체감이 약했다 —
        /// 실측(보일러실 캡처, 실내 명암 폭): 0.55 → 104.7 / 0.75 → 111.0 / 0.9 → 115.3.
        /// 평균 밝기보다 **명암 폭**이 중요하다. 등이 만든 밝은 자리는 그대로 두고
        /// 구석만 떨어져야 "등이 켜져 있다"로 읽히기 때문이다. 0.8이 그 절충이다.
        private const float IndoorDarkAmount = 0.8f;

        /// <summary>
        /// C2 — 실내 판정이 문턱을 넘을 때 앰비언트가 튀지 않게 거는 시간(초).
        /// `WorldLighting.FadeSeconds`와 같은 방식(`Time.deltaTime` 기반 `MoveTowards`)
        /// 이고, 같은 이유로 상수로 뺐다 — 문을 지나는 순간 화면이 훅 어두워지거나
        /// 밝아지면 조명이 아니라 버그로 읽힌다.
        /// </summary>
        private const float IndoorFadeSeconds = 0.5f;

        private float _indoor;        // 지금 실제로 적용 중인 실내 블렌드(0~1)
        private float _indoorTarget;  // 이번 프레임 목표(문 안쪽이면 1, 아니면 0)

        /// <summary>
        /// C2 — "지금 얼마나 어두워야 하는가"를 밤·실내 두 원인 중 **더 어두운 쪽
        /// 하나**로 합친 값. `WorldLighting`은 이제 `NightAmount` 대신 이 값을 읽어서
        /// 실내등 잠재값을 정한다 — 어두움의 원인이 밤이든 지붕이든 등은 같은 방식
        /// 으로 켜져야 한다는 발주 요구를 그대로 옮긴 것이다.
        ///
        /// **곱하지 않고 `Max`를 쓴다.** 밤에 실내에 들어가면 두 원인이 겹치는데,
        /// 곱하면(예: 두 값을 각각 어두운 쪽으로 블렌드한 뒤 다시 블렌드) 밤 실내가
        /// 낮 실내보다 훨씬 더 어두워져 "밤 실내가 검게 죽지 않게"라는 요구를
        /// 어긴다. `Max`는 밤 실내를 "그냥 밤만큼만" 어둡게 만들어 이 문제가
        /// 애초에 생기지 않는다.
        /// </summary>
        public float DarknessAmount => Mathf.Max(_night, _indoor * IndoorDarkAmount);

        private void Awake()
        {
            _weights[2] = 1f;
            if (zoneWorld == null) zoneWorld = GetComponentInParent<ZoneWorld>();
        }

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

            // C2 — 실내 판정. 새로 계산하지 않는다 — `ZoneWorld.HereIndoor`가 이미
            // 구역 종류(room·corridor=실내, outdoor=실외)로 판정해 둔 것을 그대로 쓴다.
            // `IndoorDarkAmount`가 0이면(기능 off) 목표는 항상 0이고, 아래 `DarknessAmount`는
            // `_night` 그대로가 되어 이 기능이 있기 전과 같아진다.
            _indoorTarget = IndoorDarkAmount > 0f && zoneWorld != null && zoneWorld.HereIndoor ? 1f : 0f;
            _indoor = Mathf.MoveTowards(_indoor, _indoorTarget, Time.deltaTime / IndoorFadeSeconds);

            if (globalLight == null) return;

            var (color, intensity) = Lights[Mathf.Clamp(_target, 0, Lights.Length - 1)];
            // **색은 밤에만 밀고, 밝기는 어두움 전체를 따른다.**
            // 실내가 어두운 것은 지붕이 햇빛을 가려서지 해가 진 게 아니다 — 색까지
            // `NightLight`(청색조)로 밀면 대낮 실내가 푸르게 떠서 "밤인가?"로 읽힌다.
            // 그래서 색 Lerp는 `_night`만, 밝기 Lerp는 `DarknessAmount`를 쓴다.
            var darkness = DarknessAmount;
            globalLight.color = Color.Lerp(color, NightLight.color, _night);
            globalLight.intensity = Mathf.Lerp(intensity, NightLight.intensity, darkness);
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
