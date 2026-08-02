using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 화면 효과 상태 (SAD-ART-001 §9.2 · §4.3 상태 연동).
    ///
    /// 셰이더가 무엇을 그릴지는 여기가 정한다. **판정은 하나도 하지 않는다** —
    /// 서버가 보낸 스냅샷(수분·정신력·보온 게이지·동상 여부)을 강도로 옮길 뿐이고,
    /// 어떤 상태인지는 이미 `packages/sim`이 정해서 보냈다(ARCH-02).
    ///
    /// 강도가 0인 효과는 머티리얼을 **비운다**. 렌더러 피처가 비어 있는 칸을
    /// 건너뛰므로, 평시 낮에는 블릿이 한 번도 일어나지 않는다.
    ///
    /// §15.0 접근성 토글은 `WeatherGrading`이 들고 있다 — 같은 상태를 화면
    /// 두 곳(그레이딩 · 풀스크린)이 그리므로 토글도 한 곳에 있어야 한다.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class ScreenEffects : MonoBehaviour
    {
        public GameClient client;
        public WeatherGrading grading;

        [Tooltip("§9.2 순서대로 — HeatDistort · FrostFrame · MaskFrame · NightVision · VignettePulse · GrayscaleFade")]
        public Material[] materials = new Material[ScreenEffectsFeature.SlotCount];

        /// <summary>§7.10 방독면 착용 — 화생방 훈련에서 켠다</summary>
        public bool maskOn;
        /// <summary>§4.3 야시장비</summary>
        public bool nightVision;

        /// <summary>후송·게임오버 전환. 0~1을 밖에서 밀어 넣는다</summary>
        public float fadeOut;

        private static readonly int Strength = Shader.PropertyToID("_Strength");
        private static readonly int Coverage = Shader.PropertyToID("_Coverage");
        private static readonly int Amount = Shader.PropertyToID("_Amount");

        private float _heatHaze;
        private float _nightVision;
        private float _mask;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
            Clear();
        }

        private void OnDestroy() => Clear();

        private static void Clear()
        {
            for (var i = 0; i < ScreenEffectsFeature.SlotCount; i += 1)
            {
                ScreenEffectsFeature.Materials[i] = null;
            }
        }

        private void Apply(Snapshot snapshot)
        {
            // §9.1 `VFX_HeatHaze` "혹서 이상". 아지랑이는 밴드가 만드는 것이고,
            // 열사병 맥동(§5.0)은 그 위에 따로 얹힌다
            var band = snapshot?.weather?.band;
            _heatHaze = band == SnapshotWeatherBandValues.ExtremeHot ? 1f
                      : band == SnapshotWeatherBandValues.Hot ? 0.55f
                      : 0f;
        }

        private void Update()
        {
            if (grading == null) return;

            // §15.0 왜곡 토글이 꺼져 있으면 아지랑이도 열사병 왜곡도 없다.
            // 멀미를 일으키는 것은 맥동이 아니라 화면이 흔들리는 쪽이다
            var distort = grading.allowHeatPulse
                ? Mathf.Max(_heatHaze, grading.HeatStress)
                : 0f;

            // 야시장비·방독면은 켜고 끄는 것이라 부드럽게 넘긴다 —
            // 즉시 바뀌면 장비를 쓴 것이 아니라 화면이 튄 것으로 보인다
            _nightVision = Mathf.MoveTowards(_nightVision, nightVision ? 1f : 0f, Time.deltaTime * 4f);
            _mask = Mathf.MoveTowards(_mask, maskOn ? 1f : 0f, Time.deltaTime * 5f);

            Set(ScreenEffectsFeature.Slot.HeatDistort, distort, Strength);
            Set(ScreenEffectsFeature.Slot.FrostFrame, grading.FrostBite, Coverage);
            Set(ScreenEffectsFeature.Slot.MaskFrame, _mask, Amount);
            Set(ScreenEffectsFeature.Slot.NightVision, _nightVision, Amount);
            Set(ScreenEffectsFeature.Slot.VignettePulse, grading.HeatStress, Amount);
            Set(ScreenEffectsFeature.Slot.GrayscaleFade, fadeOut, Amount);
        }

        private void Set(ScreenEffectsFeature.Slot slot, float value, int property)
        {
            var index = (int)slot;
            if (index >= materials.Length) return;

            var material = materials[index];
            if (material == null) return;

            if (value <= 0.001f)
            {
                ScreenEffectsFeature.Materials[index] = null;
                return;
            }

            material.SetFloat(property, Mathf.Clamp01(value));
            ScreenEffectsFeature.Materials[index] = material;
        }
    }
}
