using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// UI 톤 (SAD-ART-001 §3.1 · §4.1).
    ///
    /// 컨셉 한 줄이 **"군용 등사판 서류 위에 놓인 저채도 픽셀 병영"**이다.
    /// 기획서 문서 자체의 CSS 변수를 게임 UI로 승격시킨 것이라, 색 이름이
    /// `paper` `ink` `rule`인 것은 비유가 아니라 그대로 종이·잉크·괘선이다.
    ///
    /// 값은 §4.1 표와 확정 목업(`files-3/`)에서 그대로 가져왔다. §14 미결정 3의
    /// 권고("야간 시간대가 많으므로 다크 기본")를 따라 다크를 기본으로 둔다.
    ///
    /// 월드는 픽셀아트지만 **UI는 네이티브 해상도**다(§2.1). 두 레이어를 섞지
    /// 않는 것이 §3.2 3원칙의 세 번째다 — 그래서 여기서는 픽셀 스냅을 하지 않고
    /// 1920×1080 기준 좌표를 그대로 쓴다.
    /// </summary>
    public sealed class HudTheme
    {
        /* ─────────────────────────────────────────────── §4.1 팔레트 (다크) */

        /// <summary>
        /// 면 — 패널 바탕 · 머리띠 · 얹힌 칸.
        ///
        /// **목업 값(#1B1F17 · #191D14)보다 올렸다.** 구분선에 했던 것과 같은
        /// 이유다. 목업대로 두면 얹힌 칸이 패널 바탕과 명도차 1.08로, 요약 칸이나
        /// 시간대 카드가 "칠해져 있다"가 아니라 "테두리만 있다"로 보인다 —
        /// 실제로 일과표의 오늘의 필수·합동·보직 칸이 통째로 안 칠해져 보였다.
        ///
        /// 눈이 아니라 명도 대비로 잡았다. 바탕(`Paper`) 기준으로
        ///
        ///   `Paper2`  1.10 — 창 머리띠. 목업 값 그대로다
        ///   `Paper3`  1.22 — 얹힌 칸. 칠해져 있다는 것이 보여야 한다
        ///
        /// **한동안 이 값을 계속 올렸는데, 원인은 값이 아니라 `Lin`이었다.**
        /// 면에 감마가 두 번 걸려 화면에서는 `#12150F`가 `#020201`로, 얹힌 칸이
        /// `#070A04`로 나오고 있었다 — 대비는 계산대로였지만 둘 다 검정이었다.
        /// 그걸 고친 뒤로는 목업에 가까운 값으로 되돌려도 칸이 보인다.
        ///
        /// 그래도 목업의 `#191D14`(1.08)보다는 한 단계 위다. 1.08은 계산으로도
        /// 안 보이는 값이라, 채움만으로는 부족해 **칸에 괘선을 같이 두른다**(`Rule2`).
        ///
        /// 색상 계열(올리브)은 목업 그대로 두고 명도만 올렸다.
        /// </summary>
        public static readonly Color Paper = Hex("12150F");
        public static readonly Color Paper2 = Hex("1B1F17");
        public static readonly Color Paper3 = Hex("232819");
        public static readonly Color Ink = Hex("DFE2D6");
        public static readonly Color Ink2 = Hex("BCC2B1");
        /// <summary>
        /// 설명·보조 텍스트.
        ///
        /// **하얀 쪽으로 올렸다**(4.1 → 7.5). "가장 흐린 글자"라는 이유로 어둡게
        /// 두면, 정작 화면에서 규칙을 알려주는 문장(칸 안내·범례·각주)이 전부
        /// 그 색이라 읽히지 않는다. 위계는 크기가 맡는다 — 이 색은 14px 이하에서만 쓴다.
        /// </summary>
        public static readonly Color Ink3 = Hex("D4D8CB");
        /// <summary>
        /// 구분선. 목업 값(#333A2B · #262C1F)보다 한 단계 밝다.
        ///
        /// 1px 선은 면보다 훨씬 잘 사라진다 — 지도의 방 경계도, 수첩의 칸 나눔도,
        /// 막대의 빈 부분도 목업 값으로는 안 보였다. `Paper3`(#232819) 기준으로
        ///
        ///   `Rule`   2.7 — 창을 가르는 주 구분선
        ///   `Rule2`  1.9 — 막대 트랙 · 칸 테두리 · 보조 칸막이
        ///
        /// 한때 4.5 / 3.5까지 올려뒀는데, 그건 면에 감마가 두 번 걸려 배경이
        /// 통째로 검정이던 시절의 값이다(`Lin` 참조). 그걸 고친 뒤로는 선만
        /// 혼자 형광으로 떠서, 계산 기준을 실제 배경인 `Paper3`로 옮겨 다시 잡았다.
        /// </summary>
        public static readonly Color Rule = Hex("5E6B4E");
        public static readonly Color Rule2 = Hex("4C5740");
        // **작은 글씨는 크기와 대비를 같이 올렸다.**
        //
        // 위계를 색으로 세우려다 보조 문구가 배경에 묻혔다 — 화면에서 규칙을
        // 알려주는 문장(칸 안내 · 범례 · 각주)이 전부 그 색이라, 읽히지 않으면
        // 그 문장들이 없는 것과 같다. 위계는 크기가 맡고 색은 읽히는 선을 지킨다.

        /// <summary>가장 옅은 괘선 — 이미 칠해진 칸의 테두리. 칸 경계는 채움이 말한다</summary>
        public static readonly Color Rule3 = Hex("373F2E");
        public static readonly Color Accent = Hex("9AB06F");
        public static readonly Color AccentW = Hex("28311D");
        public static readonly Color Cold = Hex("7FAECF");
        public static readonly Color Heat = Hex("DB8B55");
        public static readonly Color Alert = Hex("D2594B");
        /// <summary>경고 배경. 목업이 실패 줄·필수 배지에 일관되게 쓰는 색</summary>
        public static readonly Color AlertW = Hex("2F1A17");
        /// <summary>
        /// 선택 일과의 색 — **무채색이다.**
        ///
        /// 예전에는 `Ink2`(#99A18D)를 썼는데, 색상각 84°로 합동의 `Accent`(80°)와
        /// 사실상 같은 색이었다. 명도도 5.3 대 6.0으로 붙어 있어서, 일과표 칸에
        /// 둘이 나란히 서면 어느 것이 혼자 못 하는 일인지 구분되지 않았다.
        ///
        /// 채도를 0으로 빼면 올리브 계열과 절대 안 헷갈린다(대비 1.1).
        ///
        /// 명도는 **거의 흰색**이다. 처음에는 4.4로 낮춰 "안 해도 되는 것"이라는
        /// 위계를 세웠는데, 그렇게 하면 어두운 칸에서 선택 일과가 배경에 묻혔다.
        /// 위계는 색이 아니라 **굵기**가 맡는다 — 필수·합동·하달만 굵게 그린다.
        /// </summary>
        public static readonly Color Optional = Hex("EAEAEA");
        public static readonly Color Admin = Hex("B08A2C");
        public static readonly Color White = Hex("EEF1E8");

        /// <summary>암전 바탕. 하달 창 70%, 수첩 65~68% — 목업 실측</summary>
        public static readonly Color Dim = Hex("0B0D08");

        /* ────────────────────────────────────────── §7 디자인 기준 해상도 */

        /// <summary>§7 "전 좌표는 1920 × 1080 기준. 안전 영역 마진 48px"</summary>
        public const float DesignWidth = 1920f;
        public const float DesignHeight = 1080f;
        public const float SafeMargin = 48f;

        /// <summary>
        /// 지금 화면의 **논리 크기**. 매 프레임 `Hud`가 채운다.
        ///
        /// 목업이 1920×1080이라 좌표를 거기 맞춰 잡았는데, 그러면 21:9 같은
        /// 넓은 화면에서 오른쪽 640px이 통째로 빈다 — 미니맵과 수첩이 화면
        /// 가장자리에 안 붙고 한가운데 어중간하게 뜬다.
        ///
        /// 배율은 **짧은 축에 맞춘 균일 배율**을 그대로 쓴다(글자 크기가
        /// 화면비에 따라 달라지면 안 된다). 대신 좌표계가 화면만큼 넓어지고,
        /// 가장자리에 붙는 것들은 여기서 재는 값을 쓴다.
        /// </summary>
        public static float ViewWidth { get; private set; } = DesignWidth;
        public static float ViewHeight { get; private set; } = DesignHeight;

        /// <summary>화면 → 논리 좌표 배율. 짧은 축 기준이라 글자 크기가 일정하다</summary>
        public static float ViewScale { get; private set; } = 1f;

        /// <summary>`OnGUI` 첫머리에서 한 번 부른다</summary>
        public static float Fit()
        {
            ViewScale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            if (ViewScale <= 0f) ViewScale = 1f;
            ViewWidth = Screen.width / ViewScale;
            ViewHeight = Screen.height / ViewScale;
            return ViewScale;
        }

        /// <summary>오른쪽에서 잰 x — 목업의 1920 기준 좌표를 그대로 넘기면 된다</summary>
        public static float RightOf(float designX, float width) =>
            ViewWidth - (DesignWidth - designX - width) - width;

        /// <summary>아래에서 잰 y</summary>
        public static float BottomOf(float designY, float height) =>
            ViewHeight - (DesignHeight - designY - height) - height;

        /* ─────────────────────────────────────────────────────── 스타일 */

        public readonly GUIStyle Display;   // 44~56 — 카운트다운 · 남은 필수
        public readonly GUIStyle Title;     // 26 — 창 제목
        public readonly GUIStyle Heading;   // 19~22 — 항목 제목
        public readonly GUIStyle Body;      // 17 — 본문
        public readonly GUIStyle Small;     // 16 — 보조
        public readonly GUIStyle Label;     // 13 mono — 코드번호 · 섹션 라벨
        public readonly GUIStyle Mono;      // 숫자 — 카운트다운이 흔들리지 않아야 한다
        public readonly GUIStyle MonoBig;

        private readonly Dictionary<Color, Texture2D> _solids = new Dictionary<Color, Texture2D>();
        private Texture2D _hatch;

        public HudTheme(Font body, Font mono)
        {
            // §11 타이포그래피. 폰트가 없으면 Unity 기본으로 떨어지되 **레이아웃은
            // 유지된다** — 한글 폰트가 빠졌다고 화면이 무너지면 개발이 막힌다.
            Display = Make(mono ?? body, 44, FontStyle.Bold, Ink);
            Title = Make(body, 26, FontStyle.Bold, Ink);
            Heading = Make(body, 19, FontStyle.Bold, Ink);
            Body = Make(body, 17, FontStyle.Normal, Ink);
            Small = Make(body, 16, FontStyle.Normal, Ink3);
            Label = Make(mono ?? body, 13, FontStyle.Bold, Ink3);
            Mono = Make(mono ?? body, 17, FontStyle.Normal, Ink);
            MonoBig = Make(mono ?? body, 26, FontStyle.Bold, Ink);
        }

        /// <summary>
        /// 선형 색공간 보정 — **글자 색에만 쓴다.**
        ///
        /// 프로젝트가 Linear로 렌더링하는데(WebGL 설정 · §README) IMGUI는 글자 색을
        /// 감마 값 그대로 흘려보낸다. 미리 선형으로 낮춰 넣어야 최종 출력에서
        /// §4.1 표의 값이 나온다.
        ///
        /// **면(`Solid`)에는 쓰지 않는다.** 텍스처는 sRGB로 저장되어 샘플링할 때
        /// Unity가 알아서 선형으로 바꾼다. 거기에 이걸 또 걸면 감마가 **두 번**
        /// 적용되어 색이 통째로 가라앉는다 — 브라우저 캔버스에서 픽셀을 재보니
        /// 패널 바탕이 `#12150F`가 아니라 `#020201`, 얹힌 칸이 `#2F3724`가 아니라
        /// `#070A04`로 나왔다. 명도 대비는 계산대로 1.49인데 둘 다 검정에 붙어
        /// 있어서 사람 눈에는 같은 검정이다.
        ///
        /// 창의 구분선·칸 배경이 계속 안 보여서 목업보다 밝게 올려왔던 것이
        /// 전부 이 한 줄 때문이었다. 값이 아니라 여기가 원인이었다.
        /// </summary>
        public static Color Lin(Color color) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear
                ? new Color(Mathf.GammaToLinearSpace(color.r), Mathf.GammaToLinearSpace(color.g),
                            Mathf.GammaToLinearSpace(color.b), color.a)
                : color;

        private static GUIStyle Make(Font font, int size, FontStyle style, Color color)
        {
            var s = new GUIStyle
            {
                font = font,
                fontSize = size,
                fontStyle = style,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false,
                clipping = TextClipping.Clip,
                richText = false,
            };
            s.normal.textColor = Lin(color);
            return s;
        }

        /// <summary>
        /// 스타일을 색·정렬만 바꿔 잠깐 빌려 쓴다.
        ///
        /// 스타일을 매번 새로 만들면 OnGUI가 프레임마다 할당을 하고, 그건 M0에서
        /// 확인한 "100분 힙 누수 0"을 조용히 깨뜨린다.
        /// </summary>
        public GUIStyle As(GUIStyle style, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            style.normal.textColor = Lin(color);
            style.alignment = align;
            return style;
        }

        /// <summary>크기까지 바꿔 쓴다. 목업이 같은 계열에서 크기만 달리 쓰는 곳이 많다</summary>
        public GUIStyle At(GUIStyle style, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            style.fontSize = size;
            return As(style, color, align);
        }

        /* ─────────────────────────────────────────────────────── 그리기 */

        public Texture2D Solid(Color color)
        {
            if (_solids.TryGetValue(color, out var tex) && tex != null) return tex;
            tex = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            // 감마 보정을 걸지 않는다 — sRGB 텍스처라 Unity가 이미 한다(`Lin` 주석)
            tex.SetPixel(0, 0, color);
            tex.Apply();
            _solids[color] = tex;
            return tex;
        }

        public void Fill(Rect rect, Color color) => GUI.DrawTexture(rect, Solid(color));

        public void Fill(Rect rect, Color color, float alpha)
        {
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, Solid(color));
            GUI.color = previous;
        }

        /// <summary>1px 괘선 테두리. 목업의 모든 패널이 이 형태다 — 둥근 모서리 없음</summary>
        public void Border(Rect rect, Color color, float weight = 1f)
        {
            Fill(new Rect(rect.x, rect.y, rect.width, weight), color);
            Fill(new Rect(rect.x, rect.yMax - weight, rect.width, weight), color);
            Fill(new Rect(rect.x, rect.y, weight, rect.height), color);
            Fill(new Rect(rect.xMax - weight, rect.y, weight, rect.height), color);
        }

        public void Panel(Rect rect, Color? fill = null, Color? border = null, float weight = 1f)
        {
            Fill(rect, fill ?? Paper);
            Border(rect, border ?? Rule, weight);
        }

        /// <summary>패널 헤더 — 다른 바탕 띠 + 아래 잉크색 2px. 목업의 모든 창이 이 구조다</summary>
        public void Header(Rect panel, float height)
        {
            Fill(new Rect(panel.x, panel.y, panel.width, height), Paper2);
            Fill(new Rect(panel.x, panel.y + height - 2f, panel.width, 2f), Ink);
        }

        public void Bar(Rect rect, float ratio, Color fill, Color? track = null)
        {
            Fill(rect, track ?? Rule2);
            Fill(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(ratio), rect.height), fill);
            Border(rect, Rule);
        }

        /// <summary>좌측 색 바 — 알림 카드·일과 카드의 종류 표시. 목업 공통 어휘</summary>
        public void Spine(Rect rect, Color color, float weight = 4f) =>
            Fill(new Rect(rect.x, rect.y, weight, rect.height), color);

        /// <summary>
        /// 사선 해칭.
        ///
        /// §7.1.1이 하달 창 동안 진행 바 위에 "`일시정지` 라벨 + 사선 해칭"을
        /// 요구한다. 타이머가 멈춘 것을 **바가 멈춘 것으로만** 보여주면
        /// 접속이 끊긴 것과 구분되지 않는다.
        /// </summary>
        public void Hatch(Rect rect, Color color)
        {
            if (_hatch == null)
            {
                const int n = 8;
                _hatch = new Texture2D(n, n)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Point,
                };
                for (var y = 0; y < n; y += 1)
                for (var x = 0; x < n; x += 1)
                    _hatch.SetPixel(x, y, (x + y) % n < 3 ? Color.white : Color.clear);
                _hatch.Apply();
            }

            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTextureWithTexCoords(rect, _hatch,
                new Rect(0f, 0f, rect.width / 8f, rect.height / 8f));
            GUI.color = previous;
        }

        /// <summary>키캡 — `[E]` `[TAB]`. 목업 §7.1.5의 상호작용 프롬프트 형태</summary>
        public void KeyCap(Rect rect, string key)
        {
            Fill(rect, AccentW);
            Border(rect, Accent);
            GUI.Label(rect, key, As(Mono, Accent, TextAnchor.MiddleCenter));
            As(Mono, Ink);
        }

        /// <summary>배지 — 보직 3글자, 필수/선택 라벨</summary>
        public void Chip(Rect rect, string text, Color background, Color foreground)
        {
            Fill(rect, background);
            GUI.Label(rect, text, As(Label, foreground, TextAnchor.MiddleCenter));
            As(Label, Ink2);
        }

        public float Measure(string text, GUIStyle style) =>
            string.IsNullOrEmpty(text) ? 0f : style.CalcSize(new GUIContent(text)).x;

        /// <summary>1Hz·1.5Hz 점멸. 목업이 잔여 20% 이하와 위험 링에 요구한다</summary>
        public static bool Pulse(float hz) => Mathf.Repeat(Time.unscaledTime * hz, 1f) < 0.5f;

        /* ──────────────────────────────────────────────── 밴드 · 보직 색 */

        /// <summary>§4.3 온도 밴드 6종의 강조색</summary>
        public static Color BandColor(string band) => band switch
        {
            "extremeCold" => Cold,
            "cold" => Cold,
            "warm" => Heat,
            "hot" => Heat,
            "extremeHot" => Heat,
            _ => Accent,
        };

        public static string BandLabel(string band) => band switch
        {
            "extremeCold" => "극혹한",
            "cold" => "한랭",
            "warm" => "온난",
            "hot" => "혹서",
            "extremeHot" => "극혹서",
            _ => "평시",
        };

        /// <summary>§5.3 보직 색 — 완장 색과 같은 값이라 월드와 UI가 일치한다</summary>
        public static Color RoleColor(string role) => role switch
        {
            "comms" => Cold,
            "medic" => White,
            "admin" => Admin,
            _ => Hex("6E7A50"),
        };

        public static string RoleTag(string role) => role switch
        {
            "comms" => "COM",
            "medic" => "MED",
            "admin" => "ADM",
            _ => "RFL",
        };

        public static string RoleName(string role) => role switch
        {
            "comms" => "통신",
            "medic" => "의무",
            "admin" => "행정",
            _ => "소총",
        };

        public static string RankName(string rank) => rank switch
        {
            "pfc" => "일병",
            "corporal" => "상병",
            "sergeant" => "병장",
            _ => "이병",
        };

        public static string PhaseLabel(string phase) => phase switch
        {
            "reveille" => "기상 · 점검",
            "morning" => "오전 일과",
            "lunch" => "중식 · 휴식",
            "afternoon" => "오후 일과",
            "personal" => "석식 · 개인정비",
            "rollcall" => "점호 · 판정",
            _ => phase,
        };

        public static Color Hex(string rgb)
        {
            var v = System.Convert.ToInt32(rgb, 16);
            return new Color(((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
        }
    }
}
