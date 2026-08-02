using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.Net
{
    /// <summary>
    /// 카메라 (SAD-ART-001 §2.1 · §2.2).
    ///
    /// **하이브리드 해상도**가 이 게임의 핵심 결정이다(§2.1). 월드는 논리 해상도
    /// 640×360의 픽셀 퍼펙트로 그리고, UI는 1920×1080 네이티브로 따로 그린다.
    /// 그래서 여기서 정하는 것은 월드 쪽뿐이고 HUD는 이 값들을 모른다.
    ///
    ///   PPU 32 · Reference 640×360 · Orthographic Size 360 / 2 / 32 = 5.625
    ///
    /// 추적은 **데드존**으로 한다(§2.2 96×54px). 그냥 따라가면 걸을 때마다 화면
    /// 전체가 같이 움직여 픽셀아트가 계속 흔들린다. 데드존 안에서는 카메라가
    /// 가만히 있고, 플레이어가 그 상자를 밀어야 따라간다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraRig : MonoBehaviour
    {
        /// <summary>§2.1 Pixels Per Unit</summary>
        public const int PPU = 32;
        /// <summary>§2.1 논리 해상도</summary>
        public const int RefWidth = 640;
        public const int RefHeight = 360;
        /// <summary>§2.1 360 / 2 / 32</summary>
        public const float OrthoSize = RefHeight * 0.5f / PPU;

        public Transform target;

        [Tooltip("§2.2 데드존 96×54px — 이 상자를 벗어나야 카메라가 따라간다")]
        public Vector2 deadZonePixels = new Vector2(96f, 54f);

        [Tooltip("§2.2 SmoothDamp 0.15s")]
        public float smoothTime = 0.15f;

        /// <summary>§2.2 구역 경계 — 시야 차단(§1.3-A)과 **같은 사각형**을 쓴다</summary>
        public Rect confine;
        public bool confined;

        /// <summary>하달 창 · 점호 판정 동안은 카메라를 고정한다(§2.2)</summary>
        public bool locked;

        /// <summary>§4.3 패닉 진동. 접근성 토글은 `WeatherGrading`이 들고 있다</summary>
        public WeatherGrading grading;

        private Camera _camera;
        private Vector3 _velocity;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = OrthoSize;

            // §2.1 Pixel Perfect Camera. Upscale Render Texture ON / Pixel Snapping ON /
            // Crop Frame None — 레터박스 대신 정수배 + 여백은 월드로 채운다
            var pixel = GetComponent<PixelPerfectCamera>();
            if (pixel == null) pixel = gameObject.AddComponent<PixelPerfectCamera>();
            pixel.assetsPPU = PPU;
            pixel.refResolutionX = RefWidth;
            pixel.refResolutionY = RefHeight;
            pixel.upscaleRT = true;
            pixel.pixelSnapping = true;
            pixel.cropFrameX = false;
            pixel.cropFrameY = false;
        }

        public void SnapTo(Vector2 point)
        {
            _velocity = Vector3.zero;
            transform.position = Clamp(new Vector3(point.x, point.y, -10f));
        }

        /// <summary>
        /// 구역 경계를 갈아 끼운다.
        ///
        /// **경계는 즉시 바꾼다.** 예전에는 경계 자체를 보간했는데, 그게 축이
        /// 따로 노는 원인이었다 — 복도(x17~70)와 방(x17~32)의 합집합에서 방으로
        /// 좁혀 들어가는 동안 x가 계속 줄어들어, y만 움직이면 될 전환에서
        /// 카메라가 x로도 밀렸다. 게다가 매 프레임 경계가 바뀌니 픽셀 퍼펙트
        /// 스냅이 칸을 오가며 화면이 자글거렸다.
        ///
        /// 부드러움은 경계가 아니라 **위치**에서 나온다. 경계는 딱 잘라 바꾸고,
        /// 그 경계로 자른 목표 지점까지 `SmoothDamp` 하나로 따라가면 두 축이
        /// 함께 움직인다.
        /// </summary>
        public void SetBounds(Rect bounds)
        {
            if (bounds.width <= 0f || bounds.height <= 0f)
            {
                ClearBounds();
                return;
            }
            confine = bounds;
            confined = true;
        }

        public void ClearBounds() => confined = false;

        private void LateUpdate()
        {
            if (target == null || locked) return;

            var at = transform.position;
            var to = target.position;

            // 데드존 — 상자 밖으로 나간 만큼만 목표를 민다
            var half = new Vector2(deadZonePixels.x * 0.5f / PPU, deadZonePixels.y * 0.5f / PPU);
            var want = new Vector3(at.x, at.y, -10f);

            var dx = to.x - at.x;
            if (Mathf.Abs(dx) > half.x) want.x = to.x - Mathf.Sign(dx) * half.x;

            var dy = to.y - at.y;
            if (Mathf.Abs(dy) > half.y) want.y = to.y - Mathf.Sign(dy) * half.y;

            // **자른 뒤에 따라간다.** 반대로 하면(따라간 뒤 자르면) 경계에 닿는
            // 순간 위치가 잘려나가 SmoothDamp가 매 프레임 다시 시작하고, 그게
            // 전환 때 화면이 끊기고 떨리는 이유였다
            var next = Vector3.SmoothDamp(at, Clamp(want), ref _velocity, smoothTime);

            // 진동은 Confiner **뒤에** 더한다 — 먼저 더하면 경계에서 잘려나가
            // 흔들림이 한쪽 방향으로만 나타난다
            if (grading != null)
            {
                var shake = grading.ShakeOffset();
                next.x += shake.x;
                next.y += shake.y;
            }

            transform.position = next;
        }

        /// <summary>
        /// §2.2 Confiner — 구역 밖으로 나가지 않는다.
        ///
        /// 구역이 화면보다 작으면 가운데에 고정한다. 안 그러면 좁은 방에서
        /// 카메라가 경계에 튕기며 떨린다.
        /// </summary>
        private Vector3 Clamp(Vector3 position)
        {
            if (!confined) return position;

            var halfH = OrthoSize;
            var halfW = OrthoSize * Mathf.Max(0.1f, _camera.aspect);

            // 경계가 화면보다 작으면 **중앙에 고정하지 않고 화면 크기로 넓힌다.**
            //
            // 고정하면 그 구역에 발을 들이는 순간 카메라가 중앙으로 끌려가고,
            // 그게 "확 바뀐다"의 정체였다. 복도(높이 4타일)는 화면(11.25타일)보다
            // 납작해서 들어설 때마다 세로로 튀었다.
            //
            // 넓히면 복도 밖이 조금 보이지만 그건 문제가 아니다 — 시야 차단(§1.3-A)은
            // 분대원 스프라이트를 감추는 것이지 벽을 가리는 것이 아니다.
            var box = confine;
            if (box.width < halfW * 2f)
            {
                var grow = halfW - box.width * 0.5f;
                box = new Rect(box.x - grow, box.y, box.width + grow * 2f, box.height);
            }
            if (box.height < halfH * 2f)
            {
                var grow = halfH - box.height * 0.5f;
                box = new Rect(box.x, box.y - grow, box.width, box.height + grow * 2f);
            }

            position.x = Mathf.Clamp(position.x, box.xMin + halfW, box.xMax - halfW);
            position.y = Mathf.Clamp(position.y, box.yMin + halfH, box.yMax - halfH);
            return position;
        }
    }
}
