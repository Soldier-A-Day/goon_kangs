using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// HUD 시각 체계.
    ///
    /// Unity 기본 GUI 스킨은 베벨 테두리와 회색 버튼이라 2005년처럼 보인다.
    /// 그걸 손보는 게 아니라 **통째로 걷어내고** 색·간격·글자 크기를 여기서 정한다.
    ///
    /// 이미지 파일을 쓰지 않는다. 예산(§10 UI 4MB)에 얹히고, 색 하나 바꾸는 데
    /// 이미지 편집이 필요해진다. 면은 1×1 흰 텍스처 하나로 그리고 모서리는
    /// Unity가 깎아주므로, 반지름도 색도 숫자로 바꾸면 끝난다.
    ///
    /// 온도 밴드 색이 여기 있는 이유: 5.0의 6밴드가 온도 체감의 전부이고(ARCH-01),
    /// HUD는 그걸 **말 안 해줘도 알아채게** 만드는 첫 번째 장치다. 숫자보다 색이 먼저 읽힌다.
    /// </summary>
    public sealed class HudTheme
    {
        /* ------------------------------------------------------------- 색 */

        /// <summary>
        /// 패널 바탕.
        ///
        /// 처음에 알파를 0.86으로 뒀더니 **흰 담장 위에서 글자가 묻혔다.** 0.94로
        /// 올려도 남았는데, 패널을 진단색으로 칠해보니 형상은 정상이었다 —
        /// 6% 투과가 거의 흰 지오메트리 위에서는 또렷하게 보이는 것이 원인이었다.
        ///
        /// 이 게임의 배경은 눈 덮인 연병장부터 어두운 생활관까지 밝기가 크게
        /// 흔들린다. **비침보다 가독성이 먼저다.**
        /// </summary>
        public static readonly Color Surface = new Color(0.055f, 0.063f, 0.078f, 0.975f);
        public static readonly Color SurfaceRaised = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color Divider = new Color(1f, 1f, 1f, 0.09f);

        public static readonly Color TextPrimary = new Color(0.91f, 0.92f, 0.93f);
        public static readonly Color TextSecondary = new Color(0.60f, 0.63f, 0.67f);
        public static readonly Color TextMuted = new Color(0.42f, 0.45f, 0.49f);

        public static readonly Color Accent = new Color(0.29f, 0.87f, 0.50f);
        public static readonly Color Warn = new Color(0.98f, 0.75f, 0.14f);
        public static readonly Color Danger = new Color(0.97f, 0.44f, 0.44f);

        /// <summary>온도 6밴드 (5.0). 극한으로 갈수록 채도가 오른다</summary>
        public static Color BandColor(string band) => band switch
        {
            "extremeCold" => new Color(0.45f, 0.68f, 1.00f),
            "cold" => new Color(0.55f, 0.78f, 0.96f),
            "mild" => new Color(0.65f, 0.72f, 0.78f),
            "warm" => new Color(0.97f, 0.79f, 0.45f),
            "hot" => new Color(0.98f, 0.62f, 0.35f),
            "extremeHot" => new Color(0.97f, 0.42f, 0.35f),
            _ => TextSecondary,
        };

        /* -------------------------------------------------------- 텍스처 */

        /// <summary>
        /// 1×1 흰색. 모든 면을 이걸로 그린다.
        ///
        /// 처음에는 둥근 모서리를 9-슬라이스 텍스처로 만들었는데, `GUI.DrawTexture`의
        /// 인자를 잘못 짚어 **패널이 흰 타원으로** 늘어났다. `borderWidths`는 테두리를
        /// 그리는 값이지 9-슬라이스가 아니고, 마지막 `cornerRadii`가 모서리를 둥글게
        /// 하는 값이다. 즉 Unity가 이미 해주는 일을 텍스처로 흉내내고 있었다.
        /// </summary>
        public Texture2D Flat { get; }

        /* --------------------------------------------------------- 스타일 */

        public GUIStyle Display { get; }   // 34 — 일차
        public GUIStyle Title { get; }     // 18 — 시간대
        public GUIStyle Body { get; }      // 14 — 본문
        public GUIStyle Meta { get; }      // 11 — 부가정보
        public GUIStyle Label { get; }     // 11 — 섹션 제목 (대문자 느낌)
        public GUIStyle ChipText { get; }

        public HudTheme(Font font)
        {
            Flat = Solid(Color.white);

            GUIStyle Text(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
            {
                var style = new GUIStyle
                {
                    font = font,
                    fontSize = size,
                    alignment = anchor,
                    wordWrap = false,
                    clipping = TextClipping.Clip,
                };
                style.normal.textColor = color;
                return style;
            }

            Display = Text(34, TextPrimary);
            Title = Text(18, TextPrimary);
            Body = Text(14, TextPrimary);
            Meta = Text(11, TextSecondary);
            Label = Text(11, TextSecondary);
            ChipText = Text(12, TextPrimary, TextAnchor.MiddleCenter);
        }

        private static Texture2D Solid(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        /* ----------------------------------------------------------- 그리기 */

        public void DrawPanel(Rect rect, Color? tint = null) =>
            DrawRounded(rect, 12f, tint ?? Surface);

        /// <summary>
        /// 둥근 사각형. Unity가 모서리를 직접 깎아준다.
        ///
        /// 색은 **인자로** 넘겨야 한다 — 이 오버로드는 `GUI.color`를 무시하고
        /// 여기 넘긴 색을 쓴다. GUI.color만 세팅했더니 전부 흰색으로 나왔다.
        /// </summary>
        public void DrawRounded(Rect rect, float radius, Color color)
        {
            GUI.DrawTexture(rect, Flat, ScaleMode.StretchToFill, true, 0f,
                color, Vector4.zero, new Vector4(radius, radius, radius, radius));
        }

        public void DrawFlat(Rect rect, Color color) => DrawRounded(rect, 0f, color);

        /// <summary>진행 바. 숫자보다 길이가 먼저 읽힌다</summary>
        public void DrawProgress(Rect rect, float value, Color fill)
        {
            var radius = rect.height * 0.5f;
            DrawRounded(rect, radius, new Color(1f, 1f, 1f, 0.10f));
            if (value <= 0f) return;

            var filled = new Rect(rect.x, rect.y,
                Mathf.Max(rect.height, rect.width * Mathf.Clamp01(value)), rect.height);
            DrawRounded(filled, radius, fill);
        }
    }
}
