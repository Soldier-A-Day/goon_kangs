using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 일과 판의 테두리 — 제목 · 남은 시간 · 조작 안내 · 결과.
    ///
    /// 원형은 본문만 그리고 나머지는 전부 여기가 맡는다. 14종이 저마다 제
    /// 테두리를 그리면 같은 화면이 열네 가지가 되고, 플레이어는 판이 바뀔 때마다
    /// 남은 시간이 어디 있는지 다시 찾아야 한다.
    ///
    /// ── 왜 IMGUI인가 ─────────────────────────────────────────────────────
    /// HUD 전체가 `OnGUI` + `HudTheme`으로 1920×1080 기준 좌표에 그려져 있다
    /// (§7). 여기만 uGUI 캔버스를 세우면 게임 안에 UI 체계가 두 개가 되고, 톤도
    /// 폰트도 배율도 따로 논다. 목업의 종이·잉크·괘선을 그대로 쓴다.
    ///
    /// ── 왜 화면 가운데인가 ───────────────────────────────────────────────
    /// 예전 판은 프롬프트 자리(480×66)에 얹힌 띠였다. 게이지 하나면 그래도
    /// 됐지만, 대조 점검은 16줄을 읽어야 하고 정돈은 물건을 끌어다 놓아야 한다.
    /// 판이 열리는 동안은 걷지 못하므로(그 자리에서 하는 일이다) 가운데를 써도
    /// 월드를 가리는 대가가 없다.
    /// </summary>
    public static class HudMinigame
    {
        public const float PanelW = 1000f;
        public const float PanelH = 620f;

        public static Rect Panel => new Rect(
            (HudTheme.ViewWidth - PanelW) * 0.5f,
            (HudTheme.ViewHeight - PanelH) * 0.5f - 40f,
            PanelW, PanelH);

        private const float HeaderH = 56f;
        private const float TimeH = 8f;
        private const float FooterH = 72f;
        private const float Pad = 24f;

        /// <summary>원형이 본문을 그릴 자리. 입력 판정도 같은 사각형을 쓴다</summary>
        public static Rect Body
        {
            get
            {
                var p = Panel;
                return new Rect(p.x + Pad,
                                p.y + HeaderH + TimeH + Pad,
                                p.width - Pad * 2f,
                                p.height - HeaderH - TimeH - FooterH - Pad * 2f);
            }
        }

        public static void Draw(HudTheme theme, QuestPlay play)
        {
            var board = play.Board;
            if (board == null) return;

            // 감쇠·결과 애니메이션 시계 — `Tick`과 분리되어 있다. 판이 멈춘
            // (Cleared·Failed) 뒤에도 `QuestPlay`가 더 이상 `Tick`을 안 불러도
            // 여기서는 매 프레임 도니까 플래시가 얼어붙지 않는다.
            //
            // **`Repaint`에서만 흘린다.** `OnGUI`는 프레임당 Layout·Repaint·
            // 입력 이벤트로 여러 번 불린다(`RealProbe`·`HeapProbe`와 같은
            // 자리) — 게이트 없이 매번 `Time.deltaTime`을 더하면 마우스를
            // 움직일 때마다 감쇠가 빨라진다
            if (Event.current.type == EventType.Repaint) board.Present(Time.deltaTime);

            var panel = Panel;

            // 월드를 덮는다. 판이 열린 동안은 걷지 못하므로 시선을 한 곳에 모은다
            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight),
                       HudTheme.Dim, 0.66f);

            // **판 바탕은 HUD보다 한 단 밝다.**
            //
            // 처음에는 다른 패널과 같은 `Paper`를 썼는데, 전체 암전(66%) 위에
            // 올라가니 어두운 종이 위 어두운 종이가 되어 본문이 거의 검정이었다.
            // 판은 화면을 덮고 그 안에서 손을 움직이는 곳이라 여기만 올린다.
            theme.Fill(panel, HudTheme.Paper2);
            theme.Border(panel, HudTheme.Ink, 2f);
            theme.Header(panel, HeaderH);

            DrawHeader(theme, panel, play);
            DrawTime(theme, panel, board);

            board.Draw(theme, Body);
            DrawFlash(theme, Body, board);

            DrawFooter(theme, panel, board);
            DrawResult(theme, panel, play);
        }

        /// <summary>
        /// 성공/실수 색 플래시 — 본문 위에 겹쳐 그린다.
        ///
        /// `Board.Clear`/`Miss`/`Fail`이 베이스에서 `FlashT`를 채우므로, 이
        /// 한 곳만 그리면 14종 전체가 배선 없이 같은 반응을 받는다. 예전에
        /// `RhythmBoard` 등 3종만 원형 안에서 각자 그리던 것과 다른 지점이다.
        /// </summary>
        private static void DrawFlash(HudTheme theme, Rect body, Board board)
        {
            if (board.FlashT <= 0f) return;
            var color = board.FlashSuccess ? HudTheme.Accent : HudTheme.Alert;
            // 알파 0.35 → 0, `Board.Present`가 이미 지수 감쇠시킨 `FlashT`를 그대로 곱한다
            theme.Fill(body, color, 0.35f * board.FlashT);
        }

        private static void DrawHeader(HudTheme theme, Rect panel, QuestPlay play)
        {
            var board = play.Board;

            // 원형 이름표 — 이 판이 무엇인지 한 단어로
            var chip = new Rect(panel.x + 18f, panel.y + 15f, 92f, 26f);
            theme.Chip(chip, Boards.Name(board.Spec?.type), HudTheme.AccentW, HudTheme.Accent);

            GUI.Label(new Rect(chip.xMax + 16f, panel.y + 12f, 560f, 32f), play.Label,
                theme.At(theme.Title, 24, HudTheme.Ink));

            // 난이도 — 채운 점 개수. 숫자로 쓰면 무엇의 1인지 안 읽힌다
            var level = Mathf.Clamp(board.Spec?.difficulty ?? 1, 1, 3);
            GUI.Label(new Rect(panel.xMax - 214f, panel.y + 16f, 70f, 24f), "난이도",
                theme.At(theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleRight));
            for (var i = 0; i < 3; i += 1)
            {
                var dot = new Rect(panel.xMax - 134f + i * 18f, panel.y + 22f, 11f, 11f);
                theme.Fill(dot, i < level ? HudTheme.Heat : HudTheme.Rule2);
            }

            GUI.Label(new Rect(panel.xMax - 74f, panel.y + 16f, 56f, 24f), "[ESC]",
                theme.At(theme.Label, 12, HudTheme.Ink3, TextAnchor.MiddleRight));

            DrawStreak(theme, panel);
        }

        /// <summary>
        /// 연속 A 배지 — 스택이 있을 때만 뜬다.
        ///
        /// 조여진 판이라는 것이 화면에 안 읽히면 다음 판이 빡빡해진 게 배신처럼
        /// 느껴진다. 난이도 점 옆, 판 머리에서 바로 눈에 들어오는 자리에 얹는다.
        /// </summary>
        private static void DrawStreak(HudTheme theme, Rect panel)
        {
            if (GradeStreak.Stack <= 0) return;

            var percent = Mathf.RoundToInt((1f - GradeStreak.LimitScale) * 100f);
            var text = $"연속 A ×{GradeStreak.Stack} — 시간 −{percent}%";
            var style = theme.At(theme.Label, 12, HudTheme.Heat, TextAnchor.MiddleCenter);
            var width = theme.Measure(text, style) + 20f;
            var badge = new Rect(panel.xMax - 224f - width, panel.y + 15f, width, 26f);

            theme.Fill(badge, HudTheme.Heat, 0.16f);
            theme.Border(badge, HudTheme.Heat);
            GUI.Label(badge, text, style);
        }

        /// <summary>
        /// 남은 시간.
        ///
        /// **제한 시간은 일과의 소요 그대로다**(`workSeconds`). 판 안에 시간을
        /// 따로 두면 소요와 어긋나고, 그러면 일과표가 미니게임 실력으로 흔들린다.
        /// </summary>
        private static void DrawTime(HudTheme theme, Rect panel, Board board)
        {
            var rect = new Rect(panel.x, panel.y + HeaderH, panel.width, TimeH);
            theme.Fill(rect, HudTheme.Rule3);

            var left = board.TimeLeftRatio;
            var urgent = left <= 0.2f;
            if (urgent && !HudTheme.Pulse(1.5f)) return;

            theme.Fill(new Rect(rect.x, rect.y, rect.width * left, rect.height),
                       urgent ? HudTheme.Alert : HudTheme.Accent);
        }

        private static void DrawFooter(HudTheme theme, Rect panel, Board board)
        {
            var y = panel.yMax - FooterH;
            theme.Fill(new Rect(panel.x, y, panel.width, 1f), HudTheme.Rule);

            GUI.Label(new Rect(panel.x + 20f, y + 10f, panel.width - 240f, 26f),
                board.Instruction, theme.At(theme.Body, 17, HudTheme.Ink));

            GUI.Label(new Rect(panel.x + 20f, y + 38f, panel.width - 240f, 22f),
                board.Status, theme.At(theme.Small, 14, HudTheme.Ink3));

            // 남은 초 — 숫자가 흔들리지 않게 고정폭으로
            GUI.Label(new Rect(panel.xMax - 200f, y + 14f, 180f, 42f),
                $"{Mathf.CeilToInt(board.Remaining)}초",
                theme.At(theme.MonoBig, 30,
                    board.TimeLeftRatio <= 0.2f ? HudTheme.Alert : HudTheme.Ink,
                    TextAnchor.MiddleRight));
        }

        /// <summary>
        /// 통과 · 재시도.
        ///
        /// 통과한 뒤에도 판이 잠깐 남는 이유는 **서버가 최소 진척을 보기**
        /// 때문이다(`clearQuest`). 소요의 절반도 안 지나 통과를 외치면 서버가
        /// 무시하므로, 그 문턱을 넘을 때까지 여기서 기다린다 — 설계안이 말한
        /// "일찍 끝내도 시간은 그대로 흐른다"가 여기서 그대로 성립한다.
        /// </summary>
        /// <summary>눌렸으면 참. 마우스가 위에 있으면 밝아진다</summary>
        private static bool Button(HudTheme theme, Rect rect, string label, Color accent)
        {
            var hot = rect.Contains(BoardInput.Read().Mouse);
            theme.Fill(rect, hot ? HudTheme.AccentW : HudTheme.Paper3);
            theme.Border(rect, hot ? accent : HudTheme.Rule, 2f);
            GUI.Label(rect, label,
                theme.At(theme.Body, 17, hot ? accent : HudTheme.Ink, TextAnchor.MiddleCenter));
            var clicked = hot && Input.GetMouseButtonDown(0);
            // 확정 = 관인을 찍는 소리. 저장소에서 실제로 눌리는 첫 버튼이다
            if (clicked) Sfx.Play("tap");
            return clicked;
        }

        /// <summary>등장 스케일 팝. 150ms, 1.15 → 1.0</summary>
        private const float PopDuration = 0.15f;

        /// <summary>실수 패널 흔들림 — 3프레임(≈50ms) 진폭 4px, 지수 감쇠</summary>
        private const float ShakeWindow = 0.05f;
        private const float ShakeTau = 0.018f;
        private const float ShakeHz = 30f;
        private const float ShakeAmp = 4f;

        private static void DrawResult(HudTheme theme, Rect panel, QuestPlay play)
        {
            var board = play.Board;
            if (board.State == BoardState.Running) return;

            var cleared = board.State == BoardState.Cleared;
            var box = new Rect(panel.center.x - 200f, panel.center.y - 74f, 400f, 148f);

            // **실수 패널 흔들림.** 월드 카메라가 아니라 패널 Rect 오프셋이다 —
            // IMGUI라 카메라를 흔들어도 화면이 안 흔들린다. 접근성 `screenShake`
            // 토글이 꺼져 있으면 흔들지 않는다. 성공에는 흔들림이 없다(팝만).
            if (!cleared && Accessibility.ScreenShake && board.ResultAge < ShakeWindow)
            {
                var decay = Mathf.Exp(-board.ResultAge / ShakeTau);
                box.x += Mathf.Sin(board.ResultAge * ShakeHz * Mathf.PI * 2f) * ShakeAmp * decay;
            }

            // **등장 스케일 팝.** `PlaceBoard`가 `GUIUtility.RotateAroundPivot`로
            // 각을 그리는 것과 같은 자리 — 여기서는 돌리는 대신 늘렸다 줄인다.
            var matrix = GUI.matrix;
            var popT = Mathf.Clamp01(board.ResultAge / PopDuration);
            var eased = 1f - (1f - popT) * (1f - popT) * (1f - popT);
            var scale = Mathf.Lerp(1.15f, 1f, eased);
            HudTheme.ScaleAt(scale, box.center);

            theme.Fill(box, cleared ? HudTheme.AccentW : HudTheme.AlertW, 0.96f);
            theme.Border(box, cleared ? HudTheme.Accent : HudTheme.Alert, 2f);

            if (cleared)
            {
                // **시간초과 통과는 일반 통과와 문구가 갈린다.** 다 끝낸 것과 같은
                // 말로 축하하면 시간이 다 돼서 넘어간 것이 완주처럼 읽힌다 —
                // 여기서는 "인정"이라고만 말한다(`Board.GradesOnTimeout`).
                GUI.Label(new Rect(box.x, box.y + 16f, box.width, 34f),
                    board.TimedOut ? "시간 종료 — 한 만큼 인정" : "통과",
                    theme.At(theme.Heading, 22, HudTheme.Accent, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(box.x, box.y + 48f, box.width, 56f), board.Grade(),
                    theme.At(theme.Display, 52, HudTheme.Ink, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(box.x, box.yMax - 34f, box.width, 24f),
                    play.Settling ? "일과를 마무리하는 중" : "복무 점수에 반영된다",
                    theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleCenter));
                GUI.matrix = matrix;
                return;
            }

            GUI.Label(new Rect(box.x, box.y + 18f, box.width, 32f), "못 끝냈다",
                theme.At(theme.Heading, 22, HudTheme.Alert, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(box.x, box.y + 50f, box.width, 24f),
                // 시간초과인데 Fill이 절반도 안 됐으면 사유를 짚어준다 — 안 그러면
                // "왜 시간이 남았는데도 졌지"로 읽힌다(§과업1, Fill < 0.5)
                board.TimedOut ? "절반은 채워야 인정된다 — 잃은 것은 시간이다"
                               : "다시 할 수 있다 — 잃은 것은 시간이다",
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));

            // **버튼은 마우스로 받는다.**
            //
            // 원형 열넷 중 일곱이 마우스 조작이라 실패하는 순간 손이 마우스에
            // 있다. 거기서 키보드로 옮기라고 하면 재시도가 번거로워지고,
            // 글자로만 적힌 `[E]`는 눌러야 할 것으로 읽히지도 않는다.
            play.Retry = Button(theme, new Rect(box.center.x - 152f, box.yMax - 58f, 144f, 44f),
                "다시  [E]", HudTheme.Accent);
            play.Quit = Button(theme, new Rect(box.center.x + 8f, box.yMax - 58f, 144f, 44f),
                "그만  [ESC]", HudTheme.Rule);

            GUI.matrix = matrix;
        }
    }
}
