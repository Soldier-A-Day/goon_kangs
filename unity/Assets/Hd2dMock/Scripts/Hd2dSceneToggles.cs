using UnityEngine;
using UnityEngine.Rendering;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// 원인 분리용 토글 3종 (§ HD-2D 목업 "1:후처리 2:조명 3:내부해상도").
    ///
    /// 기본값은 전부 켜짐 — "다이어트 구성" 그 자체가 기준선이다. 하나씩 꺼서
    /// 그 항목이 프레임을 얼마나 먹는지를 <see cref="Hd2dFpsMeter"/>로 읽는다.
    /// </summary>
    public sealed class Hd2dSceneToggles : MonoBehaviour
    {
        public Volume postVolume;
        public Light[] pointLights = System.Array.Empty<Light>();
        public Hd2dResolutionScaler resolutionScaler;

        [Header("조명 — 켬")]
        public Color ambientMood = new Color(0.18f, 0.19f, 0.24f);
        [Header("조명 — 끔(포인트 라이트 없이 평판 앰비언트만)")]
        public Color ambientFlat = new Color(0.55f, 0.55f, 0.55f);

        public bool PostFxOn { get; private set; } = true;
        public bool LightingOn { get; private set; } = true;
        public bool LowResOn { get; private set; } = true;

        private void Start() => ApplyAll();

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                PostFxOn = !PostFxOn;
                ApplyPostFx();
            }
            if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                LightingOn = !LightingOn;
                ApplyLighting();
            }
            if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                LowResOn = !LowResOn;
                ApplyResolution();
            }
        }

        private void ApplyAll()
        {
            ApplyPostFx();
            ApplyLighting();
            ApplyResolution();
        }

        private void ApplyPostFx()
        {
            if (postVolume != null) postVolume.enabled = PostFxOn;
        }

        private void ApplyLighting()
        {
            foreach (var light in pointLights)
            {
                if (light != null) light.enabled = LightingOn;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = LightingOn ? ambientMood : ambientFlat;
        }

        private void ApplyResolution()
        {
            if (resolutionScaler != null) resolutionScaler.SetEnabled(LowResOn);
        }
    }
}
