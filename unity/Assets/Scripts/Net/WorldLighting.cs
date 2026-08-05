using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.Net
{
    /// <summary>
    /// 실내·실외 로컬 광원의 **런타임 관리**(W3, SAD-ART-001 §9.3 + WC 발주).
    ///
    /// 광원을 어디에 놓을지(방 격자·복도 간격·창)는 씬 빌더(`BaseScene.cs`)가
    /// 에디터에서 맵 데이터(`base_map.json`)를 한 번 훑어 정한다 — 타일·소품과
    /// 같은 이유로, 8,800칸짜리 맵을 런타임마다 다시 훑을 이유가 없다.
    ///
    /// 이 컴포넌트가 하는 일은 넷이다.
    ///   1. 화면 밖 광원 끄기
    ///   2. 화면당 활성 광원 상한(`MaxActiveLights`)
    ///   3. 밤 연동 — 실내(방·복도) 등은 밝은 낮에는 완전히 꺼져 있고, 어두워질수록
    ///      (`WeatherGrading.NightAmount`) 서서히 밝아진다
    ///   4. 위 세 판정이 만드는 **모든** on/off·세기 변화를 시간에 걸쳐 페이드한다 —
    ///      `Light2D.enabled`를 즉시 토글하지 않는다(WC 발주 "지금 그냥 on/off야" 지적)
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
        /// 정상이기 때문이다(저사양·접근성 설정). 이 토글은 즉시 끈다 — 저사양
        /// 폴백·롤백 안전장치는 반응 속도가 페이드보다 우선한다.
        /// </summary>
        public bool lightingEnabled = true;

        /// <summary>씬 빌더가 배치 직후 채운다</summary>
        public Light2D[] lights = System.Array.Empty<Light2D>();

        /// <summary>
        /// `lights`와 같은 길이 · 같은 순서. 실내(방·복도) 등만 참이고, 그 등은
        /// 밤에 밝아진다(낮에는 완전히 꺼진다). 창 광원은 거짓이라 밤낮 상관없이
        /// 일정하다 — 창은 "실내 조명"이 아니라 바깥빛이 새어 드는 구조물이라는
        /// 씬 빌더 쪽 판단을 그대로 따른다(`BaseScene.PlaceRoomLights`/`PlaceCorridorLights`
        /// 는 boost=true, 창 배치 자리는 boost=false로 채운다).
        /// </summary>
        public bool[] nightBoost = System.Array.Empty<bool>();

        /// <summary>밤 블렌드를 읽는다. 없으면 항상 낮 취급(에디터 단독 재생 등)</summary>
        public WeatherGrading grading;

        /// <summary>화면 판정 기준 카메라. 비워두면 `Camera.main`</summary>
        public Camera worldCamera;

        /// <summary>화면 밖 판정 여유(월드 유닛). 광원 반경만큼 둬야 화면 가장자리에서
        /// 꺼졌다 켜졌다 깜빡이지 않는다</summary>
        private const float CullMargin = 6f;

        /// <summary>
        /// WC 발주 #3 — 세기 페이드 시간(초). `Time.deltaTime` 기반이라 프레임률과
        /// 무관하게 항상 이 시간이 걸린다. 0.3~0.6초대에서 시작한다 — 더 느리면
        /// 화면 밖으로 막 나간 등이 한참 남아 있는 것처럼 보여 반응이 굼떠 보인다.
        /// </summary>
        private const float FadeSeconds = 0.4f;

        /// <summary>이 밑으로 내려가면 실제로 `enabled = false`로 꺼서 드로우콜을 뺀다.
        /// `Mathf.MoveTowards`가 목표에 못 미치면 정확히 0에 닿으므로 아주 작은 값도
        /// 충분하다</summary>
        private const float FadeEpsilon = 0.001f;

        private float[] _baseIntensity = System.Array.Empty<float>();
        private Transform[] _transforms = System.Array.Empty<Transform>();

        /// <summary>
        /// 이번 프레임 각 광원이 "결국 도달해야 할" 세기 배율(0~1). 화면 밖·상한
        /// 탈락이면 0, 선택됐으면 밤 연동 배율(§9.3 "실내등은 밤에만") — 이 값
        /// 자체로 즉시 세기를 바꾸지 않고, `_scale`이 이 값을 향해 시간에 걸쳐
        /// 따라간다(WC 발주 #3).
        /// </summary>
        private float[] _targetScale = System.Array.Empty<float>();

        /// <summary>지금 실제로 적용 중인 세기 배율. `_targetScale`을 향해
        /// `FadeSeconds`에 걸쳐 이동한다</summary>
        private float[] _scale = System.Array.Empty<float>();

        // 화면당 상한을 고르는 동안 쓰는 고정 버퍼 — 매 프레임 할당하지 않는다.
        // 광원 수가 수십 개 수준이라 정렬 대신 삽입식 상위 K 추적으로 충분하다
        private readonly int[] _topIndex = new int[MaxActiveLights];
        private readonly float[] _topDist = new float[MaxActiveLights];

        private void Awake()
        {
            _baseIntensity = new float[lights.Length];
            _transforms = new Transform[lights.Length];
            _targetScale = new float[lights.Length];
            _scale = new float[lights.Length];
            for (var i = 0; i < lights.Length; i += 1)
            {
                if (lights[i] == null) continue;
                // **여기서 한 번만 읽는다.** 씬 빌더가 준 값이 "밤 100%"에 해당하는
                // 기준값이고, 매 프레임 `intensity`를 되읽으면 아래서 우리가 쓴
                // 보정값을 기준값으로 착각해 값이 점점 눌린다
                _baseIntensity[i] = lights[i].intensity;
                _transforms[i] = lights[i].transform;
                // 시작은 꺼진 상태(0)에서 — 첫 프레임에 목표치가 계산되면 거기서부터
                // 페이드 인 한다. 갑자기 100%로 나타나는 것보다 이쪽이 항상 안전하다
                _scale[i] = 0f;
            }
        }

        private void LateUpdate()
        {
            if (lights.Length == 0) return;

            // 전체 토글 — 이게 기존 unlit 화면으로 돌아가는 유일한 경로다.
            // 꺼진 Light2D는 2D 렌더러 조명 패스에서 완전히 빠진다. 저사양·롤백
            // 안전장치라 여기는 페이드 없이 즉시 끈다 — `_scale`도 0으로 되돌려
            // 놔야 다시 켰을 때 과거 값에서 순간이동하지 않고 페이드 인 한다
            if (!lightingEnabled)
            {
                for (var i = 0; i < lights.Length; i += 1)
                {
                    if (lights[i] == null) continue;
                    lights[i].enabled = false;
                    _scale[i] = 0f;
                    _targetScale[i] = 0f;
                }
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

            // 1단계 — 이번 프레임 목표 배율을 전부 0으로 리셋하고, 화면 안 +
            // 밤 연동상 "잠재적으로 켜질 수 있는"(potential > 0) 광원만 상한
            // 후보에 올린다. 잠재값이 이미 0인 광원(밤 연동 실내등 + 밝은 낮)을
            // 후보에서 아예 빼면, 상한 8개 슬롯을 "어차피 꺼질 등"이 낭비하지
            // 않고 실제로 보여야 할 등(창 등 등)에게 돌아간다 — 성능 이득은
            // `light.enabled = false`뿐 아니라 상한 슬롯 배분에서도 지킨다.
            var topCount = 0;

            for (var i = 0; i < lights.Length; i += 1)
            {
                _targetScale[i] = 0f;

                var light = lights[i];
                if (light == null) continue;

                var boost = i < nightBoost.Length && nightBoost[i];
                // WC 발주 #2 — 실내등은 밤 연동, 창 등은 항상 잠재값 1
                var potential = boost ? night : 1f;
                if (potential <= 0f) continue;   // 낮의 실내등 — 후보에도 안 올린다

                var pos = _transforms[i].position;
                var onScreen = !hasView
                    || (Mathf.Abs(pos.x - camPos.x) <= halfW && Mathf.Abs(pos.y - camPos.y) <= halfH);
                if (!onScreen) continue;

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
                var boost = i < nightBoost.Length && nightBoost[i];
                _targetScale[i] = boost ? night : 1f;
            }

            // 2단계 — 목표 배율을 향해 `FadeSeconds`에 걸쳐 실제 세기를 이동한다.
            // `Time.deltaTime` 기반 `MoveTowards`라 프레임률이 달라도 걸리는 시간은
            // 같다. 화면 밖으로 나가는 등·상한에서 밀려나는 등·밤에서 낮으로
            // 넘어가는 실내등 모두 이 한 경로를 거치므로 어디서도 즉시 끊기지 않는다
            var step = Time.deltaTime / FadeSeconds;

            for (var i = 0; i < lights.Length; i += 1)
            {
                var light = lights[i];
                if (light == null) continue;

                _scale[i] = Mathf.MoveTowards(_scale[i], _targetScale[i], step);

                if (_scale[i] <= FadeEpsilon)
                {
                    // 세기 0 도달 후에만 실제로 비활성화한다 — 그래야 성능 이득
                    // (드로우콜 감소)은 지키면서도 꺼지는 과정 자체는 이미 부드러웠다
                    light.enabled = false;
                    continue;
                }

                light.enabled = true;
                light.intensity = _baseIntensity[i] * _scale[i];
            }
        }
    }
}
