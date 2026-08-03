using UnityEngine;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// 좌상단 FPS 계측기 — 현재 FPS·프레임타임(ms)·1% low(최근 500프레임).
    ///
    /// 1% low는 최근 500프레임 중 가장 느린 1%(최소 1프레임)의 평균
    /// 프레임타임을 fps로 환산한 값이다. 평균 FPS는 순간의 부드러움을
    /// 가리지만, 1% low는 "가장 끊긴 순간"을 드러낸다 — WebGL 실측 판단은
    /// 그 순간을 보려는 것이다.
    /// </summary>
    public sealed class Hd2dFpsMeter : MonoBehaviour
    {
        public Hd2dSceneToggles toggles;

        private const int SampleCount = 500;
        private readonly float[] _frameSeconds = new float[SampleCount];
        private readonly float[] _sortScratch = new float[SampleCount];
        private int _index;
        private int _filled;

        private GUIStyle _style;

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            _frameSeconds[_index] = dt;
            _index = (_index + 1) % SampleCount;
            if (_filled < SampleCount) _filled += 1;
        }

        private void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
            };

            var dt = Time.unscaledDeltaTime;
            var fps = dt > 0f ? 1f / dt : 0f;
            var frameMs = dt * 1000f;
            var low1 = OnePercentLowFps();

            GUI.Box(new Rect(6, 6, 250, 110), GUIContent.none);

            var y = 10;
            GUI.Label(new Rect(14, y, 240, 20), $"FPS {fps:0.0}  ({frameMs:0.0} ms)", _style); y += 20;
            GUI.Label(new Rect(14, y, 240, 20), $"1% low  {low1:0.0} fps  ({_filled}f 표본)", _style); y += 22;
            GUI.Label(new Rect(14, y, 240, 20),
                toggles == null ? "[토글 없음]" : $"[1] 후처리 {OnOff(toggles.PostFxOn)}", _style); y += 18;
            GUI.Label(new Rect(14, y, 240, 20),
                toggles == null ? "" : $"[2] 조명   {OnOff(toggles.LightingOn)}", _style); y += 18;
            GUI.Label(new Rect(14, y, 240, 20),
                toggles == null ? "" : $"[3] 내부해상도 {(toggles.LowResOn ? "0.66x" : "1.0x")}", _style);
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";

        private float OnePercentLowFps()
        {
            if (_filled == 0) return 0f;

            System.Array.Copy(_frameSeconds, _sortScratch, _filled);
            System.Array.Sort(_sortScratch, 0, _filled);

            var worstCount = Mathf.Max(1, Mathf.RoundToInt(_filled * 0.01f));
            var sum = 0f;
            for (var i = _filled - worstCount; i < _filled; i += 1) sum += _sortScratch[i];

            var avgWorstSeconds = sum / worstCount;
            return avgWorstSeconds > 0f ? 1f / avgWorstSeconds : 0f;
        }
    }
}
