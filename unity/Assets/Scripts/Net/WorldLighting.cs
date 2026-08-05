using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.Net
{
    /// <summary>
    /// 실내·실외 로컬 광원의 **런타임 관리**(W3, SAD-ART-001 §9.3).
    ///
    /// 광원을 어디에 놓을지(방 격자·복도 간격·창)는 씬 빌더(`BaseScene.cs`)가
    /// 에디터에서 맵 데이터(`base_map.json`)를 한 번 훑어 정한다 — 타일·소품과
    /// 같은 이유로, 8,800칸짜리 맵을 런타임마다 다시 훑을 이유가 없다.
    ///
    /// 이 컴포넌트가 하는 일은 셋뿐이다.
    ///   1. 화면 밖 광원 끄기
    ///   2. 화면당 활성 광원 상한(`MaxActiveLights`)
    ///   3. 전체 토글 — 꺼지면 기존 unlit 화면과 사실상 같아진다(저사양·접근성 폴백)
    ///
    /// 배치 로직이 여기 없는 이유가 하나 더 있다. Unity 6(URP 17) 기준
    /// `Light2D.normalMapQuality`는 공개 setter가 없고 `SerializedObject`로만
    /// 켤 수 있는데, 그건 `UnityEditor` 어셈블리에만 있다. 이 스크립트는 WebGL
    /// 빌드에 들어가야 하므로 `UnityEditor`를 참조할 수 없다 — 그래서 노멀맵
    /// 품질 설정을 포함한 광원 생성 자체는 `BaseScene.cs`(에디터 전용)가 한다.
    ///
    /// 그림자 캐스터는 이번 발주에 없다(W2가 가짜 그림자를 담당) — 씬 빌더가
    /// 만드는 광원은 전부 `shadowsEnabled = false`이고, 여기서도 건드리지 않는다.
    /// </summary>
    public sealed class WorldLighting : MonoBehaviour
    {
        /// <summary>
        /// 화면당 활성 광원 상한. WebGL에서 Light2D 하나는 조명 패스에 드로우콜을
        /// 더하므로, 실내 구획이 몰린 화면에서 전부 켜두면 여기서 프레임이 죽는다.
        /// </summary>
        public const int MaxActiveLights = 8;

        /// <summary>
        /// 전체 토글. 꺼지면 모든 로컬 광원이 비활성화되어 Global Light 2D 하나만
        /// 남는다 — 그게 이 발주 이전(=unlit) 화면과 사실상 같아지는 경로다.
        /// 씬 빌더가 아니라 여기 있는 이유는, 이 값이 **실행 중** 바뀌는 게
        /// 정상이기 때문이다(저사양·접근성 설정).
        /// </summary>
        public bool lightingEnabled = true;

        /// <summary>씬 빌더가 배치 직후 채운다</summary>
        public Light2D[] lights = System.Array.Empty<Light2D>();

        /// <summary>
        /// `lights`와 같은 길이 · 같은 순서. 실내(방·복도) 등만 참이고, 그 등은
        /// 밤에 밝아진다. 창 광원은 거짓이라 밤낮 상관없이 일정하다.
        /// </summary>
        public bool[] nightBoost = System.Array.Empty<bool>();

        /// <summary>밤 블렌드를 읽는다. 없으면 항상 낮 취급(에디터 단독 재생 등)</summary>
        public WeatherGrading grading;

        /// <summary>화면 판정 기준 카메라. 비워두면 `Camera.main`</summary>
        public Camera worldCamera;

        /// <summary>화면 밖 판정 여유(월드 유닛). 광원 반경만큼 둬야 화면 가장자리에서
        /// 꺼졌다 켜졌다 깜빡이지 않는다</summary>
        private const float CullMargin = 6f;

        private float[] _baseIntensity = System.Array.Empty<float>();
        private Transform[] _transforms = System.Array.Empty<Transform>();

        // 화면당 상한을 고르는 동안 쓰는 고정 버퍼 — 매 프레임 할당하지 않는다.
        // 광원 수가 수십 개 수준이라 정렬 대신 삽입식 상위 K 추적으로 충분하다
        private readonly int[] _topIndex = new int[MaxActiveLights];
        private readonly float[] _topDist = new float[MaxActiveLights];

        private void Awake()
        {
            _baseIntensity = new float[lights.Length];
            _transforms = new Transform[lights.Length];
            for (var i = 0; i < lights.Length; i += 1)
            {
                if (lights[i] == null) continue;
                // **여기서 한 번만 읽는다.** 씬 빌더가 준 값이 "밤 100%"에 해당하는
                // 기준값이고, 매 프레임 `intensity`를 되읽으면 아래서 우리가 쓴
                // 보정값을 기준값으로 착각해 값이 점점 눌린다
                _baseIntensity[i] = lights[i].intensity;
                _transforms[i] = lights[i].transform;
            }
        }

        private void LateUpdate()
        {
            if (lights.Length == 0) return;

            // 전체 토글 — 이게 기존 unlit 화면으로 돌아가는 유일한 경로다.
            // 꺼진 Light2D는 2D 렌더러 조명 패스에서 완전히 빠진다
            if (!lightingEnabled)
            {
                foreach (var light in lights) if (light != null) light.enabled = false;
                return;
            }

            var night = grading != null ? grading.NightAmount : 0f;
            var cam = worldCamera != null ? worldCamera : Camera.main;

            var hasView = cam != null;
            var camPos = Vector3.zero;
            float halfW = 0f, halfH = 0f;
            if (hasView)
            {
                camPos = cam.transform.position;
                halfH = cam.orthographicSize + CullMargin;
                halfW = cam.orthographicSize * Mathf.Max(0.1f, cam.aspect) + CullMargin;
            }

            var topCount = 0;

            for (var i = 0; i < lights.Length; i += 1)
            {
                var light = lights[i];
                if (light == null) continue;

                var pos = _transforms[i].position;
                var onScreen = !hasView
                    || (Mathf.Abs(pos.x - camPos.x) <= halfW && Mathf.Abs(pos.y - camPos.y) <= halfH);

                if (!onScreen)
                {
                    light.enabled = false;
                    continue;
                }

                // 우선은 전부 끈다 — 아래서 상위 `MaxActiveLights`개만 다시 켠다
                light.enabled = false;

                var dist = hasView ? (pos - camPos).sqrMagnitude : 0f;

                if (topCount < MaxActiveLights)
                {
                    var insertAt = topCount;
                    while (insertAt > 0 && _topDist[insertAt - 1] > dist)
                    {
                        _topDist[insertAt] = _topDist[insertAt - 1];
                        _topIndex[insertAt] = _topIndex[insertAt - 1];
                        insertAt -= 1;
                    }
                    _topDist[insertAt] = dist;
                    _topIndex[insertAt] = i;
                    topCount += 1;
                }
                else if (dist < _topDist[MaxActiveLights - 1])
                {
                    var insertAt = MaxActiveLights - 1;
                    while (insertAt > 0 && _topDist[insertAt - 1] > dist)
                    {
                        _topDist[insertAt] = _topDist[insertAt - 1];
                        _topIndex[insertAt] = _topIndex[insertAt - 1];
                        insertAt -= 1;
                    }
                    _topDist[insertAt] = dist;
                    _topIndex[insertAt] = i;
                }
            }

            for (var k = 0; k < topCount; k += 1)
            {
                var i = _topIndex[k];
                var light = lights[i];
                light.enabled = true;

                var boost = i < nightBoost.Length && nightBoost[i];
                // 실내등은 낮 55% ~ 밤 100% — 밤에 방 안이 검게 죽지 않고, 낮에도
                // 등 자체가 안 보이게 사라지지는 않는다(§4.3 Global이 낮을 맡는다)
                light.intensity = boost
                    ? Mathf.Lerp(_baseIntensity[i] * 0.55f, _baseIntensity[i], night)
                    : _baseIntensity[i];
            }
        }
    }
}
