using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 아이콘과 링 (SAD-ART-001 §7.1.2 · §8).
    ///
    /// **파일이 아니라 코드로 그린다.** §8은 아이콘 80종을 픽셀 원본으로 지급하라고
    /// 하지만, 그건 픽셀 아티스트에게 주는 작업 지시다. UI는 네이티브 해상도이고
    /// (§2.1) 목업도 전부 벡터 도형이라, 런타임에서 사각형을 조립하는 편이
    /// 텍스처 예산도 아끼고 색을 상태에 따라 바꾸기도 쉽다.
    ///
    /// **형태로 구분한다**는 것이 여기서 지켜야 할 규칙이다(§8 · §15 색각 이상).
    /// 온도 밴드 6종은 결정/눈송이/원/반원/태양/이중태양이고, 색을 빼도
    /// 서로 달라야 한다.
    /// </summary>
    public static class HudIcons
    {
        /* ══════════════════════════════════════════════ §7.1.2 컨디션 링 */

        /// <summary>
        /// 링 하나. 값이 바뀔 때만 텍스처를 다시 칠한다.
        ///
        /// 매 프레임 원호를 그리면 OnGUI에서 수백 번 그리기가 되고, 매 프레임
        /// 텍스처를 만들면 힙이 톱질을 한다. 스탯은 스냅샷(10Hz)에서만 바뀌고
        /// 실제로는 시간대당 몇 번이라, 값이 바뀔 때만 칠하면 둘 다 피한다.
        /// </summary>
        private sealed class Ring
        {
            private const int Size = 64;

            private Texture2D _tex;
            private float _value = -1f;
            private Color _fill;
            private Color _track;
            private float _threshold = float.NaN;
            private Color _thresholdColor;

            public Texture2D Get(float value, Color fill, Color track, float threshold, Color thresholdColor)
            {
                if (_tex != null && Mathf.Abs(_value - value) < 0.004f && _fill == fill && _track == track &&
                    (float.IsNaN(_threshold) ? float.IsNaN(threshold) : Mathf.Approximately(_threshold, threshold)) &&
                    _thresholdColor == thresholdColor)
                    return _tex;

                _value = value;
                _fill = fill;
                _track = track;
                _threshold = threshold;
                _thresholdColor = thresholdColor;

                // 텍스처에 굽는 색은 **보정하지 않는다** — sRGB 텍스처라 Unity가
                // 샘플링할 때 이미 선형으로 바꾼다(HudTheme.Lin 참조). 여기 걸면
                // 감마가 두 번 적용되어 링이 검게 가라앉는다
                var fillLin = fill;
                var trackLin = track;
                var thresholdLin = thresholdColor;

                if (_tex == null)
                {
                    _tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                }

                const float outer = Size * 0.5f - 1f;
                const float inner = outer - Size * 0.135f;   // 링 두께 ≈ 지름의 13.5%
                var center = new Vector2(Size * 0.5f, Size * 0.5f);
                var pixels = new Color32[Size * Size];
                // F-2(WORKORDER) — 조건 D 문턱선의 각도 폭. 링 둘레 기준 각도 절반폭이라
                // 값과 무관하게 항상 같은 굵기로 보인다
                const float thresholdHalfWidthDeg = 2.6f;
                var thresholdAngle = threshold * 360f;

                for (var y = 0; y < Size; y += 1)
                for (var x = 0; x < Size; x += 1)
                {
                    var d = new Vector2(x + 0.5f, y + 0.5f) - center;
                    var r = d.magnitude;
                    if (r > outer || r < inner)
                    {
                        pixels[y * Size + x] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    // 12시에서 시계 방향. 게이지는 시계 방향으로 차는 것이 관습이고,
                    // 목업도 rotate(-90)으로 12시에서 시작한다
                    var angle = Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg;
                    if (angle < 0f) angle += 360f;

                    var on = angle <= value * 360f;
                    var color = on ? fillLin : trackLin;

                    // F-2 — 판정 문턱을 가는 선으로 굽는다. 값이 아직 안전해도
                    // 항상 보이므로(반응형 위험색과 달리) 사전에 예측할 수 있다.
                    // 같은 각도 계산을 재사용해서 링 값과 절대 어긋나지 않는다
                    if (!float.IsNaN(threshold) && Mathf.Abs(angle - thresholdAngle) < thresholdHalfWidthDeg)
                        color = thresholdLin;

                    // 가장자리 한 픽셀만 부드럽게. 링이 톱니로 보이면 서류 톤이 깨진다
                    var edge = Mathf.Min(outer - r, r - inner);
                    color.a *= Mathf.Clamp01(edge);
                    pixels[y * Size + x] = color;
                }

                _tex.SetPixels32(pixels);
                _tex.Apply(false);
                return _tex;
            }
        }

        private static readonly Dictionary<string, Ring> Rings = new Dictionary<string, Ring>();

        public static void DrawRing(Rect rect, string id, float value, Color fill, Color track)
        {
            DrawRing(rect, id, value, fill, track, float.NaN, default);
        }

        /// <summary>
        /// F-2(WORKORDER) — 판정 문턱선이 있는 링. `threshold`는 0~1 비율(예: 조건 D
        /// 청결 하한 20/100 = 0.2f)이고, `float.NaN`이면 문턱선 없이 기존과 같다.
        /// </summary>
        public static void DrawRing(
            Rect rect, string id, float value, Color fill, Color track,
            float threshold, Color thresholdColor)
        {
            if (!Rings.TryGetValue(id, out var ring))
            {
                ring = new Ring();
                Rings[id] = ring;
            }
            GUI.DrawTexture(rect, ring.Get(Mathf.Clamp01(value), fill, track, threshold, thresholdColor));
        }

        /* ══════════════════════════════════════════════════ 도형 프리미티브 */

        private static HudTheme _theme;

        public static void Bind(HudTheme theme) => _theme = theme;

        private static void Box(float x, float y, float w, float h, Color c) =>
            _theme.Fill(new Rect(x, y, w, h), c);

        /// <summary>
        /// 대각선. IMGUI에는 선이 없으므로 짧은 사각형을 이어 그린다.
        /// 굵기 2~4px 구간에서는 이것으로 충분히 선으로 읽힌다.
        /// </summary>
        private static void Line(Vector2 a, Vector2 b, float weight, Color c)
        {
            var steps = Mathf.CeilToInt(Vector2.Distance(a, b) / Mathf.Max(1f, weight * 0.5f));
            for (var i = 0; i <= steps; i += 1)
            {
                var p = Vector2.Lerp(a, b, steps == 0 ? 0f : i / (float)steps);
                Box(p.x - weight * 0.5f, p.y - weight * 0.5f, weight, weight, c);
            }
        }

        private static void Ellipse(Rect r, float weight, Color c, bool filled = false)
        {
            const int segments = 28;
            var center = r.center;
            var rx = r.width * 0.5f;
            var ry = r.height * 0.5f;

            if (filled)
            {
                for (var i = 0; i < segments; i += 1)
                {
                    var t = i / (float)segments * Mathf.PI;
                    var dy = Mathf.Cos(t) * ry;
                    var dx = Mathf.Sin(t) * rx;
                    Box(center.x - dx, center.y + dy - ry / segments, dx * 2f, ry * 2f / segments + 1f, c);
                }
                return;
            }

            var previous = Vector2.zero;
            for (var i = 0; i <= segments; i += 1)
            {
                var t = i / (float)segments * Mathf.PI * 2f;
                var p = new Vector2(center.x + Mathf.Sin(t) * rx, center.y - Mathf.Cos(t) * ry);
                if (i > 0) Line(previous, p, weight, c);
                previous = p;
            }
        }

        /* ══════════════════════════════════════════ §7.1.2 스탯 아이콘 6종 */

        /// <summary>
        /// 체력=십자 · 수분=물방울 · 피로=눈꺼풀 · 정신력=나선 · 청결=비누 · 포만감=식기.
        /// `rect`는 정사각형을 가정한다.
        /// </summary>
        public static void Stat(string id, Rect rect, Color c)
        {
            var u = rect.width / 24f;   // 24 단위 격자로 그린다
            float X(float v) => rect.x + v * u;
            float Y(float v) => rect.y + v * u;

            switch (id)
            {
                case "stamina":   // 십자
                    Box(X(10), Y(4), u * 4, u * 16, c);
                    Box(X(4), Y(10), u * 16, u * 4, c);
                    break;

                case "hydration": // 물방울 — 위가 뾰족하고 아래가 둥글다
                    Box(X(11), Y(3), u * 2, u * 4, c);
                    Box(X(10), Y(7), u * 4, u * 3, c);
                    Box(X(8), Y(10), u * 8, u * 3, c);
                    Ellipse(new Rect(X(6), Y(11), u * 12, u * 10), 0f, c, true);
                    break;

                case "fatigue":   // 눈꺼풀 — 감긴 눈 + 속눈썹
                    Line(new Vector2(X(4), Y(13)), new Vector2(X(12), Y(9)), u * 1.8f, c);
                    Line(new Vector2(X(12), Y(9)), new Vector2(X(20), Y(13)), u * 1.8f, c);
                    Box(X(4), Y(13), u * 16, u * 1.6f, c);
                    Box(X(7), Y(15), u * 1.4f, u * 3, c);
                    Box(X(11.5f), Y(15.5f), u * 1.4f, u * 3.4f, c);
                    Box(X(16), Y(15), u * 1.4f, u * 3, c);
                    break;

                case "mental":    // 나선 — 안에서 밖으로 도는 선
                    Line(new Vector2(X(12), Y(11)), new Vector2(X(15), Y(11)), u * 1.8f, c);
                    Line(new Vector2(X(15), Y(11)), new Vector2(X(15), Y(15)), u * 1.8f, c);
                    Line(new Vector2(X(15), Y(15)), new Vector2(X(9), Y(15)), u * 1.8f, c);
                    Line(new Vector2(X(9), Y(15)), new Vector2(X(9), Y(8)), u * 1.8f, c);
                    Line(new Vector2(X(9), Y(8)), new Vector2(X(18), Y(8)), u * 1.8f, c);
                    Line(new Vector2(X(18), Y(8)), new Vector2(X(18), Y(19)), u * 1.8f, c);
                    break;

                case "hygiene":   // 비누 + 거품
                    Box(X(4), Y(12), u * 16, u * 8, c);
                    Box(X(6), Y(10), u * 2, u * 2, c);
                    Box(X(11), Y(7), u * 2.5f, u * 2.5f, c);
                    Box(X(16), Y(9), u * 2, u * 2, c);
                    break;

                case "satiety":   // 식기 — 그릇 + 받침
                    Box(X(3), Y(8), u * 18, u * 3, c);
                    Box(X(5), Y(11), u * 14, u * 3, c);
                    Box(X(8), Y(14), u * 8, u * 2, c);
                    Box(X(4), Y(18), u * 16, u * 2, c);
                    break;
            }
        }

        /* ═════════════════════════════════════════ §4.3 온도 밴드 6종 */

        /// <summary>
        /// 결정 / 눈송이 / 원 / 반원 / 태양 / 이중태양.
        ///
        /// §8이 "형태로 구분"을 못박은 항목이다. 색각 이상 옵션에서는 색이
        /// 사라지므로, 이 여섯이 실루엣만으로 갈라져야 한다.
        /// </summary>
        public static void Band(string band, Rect rect, Color c)
        {
            var u = rect.width / 48f;
            var mid = rect.center;
            var w = u * 3f;

            switch (band)
            {
                case "extremeCold":  // 결정 — 세 축 + 가지
                    Line(new Vector2(mid.x, rect.y + u * 6), new Vector2(mid.x, rect.yMax - u * 6), w, c);
                    Line(new Vector2(rect.x + u * 9, rect.y + u * 15), new Vector2(rect.xMax - u * 9, rect.yMax - u * 15), w, c);
                    Line(new Vector2(rect.xMax - u * 9, rect.y + u * 15), new Vector2(rect.x + u * 9, rect.yMax - u * 15), w, c);
                    Line(new Vector2(mid.x, rect.y + u * 12), new Vector2(mid.x - u * 5, rect.y + u * 17), w * 0.7f, c);
                    Line(new Vector2(mid.x, rect.y + u * 12), new Vector2(mid.x + u * 5, rect.y + u * 17), w * 0.7f, c);
                    Line(new Vector2(mid.x, rect.yMax - u * 12), new Vector2(mid.x - u * 5, rect.yMax - u * 17), w * 0.7f, c);
                    Line(new Vector2(mid.x, rect.yMax - u * 12), new Vector2(mid.x + u * 5, rect.yMax - u * 17), w * 0.7f, c);
                    break;

                case "cold":         // 눈송이 — 축만. 결정보다 단순하다
                    Line(new Vector2(mid.x, rect.y + u * 8), new Vector2(mid.x, rect.yMax - u * 8), w, c);
                    Line(new Vector2(rect.x + u * 8, mid.y), new Vector2(rect.xMax - u * 8, mid.y), w, c);
                    Line(new Vector2(rect.x + u * 12, rect.y + u * 12), new Vector2(rect.xMax - u * 12, rect.yMax - u * 12), w, c);
                    Line(new Vector2(rect.xMax - u * 12, rect.y + u * 12), new Vector2(rect.x + u * 12, rect.yMax - u * 12), w, c);
                    break;

                case "normal":       // 원
                    Ellipse(new Rect(rect.x + u * 10, rect.y + u * 10, u * 28, u * 28), w, c);
                    break;

                case "warm":         // 반원 — 아래쪽만
                    for (var i = 0; i < 14; i += 1)
                    {
                        var t = i / 13f * Mathf.PI;
                        var dx = Mathf.Sin(t) * u * 14f;
                        Box(mid.x - dx, mid.y + Mathf.Abs(Mathf.Cos(t)) * 0f + i * u, dx * 2f, u * 1.2f, c);
                    }
                    Box(mid.x - u * 14, mid.y - u, u * 28, w, c);
                    break;

                case "hot":          // 태양 — 원 + 4방향 광선
                    Ellipse(new Rect(rect.x + u * 14, rect.y + u * 14, u * 20, u * 20), 0f, c, true);
                    Box(mid.x - w * 0.5f, rect.y + u * 4, w, u * 7, c);
                    Box(mid.x - w * 0.5f, rect.yMax - u * 11, w, u * 7, c);
                    Box(rect.x + u * 4, mid.y - w * 0.5f, u * 7, w, c);
                    Box(rect.xMax - u * 11, mid.y - w * 0.5f, u * 7, w, c);
                    break;

                case "extremeHot":   // 이중태양 — 태양 + 바깥 링. 혹서와 형태가 다르다
                    Ellipse(new Rect(rect.x + u * 17, rect.y + u * 17, u * 14, u * 14), 0f, c, true);
                    Ellipse(new Rect(rect.x + u * 9, rect.y + u * 9, u * 30, u * 30), w * 0.8f, c);
                    Box(mid.x - w * 0.5f, rect.y + u * 2, w, u * 5, c);
                    Box(mid.x - w * 0.5f, rect.yMax - u * 7, w, u * 5, c);
                    Box(rect.x + u * 2, mid.y - w * 0.5f, u * 5, w, c);
                    Box(rect.xMax - u * 7, mid.y - w * 0.5f, u * 5, w, c);
                    break;
            }
        }

        /* ═════════════════════════════════════ §7.1.3+§7.9 사람 마커 — 보직 형태 */

        /// <summary>
        /// 미니맵·부대 지도의 분대원 점 — 보직별 형태 (H-4 색각 이상 대응).
        ///
        /// 지금까지 보직 구분은 `HudTheme.RoleColor` 색 하나뿐이었다. 적록색각에서는
        /// 소총(연두)과 의무(흰색 근처)가 명도로 거의 겹쳐 보일 수 있다 — 온도 밴드
        /// 6종이 이미 지키는 원칙("색을 빼도 서로 달라야 한다", `Band` 참고)을 사람
        /// 마커에도 그대로 적용한다.
        ///
        /// 소총=원 · 통신=사각 · 의무=십자 · 행정=삼각. 같은 구역/타 구역 구분은
        /// 더 이상 이 형태가 아니라 호출자가 넘기는 색의 **알파**로 갈린다 —
        /// 형태 축을 보직에 내주면서 옮긴 것이다(Hud.cs "5. 사람" 참고).
        /// </summary>
        public static void RoleShape(string role, Rect r, Color c)
        {
            switch (role)
            {
                case "comms":   // 사각
                    Box(r.x, r.y, r.width, r.height, c);
                    break;

                case "medic":   // 십자 — Stat("stamina")와 같은 형태 언어
                {
                    var bar = r.width * 0.34f;
                    Box(r.center.x - bar * 0.5f, r.y, bar, r.height, c);
                    Box(r.x, r.center.y - bar * 0.5f, r.width, bar, c);
                    break;
                }

                case "admin":   // 삼각
                    Triangle(r, c);
                    break;

                default:        // 소총 — 원
                    Dot(r, c);
                    break;
            }
        }

        /// <summary>위로 뾰족한 채운 삼각형. `Band`의 "warm" 반원과 같은 방식으로
        /// 가로 막대를 쌓아 곡선 없이 근사한다 — IMGUI에는 다각형이 없다</summary>
        private static void Triangle(Rect r, Color c)
        {
            const int rows = 12;
            var rowH = r.height / rows;
            for (var i = 0; i < rows; i += 1)
            {
                var t = (i + 1f) / rows;
                var w = r.width * t;
                Box(r.center.x - w * 0.5f, r.y + r.height - (i + 1) * rowH, w, rowH + 0.6f, c);
            }
        }

        /* ══════════════════════════════════════ §7.8 퀵 커맨드 8종 */

        /// <summary>
        /// 집합 / 대기 / 이상 없음 / 지원 요청 / 완료 / 못 함 / 여기 / 서둘러.
        ///
        /// 보이스가 없으므로 이 8종이 소통의 전부다(§7.8). 서로 확실히
        /// 구분되는 것이 §8.0의 성립 조건이라 도형을 최대한 다르게 잡는다.
        /// </summary>
        public static void Command(string id, Rect rect, Color c)
        {
            var u = rect.width / 48f;
            var mid = rect.center;
            var w = u * 4f;

            switch (id)
            {
                case "assemble":   // 모이는 화살표 — 위로 + 안으로
                    Box(mid.x - w * 0.5f, rect.y + u * 6, w, u * 24, c);
                    Line(new Vector2(mid.x - u * 7, rect.y + u * 13), new Vector2(mid.x, rect.y + u * 6), w, c);
                    Line(new Vector2(mid.x + u * 7, rect.y + u * 13), new Vector2(mid.x, rect.y + u * 6), w, c);
                    Line(new Vector2(rect.x + u * 8, rect.yMax - u * 6), new Vector2(mid.x - u * 4, rect.yMax - u * 16), w, c);
                    Line(new Vector2(rect.xMax - u * 8, rect.yMax - u * 6), new Vector2(mid.x + u * 4, rect.yMax - u * 16), w, c);
                    break;

                case "wait":       // 손바닥
                    Box(mid.x - u * 12, mid.y - u * 4, u * 24, u * 16, c);
                    Box(mid.x - u * 12, mid.y - u * 14, u * 5, u * 12, c);
                    Box(mid.x - u * 5, mid.y - u * 18, u * 5, u * 16, c);
                    Box(mid.x + u * 2, mid.y - u * 16, u * 5, u * 14, c);
                    Box(mid.x + u * 9, mid.y - u * 12, u * 4, u * 10, c);
                    break;

                case "allClear":   // 체크 1개
                    Line(new Vector2(rect.x + u * 10, mid.y), new Vector2(mid.x - u * 2, mid.y + u * 10), u * 5, c);
                    Line(new Vector2(mid.x - u * 2, mid.y + u * 10), new Vector2(rect.xMax - u * 8, mid.y - u * 12), u * 5, c);
                    break;

                case "needHelp":   // 손 흔듦 — 위로 뻗은 팔 + 호선
                    Box(mid.x - w * 0.5f, mid.y - u * 14, w, u * 26, c);
                    Line(new Vector2(mid.x, mid.y - u * 14), new Vector2(mid.x - u * 10, mid.y - u * 20), w, c);
                    Line(new Vector2(mid.x, mid.y - u * 14), new Vector2(mid.x + u * 10, mid.y - u * 20), w, c);
                    Line(new Vector2(mid.x - u * 14, mid.y + u * 4), new Vector2(mid.x, mid.y - u * 4), w, c);
                    Line(new Vector2(mid.x + u * 14, mid.y + u * 4), new Vector2(mid.x, mid.y - u * 4), w, c);
                    break;

                case "done":       // 이중 체크
                    Line(new Vector2(rect.x + u * 6, mid.y + u * 2), new Vector2(rect.x + u * 15, mid.y + u * 11), w, c);
                    Line(new Vector2(rect.x + u * 15, mid.y + u * 11), new Vector2(rect.x + u * 30, mid.y - u * 10), w, c);
                    Line(new Vector2(rect.x + u * 22, mid.y + u * 2), new Vector2(rect.x + u * 28, mid.y + u * 8), w, c);
                    Line(new Vector2(rect.x + u * 28, mid.y + u * 8), new Vector2(rect.xMax - u * 4, mid.y - u * 14), w, c);
                    break;

                case "cannot":     // 사선 X
                    Line(new Vector2(rect.x + u * 10, rect.y + u * 10), new Vector2(rect.xMax - u * 10, rect.yMax - u * 10), u * 5, c);
                    Line(new Vector2(rect.xMax - u * 10, rect.y + u * 10), new Vector2(rect.x + u * 10, rect.yMax - u * 10), u * 5, c);
                    break;

                case "overHere":   // 하향 핀
                    Ellipse(new Rect(mid.x - u * 13, rect.y + u * 6, u * 26, u * 26), 0f, c, true);
                    Line(new Vector2(mid.x - u * 9, rect.y + u * 26), new Vector2(mid.x, rect.yMax - u * 6), u * 5, c);
                    Line(new Vector2(mid.x + u * 9, rect.y + u * 26), new Vector2(mid.x, rect.yMax - u * 6), u * 5, c);
                    break;

                case "hurry":      // 시계
                    Ellipse(new Rect(rect.x + u * 8, rect.y + u * 8, u * 32, u * 32), w, c);
                    Box(mid.x - w * 0.4f, mid.y - u * 11, w * 0.8f, u * 12, c);
                    Box(mid.x, mid.y - w * 0.4f, u * 9, w * 0.8f, c);
                    break;
            }
        }

        /// <summary>§7.8 표 — 8슬롯 방향(↑ ↗ → ↘ ↓ ↙ ← ↖)과 한글 이름</summary>
        public static readonly (string id, string label)[] Commands =
        {
            ("assemble", "집합"), ("wait", "대기"), ("allClear", "이상 없음"), ("needHelp", "지원 요청"),
            ("done", "완료"), ("cannot", "못 함"), ("overHere", "여기"), ("hurry", "서둘러"),
        };

        /// <summary>§7.8 색 — SFX 성격과 짝을 이룬다</summary>
        public static Color CommandColor(string id) => id switch
        {
            "assemble" => HudTheme.Accent,
            "wait" => HudTheme.Ink2,
            "allClear" => HudTheme.Accent,
            "needHelp" => HudTheme.Heat,
            "done" => HudTheme.Accent,
            "cannot" => HudTheme.Alert,
            "overHere" => HudTheme.Cold,
            _ => HudTheme.Heat,
        };

        /* ═══════════════════════════════════════════════ 시스템 아이콘 */

        public static void Check(Rect r, Color c)
        {
            var u = r.width / 24f;
            Line(new Vector2(r.x + u * 4, r.center.y), new Vector2(r.x + u * 10, r.yMax - u * 6), u * 3f, c);
            Line(new Vector2(r.x + u * 10, r.yMax - u * 6), new Vector2(r.xMax - u * 3, r.y + u * 5), u * 3f, c);
        }

        public static void Cross(Rect r, Color c)
        {
            var u = r.width / 24f;
            Line(new Vector2(r.x + u * 5, r.y + u * 5), new Vector2(r.xMax - u * 5, r.yMax - u * 5), u * 3.2f, c);
            Line(new Vector2(r.xMax - u * 5, r.y + u * 5), new Vector2(r.x + u * 5, r.yMax - u * 5), u * 3.2f, c);
        }

        /// <summary>빈 체크박스 — 수첩의 미완료 항목</summary>
        public static void EmptyBox(Rect r, Color c) => _theme.Border(r, c, 2f);

        public static void Dot(Rect r, Color c) => Ellipse(r, 0f, c, true);

        public static void Circle(Rect r, float weight, Color c) => Ellipse(r, weight, c);

        /// <summary>퀘스트 목표 마커 — 핀 + 원. 목업이 월드와 지도 양쪽에 쓰는 형태</summary>
        public static void QuestPin(Rect r, Color c)
        {
            var u = r.width / 24f;
            Ellipse(new Rect(r.x + u * 4, r.y, u * 16, u * 16), 0f, c, true);
            Line(new Vector2(r.x + u * 7, r.y + u * 13), new Vector2(r.center.x, r.yMax), u * 3f, c);
            Line(new Vector2(r.xMax - u * 7, r.y + u * 13), new Vector2(r.center.x, r.yMax), u * 3f, c);
        }
    }
}
