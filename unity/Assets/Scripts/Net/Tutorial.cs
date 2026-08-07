using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 조작 키 전체 안내 — H2 발주.
    ///
    /// **8단계 온보딩 카드는 여기 없다.** "언제 무엇을 하는가"는 이제 로비
    /// (Next.js `/tutorial`)가 게임에 들어가기 전에 하나하나 넘겨 보여준다 —
    /// 게임 안에서 따라다니며 뜨는 카드가 불편하다는 지적(F1 회고)에 따라
    /// 전면 교체했다. 이 파일에 남은 것은 **"지금 이 키가 뭐였지"를 언제든
    /// 확인하는 전체 키 목록 창** 하나뿐이다.
    ///
    /// ── 여는 키 ──────────────────────────────────────────────────────────
    /// `F1` → `1`로 바꿨다(사용자 지시: "실제 게임에서 조작키 창 뜨는 건 1번
    /// 키 눌렀을 때"). `1`은 이미 `Q(홀드)+1~8` 퀵 커맨드(`HudScreens.cs:130-136`)와
    /// `C(홀드)+1~8` 정형 문구(`LocalPlayer.cs:114-124`)가 쓰고 있어, **Q나 C를
    /// 누르고 있지 않을 때만** `1`을 이 창의 여닫이로 받는다(`Update()` 참고).
    /// 미니게임 판(`SeqBoard` 등)의 숫자키는 판이 열려 있는 동안 `Hud`가 넘기는
    /// `busy`(`play.QuestId != null`)로 걸러진다 — 판이 열려 있으면 이 창이
    /// 새로 열리지 않는다.
    ///
    /// ── 겹치지 않는다 ────────────────────────────────────────────────────
    /// `Hud`가 이미 계산해 둔 "다른 전체화면이 떠 있다"(하달 창·마감 큐·
    /// 미니게임·통합 수첩 등)를 `busy`로 받아 그 동안은 그리지 않는다.
    ///
    /// ── 저장 없음 ────────────────────────────────────────────────────────
    /// 예전에는 8단계 완료 여부를 `PlayerPrefs`에 저장하고, 로비 설정의
    /// "튜토리얼 다시 보기"가 그 값을 초기화하는 `window.__sadSettings` 신호
    /// 통로를 썼다. 8단계 카드가 사라지면서 둘 다 불필요해져 지웠다 — 로비
    /// 튜토리얼(`/tutorial`)은 언제나 다시 볼 수 있어 저장할 "진행 상태"가
    /// 없다. 이 창(전체 키 안내)도 상태 없이 매번 같은 내용을 그릴 뿐이다.
    /// </summary>
    public sealed class Tutorial : MonoBehaviour
    {
        // `BaseScene.cs`(소유 파일 아님)가 씬 생성 시 세 필드를 그대로 물린다
        // (`net.AddComponent<Tutorial>()` 이후 `tutorial.client = client;` 등).
        // 8단계 카드 판정에 쓰던 필드였지만, 그 판정 자체가 없어져 이 파일
        // 안에서는 더 이상 읽지 않는다 — BaseScene.cs를 고치지 않고 컴파일을
        // 유지하기 위해 필드만 남겨 둔다(이 발주의 소유 파일이 아니다).
        public GameClient client;
        public Interactor interactor;
        public QuestPlay play;

        /// <summary>전체 키 안내 창이 열려 있는가. 완료 여부·요일과 무관하게
        /// 언제든 열고 닫을 수 있다</summary>
        private bool _keyGuideOpen;

        /// <summary>지난 프레임 `Draw()`가 받은 `busy`. `1`은 `Update()`에서
        /// 처리하는데 `busy`는 `Draw()`(OnGUI)만 알아서, 한 프레임 지연된 값으로
        /// "다른 전체화면이 떠 있으면 물러난다" 규칙을 지킨다 — 안내창 하나
        /// 여닫는 데 1프레임 지연은 티가 나지 않는다</summary>
        private bool _lastBusy;

        /* ══════════════════════════════════════════════════ 입력 */

        private void Update()
        {
            // `1`은 Q(홀드)+1~8 퀵 커맨드·C(홀드)+1~8 정형 문구가 이미 쓰는
            // 숫자다. 둘 중 하나라도 누르고 있으면 이 창의 여닫이로 받지
            // 않는다 — 그래야 전술 지시·정형 문구 1번을 누를 때 이 창이
            // 끼어들지 않는다. 미니게임 숫자키(`SeqBoard` 등)는 판이 열린
            // 동안 `busy`(`Hud.cs`가 넘기는 `play.QuestId != null`)로 걸러져
            // `!_lastBusy` 조건에서 막힌다.
            var quickKeyHeld = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C);
            if (!quickKeyHeld && Input.GetKeyDown(KeyCode.Alpha1) && (_keyGuideOpen || !_lastBusy))
                _keyGuideOpen = !_keyGuideOpen;

            if (_keyGuideOpen && Input.GetKeyDown(KeyCode.Escape))
                _keyGuideOpen = false;
        }

        /* ══════════════════════════════════════════════════ 그리기 */

        /// <summary>
        /// `Hud.OnGUI`가 매 프레임 부른다. `busy`는 하달 창·마감 큐·미니게임·
        /// 통합 수첩처럼 화면을 다 쓰는 것이 떠 있는지를 호출 쪽이 판단해 넘긴다 —
        /// 이 파일은 그 판정 기준(`HudScreens.BlocksMovement` 등)을 모른다.
        /// </summary>
        public void Draw(HudTheme theme, bool busy)
        {
            _lastBusy = busy; // Update()가 다음 프레임 "1" 여닫이 판정에 쓴다
            if (!_keyGuideOpen || busy) return;
            DrawKeyGuide(theme);
        }

        /// <summary>키캡을 왼쪽부터 나란히 그린다. 폭은 글자 수에 맞춰 잰다(`Measure`)
        /// — "E"처럼 한 글자짜리와 "SPACE"처럼 긴 것이 같은 칸이면 어색하다</summary>
        private void DrawKeyRow(HudTheme theme, Rect area, string[] keys)
        {
            if (keys == null || keys.Length == 0) return;
            var x = area.x;
            var capH = Mathf.Min(30f, area.height);
            var y = area.y + (area.height - capH) * 0.5f;
            foreach (var key in keys)
            {
                var w = Mathf.Max(34f, theme.Measure(key, theme.Mono) + 20f);
                theme.KeyCap(new Rect(x, y, w, capH), key);
                x += w + 8f;
            }
        }

        /* ══════════════════════════════════════════ 전체 키 안내 (1) */

        /// <summary>
        /// "실제로 존재하는 키만" 적는다. 각 줄의 출처(파일:줄)는
        /// PR 보고에 그대로 옮긴 표와 같다:
        ///
        /// 이동 W/A/S/D          — LocalPlayer.cs:104(축) · ProjectSettings/InputManager.asset(Horizontal/Vertical → a/d, s/w)
        /// 달리기 SHIFT          — LocalPlayer.cs:107
        /// 상호작용 E             — QuestPlay.cs:93 · Rescue.cs:77 · MaskDrill.cs:93
        /// 통합 수첩 TAB          — HudScreens.cs:171-190
        /// 지도 M                — HudScreens.cs:191-192
        /// 퀵 커맨드 Q, 1~8       — HudScreens.cs:130-136
        /// 정형 문구 C, 1~8       — LocalPlayer.cs:114-124
        /// 컨디션 수치 ALT        — Hud.cs:1854
        /// 닫기 ESC              — HudScreens.cs:194 · QuestPlay.cs:98 · Tutorial.cs(본 파일)
        /// 시간대 스킵(키 아님)    — Hud.cs:737-763(마우스 클릭 버튼, GameClient.cs:180 VoteSkip)
        /// 미니게임 확인 SPACE/ENTER — Minigame/Board.cs:75-80(공통 Tap/Confirm) · HudScreens.cs:517-542(점호·승급·취침 확인)
        /// 방독면 훈련 G          — MaskDrill.cs:86
        /// 이 안내 1              — Tutorial.cs(본 파일, Update의 Alpha1 분기)
        /// </summary>
        private static readonly (string[] keys, string label, string desc)[] KeyGuideEntries =
        {
            (new[] { "W", "A", "S", "D" }, "이동", "상하좌우로 걷는다 (방향키도 된다)"),
            (new[] { "SHIFT" }, "달리기", "이동 중 누르고 있으면 뛴다"),
            (new[] { "E" }, "상호작용", "목표 앞에서 일과를 시작한다 · 구조는 누르고 있는다"),
            (new[] { "TAB" }, "통합 수첩", "일과표 → 일과요약 → 기록 순으로 넘긴다"),
            (new[] { "M" }, "지도", "미니맵을 전체화면으로 연다"),
            (new[] { "Q", "1~8" }, "퀵 커맨드", "Q를 누른 채 숫자로 전술을 지시한다"),
            (new[] { "C", "1~8" }, "정형 문구", "C를 누른 채 숫자로 짧게 말한다"),
            (new[] { "ALT" }, "컨디션 수치", "누르고 있으면 스탯 수치가 보인다"),
            (new[] { "ESC" }, "닫기", "열린 창을 닫는다"),
            (System.Array.Empty<string>(), "시간대 스킵", "화면의 [투표하기]를 마우스로 누른다 (키 아님)"),
            (new[] { "SPACE", "ENTER" }, "미니게임 확인", "대부분의 판에서 확인·탭으로 쓴다 (판마다 다를 수 있다)"),
            (new[] { "G" }, "방독면 훈련", "훈련일에 착용 절차를 시작한다"),
            (new[] { "1" }, "이 안내", "언제든 열고 닫는다"),
        };

        private void DrawKeyGuide(HudTheme theme)
        {
            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight), HudTheme.Dim, 0.86f);

            const float width = 900f;
            const float headerH = 72f;
            const float rowH = 42f;
            const float pad = 32f;
            var height = headerH + KeyGuideEntries.Length * rowH + pad + 40f;
            var rect = new Rect((HudTheme.ViewWidth - width) * 0.5f, (HudTheme.ViewHeight - height) * 0.5f,
                width, height);

            theme.Panel(rect, HudTheme.Paper, HudTheme.Rule, 2f);
            theme.Header(rect, headerH);
            GUI.Label(new Rect(rect.x + 32f, rect.y + 10f, 400f, 20f), "조작 · 키 안내",
                theme.At(theme.Label, 12, HudTheme.Accent));
            GUI.Label(new Rect(rect.x + 32f, rect.y + 32f, 500f, 34f), "지금까지 나온 키 전체",
                theme.At(theme.Title, 22, HudTheme.Ink));

            var y = rect.y + headerH + pad * 0.5f;
            foreach (var (keys, label, desc) in KeyGuideEntries)
            {
                var rowX = rect.x + pad;
                var rowWidth = width - pad * 2f;
                DrawKeyRow(theme, new Rect(rowX, y, 170f, rowH), keys);
                GUI.Label(new Rect(rowX + 178f, y, 150f, rowH), label,
                    theme.At(theme.Body, 16, HudTheme.Ink));
                GUI.Label(new Rect(rowX + 178f + 156f, y, rowWidth - 178f - 156f, rowH), desc,
                    theme.At(theme.Small, 14, HudTheme.Ink2));
                y += rowH;
            }

            GUI.Label(new Rect(rect.x, rect.yMax - 34f, width, 24f), "닫기 — [1] 또는 [ESC]",
                theme.At(theme.Small, 13, HudTheme.Ink2, TextAnchor.MiddleCenter));
        }
    }
}
