using UnityEngine;
using UnityEngine.UI;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// "다이어트" 내부 해상도 렌더링 (§ HD-2D 목업 "내부 해상도 ~0.66배").
    ///
    /// 카메라를 화면보다 작은 <see cref="RenderTexture"/>에 그리고, 전체화면
    /// <see cref="RawImage"/>로 늘려 올린다. 켜져 있을 때는 카메라가 백버퍼에
    /// 직접 그리지 않으므로(targetTexture가 있으면 그쪽으로만 그린다) 이중으로
    /// 그려질 걱정이 없다. 꺼지면 targetTexture를 비우고 오버레이를 숨겨
    /// 원래 해상도로 돌아간다.
    /// </summary>
    public sealed class Hd2dResolutionScaler : MonoBehaviour
    {
        public Camera targetCamera;
        public Canvas overlayCanvas;
        public RawImage display;

        [Range(0.25f, 1f)]
        public float scale = 0.66f;

        private RenderTexture _rt;
        private int _rtWidth;
        private int _rtHeight;

        public bool LowResEnabled { get; private set; }

        private void OnDestroy() => ReleaseRt();

        public void SetEnabled(bool on)
        {
            LowResEnabled = on;
            if (on)
            {
                EnsureRt();
                targetCamera.targetTexture = _rt;
                display.texture = _rt;
                overlayCanvas.enabled = true;
            }
            else
            {
                targetCamera.targetTexture = null;
                overlayCanvas.enabled = false;
                ReleaseRt();
            }
        }

        private void Update()
        {
            if (!LowResEnabled) return;
            var w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * scale));
            var h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * scale));
            if (w == _rtWidth && h == _rtHeight) return;

            EnsureRt();
            targetCamera.targetTexture = _rt;
            display.texture = _rt;
        }

        private void EnsureRt()
        {
            var w = Mathf.Max(2, Mathf.RoundToInt(Screen.width * scale));
            var h = Mathf.Max(2, Mathf.RoundToInt(Screen.height * scale));
            if (_rt != null && _rtWidth == w && _rtHeight == h) return;

            ReleaseRt();
            _rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default)
            {
                filterMode = FilterMode.Bilinear,
                name = "Hd2dMock_LowRes",
            };
            _rt.Create();
            _rtWidth = w;
            _rtHeight = h;
        }

        private void ReleaseRt()
        {
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
            _rtWidth = 0;
            _rtHeight = 0;
        }
    }
}
