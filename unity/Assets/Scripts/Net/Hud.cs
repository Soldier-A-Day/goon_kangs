using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 인게임 HUD (SAD-ART-001 §7.1).
    ///
    /// 좌표는 전부 **1920×1080 기준**이고 확정 목업(`files-3/MOCKUP_01_HUD_인게임.svg`)
    /// 실측이다. 창 크기가 달라도 비율만 바뀐다 — §2.1이 UI를 네이티브 해상도로
    /// 분리한 이유가 이것이라, 월드가 640×360으로 픽셀 퍼펙트여도 HUD는 선명하다.
    ///
    /// **판단하지 않는다.** 버튼은 의도를 보낼 뿐이고 되는지 안 되는지는 서버가
    /// 정한다. 클라가 "지금은 못 한다"를 스스로 막으면 규칙이 두 곳에 살게 되고
    /// (ARCH-02), 서버가 거절할 상황을 클라가 다르게 판단하는 순간 화면과 실제가
    /// 갈라진다. 그래서 잠그는 대신 **왜 안 되는지 읽을 거리를 준다**(§7.1.5).
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public GameClient client;
        public NetBootstrap boot;
        public Interactor interactor;
        public ZoneWorld world;
        public ZoneVisibility visibility;
        public WeatherGrading grading;
        /// <summary>§9.0 사이드뷰 코스 — 페이스 게이지를 그린다</summary>
        public LaneRun lane;
        /// <summary>§9.0 방독면 착용 3단계 QTE</summary>
        public MaskDrill mask;
        /// <summary>§10.0 후송 — 화면이 회색으로 빠지는 동안 안내를 띄운다</summary>
        public Evacuation evacuation;
        /// <summary>§6.1 일과 수행 — E로 열리는 판</summary>
        public QuestPlay play;
        public Font font;
        public Font monoFont;

        private HudTheme _theme;
        private HudScreens _screens;
        private readonly List<Toast> _toasts = new List<Toast>();
        private readonly List<Bubble> _bubbles = new List<Bubble>();

        /// <summary>E-2 — conditionCritical 중복 억제. sim은 시간대마다 임계 조건이
        /// 유지되는 한 계속 이 이펙트를 낸다(`condition.ts` raiseCriticals). 그대로
        /// 다 띄우면 같은 사람·같은 스탯 토스트가 시간대마다 스팸이 된다 — 다른
        /// 침묵 판정 토스트엔 이런 반복 신호가 없어 선례가 없으므로 여기서 새로
        /// 정한다. 키는 "memberId|stat", 값은 마지막으로 띄운 시각(unscaledTime)</summary>
        private readonly Dictionary<string, float> _criticalShownAt = new Dictionary<string, float>();

        /// <summary>§7.1.6 알림 카드 — 최대 4개 스택, 기본 4초 후 페이드.
        /// `life`는 총 표시 시간(페이드 0.6초 포함) — 후송처럼 더 오래 읽혀야
        /// 하는 문장은 개별로 늘린다(C-1b)</summary>
        private sealed class Toast
        {
            public string tag;
            public string text;
            public Color color;
            public float born;
            public float life = 4.6f;
        }

        /// <summary>§7.8 발신 시 동시 출력 3종 중 하나 — 머리 위 말풍선 3초.
        /// `command`가 있으면 아이콘 말풍선(퀵 커맨드), 없고 `text`만 있으면
        /// 문장 말풍선(하달 반응 — C-1b)</summary>
        private sealed class Bubble
        {
            public string memberId;
            public string command;
            public string text;
            public float born;
        }

        /// <summary>
        /// B-3 정형 문구 8종 — id는 프로토콜 `quickPhraseSchema`와 같은 순서다.
        ///
        /// 요청·위치·정서·응답이 고루 있다. quickCommand(8.0)는 타임 프레셔
        /// 구간의 전술 지시어라 어휘가 다르다 — 이쪽은 언제든 쓰는 사회적
        /// 문구라서, 자유 채팅의 트롤링 없이 "말을 건 느낌"을 내는 것이 목적이다.
        /// 숫자 순서가 곧 휠·번호 입력의 순서다(`LocalPlayer.QuickPhraseIds`와
        /// 반드시 같은 순서를 유지한다).
        /// </summary>
        private static readonly (string id, string text)[] QuickPhrases =
        {
            ("assist", "손 좀 빌려주십시오"),
            ("goingFirst", "먼저 갑니다"),
            ("gather", "집합"),
            ("here", "여기다"),
            ("thanks", "고맙다"),
            ("comingNow", "지금 간다"),
            ("danger", "위험하다"),
            ("wellDone", "잘했다"),
        };

        private static string QuickPhraseLabel(string id)
        {
            foreach (var (key, text) in QuickPhrases)
            {
                if (key == id) return text;
            }
            return id;
        }

        private void Awake()
        {
            _screens = new HudScreens(this);
        }

        private HudScreens _labScreens;

        /// <summary>
        /// T단계 — ScreenLab(F10) 전용 `HudScreens` 인스턴스.
        ///
        /// **진짜 `_screens`를 빌려주지 않는다.** 실전 `_screens`는 `OnEvent`·
        /// `OnSnapshot`을 계속 구독하고 있어서, 랩이 그 안의 `_screen`을 강제로
        /// 바꾸는 동안 실제 하달 창이나 판정이 도착하면 서로 덮어써 버린다 —
        /// "랩이 실전 상태를 오염시키지 않는다"는 원칙이 깨진다. 그래서 그리기
        /// 코드(`DrawRollCall` 등)만 같이 쓰고 상태는 완전히 별개인 인스턴스를
        /// 하나 더 둔다 — 생성자가 요구하는 `Hud` 참조 말고는 실전과 아무것도
        /// 공유하지 않는다.
        /// </summary>
        internal HudScreens LabScreens => _labScreens ??= new HudScreens(this) { LabPreviewOnly = true };

        /// <summary>화생방 마스크 훈련 — 이것도 이동 잠금 소스다. 씬 배선 없이
        /// 한 번 찾아 둔다(잠금 대입이 매 프레임 덮어쓰는 구조라, 소스를 빼먹으면
        /// 그쪽 잠금이 다음 프레임에 지워진다)</summary>
        private MaskDrill _maskDrill;

        private void OnEnable()
        {
            _maskDrill = FindFirstObjectByType<MaskDrill>();
            if (client == null) return;
            client.EventReceived += OnEvent;
            client.SnapshotReceived += OnSnapshot;
            // E-3 — 오늘의 기록(수첩 탭). OnEvent의 토스트 스위치와 별개로 구독해
            // 기존 case를 하나도 건드리지 않는다(WORKORDER.md E-3 제약, 병합 충돌 최소화)
            client.EventReceived += CollectJournal;
        }

        private void OnDisable()
        {
            if (client == null) return;
            client.EventReceived -= OnEvent;
            client.SnapshotReceived -= OnSnapshot;
            client.EventReceived -= CollectJournal;
        }

        private void OnSnapshot(Snapshot snapshot)
        {
            CollectQuestJournal(snapshot);
            _screens.OnSnapshot(snapshot);
        }

        /* ══════════════════════════════════════════════════════ 입력 */

        private void Update()
        {
            // 실험실이 떠 있으면 게임 입력을 전부 물린다 — 창 토글도 이동도.
            // 실험판의 조작(스페이스 탭·숫자·R)이 게임으로 새면 판을 재는 동안
            // 등 뒤에서 일과표가 열리고 사람이 걸어 다닌다. T단계 — ScreenLab(F10)·
            // WorldLab(F11)도 같은 이유로 같이 물린다
            if (BoardLab.IsOpen || ScreenLab.IsOpen || WorldLab.IsOpen)
            {
                if (world?.player != null) world.player.Suspended = true;
                return;
            }

            _screens.Update();

            if (world?.player != null)
            {
                // 판이 열려 있는 동안(QuestPlay)의 잠금도 여기서 같이 본다 — 이
                // 대입이 매 프레임 돌기 때문에, 한 소스만 보면 다른 쪽이 걸어둔
                // 잠금을 다음 프레임에 지워버린다(실플레이 신고: 판을 깨는 중에
                // 캐릭터가 걸어 다녔다). 잠금의 소스는 둘 다다: 전체화면 창과 판.
                world.player.Suspended = _screens.BlocksMovement || play?.QuestId != null
                    || (_maskDrill != null && _maskDrill.Active);
            }

            // 상호작용은 `QuestPlay`가 맡는다 — E를 누르면 그 일과의 판이 열리고,
            // 통과하면 끝난다. HUD는 그 판을 그리기만 한다
        }

        /* ══════════════════════════════════════════════════════ 그리기 */

        private void OnGUI()
        {
            if (client == null) return;
            if (_theme == null)
            {
                _theme = new HudTheme(font, monoFont != null ? monoFont : font);
                HudIcons.Bind(_theme);
            }

            // §7 기준 해상도로 스케일한다. 비율을 지켜야 목업 좌표가 그대로 산다.
            // **좌표계는 화면만큼 넓어진다** — 넓은 화면에서 오른쪽이 비지 않게
            var scale = HudTheme.Fit();
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                                       new Vector3(scale, scale, 1f));

            _mouse = new Vector2(Input.mousePosition.x / scale,
                                 (Screen.height - Input.mousePosition.y) / scale);
            _hovered = -1;

            // A-1 준비 — 미니게임 실험대(BoardLab.cs). F9로 열고 닫는다. QuestPlay를
            // 거치지 않으므로 서버 연결·스냅샷과 무관하게 동작한다.
            //
            // **열려 있는 동안은 실험실이 화면의 전부다.** 뒤에 HUD를 같이 그리면
            // 판을 재는 화면 위에 미니맵·수첩이 얹혀 정신이 없고, 실험실의
            // 1·2·3(난이도)이 게임 쪽 숫자 입력과 겹쳐 양쪽에 다 먹는다
            BoardLab.Draw(_theme);
            if (BoardLab.IsOpen)
            {
                GUI.matrix = matrix;
                return;
            }

            // T단계 — 화면 실험대(F10)·월드 실험대(F11). BoardLab과 같은 원칙:
            // 열려 있는 동안은 그것이 화면의 전부다. 실전 Hud를 갖고 있어야
            // 실물 그리기 코드(HudScreens·DrawCondition·Notify 등)를 그대로
            // 재사용할 수 있어 `this`를 넘긴다
            ScreenLab.Draw(_theme, this);
            if (ScreenLab.IsOpen)
            {
                GUI.matrix = matrix;
                return;
            }

            WorldLab.Draw(_theme, this);
            if (WorldLab.IsOpen)
            {
                GUI.matrix = matrix;
                return;
            }

            var snapshot = client.Latest;

            // **런이 끝났거나 붙지 못했으면 여기서 끝난다.**
            // 그 아래 HUD는 그릴 이유가 없고, 멈춘 화면을 그대로 두면
            // 플레이어는 퇴소된 줄도 모른 채 얼어붙은 부대를 본다
            //
            // 단, 하루 마감 연출(판정 → 승급 → 취침)이 아직 사용자 확인을 못
            // 받았으면 보류한다 — 실패로 끝나는 날은 서버가 dayJudged(failed)와
            // runEnded를 같은 프레임에 함께 보내는데, 그걸 그대로 따르면 이
            // 화면이 판정 화면(왜 졌는지)을 읽기도 전에 덮어 버린다(사용자 반려:
            // "점호 판정보다 퇴소창이 먼저 뜨는 건 뭐야?"). `_screens.DayEndInProgress`가
            // 그 연출의 20초 강제 흘림 문턱까지 갖고 있어 여기서 소프트락이 생기지
            // 않는다 — 판정 화면이 어떤 이유로 안 떴는데(재접속 등) 런이 끝나 있는
            // 경우는 애초에 이 조건이 거짓이라(화면이 RollCall/Rank/Sleep이 아니면)
            // 즉시 아래로 흘러 HudEnding이 뜬다.
            if (!_screens.DayEndInProgress &&
                HudEnding.Draw(_theme, client, () => boot?.RestartRun(), GoLobby,
                    boot != null && boot.Restarting, boot != null ? boot.RestartError : ""))
            {
                GUI.matrix = matrix;
                return;
            }

            // **끊김은 화면이 말해야 한다.** `Latest` 스냅샷은 끊긴 뒤에도 마지막
            // 값을 유지하므로, 이 배너가 없으면 재접속하는 동안 화면은 멀쩡한
            // 부대를 그대로 그린다 — 움직임만 안 먹는 화면은 고장으로 읽힌다.
            DrawConnectionBanner();

            // 월드 위에 겹치는 것부터 — 이름표·말풍선은 UI 패널보다 뒤여야 한다
            DrawWorldOverlay(snapshot);

            DrawStateOverlays();
            DrawWhereAmI();
            DrawPhaseBar(snapshot);
            DrawSkipVote(snapshot);
            DrawToasts();
            DrawMinimap(snapshot);
            DrawNotebookSummary(snapshot);
            DrawCondition(snapshot);
            DrawPrompt(snapshot);
            DrawBodyHeat(snapshot);
            DrawLane();
            DrawMask();
            DrawEvacuation();
            DrawMinigame();
            DrawPhraseWheel();

            _screens.Draw(_theme, snapshot);

            GUI.matrix = matrix;
        }

        public HudTheme Theme => _theme;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void SadGoLobby();
#endif

        /// <summary>웹 셸의 로비로 돌아간다. 에디터에서는 로그만 남긴다</summary>
        private static void GoLobby()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SadGoLobby();
#else
            Debug.Log("[hud] 로비로 (에디터에서는 이동하지 않는다)");
#endif
        }

        /* ───────────────────────────────── §9.0 사이드뷰 코스 (행군 · 유격) */

        /// <summary>
        /// 페이스 게이지 (§9.0 "너무 앞서면 체력 과소모, 뒤처지면 대열 이탈").
        ///
        /// **가운데가 안전한 게이지**다. 이 게임의 다른 막대는 전부 "많을수록
        /// 좋다"인데 이것만 다르므로, 안전 구간을 눈에 보이게 칠하지 않으면
        /// 플레이어는 끝까지 밀어붙이려 든다.
        /// </summary>
        private void DrawLane()
        {
            if (lane == null || !lane.Running) return;

            var box = new Rect(HudTheme.ViewWidth * 0.5f - 260f, 96f, 520f, 92f);
            _theme.Panel(box, HudTheme.Paper2, HudTheme.Rule);

            GUI.Label(new Rect(box.x + 16f, box.y + 10f, 300f, 20f), lane.LegName,
                _theme.At(_theme.Label, 13, HudTheme.Ink2));
            GUI.Label(new Rect(box.xMax - 176f, box.y + 10f, 160f, 20f),
                $"체크포인트 {lane.Checkpoints}/4",
                _theme.At(_theme.Label, 13, HudTheme.Accent, TextAnchor.MiddleRight));

            // 코스 진행
            _theme.Bar(new Rect(box.x + 16f, box.y + 36f, box.width - 32f, 8f),
                       lane.Progress, HudTheme.Accent, HudTheme.Rule);

            // 페이스 — 안전 구간을 먼저 칠하고 그 위에 바늘을 세운다
            var pace = new Rect(box.x + 16f, box.y + 56f, box.width - 32f, 14f);
            _theme.Fill(pace, HudTheme.Rule);
            _theme.Fill(new Rect(pace.x + pace.width * 0.2f, pace.y,
                                 pace.width * 0.6f, pace.height), HudTheme.AccentW);

            var safe = lane.Pace > 0.2f && lane.Pace < 0.8f;
            _theme.Fill(new Rect(pace.x + pace.width * lane.Pace - 2f, pace.y - 3f,
                                 4f, pace.height + 6f),
                        safe ? HudTheme.Accent : HudTheme.Alert);

            if (!safe)
            {
                GUI.Label(new Rect(box.x, box.y + 72f, box.width, 20f),
                    lane.Pace >= 0.8f ? "과속 — 체력이 빠진다" : "낙오 — 대열로 붙어라",
                    _theme.At(_theme.Small, 14, HudTheme.Alert, TextAnchor.MiddleCenter));
            }
        }

        /* ────────────────────────────────────── §9.0 방독면 착용 컷인 */

        /// <summary>
        /// 방독면 3단계 QTE (§1.3-3 재설계 "정면 클로즈업 컷인 오버레이").
        ///
        /// 화면을 덮는다. 착용하는 동안 다른 것을 못 하는 것이 이 절차의
        /// 압박이고, 뒤에서 부대가 계속 도는 것이 보이면 그 압박이 사라진다.
        /// </summary>
        private void DrawMask()
        {
            if (mask == null || !mask.Active) return;

            _theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight),
                        HudTheme.Dim, 0.88f);

            var box = new Rect(HudTheme.ViewWidth * 0.5f - 340f,
                               HudTheme.DesignHeight * 0.5f - 150f, 680f, 300f);
            _theme.Panel(box, HudTheme.Paper2, HudTheme.Alert, 2f);

            GUI.Label(new Rect(box.x + 28f, box.y + 20f, 400f, 22f),
                "화생방 — 방독면 착용", _theme.At(_theme.Label, 13, HudTheme.Alert));
            GUI.Label(new Rect(box.x + 28f, box.y + 48f, 620f, 36f),
                MaskDrill.Steps[Mathf.Clamp(mask.Step, 0, 2)],
                _theme.At(_theme.Title, 28, HudTheme.Ink));

            // 단계 표시 — 세 칸이 차례로 찬다
            for (var i = 0; i < MaskDrill.Steps.Length; i += 1)
            {
                _theme.Fill(new Rect(box.x + 28f + i * 40f, box.y + 98f, 32f, 6f),
                            i < mask.Step ? HudTheme.Accent : HudTheme.Rule);
            }

            // 타이밍 창
            var bar = new Rect(box.x + 28f, box.y + 130f, box.width - 56f, 26f);
            _theme.Fill(bar, HudTheme.Rule);
            _theme.Fill(new Rect(bar.x + bar.width * mask.WindowMin, bar.y,
                                 bar.width * (mask.WindowMax - mask.WindowMin), bar.height),
                        HudTheme.AccentW);
            _theme.Fill(new Rect(bar.x + bar.width * mask.Cursor - 2f, bar.y - 4f,
                                 4f, bar.height + 8f), HudTheme.Ink);

            GUI.Label(new Rect(box.x + 28f, box.y + 172f, 620f, 24f),
                "구간에 들어올 때 Space", _theme.At(_theme.Body, 16, HudTheme.Ink2));

            if (mask.MissFlash > 0f)
            {
                GUI.Label(new Rect(box.x + 28f, box.y + 202f, 620f, 26f),
                    "밀폐 실패 — 한 단계 되돌아간다",
                    _theme.At(_theme.Heading, 18, HudTheme.Alert));
            }
        }

        /* ────────────────────────────────────────────── §10.0 후송 */

        private void DrawEvacuation()
        {
            if (evacuation == null || evacuation.Fade <= 0.02f) return;

            var box = new Rect(HudTheme.ViewWidth * 0.5f - 300f,
                               HudTheme.DesignHeight * 0.5f - 70f, 600f, 140f);
            _theme.Panel(box, HudTheme.Paper2, HudTheme.Alert, 2f);

            GUI.Label(new Rect(box.x + 28f, box.y + 22f, 400f, 22f), "EVA — 후송",
                _theme.At(_theme.Label, 13, HudTheme.Alert));
            GUI.Label(new Rect(box.x + 28f, box.y + 50f, 540f, 34f), Evacuation.Notice,
                _theme.At(_theme.Title, 24, HudTheme.Ink));
            GUI.Label(new Rect(box.x + 28f, box.y + 96f, 540f, 24f),
                "분대의 하루는 계속된다 — 남은 필수는 대행으로 넘어간다",
                _theme.At(_theme.Body, 15, HudTheme.Ink2));
        }

        /* ────────────────────────────────────── B-3 정형 문구(quick-phrase) 목록 */

        /// <summary>
        /// C를 누르고 있는 동안 뜨는 quick-phrase 목록. 숫자 1~8(`LocalPlayer`가
        /// 읽는다) 또는 여기 클릭으로 고른다.
        ///
        /// 8.0 퀵 커맨드의 원형 라디얼(Q, `HudScreens.DrawRadial`)과 다르게 세로
        /// 목록으로 그렸다 — 두 휠이 같이 뜰 일이 없어(Q/C가 겹치지 않는다) 같은
        /// 모양을 흉내 낼 이유가 없고, 문장이 길어 원형보다 목록이 더 읽기 쉽다.
        /// 발화 자체의 자격(스팸 가드)은 서버가 판정한다(ARCH-02) — 여기서는
        /// 고른 것을 그대로 서버로 보낼 뿐이다.
        /// </summary>
        private void DrawPhraseWheel()
        {
            if (world?.player == null || !world.player.PhraseWheelOpen) return;

            const float rowHeight = 42f;
            var box = new Rect(HudTheme.ViewWidth * 0.5f - 170f,
                HudTheme.DesignHeight * 0.5f - QuickPhrases.Length * rowHeight * 0.5f - 12f,
                340f, QuickPhrases.Length * rowHeight + 24f);
            _theme.Panel(box, HudTheme.Paper2, HudTheme.Rule);

            GUI.Label(new Rect(box.x + 12f, box.y + 4f, box.width - 24f, 20f),
                "정형 문구 — 숫자 또는 클릭",
                _theme.At(_theme.Label, 12, HudTheme.Ink2));

            for (var i = 0; i < QuickPhrases.Length; i += 1)
            {
                var (id, text) = QuickPhrases[i];
                var row = new Rect(box.x + 8f, box.y + 24f + i * rowHeight, box.width - 16f,
                    rowHeight - 4f);
                if (row.Contains(_mouse)) _theme.Fill(row, HudTheme.AccentW, 0.35f);

                GUI.Label(new Rect(row.x + 6f, row.y, 28f, row.height), $"{i + 1}",
                    _theme.At(_theme.Heading, 16, HudTheme.Ink2));
                GUI.Label(new Rect(row.x + 36f, row.y, row.width - 44f, row.height), text,
                    _theme.At(_theme.Body, 16, HudTheme.Ink, TextAnchor.MiddleLeft));

                if (GUI.Button(row, "", GUIStyle.none)) client.QuickPhrase(id);
            }
        }

        /* ─────────────────────────────── 현재 위치 · 근처 안내 (좌상단) */

        /// <summary>
        /// 지금 어디에 있는가.
        ///
        /// 걸어서 구역을 옮기게 되면서 필요해졌다 — 문을 몇 개 지나면 방 25개
        /// 중 어디인지 금세 헷갈리고, 미니맵만으로는 "실내인지 밖인지"가 안 읽힌다.
        ///
        /// 그 아래에 **근처에 무엇이 있는지**를 띄운다. 바닥 타일에 이름을 박는
        /// 대신인데, 타일에 구운 글자는 소품에 가리고 캐릭터가 밟고 서면
        /// 안 보인다. 상호작용 프롬프트와 같은 자리에 두면 시선이 한 곳에 모인다.
        /// </summary>
        private void DrawWhereAmI()
        {
            if (world == null) return;

            var here = world.HereName;
            var outdoor = world.Here == null;

            var rect = new Rect(48f, 48f, 380f, 64f);
            _theme.Fill(rect, HudTheme.Paper, 0.92f);
            _theme.Spine(rect, outdoor ? HudTheme.Cold : HudTheme.Accent);

            GUI.Label(new Rect(rect.x + 22f, rect.y + 8f, 340f, 20f),
                outdoor ? "OUTDOOR" : "LOCATION",
                _theme.At(_theme.Label, 12, outdoor ? HudTheme.Cold : HudTheme.Accent));
            GUI.Label(new Rect(rect.x + 22f, rect.y + 30f, 340f, 28f), here,
                _theme.At(_theme.Heading, 21, HudTheme.Ink));

            if (world.player == null) return;
            var at = (Vector2)world.player.transform.position;
            var y = rect.yMax + 8f;

            // 근처 문 안내 — 바닥에 이름을 박는 대신이다
            if (world.NearestDoor(at, 5f, out var label, out var isExit))
            {
                var hint = new Rect(rect.x, y, rect.width, 40f);
                _theme.Fill(hint, HudTheme.Paper, 0.94f);
                _theme.Border(hint, HudTheme.Rule);
                _theme.Spine(hint, isExit ? HudTheme.Heat : HudTheme.Accent, 3f);
                GUI.Label(new Rect(hint.x + 20f, hint.y, hint.width - 40f, hint.height),
                    (isExit ? "▶  " : "▪  ") + label,
                    _theme.At(_theme.Body, 17, isExit ? HudTheme.Heat : HudTheme.Ink));
                y = hint.yMax + 8f;
            }

            DrawCompass(new Rect(rect.x, y, rect.width, 0f), at);
        }

        /// <summary>§4.3 방위 8분할 — 화살표는 형태로 읽힌다</summary>
        private static readonly string[] Arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };

        /// <summary>
        /// 어느 쪽에 무엇이 있는가.
        ///
        /// 걸어서만 이동하는데 부대가 110×85 타일이라, 지도를 열지 않으면
        /// 방향 감각이 사라진다. 가까운 곳 넷만 띄운다 — 전부 띄우면 읽는 데
        /// 시간이 걸리고, 그건 지도를 여는 것과 다를 바 없다.
        /// </summary>
        private void DrawCompass(Rect anchor, Vector2 at)
        {
            if (world?.buildings == null) return;

            _compass.Clear();

            foreach (var building in world.buildings)
            {
                // 지금 들어와 있는 동은 빼고. "여기가 여기다"는 위에 이미 있다
                if (building.area.Contains(at)) continue;
                _compass.Add((building.name, building.area.center));
            }
            foreach (var zone in world.zones)
            {
                if (zone == null || zone.kind != "outdoor") continue;
                if (zone.area.Contains(at)) continue;
                _compass.Add((zone.zoneName, zone.area.center));
            }

            _compass.Sort((a, b) =>
                Vector2.SqrMagnitude(a.at - at).CompareTo(Vector2.SqrMagnitude(b.at - at)));

            var count = Mathf.Min(4, _compass.Count);
            if (count == 0) return;

            var panel = new Rect(anchor.x, anchor.y, anchor.width, 28f + count * 24f);
            _theme.Fill(panel, HudTheme.Paper, 0.94f);
            _theme.Border(panel, HudTheme.Rule);
            GUI.Label(new Rect(panel.x + 20f, panel.y + 5f, 300f, 18f), "NEARBY",
                _theme.At(_theme.Label, 11, HudTheme.Ink2));

            for (var i = 0; i < count; i += 1)
            {
                var (name, center) = _compass[i];
                var delta = center - at;
                // 8분할. 12시에서 시계 방향이라 화살표 배열과 순서가 같다
                var angle = Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg;
                if (angle < 0f) angle += 360f;
                var arrow = Arrows[Mathf.RoundToInt(angle / 45f) % 8];

                var row = new Rect(panel.x + 20f, panel.y + 26f + i * 24f, panel.width - 40f, 22f);
                GUI.Label(row, arrow, _theme.At(_theme.Heading, 18, HudTheme.Accent));
                GUI.Label(new Rect(row.x + 26f, row.y, row.width - 90f, row.height), name,
                    _theme.At(_theme.Body, 15, HudTheme.Ink));
                // 1타일 = 0.5m (PLAN 01 축척)
                GUI.Label(new Rect(row.xMax - 64f, row.y, 60f, row.height),
                    $"{delta.magnitude * 0.5f:0}m",
                    _theme.At(_theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleRight));
            }
        }

        private readonly List<(string name, Vector2 at)> _compass =
            new List<(string, Vector2)>();

        /* ─────────────────────────────────────── 연결 상태 배너 */

        /// <summary>
        /// 재접속 중·재접속 실패를 화면 상단 중앙에 띄운다.
        ///
        /// 시간대 바 바로 위다 — 화면에서 가장 먼저 읽히는 줄이고, 게임을
        /// 가리지 않는다. 재접속 중에는 몇 차 시도인지까지 적는다. "재접속 중"
        /// 한 마디만 있으면 언제까지 기다려야 하는지 알 수 없고, 모르는 기다림은
        /// 곧 새로고침이다 — 새로고침하면 백오프가 처음부터 다시 시작된다.
        /// </summary>
        private void DrawConnectionBanner()
        {
            if (boot == null) return;

            string text = null;
            var color = HudTheme.Heat;
            if (boot.Reconnecting)
            {
                text = $"무전 재접속 중 ({boot.ReconnectAttempt}/4차) — 자리를 지키십시오";
            }
            else if (boot.Status == "연결 끊김")
            {
                // 네 번 다 실패했다. 이제 기다림이 아니라 선택이 필요하다
                text = "연결이 끊겼습니다 — 새로고침으로 다시 붙을 수 있습니다";
                color = HudTheme.Alert;
            }
            DrawConnectionBanner(text, color);
        }

        /// <summary>
        /// 실제 그리는 자리. `NetBootstrap` 판정(`boot.Reconnecting` 등)과 그리기를
        /// 갈라뒀다 — `boot`는 재접속 상태의 private setter라 서버 없이는 재접속
        /// 배너를 하나도 재현할 수 없었다(ScreenLab 요구). 위 무인자 버전이 여전히
        /// 실전 판정을 그대로 하고 이 함수를 부르므로 동작은 그대로다.
        /// </summary>
        private void DrawConnectionBanner(string text, Color color)
        {
            if (text == null) return;

            var rect = new Rect(HudTheme.ViewWidth * 0.5f - 320f, 8f, 640f, 34f);
            _theme.Fill(rect, HudTheme.Paper, 0.94f);
            _theme.Spine(rect, color);
            GUI.Label(rect, text, _theme.At(_theme.Body, 15, color, TextAnchor.MiddleCenter));
        }

        /* ─────────────────────────────────────── §7.1.1 상단 시간대 바 */

        private void DrawPhaseBar(Snapshot snapshot)
        {
            // 목업 실측: 960×72 @ (480, 48)
            var rect = new Rect(480f, 48f, 960f, 72f);
            _theme.Fill(rect, HudTheme.Paper, 0.94f);
            _theme.Border(rect, HudTheme.Rule);
            _theme.Fill(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), HudTheme.Ink);

            if (snapshot?.phase == null)
            {
                GUI.Label(new Rect(rect.x + 24f, rect.y, 400f, rect.height),
                    boot != null ? boot.Status : "연결 중", _theme.As(_theme.Title, HudTheme.Ink2));
                return;
            }

            var band = snapshot.weather?.band ?? "normal";
            var bandColor = HudTheme.BandColor(band);

            // 밴드 아이콘 48×48 — §7.1.1 "색 + 형태로 구분"
            var icon = new Rect(rect.x + 24f, rect.y + 12f, 48f, 48f);
            _theme.Fill(icon, HudTheme.AccentW);
            HudIcons.Band(band, icon, bandColor);

            GUI.Label(new Rect(rect.x + 88f, rect.y + 18f, 200f, 36f),
                HudTheme.PhaseLabel(snapshot.phase.id), _theme.At(_theme.Title, 24, HudTheme.Ink));

            var weather = $"{HudTheme.BandLabel(band)} {snapshot.weather?.feelsLike ?? 0d:0}°C";
            GUI.Label(new Rect(rect.x + 220f, rect.y + 20f, 260f, 32f), weather,
                _theme.At(_theme.Heading, 16, bandColor));

            // 진행 바 400×12 @ x+400, y+30
            var bar = new Rect(rect.x + 400f, rect.y + 30f, 400f, 12f);
            var elapsed = (float)snapshot.phase.elapsedMs;
            var duration = Mathf.Max(1f, (float)snapshot.phase.durationMs);
            var left = Mathf.Clamp01(1f - elapsed / duration);

            // §7.1.1 "잔여 20% 이하부터 alert로 전환 + 1Hz 점멸"
            var urgent = left <= 0.2f;
            var fill = urgent ? HudTheme.Alert : HudTheme.Accent;
            if (urgent && !HudTheme.Pulse(1f)) fill = HudTheme.Rule;
            _theme.Bar(bar, elapsed / duration, fill);

            var paused = snapshot.phase.delegationWindowMsLeft > 0d;
            if (paused)
            {
                // §7.1.1 하달 창 중에는 사선 해칭 + `일시정지` 라벨.
                // 바가 멈춘 것만으로는 접속이 끊긴 것과 구분되지 않는다
                _theme.Hatch(bar, new Color(HudTheme.Cold.r, HudTheme.Cold.g, HudTheme.Cold.b, 0.5f));
                GUI.Label(new Rect(bar.x, bar.yMax + 4f, 200f, 20f), "일시정지 — 하달 창",
                    _theme.At(_theme.Label, 12, HudTheme.Cold));
            }
            else
            {
                GUI.Label(new Rect(bar.x, bar.yMax + 4f, 240f, 20f),
                    $"TIMESLOT {snapshot.phase.clock}", _theme.At(_theme.Label, 12, HudTheme.Ink2));
            }

            var remain = Mathf.Max(0f, (duration - elapsed) / 1000f);
            var clock = $"{Mathf.FloorToInt(remain / 60f):00}:{Mathf.FloorToInt(remain % 60f):00}";

            if (paused)
            {
                // 하달 창이 열려 있는 동안은 위 clock이 그대로 멈춰 있다 — 그건 설계다
                // (`step.ts` applyTick, "하달 창이 열려 있는 동안은 시간대 타이머가
                // 멈춰 있다"). 문제는 **멈춘 이유를 시간 표시 자리가 말하지 않는다는
                // 것**이었다 — 하달 창을 이미 확정하고 닫은 사람 눈에는 화면이 멀쩡한데
                // 시계만 서 있는, 설명 없는 정지로 보였다. 조기 종료가 들어가도 아직
                // 확정 안 한 사람이 있으면 정지는 여전히 생기므로 이 표시는 그대로 둔다.
                var pauseLeft = Mathf.CeilToInt((float)snapshot.phase.delegationWindowMsLeft / 1000f);
                GUI.Label(new Rect(rect.xMax - 360f, rect.y + 18f, 336f, 40f),
                    $"하달 대기 — 시간 정지 ({pauseLeft}초)",
                    _theme.At(_theme.Heading, 18, HudTheme.Cold, TextAnchor.MiddleRight));
            }
            else
            {
                GUI.Label(new Rect(rect.xMax - 176f, rect.y + 18f, 152f, 40f), clock,
                    _theme.At(_theme.Display, 30, urgent ? HudTheme.Alert : HudTheme.Ink, TextAnchor.MiddleRight));
            }
        }

        /// <summary>내가 지금 스킵에 투표한 상태인가 — 순전히 표시용 로컬 기억이다.
        /// 서버(`room.ts` skipVotes)는 시간대가 바뀌어도 투표를 지우지 않으므로
        /// (재시작 때만 clear), 여기서도 시간대가 바뀐다고 껐다 켜지 않는다 — 다시
        /// 눌러야 꺼지는 실제 서버 상태를 그대로 따라간다</summary>
        private bool _skipVoted;

        /// <summary>
        /// TIME-01 실전 시간대 스킵 투표 — 서버 `voteSkip`은 있었는데 클라 버튼이 없었다.
        ///
        /// 정족수도 스킵 실행도 서버가 정한다(`room.ts` voteSkip, 생존 인원의 3/4).
        /// 여기서는 찬반만 토글해 보낸다 — 몇 명이 더 눌렀는지는 스냅샷에 안 실려 있어
        /// (`skipVotes`가 서버 내부 집합일 뿐 프로토콜 필드가 아니다) 개표 현황은
        /// 못 보여준다. 정족수가 차면 시간대 바가 스스로 넘어가는 것으로 결과가 보인다.
        /// </summary>
        /// <summary>마지막으로 스킵 버튼 클릭을 처리한 프레임 — OnGUI가 한 프레임에
        /// 여러 번 돌아(Layout·Repaint) 토글이 클릭당 두 번 뒤집히는 것을 막는다</summary>
        private int _skipClickFrame = -1;
        /// <summary>투표 상태가 유효한 시간대 — 칸이 넘어가면 서버가 표를 비우므로
        /// 클라 토글도 같이 리셋해야 화면과 실제가 갈라지지 않는다</summary>
        private string _skipPhaseId;

        private void DrawSkipVote(Snapshot snapshot)
        {
            if (snapshot?.phase == null) return;

            if (snapshot.phase.id != _skipPhaseId)
            {
                _skipPhaseId = snapshot.phase.id;
                _skipVoted = false;
            }

            // 목업 실측 없는 신규 버튼 — 시간대 바(480,48,960,72) 오른쪽, 미니맵
            // (x=1592) 왼쪽 사이 좁은 틈에 끼운다. 그래서 폭을 짧게 잡는다
            var rect = new Rect(1448f, 48f, 136f, 72f);
            var hot = rect.Contains(_mouse);
            _theme.Fill(rect, _skipVoted ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
            _theme.Border(rect, _skipVoted ? HudTheme.Accent : HudTheme.Rule);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 22f), "시간대 스킵",
                _theme.At(_theme.Small, 13, _skipVoted ? HudTheme.Accent : HudTheme.Ink));
            GUI.Label(new Rect(rect.x + 8f, rect.y + 32f, rect.width - 16f, 30f),
                _skipVoted ? "투표함  [취소]" : "투표하기",
                _theme.At(_theme.Label, 12, _skipVoted ? HudTheme.Accent : HudTheme.Ink2));

            if (hot && Input.GetMouseButtonDown(0) && Time.frameCount != _skipClickFrame)
            {
                _skipClickFrame = Time.frameCount;
                _skipVoted = !_skipVoted;
                client.VoteSkip(_skipVoted);
            }
        }

        /* ─────────────────────────────────────── §7.1.6 좌상단 알림 스택 */

        private void OnEvent(ServerEvent item)
        {
            if (item == null) return;

            switch (item.type)
            {
                case ServerEventTypeValues.QuickCommand:
                    // §7.8 말풍선 3초. 미니맵 핑은 무전 연결 시에만(§1.3-C)
                    _bubbles.Add(new Bubble
                    {
                        memberId = item.memberId, command = item.command, born = Time.unscaledTime,
                    });
                    return;

                case ServerEventTypeValues.QuickPhrase:
                    // B-3 정형 문구 소통 — 아이콘이 아니라 문장 말풍선이다.
                    // quickCommand(8.0 전술 지시)와 어휘가 다르다는 것을 한눈에
                    // 보여주려고 일부러 다른 그림(문장 vs 아이콘)을 쓴다.
                    // `auto`(합동 판을 열고 있는 동안의 자동 발화, §4항)도 사람이
                    // 누른 것과 같은 말풍선으로 보여준다 — 도와달라는 말이 자동이든
                    // 수동이든 화면이 다르게 취급할 이유가 없다.
                    _bubbles.Add(new Bubble
                    {
                        memberId = item.memberId, text = QuickPhraseLabel(item.phrase),
                        born = Time.unscaledTime,
                    });
                    return;

                case ServerEventTypeValues.SurpriseRaised:
                    // **돌발은 하던 일을 끊고 들어온다.**
                    //
                    // 예전에는 알림 카드 하나가 전부라, 돌발이 "일과 목록에 한 줄
                    // 늘어난 것"이었다. 그러면 별도 슬롯이지 돌발이 아니다 —
                    // 판이 열려 있으면 그 판을 여기서 끊는다. 진척은 서버가 들고
                    // 있으므로 잃는 것은 판에 쓴 시간이고, 그게 돌발의 대가다.
                    //
                    // C-1b — 태그를 퀘스트 이름으로, 본문을 상황 도입 문장으로
                    // 바꿨다. 예전엔 태그가 "돌발" 하나뿐이라 무슨 일이 왔는지
                    // 본문(퀘스트 라벨)까지 읽어야 알 수 있었다.
                    {
                        var intro = HudDialogue.SurpriseIntro(item.questId, CurrentDay());
                        if (play != null && play.QuestId != null)
                        {
                            play.Close();
                            Notify(item.label, $"{intro} — 하던 일이 끊겼다", HudTheme.Alert, 6.2f);
                            return;
                        }
                        Notify(item.label, intro, HudTheme.Heat, 6.2f);
                        return;
                    }

                case ServerEventTypeValues.DisciplineChanged:
                    Notify("군기", $"{item.to:0} · {item.band}", HudTheme.Alert);
                    return;

                case ServerEventTypeValues.ChoreDelegated:
                    Notify("하달", NameOf(item.fromId) + " → " + NameOf(item.toId), HudTheme.Heat);
                    // C-1b 정서 텍스트 — 받는 사람 머리 위에 계급 조합별 반응 한 줄.
                    // 하달 시스템의 짬때리기가 이미 코드로 완성돼 있는데(하달 군기 −2·
                    // 3일 연속 시 정신력 회복 −30%) 화면엔 아이콘 하나뿐이었다.
                    _bubbles.Add(new Bubble
                    {
                        memberId = item.toId,
                        text = DelegationLine(RankOf(item.fromId), RankOf(item.toId)),
                        born = Time.unscaledTime,
                    });
                    return;

                case ServerEventTypeValues.ChoreVetoed:
                    Notify("거부", NameOf(item.memberId), HudTheme.Ink2);
                    _bubbles.Add(new Bubble
                    {
                        memberId = item.memberId,
                        text = VetoLines[UnityEngine.Random.Range(0, VetoLines.Length)],
                        born = Time.unscaledTime,
                    });
                    return;

                case ServerEventTypeValues.MemberEvacuated:
                {
                    // C-1b 후송 문장 — 혼자 조용히 실려 나가던 것을 남은 사람들에게 알린다.
                    // 5.6초(기본 4.6초보다 길게) — "그 몫까지 채운다"는 부담을 방금
                    // 이해할 시간이 필요하다. 인원 수는 뭉뚱그리지 않고 실제로 센다 —
                    // "남은 사람이"보다 "남은 셋이"가 그 부담의 크기를 정확히 말한다.
                    var name = NameOf(item.memberId);
                    Notify("후송", HudDialogue.EvacuationLine(name, RemainingCount(item.memberId), CurrentDay()),
                        HudTheme.Alert, 5.6f);
                    return;
                }

                case ServerEventTypeValues.MemberReturned:
                {
                    // E-1 — 후송 복귀는 계급 리셋·재활 2일이 통보 없이 적용되던 항목이다
                    // (감사 목록 4). asRecruit는 그중 가장 아픈 결과라 색·지속시간으로 강조한다.
                    var name = NameOf(item.memberId);
                    if (item.asRecruit)
                        Notify("복귀", $"{name}, 이병으로 — 계급 초기화", HudTheme.Alert, 6.6f);
                    else
                        Notify("복귀", $"{name}, 원래 계급 그대로", HudTheme.Accent);
                    return;
                }

                case ServerEventTypeValues.ChoreReassigned:
                    // E-1 — Unity에 case 자체가 없어 분대장 개입이 화면에 안 보이던 항목
                    Notify("재배정", $"분대장이 {NameOf(item.toId)}에게 — {QuestLabel(item.questId)}", HudTheme.Heat);
                    return;

                case ServerEventTypeValues.SupplyClaimed:
                    Notify("보급 도착", SupplySummary(item.items), HudTheme.Accent);
                    return;

                case ServerEventTypeValues.ForcedSleep:
                    Notify("강제 취침", $"{NameOf(item.memberId)}, 이 칸 일과 잠김", HudTheme.Heat);
                    return;

                case ServerEventTypeValues.FrostbiteRelieved:
                    Notify("동상 해제", $"{NameOf(item.memberId)} — 의무병 {NameOf(item.byId)}", HudTheme.Accent);
                    return;

                case ServerEventTypeValues.ReliefGranted:
                    // B-4 — 발동은 결단이니 결과도 토스트로 못 박는다. questId가 있으면
                    // 분대장 우선순위 지정(그 퀘스트가 필수→선택으로 봐준 것), 없으면
                    // 간부 구제(그날 미달 1건을 상쇄하기로 예약한 것)다.
                    if (item.by == "leader")
                        Notify("구제 발동", $"분대장이 봐줬다 — {QuestLabel(item.questId)}", HudTheme.Heat);
                    else
                        Notify("간부 구제 발동", "오늘 저녁 발동 — 그날 미달 1건을 상쇄한다", HudTheme.Heat);
                    return;

                case ServerEventTypeValues.ReliefRefused:
                    // 침묵 판정을 새로 만들지 않는다(E단계 원칙) — 발동 버튼을 눌렀는데
                    // 아무 반응이 없으면 자동 상쇄 시절의 "썼는지도 몰랐다"와 본질이 같다.
                    Notify(item.by == "leader" ? "구제 발동 거부" : "간부 구제 발동 거부",
                        HudTheme.ReliefRefusalText(item.reason), HudTheme.Alert);
                    return;

                case ServerEventTypeValues.RadioChanged:
                {
                    // C-1b 무전 상태 역할별 문장 — `RadioLabelForMe`(우상단 상시 라벨)와는
                    // 다른 자리다. 저건 "지금 상태가 뭔가"를 계속 보여주고, 이건 "방금
                    // 뭐가 바뀌었나"를 한 번 토스트로 알린다. 통신병만 그 전환이 자기
                    // 손에서 시작됐다는 걸 안다 — 규칙은 그대로, 문장만 보직에 따라 갈린다.
                    var isComms = client != null && RoleOf(client.MemberId) == SnapshotMembersItemRoleValues.Comms;
                    var (tag, text, color) = HudDialogue.RadioChangeLine(item.radioState, isComms, CurrentDay());
                    Notify(tag, text, color);
                    return;
                }

                case ServerEventTypeValues.DelegationRefused:
                    // C-1b — snapshot.ts가 명시적으로 버리던 것을 되살렸다. 하달 창이
                    // 화면 전체를 암전시키므로(HudScreens.Backdrop) 여기 토스트는 창이
                    // 닫힌 뒤에만 보인다 — 열려 있는 동안의 표시는 DrawDelegation이 맡는다
                    Notify("하달 거부", $"{QuestLabel(item.questId)} — {HudTheme.DelegationRefusalText(item.reason)}",
                        HudTheme.Alert);
                    break;

                case ServerEventTypeValues.ConditionCritical:
                {
                    // E-2 — snapshot.ts가 "이 발주 범위 밖"이라며 명시적으로 버리던 것을
                    // 되살렸다. 스태미나 0·탈수 2단계·강제취침 임계는 후송/강제취침
                    // 직전 신호인데, 예전엔 결과(후송·강제취침 토스트)만 보이고 예고가
                    // 없었다(감사 목록 1). 같은 시간대 동안 임계가 유지되면 sim이 매
                    // 정산마다 다시 낸다(`condition.ts` raiseCriticals) — 30초 억제로
                    // 같은 사람·같은 스탯이 도배되지 않게 한다
                    var key = item.memberId + "|" + item.stat;
                    if (_criticalShownAt.TryGetValue(key, out var last) &&
                        Time.unscaledTime - last < 30f)
                    {
                        return;
                    }
                    _criticalShownAt[key] = Time.unscaledTime;
                    Notify("위험", ConditionCriticalText(item.stat, NameOf(item.memberId)), HudTheme.Alert);
                    return;
                }

                case ServerEventTypeValues.CrisisStarted:
                {
                    // B-2 — 화면이 결과(후송)보다 먼저 말한다. 즉시 후송하던 것을
                    // "위기"가 가로챈 자리다(evacuation.ts checkCollapses)
                    var name = NameOf(item.memberId);
                    var seconds = Mathf.RoundToInt((float)item.crisisMs / 1000f);
                    Sfx.Play("alarm"); // D-4 3단계 — 토스트보다 먼저 귀로 알아챈다
                    Notify("위기", $"{name}{HudTheme.Josa(name, "이", "가")} 쓰러졌다 — {seconds}초 안에 가라",
                        HudTheme.Alert, 5.6f);
                    return;
                }

                case ServerEventTypeValues.CrisisRescued:
                {
                    // B-2 — 구조 성공. 실패(시간 만료)는 새 case가 아니라 기존
                    // MemberEvacuated가 그대로 나간다 — 긴장을 물타기하지 않는다
                    var name = NameOf(item.memberId);
                    var rescuer = NameOf(item.rescuerId);
                    Notify("구조", $"{rescuer}{HudTheme.Josa(rescuer, "이", "가")} {name}{HudTheme.Josa(name, "을", "를")} 살렸다",
                        HudTheme.Accent);
                    return;
                }

                case ServerEventTypeValues.WeatherRolled:
                    Notify("기온", item.label, HudTheme.BandColor(item.band));
                    return;

                case ServerEventTypeValues.HiddenUnlocked:
                    Notify("히든", item.label, HudTheme.Accent);
                    return;

                case ServerEventTypeValues.PhaseEnded when item.lockedCount > 0d:
                    Notify("시간대 종료", $"미완료 {item.lockedCount:0}건 잠김", HudTheme.Alert);
                    return;

                case ServerEventTypeValues.Log:
                    // E-1 — 프로토콜엔 있는데 Unity가 참조 0건이던 case(감사 목록 5).
                    // 다만 "delegationWindowClosed"는 화면 신호가 아니라 delegation.ts가
                    // 내부적으로 쓰는 코드 문자열이다 — 그대로 띄우면 사람이 못 읽는
                    // 영문 토큰이 뜬다. 이미 하달 창이 스스로 닫히는 것으로 같은 사실을
                    // 말하므로(HudScreens 타이머) 그 값만 걸러낸다.
                    if (item.message == "delegationWindowClosed") return;
                    // F-2(WORKORDER) — "내일 예고 |"로 시작하면 취침 정산 화면이
                    // 전용으로 그린다(`HudScreens.cs` DrawSleep). 파이프로 구분된
                    // 원문을 토스트로 그대로 띄우면 보기 흉하니 여기서는 안 띄우고
                    // 넘긴다 — `judge.ts` `pushTomorrowPreview`가 이 형식으로 보낸다
                    // (새 프로토콜 필드 대신 기존 log 이펙트를 쓰는 이유는 그 주석 참고).
                    if (item.message.StartsWith("내일 예고 |"))
                    {
                        _screens.OnEvent(item);
                        return;
                    }
                    Notify("알림", item.message, HudTheme.Heat);
                    return;
            }

            _screens.OnEvent(item);
        }

        /// <summary>C-1b 하달 반응 — 계급 차 1단계는 편한 말투, 2단계 이상은 격식체.
        /// 총 8~12개 풀 안에서 무작위로 골라 같은 사람이 매번 똑같은 대사를 반복하지
        /// 않게 한다. 실존 부대 표현·혐오 은어는 쓰지 않는다(경계 — WORKORDER C-1b).</summary>
        private static readonly string[] AcceptCasualLines =
        {
            "또 나야?", "이번엔 진짜 마지막입니다", "예, 하겠습니다", "바로 하겠습니다",
        };

        private static readonly string[] AcceptFormalLines =
        {
            "예, 알겠습니다", "지시대로 하겠습니다", "……예.", "곧 처리하겠습니다.",
        };

        private static readonly string[] VetoLines =
        {
            "…알았다", "이번엔 넘어가마", "알겠다, 다음엔 없다", "그렇게 하지",
        };

        private static string DelegationLine(string fromRank, string toRank)
        {
            var gap = HudTheme.RankIndex(fromRank) - HudTheme.RankIndex(toRank);
            var pool = gap >= 2 ? AcceptFormalLines : AcceptCasualLines;
            return pool[UnityEngine.Random.Range(0, pool.Length)];
        }

        /// <summary>E-2 — 컨디션 붕괴 임계 3종(`packages/sim/src/condition.ts` raiseCriticals)을
        /// 문구로. 기존 침묵 토스트 12종의 건조한 톤·길이를 맞춘다 — "무슨 일이
        /// 벌어지는지"와 "그대로 두면 어떻게 되는지"를 한 줄에 담는다</summary>
        private static string ConditionCriticalText(string stat, string name) => stat switch
        {
            "stamina" => $"{name} 탈진 직전 — 그대로 두면 실려 나간다",
            "hydration" => $"{name} 탈수 직전 — 그대로 두면 실려 나간다",
            "fatigue" => $"{name} 피로 한계 — 그대로 두면 강제로 재운다",
            _ => $"{name} 컨디션 위험",
        };

        /// <summary>E-1 — SupplyClaimed의 품목 배열을 "품목 요약" 문장으로. 항목이
        /// 많으면 앞 4개만 이름으로 적고 나머지는 건수로 뭉친다</summary>
        private static string SupplySummary(string[] items)
        {
            if (items == null || items.Length == 0) return "품목 없음";
            var shown = new List<string>();
            for (var i = 0; i < items.Length && i < 4; i += 1) shown.Add(ItemNames.Of(items[i]));
            var extra = items.Length > shown.Count ? $" 외 {items.Length - shown.Count}건" : "";
            return string.Join(" · ", shown) + extra;
        }

        /// <summary>ScreenLab(F10) 전용 — 토스트를 실물 그리기 코드(`Notify`·`DrawToasts`)
        /// 그대로 띄운다. 서버 이벤트를 거치지 않을 뿐 화면에 쌓이고 사라지는 방식은
        /// 실전과 완전히 같다 — 여기서 그리기를 복붙하지 않는 이유가 이것이다</summary>
        public void DebugNotify(string tag, string text, Color color, float life = 4.6f) =>
            Notify(tag, text, color, life);

        /// <summary>ScreenLab(F10) 전용 — 컨디션 게이지(`DrawCondition`)를 가짜 스냅샷으로
        /// 그대로 그린다. 이미 `snapshot` 파라미터를 받는 구조라 새 코드 없이 노출만 한다</summary>
        public void DebugCondition(Snapshot snapshot) => DrawCondition(snapshot);

        /// <summary>ScreenLab(F10) 전용 — 재접속 배너를 문구·색만 넣어 그대로 띄운다.
        /// 아래 <see cref="DrawConnectionBanner()"/>가 여전히 실전 경로(`boot`)를 그대로 쓴다 —
        /// 이 오버로드는 그 안의 그리기만 떼어 판 것이지 판정을 새로 만들지 않았다</summary>
        public void DebugConnectionBanner(string text, Color color) => DrawConnectionBanner(text, color);

        /// <summary>ScreenLab(F10) 전용 — `DebugNotify`로 쌓은 토스트를 실제로 그린다.
        /// 랩이 열려 있는 동안은 `OnGUI`의 실전 `DrawToasts()` 호출까지 도달하지
        /// 않으므로(§조기 return) 이 통로가 없으면 쌓인 토스트가 화면에 안 뜬다</summary>
        public void DebugDrawToasts() => DrawToasts();

        /// <summary>ScreenLab(F10) 전용 — 보온 게이지(`DrawBodyHeat`)를 가짜 스냅샷으로
        /// 그대로 그린다. `DrawCondition`과 같은 이유로 새 코드 없이 노출만 한다</summary>
        public void DebugBodyHeat(Snapshot snapshot) => DrawBodyHeat(snapshot);

        private void Notify(string tag, string text, Color color, float life = 4.6f)
        {
            _toasts.Add(new Toast { tag = tag, text = text, color = color, born = Time.unscaledTime, life = life });
            // §7.1.6 최대 4개 스택
            while (_toasts.Count > 4) _toasts.RemoveAt(0);
        }

        private void DrawToasts()
        {
            for (var i = _toasts.Count - 1; i >= 0; i -= 1)
            {
                if (Time.unscaledTime - _toasts[i].born > _toasts[i].life) _toasts.RemoveAt(i);
            }

            // 목업 실측: 380×64 @ (48, 48), 간격 76
            for (var i = 0; i < _toasts.Count; i += 1)
            {
                var toast = _toasts[i];
                var age = Time.unscaledTime - toast.born;
                var fadeStart = toast.life - 0.6f;
                var alpha = age > fadeStart ? Mathf.Clamp01(1f - (age - fadeStart) / 0.6f) : 1f;

                var rect = new Rect(48f, 132f + i * 76f, 380f, 64f);
                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);

                _theme.Fill(rect, HudTheme.Paper, 0.92f * alpha);
                _theme.Spine(rect, toast.color);
                GUI.Label(new Rect(rect.x + 22f, rect.y + 8f, 340f, 20f), toast.tag,
                    _theme.At(_theme.Label, 12, toast.color));
                GUI.Label(new Rect(rect.x + 22f, rect.y + 30f, 340f, 26f), toast.text,
                    _theme.At(_theme.Body, 17, HudTheme.Ink));

                GUI.color = previous;
            }
        }

        private string NameOf(string memberId)
        {
            var snapshot = client.Latest;
            if (snapshot?.members == null || string.IsNullOrEmpty(memberId)) return "누군가";
            foreach (var member in snapshot.members)
            {
                if (member?.id == memberId) return member.name;
            }
            return "누군가";
        }

        /// <summary>C-1b — 계급 조합별 하달 반응 문구를 고를 때 쓴다</summary>
        private string RankOf(string memberId)
        {
            var snapshot = client.Latest;
            if (snapshot?.members == null || string.IsNullOrEmpty(memberId)) return SnapshotMembersItemRankValues.Private;
            foreach (var member in snapshot.members)
            {
                if (member?.id == memberId) return member.rank;
            }
            return SnapshotMembersItemRankValues.Private;
        }

        /// <summary>E-1 — 재배정·하달 거부 토스트에 일과 이름을 함께 적는다</summary>
        private string QuestLabel(string questId)
        {
            var snapshot = client.Latest;
            if (snapshot?.quests == null || string.IsNullOrEmpty(questId)) return "일과";
            foreach (var quest in snapshot.quests)
            {
                if (quest?.id == questId) return quest.label;
            }
            return "일과";
        }

        /// <summary>C-1b — 대사 결정론 시드용. 스냅샷이 아직 없을 때(연결 직후)는
        /// 0으로 떨어지는데, 그 시점엔 애초에 이 문구들을 낼 이벤트도 안 온다</summary>
        private int CurrentDay() => (int)(client.Latest?.day ?? 0d);

        /// <summary>C-1b 후송 문장 — 방금 실려 나간 사람을 뺀 나머지 인원 수를 센다.
        /// 이미 후송된 사람(`Evacuated`)도 뺀다 — "남은 인원"은 지금 뛰고 있는
        /// 사람 수지, 편성표에 이름만 남은 사람 수가 아니다</summary>
        private int RemainingCount(string evacuatedId)
        {
            var snapshot = client.Latest;
            if (snapshot?.members == null) return 0;
            var count = 0;
            foreach (var member in snapshot.members)
            {
                if (member == null || member.id == evacuatedId) continue;
                if (member.presence == SnapshotMembersItemPresenceValues.Evacuated) continue;
                count += 1;
            }
            return count;
        }

        /// <summary>
        /// C-1b 무전 역할 분기 — 두절 문구를 통신병 화면에서만 다르게 읽힌다.
        ///
        /// **규칙은 그대로다.** 무전 상태(`ZoneVisibility.Radio`)는 서버가 소유한
        /// 분대 공통 값이라 손대지 않는다 — 여기서 바뀌는 것은 그 값을 옮기는
        /// 문자열뿐이다(클라 분기만). 통신병은 "본인이 그 두절의 당사자"라는 것을
        /// 다른 보직과 같은 문장으로는 알 길이 없었다.
        /// </summary>
        public string RadioLabelForMe(RadioState radio, ZoneVisibility vis)
        {
            if (radio == RadioState.Down && client != null && RoleOf(client.MemberId) == SnapshotMembersItemRoleValues.Comms)
                return "네 안테나가 죽었다";
            return vis != null ? vis.RadioLabel : "무전 연결";
        }

        private string RoleOf(string memberId)
        {
            var snapshot = client.Latest;
            if (snapshot?.members == null || string.IsNullOrEmpty(memberId)) return "";
            foreach (var member in snapshot.members)
            {
                if (member?.id == memberId) return member.role;
            }
            return "";
        }

        /* ─────────────────────────────────────── §7.1.3 우상단 미니맵 */

        private void DrawMinimap(Snapshot snapshot)
        {
            // 미니맵·수첩 요약을 우상단에 한 덩어리로 붙인다(§7.1.3+§7.1.4 재배치).
            //
            // 폭은 **수첩 쪽(280) 기준으로 통일했다** — 반대로 수첩을 미니맵 폭(220)에
            // 맞추면 `DrawNotebookSummary`의 행이 라벨 120px + 값 140px 고정폭이라
            // 220에서는 이미 여백 없이 겹치던 것이 더 잘려 보인다. 미니맵은 내용이
            // `DrawSectorPlan`에서 컨테이너에 맞춰 자동으로 프레이밍되므로 넓혀도 안전하다.
            // x는 기존 수첩과 같은 1592(오른쪽 끝 1872 = 1920 - SafeMargin 48)를 그대로 쓴다.
            var rect = new Rect(HudTheme.RightOf(1592f, 280f), 48f, 280f, 220f);
            _theme.Fill(rect, HudTheme.Paper, 0.94f);
            _theme.Border(rect, HudTheme.Rule);

            var head = new Rect(rect.x, rect.y, rect.width, 26f);
            _theme.Fill(head, HudTheme.Paper2);
            GUI.Label(new Rect(head.x + 12f, head.y, 120f, head.height), "SECTOR MAP",
                _theme.At(_theme.Label, 11, HudTheme.Ink2));

            var radio = visibility != null ? visibility.Radio : RadioState.Ok;
            var radioColor = visibility != null ? visibility.RadioColor : HudTheme.Accent;
            var label = RadioLabelForMe(radio, visibility);

            // §7.1.3 두절이면 적색 라벨이 항상 떠 있어야 한다
            GUI.Label(new Rect(head.x + 100f, head.y, 96f, head.height), label,
                _theme.At(_theme.Label, 11, radioColor, TextAnchor.MiddleRight));
            HudIcons.Dot(new Rect(head.xMax - 16f, head.y + 9f, 8f, 8f), radioColor);

            var body = new Rect(rect.x + 12f, head.yMax + 10f, rect.width - 24f, rect.height - head.height - 22f);

            // **잘라낸다.** 미니맵은 구역을 확대해서 보므로 옆방이 패널 밖으로
            // 뻗어 나간다. 그대로 두면 지도가 화면 절반에 그려진다.
            // 그룹 안에서는 좌표가 그룹 기준이라 원점을 0으로 넘긴다
            GUI.BeginGroup(body);
            DrawSectorPlan(new Rect(0f, 0f, body.width, body.height), snapshot, radio);
            GUI.EndGroup();
        }

        /// <summary>
        /// 부대 배치도. 미니맵과 부대 지도(§7.9)가 **같은 그림**을 쓴다 —
        /// 두 화면의 배치가 다르면 익힌 지도가 쓸모없어진다.
        ///
        /// 방을 낱개로 늘어놓지 않고 **동 단위 사각형**으로 묶는다. 방 25개가
        /// 다 그려지면 어느 것이 한 건물인지 읽히지 않고, 부대를 익히는 단위는
        /// 방이 아니라 동이다.
        ///
        /// 내 위치는 구역 중앙이 아니라 **실제 좌표**로 찍는다. 중앙에 고정하면
        /// 큰 연병장에서 어디쯤인지 알 수 없고, 걸어도 점이 안 움직인다.
        /// </summary>
        /// <summary>
        /// 구역이 속한 권역.
        ///
        /// 부대와 훈련장은 걸어서 이어져 있지만 **한 지도에 그릴 것은 아니다.**
        /// 사이드뷰 코스는 아예 다른 화면이라 또 따로 둔다.
        /// </summary>
        /// <summary>
        /// 미니맵이 한 번에 보는 폭(타일).
        ///
        /// 부대에서 제일 넓은 야외 구역(연병장 46타일)이 겨우 들어가는 값이다 —
        /// 이보다 넓히면 방이 손톱만 해지고, 좁히면 연병장 한가운데서 사방이
        /// 잘려 어디쯤인지 알 수 없다.
        /// </summary>
        private const float MaxView = 48f;

        /// <summary>
        /// 시야 중심을 지도 안에 물린다. 다 담기면 가운데로 놓는다.
        /// </summary>
        private static float Framed(float at, float view, float lo, float hi) =>
            view >= hi - lo
                ? (lo + hi) * 0.5f
                : Mathf.Clamp(at, lo + view * 0.5f, hi - view * 0.5f);

        private static string RegionOf(string zone)
        {
            if (string.IsNullOrEmpty(zone) || !zone.StartsWith("TR")) return "base";
            // TR03 · TR07 · TR08 은 사이드뷰 코스다 — 위에서 본 지도가 아니다
            return zone == "TR03" || zone == "TR07" || zone == "TR08" ? "lane" : "training";
        }

        /// <summary>
        /// 부대 배치도 (§7.9). 미니맵과 부대 지도가 **같은 그림**을 쓴다 —
        /// 두 화면의 배치가 다르면 익힌 지도가 쓸모없어진다.
        ///
        /// ── 도면처럼 그린다 ──────────────────────────────────────────────
        /// 이 화면을 세 번 고쳐 쓰고 나서야 문제가 방식에 있다는 것이 분명해졌다.
        /// 구역마다 **채운 사각형**을 그리면 무엇을 해도 상자가 늘어선 표가 되지
        /// 배치도가 되지 않는다. 그래서 단위를 바꿨다 — 이 함수가 그리는 것은
        /// 사각형이 아니라 **벽과 문**이다.
        ///
        ///   1. 벽    한 겹 선. 동 외벽은 두껍고 방 칸막이는 얇다
        ///   2. 문    벽을 **지운 자리**다. 도면이 문을 그리는 방식 그대로이고,
        ///            "여기로 드나든다"가 선 하나 없이 읽힌다
        ///   3. 채움  지금 서 있는 구역에만. 그것이 이 지도의 유일한 상태다
        ///
        /// 나머지(이름 · 분대원 마커)는 그 위에 얹는다.
        /// </summary>
        public void DrawSectorPlan(Rect area, Snapshot snapshot, RadioState radio, bool detailed = false)
        {
            if (world?.zones == null || world.zones.Length == 0) return;

            // 지금 있는 권역만 그린다. 훈련 맵까지 한 장에 넣으면 부대가
            // 손톱만 해지고, 지도의 목적은 "여기가 어디인가"이지 세계의 크기가 아니다
            var region = RegionOf(visibility != null ? visibility.CurrentZone : null);

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var zone in world.zones)
            {
                if (zone == null || RegionOf(zone.id) != region) continue;
                min = Vector2.Min(min, zone.area.min);
                max = Vector2.Max(max, zone.area.max);
            }
            if (max.x <= min.x || max.y <= min.y) return;

            // 여백 — 도면은 종이 끝에 붙여 그리지 않는다. 동 이름이 위로 나가므로
            // 위쪽을 더 준다
            var pad = detailed ? 26f : 14f;
            var inner = new Rect(area.x + pad, area.y + pad, area.width - pad * 2f,
                                 area.height - pad * 2f);

            // **미니맵은 지금 있는 구역을 확대해서 본다.**
            //
            // 권역 전체를 220px 안에 욱여넣으면 방 하나가 손톱만 해져서,
            // 내가 어느 방에 있는지는 알아도 **그 방 안 어디인지**를 알 수 없다.
            // 부대 전체 배치는 지도 창(TAB)이 맡고, 여기는 "지금 여기"만 본다.
            // 둘레를 조금 남기는 것은 옆방으로 가는 문이 보여야 하기 때문이다.
            if (!detailed && world.Here != null)
            {
                var seen = world.Here.area;

                // 구역과 그 둘레 55%가 패널을 채우는 배율 — 방에서는 지금까지와 같다
                var fit = Mathf.Min(inner.width / (seen.width * 2.1f),
                                    inner.height / (seen.height * 2.1f));

                // **한 번에 이보다 넓게 보지 않는다.**
                //
                // 구역을 통째로 담으려 하면 긴 구역에서 무너진다 — 생활관동 복도는
                // 56타일짜리 한 칸이라, 둘레까지 얹으면 118타일을 220px에 넣게 되고
                // 배율이 부대 지도와 같아진다. 지도 밖까지 프레임이 나가서 패널
                // 왼쪽 위 3분의 1이 빈 종이가 되고, 부대는 오른쪽 아래로 쏠린다.
                // 그게 복도에서 미니맵이 깨져 보이던 것의 정체다.
                var zoom = Mathf.Max(fit, inner.width / MaxView);
                var view = new Vector2(inner.width / zoom, inner.height / zoom);

                // 다 못 담으면 **사람을 따라간다.** 구역 중앙에 고정하면 복도
                // 동쪽 끝에 서 있는데 화면은 서쪽 문을 비춘다
                var focus = world.player != null
                    ? (Vector2)world.player.transform.position
                    : seen.center;

                // 지도 밖을 비추지 않는다 — 빈 종이가 차지한 만큼 부대가 줄어든다
                focus = new Vector2(Framed(focus.x, view.x, min.x, max.x),
                                    Framed(focus.y, view.y, min.y, max.y));

                min = focus - view * 0.5f;
                max = focus + view * 0.5f;
            }

            var span = max - min;
            var k = Mathf.Min(inner.width / span.x, inner.height / span.y);
            var ox = inner.x + (inner.width - span.x * k) * 0.5f;
            var oy = inner.y + (inner.height - span.y * k) * 0.5f;

            // 월드 y는 위로, 화면 y는 아래로 증가한다
            Vector2 ToScreen(Vector2 at) =>
                new Vector2(ox + (at.x - min.x) * k, oy + (max.y - at.y) * k);

            Rect BoxOf(Rect r)
            {
                var a = ToScreen(new Vector2(r.xMin, r.yMax));
                return new Rect(a.x, a.y, r.width * k, r.height * k);
            }

            bool InFrame(Rect r) => r.Overlaps(new Rect(min.x, min.y, span.x, span.y));

            bool TryBuilding(Rect r, out ZoneWorld.Building found)
            {
                foreach (var b in world.buildings)
                {
                    if (b.area.Contains(r.center)) { found = b; return true; }
                }
                found = default;
                return false;
            }

            var here = visibility != null ? visibility.CurrentZone : "";

            // 배치도에는 선이 **두 종류뿐**이다.
            //
            //   heavy  영역 벽 — 동과 독립 구역을 감싸는 테두리
            //   light  격벽   — 그 영역 안에서 방을 나누는 칸막이
            //
            // **둘의 차이가 눈에 띄어야** "이건 건물 하나이고 안이 이렇게 나뉘어
            // 있다"가 읽힌다. 한때 1.68px 대 1.00px까지 좁혀놓았더니 두 선이
            // 같아 보였고, 그러면 방마다 상자가 늘어선 것과 구분이 안 된다.
            //
            // 최소 3배를 유지한다. 미니맵처럼 배율이 작을 때도 영역 벽은
            // 2px 아래로 내려가지 않는다 — 그 아래는 선이 아니라 얼룩이다.
            var heavy = Mathf.Max(2f, k * 0.45f);
            var light = Mathf.Max(1f, k * 0.12f);

            // **선은 정수 픽셀에 앉힌다.**
            //
            // 격벽과 문은 같은 위치를 계산해서 나오는데(둘 다 두 방의 중점),
            // 소수점이 조금 다르면 1px짜리 선이 서로 다른 픽셀 줄에 떨어진다 —
            // 그게 "두께는 같은데 주황색이 한 줄 위" 의 정체였다.
            //
            // 스냅하면 값이 반 픽셀 안쪽에서 다르든 같은 줄에 앉는다. 덤으로
            // 1px 선이 두 줄에 반씩 걸쳐 흐려지는 것도 없어진다.
            Rect Snap(Rect r) => new Rect(
                Mathf.Round(r.x), Mathf.Round(r.y),
                Mathf.Max(1f, Mathf.Round(r.width)), Mathf.Max(1f, Mathf.Round(r.height)));

            void Wall(float x, float y, float w, float h, Color c) =>
                _theme.Fill(Snap(new Rect(x, y, w, h)), c);

            void Outline(Rect box, float weight, Color c)
            {
                Wall(box.xMin, box.yMin, box.width, weight, c);
                Wall(box.xMin, box.yMax - weight, box.width, weight, c);
                Wall(box.xMin, box.yMin, weight, box.height, c);
                Wall(box.xMax - weight, box.yMin, weight, box.height, c);
            }

            /// 문이 앉을 자리. **자기가 뚫은 벽과 정확히 겹쳐야** 한다 —
            /// 반 칸이라도 어긋나면 벽 옆에 색 띠가 하나 더 붙은 것처럼 보인다
            Rect DoorLine(ZoneWorld.Door door)
            {
                var box = BoxOf(door.area);
                var vertical = door.area.width < door.area.height;
                var thick = door.isExit ? heavy : light;

                if (door.isExit)
                {
                    // 동 출입구는 외벽 위에 있다. 외벽 선은 동 사각형의 변에
                    // 붙어 그려지므로 그 변에 맞춘다
                    foreach (var b in world.buildings)
                    {
                        if (!b.area.Overlaps(door.area)) continue;
                        var shell = BoxOf(b.area);
                        return vertical
                            ? new Rect(box.center.x < shell.center.x
                                           ? shell.xMin : shell.xMax - thick,
                                       box.yMin, thick, box.height)
                            : new Rect(box.xMin,
                                       box.center.y < shell.center.y
                                           ? shell.yMin : shell.yMax - thick,
                                       box.width, thick);
                    }
                }

                // 방문은 격벽 한가운데에 온다 — 격벽도 두 방의 중점에 긋는다
                return vertical
                    ? new Rect(box.center.x - thick * 0.5f, box.yMin, thick, box.height)
                    : new Rect(box.xMin, box.center.y - thick * 0.5f, box.width, thick);
            }

            /* ── 1. 지금 서 있는 구역 — 유일한 채움 ── */
            foreach (var zone in world.zones)
            {
                if (zone == null || zone.id != here) continue;
                if (RegionOf(zone.id) != region) continue;
                _theme.Fill(BoxOf(zone.area), HudTheme.Accent, 0.30f);
            }

            /* ── 2. 벽 ── */

            // 동 외벽 · 독립 구역 — 두꺼운 선
            foreach (var building in world.buildings)
            {
                if (!InFrame(building.area)) continue;
                Outline(BoxOf(building.area), heavy, HudTheme.Accent);
            }
            foreach (var zone in world.zones)
            {
                if (zone == null || RegionOf(zone.id) != region) continue;
                // 야외이거나, 동에 속하지 않은 단독 건물(보일러실)이거나
                if (zone.kind != "outdoor" && TryBuilding(zone.area, out _)) continue;
                Outline(BoxOf(zone.area), heavy, HudTheme.Accent);
            }

            // 방 칸막이 — 얇은 선. **동 외벽에 닿는 변은 그리지 않는다.**
            // 그리면 외벽이 두 겹이 되어 "벽 안에 방벽이 또" 있는 모양이 된다
            //
            // **격벽은 두 방의 한가운데 한 줄만 긋는다.**
            //
            // 이 맵은 이웃이 두 가지다 — 가로로는 벽 타일 한 칸을 사이에 두고
            // 떨어져 있고, 세로로는 (방과 복도처럼) 변을 그대로 맞대고 있다.
            // 앞서 전부 한 칸씩 부풀렸더니, 이미 맞닿아 있던 복도 쪽이 서로를
            // 지나쳐 **이중벽**이 됐다.
            //
            // 두 경우를 한 규칙으로 푼다 — 가까운 맞은편 변을 찾아 그 **중점**에
            // 긋는다. 맞닿아 있으면 중점이 곧 그 변이고, 한 칸 떨어져 있으면
            // 벽 한가운데다. 어느 쪽이든 선은 한 줄이다.
            var peers = new List<Rect>();

            foreach (var zone in world.zones)
            {
                if (zone == null || zone.kind == "outdoor") continue;
                if (RegionOf(zone.id) != region) continue;
                if (!TryBuilding(zone.area, out var owner)) continue;

                peers.Clear();
                foreach (var other in world.zones)
                {
                    if (other == null || other == zone || other.kind == "outdoor") continue;
                    if (!owner.area.Contains(other.area.center)) continue;
                    peers.Add(other.area);
                }

                var r = zone.area;

                // 맞은편 변이 1.5타일 안이면 그 중점으로 옮긴다.
                //
                // **떨어진 경우와 겹친 경우를 모두 본다.** 방끼리는 벽 타일을
                // 사이에 두고 떨어져 있고(gap +1), 방과 복도는 벽 한 장을
                // 공유하느라 사각형이 겹친다(gap −1).
                //
                // 그리고 **실제로 맞닿은 방만** 후보로 삼는다. 이걸 빼먹었더니
                // 체단장(아래 줄)의 왼쪽 변이 2분대(위 줄)의 오른쪽 변과 우연히
                // 1타일 거리라 그쪽으로 붙었고, 사지방은 제자리에 그어서
                // 그 사이만 선이 두 줄이 됐다. 닿지도 않은 방과 벽을 나눌 수는 없다.
                float Span(float a1, float a2, float b1, float b2) =>
                    Mathf.Min(a2, b2) - Mathf.Max(a1, b1);

                float Meet(float mine, System.Func<Rect, float> facing, bool horizontal)
                {
                    var best = mine;
                    var near = float.MaxValue;
                    foreach (var p in peers)
                    {
                        // 수직 방향으로 겹쳐야 옆에 붙은 방이다.
                        // 공유 벽 한 장(1타일)만 겹친 것은 옆이 아니라 위아래다
                        var shared = horizontal
                            ? Span(r.yMin, r.yMax, p.yMin, p.yMax)
                            : Span(r.xMin, r.xMax, p.xMin, p.xMax);
                        if (shared <= 1.5f) continue;

                        var gap = Mathf.Abs(facing(p) - mine);
                        if (gap > 1.5f || gap >= near) continue;
                        near = gap;
                        best = (mine + facing(p)) * 0.5f;
                    }
                    return best;
                }

                var left = Meet(r.xMin, p => p.xMax, true);
                var right = Meet(r.xMax, p => p.xMin, true);
                var bottom = Meet(r.yMin, p => p.yMax, false);
                var top = Meet(r.yMax, p => p.yMin, false);

                var box = BoxOf(Rect.MinMaxRect(left, bottom, right, top));
                var shell = BoxOf(owner.area);

                // 동 외벽에 닿는 변은 그리지 않는다 — 그리면 외벽이 두 겹이 된다.
                // 오차는 픽셀이 아니라 **타일**로 잰다. 미니맵과 지도는 배율이
                // 다르고, 고정 픽셀로 두면 한쪽에서만 성립한다
                var edge = k * 0.9f;

                // **선 위에 걸쳐 긋는다** — 안쪽으로 밀어 그리면 문과 반 두께만큼
                // 어긋난다. 문은 제 자리를 중심으로 그리기 때문이고, 그 반 칸이
                // 지도에서 "벽 옆에 색 띠가 하나 더 붙은" 모양으로 보였다.
                var half = light * 0.5f;
                if (box.xMin > shell.xMin + edge)
                    Wall(box.xMin - half, box.yMin, light, box.height, HudTheme.Accent);
                if (box.xMax < shell.xMax - edge)
                    Wall(box.xMax - half, box.yMin, light, box.height, HudTheme.Accent);
                if (box.yMin > shell.yMin + edge)
                    Wall(box.xMin, box.yMin - half, box.width, light, HudTheme.Accent);
                if (box.yMax < shell.yMax - edge)
                    Wall(box.xMin, box.yMax - half, box.width, light, HudTheme.Accent);
            }

            /* ── 3. 문 = 벽 위에 덧댄 주황색 선 ──
             *
             * 벽을 **지우지 않는다.** 지워서 틈을 내면 도면답기는 한데, 동
             * 출입구가 복도 폭 그대로(4타일) 뚫려 있어서 좌우 외벽이 그 높이만큼
             * 통째로 사라지고 동이 위아래 두 덩이로 갈라져 보였다.
             *
             * 대신 그 자리를 **색으로 덧댄다.** 윤곽은 끊기지 않은 채로 남고,
             * 주황색이 곧 "여기가 열려 있다"가 된다 — 이 화면에서 초록이 아닌
             * 유일한 색이라 세지 않아도 눈에 들어온다.
             *
             * **문은 전부 열려 있다.** 한때 다른 분대 생활관을 "잠김"으로 적어
             * 뒀지만 그 플래그를 아무도 안 봐서 실제로는 늘 들어갈 수 있었다.
             * 지금은 그 거짓말을 지웠고, 지도도 있는 그대로 그린다. */
            foreach (var door in world.doors)
            {
                if (!InFrame(door.area)) continue;
                _theme.Fill(Snap(DoorLine(door)), HudTheme.Heat);
            }

            /* ── 4. 이름 ──
             *
             * "방 이름이 한눈에 안 들어온다"(플레이어 보고). 셋을 합쳐서 푼다.
             *
             *   1. 글자 뒤에 종이색 칩을 깐다 — 벽선·문 위에 얹혀도 읽힌다
             *   2. 상자에 안 들어가면 밖으로 빼고 가는 지시선으로 잇는다
             *   3. 글자를 키운다 — 예전 13pt 상한은 1920 기준 전체화면
             *      지도에는 작았다
             *
             * **이름은 방 한가운데**에 놓는다(칩이 있으면). 왼쪽 위에 붙이면
             * 벽선과 붙어서 글자가 벽에 걸린 표찰처럼 보이고, 어느 방의
             * 이름인지도 한 칸 애매해진다. 짧은 이름을 쓰는 것도 그대로다 —
             * 긴 이름은 좁은 방에서 잘리고, 잘린 이름은 없는 이름과 같다. */
            if (detailed)
            {
                // 폰트는 상자 **높이와 폭을 같이** 본다. 예전에는 높이만 보고
                // 정했는데, 무기고처럼 세로로 길고 가로가 좁은 방은 높이 기준
                // 글자가 폭을 넘어설 뻔했다. `GUIStyle.CalcSize`로 정확히 잴
                // 수도 있지만 그건 프레임마다 `GUIContent`를 새로 만든다(M0
                // "힙 누수 0"). 한글 완성형 글자는 폭이 폰트 크기에 거의
                // 맞먹는 정사각형이라 "글자 수 × 폰트 크기"면 넉넉히 맞고,
                // `widthMargin`이 오차를 흡수한다.
                const float minFont = 10f, maxFont = 20f, heightMul = 0.16f;
                const float widthMargin = 14f, charWidth = 1f;

                // 칩은 글자 폭만큼만 깐다 — 방을 다 덮으면 채움처럼 보인다
                Rect ChipOf(Vector2 center, string text, float fontSize) =>
                    new Rect(center.x - (text.Length * fontSize * charWidth + 8f) * 0.5f,
                              center.y - (fontSize * 1.3f + 4f) * 0.5f,
                              text.Length * fontSize * charWidth + 8f, fontSize * 1.3f + 4f);

                // 상자 안에 못 들어가는 방(현재 base_map.json 25개 중에는 없다 —
                // 계산으로 확인했다)은 여기로 온다. 네 방향으로 "다음 방(복도
                // 제외)까지 거리"를 재서 가장 넓게 뚫린 쪽에 이름을 내놓는다.
                // 복도는 이름이 없는 통로라 지나가도 되지만, **다른 방과는
                // 겹치면 안 된다** — 그래서 복도만 후보에서 뺀다.
                void DrawOutside(ZoneMap zone, Rect box, string label, Color color)
                {
                    float ClearWorld(Vector2 dir)
                    {
                        var r = zone.area;
                        var best = 6f; // 후보가 없으면 반 칸만 — 안전 쪽으로
                        foreach (var other in world.zones)
                        {
                            if (other == null || other == zone) continue;
                            if (RegionOf(other.id) != region || other.kind == "corridor") continue;

                            if (dir.x != 0f)
                            {
                                var overlap = Mathf.Min(r.yMax, other.area.yMax) -
                                              Mathf.Max(r.yMin, other.area.yMin);
                                if (overlap <= 0f) continue;
                                var gap = dir.x > 0f
                                    ? other.area.xMin - r.xMax
                                    : r.xMin - other.area.xMax;
                                if (gap >= 0f) best = Mathf.Min(best, gap);
                            }
                            else
                            {
                                var overlap = Mathf.Min(r.xMax, other.area.xMax) -
                                              Mathf.Max(r.xMin, other.area.xMin);
                                if (overlap <= 0f) continue;
                                var gap = dir.y > 0f
                                    ? other.area.yMin - r.yMax
                                    : r.yMin - other.area.yMax;
                                if (gap >= 0f) best = Mathf.Min(best, gap);
                            }
                        }
                        return best;
                    }

                    var chosen = Vector2.right;
                    var bestClear = -1f;
                    foreach (var d in new[] { Vector2.right, Vector2.left, Vector2.up, Vector2.down })
                    {
                        var c = ClearWorld(d);
                        if (c <= bestClear) continue;
                        bestClear = c;
                        chosen = d;
                    }

                    // 화면 y는 월드 y와 반대로 커진다
                    var screenDir = new Vector2(chosen.x, -chosen.y);
                    var reach = Mathf.Min(bestClear * k * 0.5f, 44f);
                    var edge = chosen.x != 0f ? box.width * 0.5f : box.height * 0.5f;
                    var start = box.center + screenDir * edge;
                    var end = start + screenDir * reach;

                    // 지시선 — 가늘게, 도면의 인출선과 같은 무게
                    if (chosen.x != 0f)
                        _theme.Fill(new Rect(Mathf.Min(start.x, end.x), start.y - 0.5f,
                            Mathf.Abs(end.x - start.x), 1f), HudTheme.Rule2);
                    else
                        _theme.Fill(new Rect(start.x - 0.5f, Mathf.Min(start.y, end.y),
                            1f, Mathf.Abs(end.y - start.y)), HudTheme.Rule2);

                    var style = _theme.At(_theme.Small, (int)minFont, color, TextAnchor.MiddleCenter);
                    var tag = ChipOf(end, label, minFont);
                    _theme.Fill(tag, HudTheme.Paper, 0.88f);
                    GUI.Label(tag, label, style);
                }

                foreach (var zone in world.zones)
                {
                    if (zone == null || RegionOf(zone.id) != region) continue;
                    if (zone.kind == "corridor") continue;   // 복도는 지나가는 곳이다

                    var box = BoxOf(zone.area);
                    var label = ZoneNames.ShortOf(zone.id);
                    var color = zone.id == here ? HudTheme.Ink : HudTheme.Ink2;

                    // **이름은 방 한가운데**에 놓고, 글자 뒤에 종이색 칩을 깐다.
                    // 왼쪽 위에 붙이면 글자가 벽에 걸린 표찰처럼 보이고, 칩이
                    // 없으면 벽선과 문 위에 글자가 그대로 얹혀 둘 다 안 읽힌다.
                    //
                    // 크기는 높이와 **폭** 둘 다로 잰다. 높이로만 재던 시절에는
                    // 좁고 긴 방에서 이름이 방을 넘어가 잘렸고, **잘린 이름은
                    // 없는 이름과 같다**.
                    var byHeight = Mathf.Clamp(box.height * heightMul, minFont, maxFont);
                    var byWidth = label.Length > 0
                        ? (box.width - widthMargin) / (label.Length * charWidth)
                        : maxFont;
                    var size = Mathf.Min(byHeight, byWidth);

                    if (size < minFont || box.width < 40f || box.height < 22f)
                    {
                        DrawOutside(zone, box, label, color);
                        continue;
                    }

                    var fontSize = Mathf.FloorToInt(size);
                    var style = _theme.At(_theme.Small, fontSize, color, TextAnchor.MiddleCenter);
                    _theme.Fill(ChipOf(box.center, label, size), HudTheme.Paper, 0.82f);
                    GUI.Label(box, label, style);
                }
            }

            // 동 이름은 외벽 **위쪽 바깥**에 — 도면의 표제와 같은 자리다
            foreach (var building in world.buildings)
            {
                if (!InFrame(building.area)) continue;
                var box = BoxOf(building.area);
                if (!detailed && box.width <= 40f) continue;

                var mine = world.Here != null && building.area.Overlaps(world.Here.area);
                GUI.Label(new Rect(box.x, box.y - (detailed ? 28f : 13f),
                                   Mathf.Max(box.width, detailed ? 220f : 96f), 24f),
                    building.name,
                    _theme.At(_theme.Label, detailed ? 13 : 11,
                        mine ? HudTheme.Accent : HudTheme.Ink3));
            }

            if (snapshot?.members == null) return;

            /* ── 5. 사람 ── */

            // 내 위치 — **실제 좌표**로 찍는다. 구역 중앙에 고정하면 걸어도
            // 점이 안 움직이고, 넓은 연병장에서 어디쯤인지 알 수 없다
            if (world.player != null &&
                InFrame(new Rect(world.player.transform.position, Vector2.one)))
            {
                var me = ToScreen(world.player.transform.position);
                HudIcons.Circle(new Rect(me.x - 7f, me.y - 7f, 14f, 14f), 2f, HudTheme.White);
                HudIcons.Dot(new Rect(me.x - 3f, me.y - 3f, 6f, 6f), HudTheme.White);
            }

            foreach (var member in snapshot.members)
            {
                if (member == null || member.id == client.MemberId) continue;

                // §1.3-C 마커 게이팅 — 같은 구역이면 늘 보이고, 다른 구역은 무전이 살아야 한다
                var sameZone = visibility == null || visibility.CanSee(member.zone);
                if (!sameZone && radio == RadioState.Down) continue;
                if (!sameZone && !(visibility?.MarkerVisible ?? true)) continue;

                ZoneMap zone = null;
                foreach (var candidate in world.zones)
                {
                    if (candidate != null && candidate.id == member.zone) { zone = candidate; break; }
                }
                if (zone == null || RegionOf(zone.id) != region) continue;

                var at = ToScreen(zone.area.center);
                var dot = new Rect(at.x - 5f, at.y - 5f, 10f, 10f);
                var roleColor = HudTheme.RoleColor(member.role);

                // H-4 색각 이상 대응 — 형태 = 보직(HudIcons.RoleShape), 알파 = 가시성.
                // 예전에는 형태(속 찬 점/속 빈 원)가 가시성을 맡고 색 하나가 보직을
                // 전부 졌다 — 적록색각에서 소총·의무 색이 겹쳐 보이면 구분할 방법이
                // 없었다. §7.9 범례가 이 둘을 나눠서 설명한다(HudScreens.cs DrawBaseMap)
                var color = sameZone ? roleColor : new Color(roleColor.r, roleColor.g, roleColor.b, 0.55f);
                HudIcons.RoleShape(member.role, dot, color);
            }
        }

        /// <summary>벽 두께만큼 부풀린다 — 이웃 방과 변을 맞대게 하려는 것이다</summary>
        private static Rect Grow(Rect r, float by) =>
            new Rect(r.x - by * 0.5f, r.y - by * 0.5f, r.width + by, r.height + by);

        /* ─────────────────────────────────────── §7.1.4 우측 수첩 요약 */

        private void DrawNotebookSummary(Snapshot snapshot)
        {
            // 미니맵 바로 아래 붙인다(§7.1.3+§7.1.4). 폭 280은 `DrawMinimap`과
            // 통일한 값 — 근거는 그쪽 주석 참고. x도 같은 RightOf(1592, 280)를 써서
            // 오른쪽 변을 정확히 맞춘다.
            //
            // y는 더 이상 BottomOf(화면 아래 기준)로 재지 않는다 — 그러면 화면비가
            // 바뀔 때 미니맵과 수첩이 서로 다른 기준(위/아래)으로 움직여 간격이
            // 벌어지거나 겹칠 수 있다. 대신 미니맵 하단(48 + 220)에서 곧장 이어 붙인다.
            //
            // 간격 4px: 토스트 목록의 76-64=12px(따로 노는 항목들 간격)보다 좁게 잡아
            // "각자인데 붙어 있다"가 아니라 "한 덩어리인데 두 판"으로 읽히게 한다.
            const float minimapY = 48f;
            const float minimapHeight = 220f;
            const float panelGap = 4f;
            var rect = new Rect(HudTheme.RightOf(1592f, 280f),
                                minimapY + minimapHeight + panelGap, 280f, 120f);

            var counts = HudScreens.CountQuests(snapshot, client.MemberId);
            var left = counts.requiredTotal - counts.requiredDone;

            // §7.1.4 필수 미완료가 남은 채 시간대 잔여 20% 이하가 되면 전체 카드가 점멸
            var phase = snapshot?.phase;
            var urgent = left > 0 && phase != null && phase.durationMs > 0d &&
                         1d - phase.elapsedMs / phase.durationMs <= 0.2d;

            _theme.Fill(rect, HudTheme.Paper, 0.94f);
            _theme.Border(rect,
                urgent && HudTheme.Pulse(1f) ? HudTheme.Alert : urgent ? HudTheme.Rule : HudTheme.Rule,
                urgent ? 2f : 1f);

            GUI.Label(new Rect(rect.x + 16f, rect.y + 12f, 240f, 20f), "NOTEBOOK  [TAB]",
                _theme.At(_theme.Label, 11, HudTheme.Ink2));

            Row(rect, 44f, "필수", $"{counts.requiredDone} / {counts.requiredTotal}",
                left > 0 ? HudTheme.Alert : HudTheme.Accent, 19);
            Row(rect, 70f, "선택", $"{counts.optionalDone} / {counts.optionalTotal}", HudTheme.Ink2, 17);
            Row(rect, 94f, "합동", counts.jointLabel, counts.jointColor, 15);

            void Row(Rect box, float y, string label, string value, Color color, int size)
            {
                GUI.Label(new Rect(box.x + 16f, box.y + y, 120f, 22f), label,
                    _theme.At(_theme.Body, 17, HudTheme.Ink));
                GUI.Label(new Rect(box.xMax - 156f, box.y + y, 140f, 22f), value,
                    _theme.At(_theme.Mono, size, color, TextAnchor.MiddleRight));
            }
        }

        /* ─────────────────────────────────────── §7.1.2 좌하단 컨디션 링 */

        /// <summary>§7.1.2 표의 6스탯. 취침 정산(§7.6)도 같은 순서를 쓴다</summary>
        public static readonly (string id, string name)[] Stats =
        {
            ("stamina", "체력"), ("hydration", "수분"), ("fatigue", "피로"),
            ("mental", "정신력"), ("hygiene", "청결"), ("satiety", "포만감"),
        };

        private void DrawCondition(Snapshot snapshot)
        {
            var me = HudScreens.FindMember(snapshot, client.MemberId);
            if (me?.stats == null) return;

            // 목업 실측: 클러스터 원점 (48, 912). 3×2 배치 — 아래 가장자리에서 잰다
            var origin = new Vector2(48f, HudTheme.ViewHeight - (HudTheme.DesignHeight - 912f));
            var showValues = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            // **바탕 패널을 두지 않는다.**
            //
            // 검은 판을 깔면 읽히기는 하지만 월드를 그만큼 가린다. §7.1.2가 이
            // 클러스터를 화면 구석에 얇게 두라고 한 이유가 그것이다.
            //
            // 대신 링 자체를 강하게 만든다 — 트랙을 가장 어두운 색(`Dim`)으로
            // 깔면 배경이 나무 바닥이든 눈밭이든 링의 테두리가 항상 잡히고,
            // 그 위의 채움은 밝은 색이라 두 겹으로 읽힌다.

            for (var i = 0; i < Stats.Length; i += 1)
            {
                var (id, name) = Stats[i];
                var raw = Value(me.stats, id);

                // 피로만 0에서 시작해 100으로 오른다 — 링은 "남은 여유"를 채운다
                var value = id == "fatigue" ? 1f - raw / 100f : raw / 100f;
                var danger = Danger(id, raw);

                var cx = origin.x + (i % 3) * 60f;
                var cy = origin.y + (i / 3) * 64f;

                // §7.1.2 평시 40px / 위험 진입 시 56px + 1.5Hz 점멸.
                //
                // 명세의 평시 α0.55는 **너무 흐렸다.** 여섯 개가 다 회색 얼룩으로
                // 보여서 어느 것이 무슨 수치인지는커녕 몇 개인지도 안 읽혔다.
                // 위험과 평시를 가르는 것은 알파가 아니라 **크기와 색**이면 충분하다
                var size = danger ? 56f : 44f;
                var alpha = 1f;
                if (danger && !HudTheme.Pulse(1.5f)) alpha = 0.75f;

                var rect = new Rect(cx + (56f - size) * 0.5f, cy + (56f - size) * 0.5f, size, size);
                var color = Tint(id, danger);

                // §7.1.2는 "링 중앙에 수치 표기 없음(시야 방해)"이라 못박았지만,
                // 그러면 여섯 개가 무엇인지 알 방법이 없다. 마우스를 올렸을 때만
                // 이름과 값을 띄우면 평소 화면은 그대로 두면서 배울 수 있다
                if (rect.Contains(_mouse)) _hovered = i;

                var previous = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                // 트랙을 `Dim`으로 — 링이 어떤 배경 위에 놓여도 윤곽이 남는다.
                // `Rule2`는 이제 밝아져서 트랙으로 쓰면 채움과 구분이 안 된다
                //
                // F-2(WORKORDER) — 청결 링에만 조건 D 문턱선을 굽는다. 위험색은
                // 문턱을 이미 넘은 "뒤"에만 켜지는 반응형이라 사전 예측이 안 됐다
                // (§7.1.2 danger 판정 참고) — 이 선은 항상 보여서 미리 대비하게 한다.
                // 값 20은 `packages/sim/src/judge.ts`의 `HYGIENE_FLOOR` — 서버 소유
                // 규칙이라 원래는 스냅샷에서 읽어야 맞지만(ARCH-02), 지금은 Unity
                // `Generated/Protocol.cs` 재생성이 금지돼 있어 새 필드를 못 읽는다.
                // 그래서 이미 같은 값을 하드코딩해 쓰던 자리(§7.1.2 `Danger` — 바로
                // 아래, 그리고 스탯 툴팁 문구 "20 이하면 점호 조건 D가 깨진다")와
                // 똑같이 상수로 맞춰 둔다 — 값이 바뀌면 세 곳을 같이 고쳐야 한다.
                var threshold = id == "hygiene" ? HygieneFloor / 100f : float.NaN;
                HudIcons.DrawRing(rect, id, value, color, HudTheme.Dim, threshold, HudTheme.Alert);
                HudIcons.Stat(id, new Rect(rect.x + size * 0.28f, rect.y + size * 0.28f,
                                           size * 0.44f, size * 0.44f), HudTheme.White);
                GUI.color = previous;

                // §7.1.2 "링 중앙에 수치 표기 없음. Alt 키 홀드 시 수치 표시"
                if (showValues || danger)
                {
                    GUI.Label(new Rect(rect.x - 8f, rect.yMax - 2f, rect.width + 16f, 18f),
                        $"{raw:0}", _theme.At(_theme.Label, 12, color, TextAnchor.MiddleCenter));
                }
            }

            GUI.Label(new Rect(origin.x, origin.y + 128f, 300f, 20f), "CONDITION   [ALT] 수치",
                _theme.At(_theme.Label, 11, HudTheme.Ink2));

            if (_hovered >= 0 && _hovered < Stats.Length) DrawStatTip(origin, me.stats, _hovered);
        }

        /// <summary>마우스를 올린 링이 무엇인지</summary>
        private int _hovered = -1;
        private Vector2 _mouse;

        private static readonly string[] StatHints =
        {
            "0이 되면 그 자리에서 쓰러진다",
            "30 이하 탈수 · 10 이하 열사병",
            "100이면 강제 수면 — 그 칸의 일과를 놓친다",
            "0이면 패닉. 화면이 떨린다",
            "20 이하면 점호 조건 D가 깨진다",
            "0이면 체력이 깎이기 시작한다",
        };

        private void DrawStatTip(Vector2 origin, SnapshotMembersItemStats stats, int index)
        {
            var (id, name) = Stats[index];
            var value = Value(stats, id);
            var danger = Danger(id, value);

            var rect = new Rect(origin.x, origin.y - 74f, 300f, 66f);
            _theme.Fill(rect, HudTheme.Paper, 0.96f);
            _theme.Border(rect, danger ? HudTheme.Alert : HudTheme.Rule);
            _theme.Spine(rect, danger ? HudTheme.Alert : HudTheme.Accent, 3f);

            GUI.Label(new Rect(rect.x + 18f, rect.y + 8f, 200f, 24f), name,
                _theme.At(_theme.Heading, 19, HudTheme.Ink));
            GUI.Label(new Rect(rect.xMax - 90f, rect.y + 8f, 70f, 24f), $"{value:0}",
                _theme.At(_theme.Display, 22, danger ? HudTheme.Alert : HudTheme.Accent,
                    TextAnchor.MiddleRight));
            GUI.Label(new Rect(rect.x + 18f, rect.y + 36f, rect.width - 36f, 22f), StatHints[index],
                _theme.At(_theme.Small, 13, HudTheme.Ink2));
        }

        public static float Value(SnapshotMembersItemStats stats, string id) => id switch
        {
            "stamina" => (float)stats.stamina,
            "hydration" => (float)stats.hydration,
            "fatigue" => (float)stats.fatigue,
            "mental" => (float)stats.mental,
            "hygiene" => (float)stats.hygiene,
            _ => (float)stats.satiety,
        };

        /// <summary>
        /// 10.0 조건 D — 청결 하한. `packages/sim/src/judge.ts`의 `HYGIENE_FLOOR`와
        /// 값을 맞춰야 한다(서버 소유 규칙, ARCH-02) — 스냅샷에 실어 보내는 게
        /// 정석이지만 `Generated/Protocol.cs` 재생성이 금지돼 있어 지금은 상수로
        /// 미러링한다(F-2, WORKORDER). 값이 바뀌면 여기와 `DrawCondition`의 문턱선,
        /// `StatHints`의 툴팁 문구까지 셋을 같이 고쳐야 한다.
        /// </summary>
        public const float HygieneFloor = 20f;

        /// <summary>§7.1.2 임계값 — 피로만 방향이 반대다</summary>
        public static bool Danger(string id, float value) => id switch
        {
            "fatigue" => value >= 70f,
            "hygiene" => value <= HygieneFloor,
            _ => value <= 30f,
        };

        private static Color Tint(string id, bool danger) => id switch
        {
            "hydration" => danger ? HudTheme.Heat : HudTheme.Cold,
            _ => danger ? HudTheme.Alert : HudTheme.Accent,
        };

        /* ─────────────────────────────────────── §7.1.5 상호작용 프롬프트 */

        private void DrawPrompt(Snapshot snapshot)
        {
            if (_screens.BlocksMovement) return;

            // 목업 실측: 480×88 @ (720, 880)
            // 목업 실측: 480×88 @ (720, 880) — 가로 가운데, 아래에서 잰다
            var rect = new Rect(HudTheme.ViewWidth * 0.5f - 240f,
                                HudTheme.BottomOf(880f, 88f), 480f, 88f);

            // 판이 열려 있으면 프롬프트는 그리지 않는다. 판은 화면 가운데를
            // 쓰고(`DrawMinigame`), 그 동안은 걷지도 못하므로 "다가서라"는
            // 안내가 남아 있을 이유가 없다
            if (play != null && play.QuestId != null) return;

            var near = interactor?.Nearest;
            if (near == null) return;

            // 2행이 조건 미충족 사유다. 충족하면 accent로 바뀐다
            var (reason, ok) = ReasonFor(near, snapshot);

            _theme.Fill(rect, HudTheme.Paper, 0.94f);
            _theme.Border(rect, ok ? HudTheme.Accent : HudTheme.Alert, 2f);

            _theme.KeyCap(new Rect(rect.x + 20f, rect.y + 18f, 34f, 30f), "E");
            GUI.Label(new Rect(rect.x + 68f, rect.y + 16f, 400f, 34f), near.label,
                _theme.At(_theme.Heading, 22, HudTheme.Ink));

            GUI.Label(new Rect(rect.x + 20f, rect.y + 54f, 380f, 24f), reason,
                _theme.At(_theme.Body, 17, ok ? HudTheme.Accent : HudTheme.Alert));

            GUI.Label(new Rect(rect.xMax - 180f, rect.y + 54f, 160f, 24f), near.detail,
                _theme.At(_theme.Label, 13, HudTheme.Ink2, TextAnchor.MiddleRight));

            // 진척 — 서버가 세는 값을 표시만 한다
            if (near.progress > 0f)
            {
                _theme.Bar(new Rect(rect.x, rect.yMax - 4f, rect.width, 4f),
                    near.progress, HudTheme.Accent);
            }

        }

        /// <summary>
        /// 열려 있는 일과 판 (`docs/game_spec.md` 원형 14종).
        ///
        /// 테두리·제목·남은 시간·결과는 `HudMinigame`이 그리고 본문은 원형이
        /// 그린다. 여기서는 자리만 내어준다 — 판이 열려 있으면 화면 가운데를
        /// 통째로 쓰고, 그 동안 플레이어는 걷지 못한다.
        /// </summary>
        private void DrawMinigame()
        {
            if (play == null || play.QuestId == null || play.Board == null) return;
            HudMinigame.Draw(_theme, play);
        }

        /// <summary>
        /// §7.1.5 "2행은 **왜 진행이 안 되는지를 항상 명시**한다."
        ///
        /// 기획서 §15.0이 명시한 요구사항이며, 협동 강제 구조에서 가장 중요한 UX다.
        /// 여기서 하는 것은 판정이 아니라 **읽어주기**다 — 서버가 준 값(요구 인원,
        /// 같은 구역 인원)을 그대로 문장으로 옮긴다.
        /// </summary>
        private (string, bool) ReasonFor(Interactable point, Snapshot snapshot)
        {
            if (point.minActors > 1)
            {
                var here = 0;
                if (snapshot?.members != null)
                {
                    foreach (var member in snapshot.members)
                    {
                        if (member != null && member.zone == visibility?.CurrentZone) here += 1;
                    }
                }
                return here >= point.minActors
                    ? ($"✓ {here}/{point.minActors} — 홀드", true)
                    : ($"⚠ {point.minActors}인 필요 — 현재 {here}명", false);
            }

            // **무슨 판인지 여기서 말한다.**
            //
            // 전에는 무엇이든 "[E] 홀드"라고 적어놨다. 화면이 늘 같은 말을 하니
            // 붙잡고만 있으면 되는 줄 알 수밖에 없었다. 이름을 보고 유추하던
            // 것도 걷어냈다 — 원형은 서버가 알려준다.
            if (string.IsNullOrEmpty(point.minigameType))
            {
                // 판이 없는 일과 — 회복 · 합동 · 훈련. 붙잡고 있으면 시간이 끝낸다
                return point.progress > 0f
                    ? ($"[E] 이어서 — {point.progress * 100f:0}%", true)
                    : ("[E] 홀드", true);
            }

            var board = Boards.Name(point.minigameType);
            return point.progress > 0f
                ? ($"[E] {board} — 이어서 {point.progress * 100f:0}%", true)
                : ($"[E] {board}", true);
        }

        /* ─────────────────────────── §4.3 상태 연동 오버라이드 (프레임) */

        /// <summary>
        /// 화면 프레임으로 표현하는 상태 이상.
        ///
        /// §9.2가 이것들을 풀스크린 셰이더(`SH_FrostFrame` · `SH_Vignette_Pulse`)로
        /// 요구하지만, 셰이더 그래프 없이도 **화면 4변 그라디언트**로 같은 정보를
        /// 준다 — 색과 덮는 범위가 곧 신호이고, 그건 UI 레이어에서 그릴 수 있다.
        /// 열 왜곡(`SH_HeatDistort`)과 야시장비(`SH_NightVision`)는 UV를 건드려야
        /// 하므로 여기서 대체할 수 없다.
        ///
        /// 전부 §15.0 접근성 개별 토글을 거친다 — `WeatherGrading`이 꺼두면 0이다.
        /// </summary>
        private void DrawStateOverlays()
        {
            if (grading == null) return;

            var screen = new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight);

            // 동상 — 화면 4변 결빙 프레임 페이드인(§4.3)
            if (grading.FrostBite > 0f)
            {
                var depth = 60f + 90f * grading.FrostBite;
                var alpha = 0.18f + 0.3f * grading.FrostBite;
                Edge(screen, depth, HudTheme.Cold, alpha);
            }

            // 열사병 2단계 — 화면 가장자리 붉은 펄스 (§4.3 주기 1.2s)
            if (grading.HeatStress > 0f)
            {
                var pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.2f);
                Edge(screen, 140f, HudTheme.Alert, 0.35f * grading.HeatStress * pulse);
            }

            void Edge(Rect area, float depth, Color color, float alpha)
            {
                // 그라디언트 대신 층을 쌓는다. 4~5겹이면 32px 픽셀아트 위에서
                // 단계가 보이지 않고, 텍스처를 만들 필요도 없다
                const int layers = 5;
                for (var i = 0; i < layers; i += 1)
                {
                    var t = (i + 1) / (float)layers;
                    var thickness = depth * t;
                    var a = alpha / layers;
                    _theme.Fill(new Rect(area.x, area.y, area.width, thickness), color, a);
                    _theme.Fill(new Rect(area.x, area.yMax - thickness, area.width, thickness), color, a);
                    _theme.Fill(new Rect(area.x, area.y, thickness, area.height), color, a);
                    _theme.Fill(new Rect(area.xMax - thickness, area.y, thickness, area.height), color, a);
                }
            }
        }

        /* ─────────────────── §5.0 혹한 특수 규칙 — 보온 게이지 (목업 실측) */

        private void DrawBodyHeat(Snapshot snapshot)
        {
            var me = HudScreens.FindMember(snapshot, client.MemberId);
            if (me == null) return;

            // 서버가 극혹한에서만 게이지를 흘린다(`warmth.ts`). 0이면 이 밴드가
            // 아니거나 이미 동상이라, 게이지 UI를 띄울 일이 없다
            var frostbitten = me.frostbitten;
            if (me.warmthRemainingMs <= 0d && !frostbitten) return;

            // 목업 실측: (1290, 900) — 오른쪽·아래 가장자리에서 잰다
            var origin = new Vector2(HudTheme.RightOf(1290f, 320f),
                                     HudTheme.ViewHeight - (HudTheme.DesignHeight - 900f));
            GUI.Label(new Rect(origin.x, origin.y - 14f, 320f, 20f), "BODY HEAT — 열원 접촉까지",
                _theme.At(_theme.Label, 11, HudTheme.Cold));

            // 전체 길이(90초)는 서버 데이터 테이블에 있고 스냅샷은 **남은 시간만**
            // 준다(ARCH-02). 비율을 그리려면 밴드 규칙을 알아야 하는데 그건
            // 클라가 가질 것이 아니므로, 남은 시간을 그대로 게이지로 쓴다
            var seconds = (float)me.warmthRemainingMs / 1000f;
            var ratio = Mathf.Clamp01(seconds / WarmthFullSeconds);

            _theme.Bar(new Rect(origin.x, origin.y + 10f, 300f, 16f), ratio,
                frostbitten ? HudTheme.Alert : HudTheme.Cold);

            GUI.Label(new Rect(origin.x + 160f, origin.y + 30f, 140f, 26f),
                $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}",
                _theme.At(_theme.Mono, 20, frostbitten ? HudTheme.Alert : HudTheme.Cold,
                    TextAnchor.MiddleRight));

            if (frostbitten)
            {
                // §5.0 — 열원에 돌아가는 것만으로는 안 풀린다. 의무병을 찾아야 한다
                GUI.Label(new Rect(origin.x, origin.y + 30f, 240f, 26f), "동상 — 의무병 필요",
                    _theme.At(_theme.Body, 15, HudTheme.Alert));
            }
            else if (ratio < 0.35f)
            {
                GUI.Label(new Rect(origin.x, origin.y + 30f, 200f, 26f), "동상 위험",
                    _theme.At(_theme.Body, 15, HudTheme.Alert));
            }
        }

        /// <summary>
        /// §5.0 보온 게이지의 전체 길이(초).
        ///
        /// 서버 데이터 테이블(`temperature.json`)의 값과 같아야 하지만 **판정에는
        /// 쓰이지 않는다** — 게이지 막대의 비율을 그리기 위한 표시용 상수다.
        /// 서버가 이 값을 바꾸면 막대가 덜 차거나 넘칠 뿐 규칙은 그대로다.
        /// </summary>
        private const float WarmthFullSeconds = 90f;

        /* ─────────────────────────────── 월드 오버레이 — 이름표 · 말풍선 */

        private void DrawWorldOverlay(Snapshot snapshot)
        {
            var camera = world?.camera != null ? world.camera.GetComponent<Camera>() : Camera.main;
            if (camera == null || snapshot?.members == null) return;

            var scale = HudTheme.ViewScale;

            // 내 캐릭터
            if (world.player != null)
            {
                var me = HudScreens.FindMember(snapshot, client.MemberId);
                if (me != null) Nameplate(camera, scale, world.player.transform, me, true);
            }

            // 보이는 분대원만 — §1.3-A. 안 보이는 사람에게 이름표를 띄우면
            // 스프라이트를 감춘 의미가 사라진다
            if (world.squad != null)
            {
                foreach (var pair in world.squad.Visible)
                {
                    var member = HudScreens.FindMember(snapshot, pair.Key);
                    if (member != null) Nameplate(camera, scale, pair.Value, member, false);
                }
            }

            DrawBubbles(camera, scale, snapshot);
        }

        private void Nameplate(Camera camera, float scale, Transform at,
                               SnapshotMembersItem member, bool mine)
        {
            var screen = camera.WorldToScreenPoint(at.position + Vector3.up * 1.7f);
            if (screen.z < 0f) return;

            var x = screen.x / scale;
            var y = (Screen.height - screen.y) / scale;

            // 목업 실측은 86×20이지만 **글자 폭에 맞춘다** — 이름이 길면 잘려서
            // "리 통신병 · CO"처럼 보이고, §5.3이 색각 이상 대응으로 못박은
            // 보직 3글자가 화면에서 사라진다
            var text = $"{member.name} · {HudTheme.RoleTag(member.role)}";
            var width = Mathf.Max(86f, _theme.Measure(text, _theme.Label) + 20f);
            var rect = new Rect(x - width * 0.5f, y - 10f, width, 20f);
            _theme.Fill(rect, HudTheme.Paper, 0.85f);
            GUI.Label(rect, text,
                _theme.At(_theme.Label, 13, mine ? HudTheme.Ink : HudTheme.Ink2, TextAnchor.MiddleCenter));

            // §5.4 분대장 롤 — 이름표 왼쪽에 올리브 마름모
            if (client.Latest?.leaderId == member.id)
            {
                HudIcons.Dot(new Rect(rect.x - 14f, rect.y + 5f, 10f, 10f), HudTheme.Accent);
            }
        }

        private void DrawBubbles(Camera camera, float scale, Snapshot snapshot)
        {
            for (var i = _bubbles.Count - 1; i >= 0; i -= 1)
            {
                // §7.8 말풍선 3초 유지
                if (Time.unscaledTime - _bubbles[i].born > 3f) _bubbles.RemoveAt(i);
            }

            foreach (var bubble in _bubbles)
            {
                Transform at = null;
                if (bubble.memberId == client.MemberId && world.player != null)
                {
                    at = world.player.transform;
                }
                else if (world.squad != null)
                {
                    foreach (var pair in world.squad.Visible)
                    {
                        if (pair.Key == bubble.memberId) { at = pair.Value; break; }
                    }
                }
                if (at == null) continue;

                var screen = camera.WorldToScreenPoint(at.position + Vector3.up * 2.4f);
                if (screen.z < 0f) continue;

                var x = screen.x / scale;
                var y = (Screen.height - screen.y) / scale;

                if (!string.IsNullOrEmpty(bubble.command))
                {
                    // 목업 실측: 말풍선 118×46
                    var rect = new Rect(x - 59f, y - 46f, 118f, 46f);
                    var color = HudIcons.CommandColor(bubble.command);
                    var label = Label(bubble.command);

                    _theme.Fill(rect, HudTheme.Paper, 0.95f);
                    _theme.Border(rect, color);
                    HudIcons.Command(bubble.command, new Rect(rect.x + 8f, rect.y + 9f, 28f, 28f), color);
                    GUI.Label(new Rect(rect.x + 42f, rect.y, 70f, rect.height), label,
                        _theme.At(_theme.Heading, 17, color));
                }
                else if (!string.IsNullOrEmpty(bubble.text))
                {
                    // C-1b 문장 말풍선(하달 반응) — 아이콘이 없으니 글자 폭에 맞춰 넓힌다
                    var width = Mathf.Clamp(_theme.Measure(bubble.text, _theme.Body) + 32f, 90f, 260f);
                    var rect = new Rect(x - width * 0.5f, y - 40f, width, 36f);
                    _theme.Fill(rect, HudTheme.Paper, 0.95f);
                    _theme.Border(rect, HudTheme.Ink2);
                    GUI.Label(rect, bubble.text,
                        _theme.At(_theme.Body, 16, HudTheme.Ink, TextAnchor.MiddleCenter));
                }
            }

            static string Label(string id)
            {
                foreach (var (key, name) in HudIcons.Commands)
                {
                    if (key == id) return name;
                }
                return id;
            }
        }

        /* ═══════════════════════════════════════════ E-3 오늘의 기록(수첩 탭) */

        /// <summary>수첩 "오늘의 기록" 탭(HudScreens)이 표시하는 한 줄.</summary>
        public readonly struct JournalEntry
        {
            public readonly string clock;
            public readonly string phase;
            public readonly string tag;
            public readonly string text;
            public readonly Color color;

            public JournalEntry(string clock, string phase, string tag, string text, Color color)
            {
                this.clock = clock;
                this.phase = phase;
                this.tag = tag;
                this.text = text;
                this.color = color;
            }
        }

        private readonly List<JournalEntry> _journal = new List<JournalEntry>();
        private double _journalDay = -1d;

        /// <summary>수첩 "오늘의 기록" 탭이 읽는다. 최신이 [0](시간순, 최신 위)</summary>
        public IReadOnlyList<JournalEntry> Journal => _journal;

        /// <summary>
        /// WORKORDER.md E-3 — 그날 이펙트를 시간순 일지로 쌓는다. RimWorld식 일지처럼
        /// "이미 벌어진 일"만 담는다(숨김의 원칙: 판정·소모된 자원·손해 원인은 전부
        /// 싣는다). 서버에 저장하지 않는 세션 로컬 리스트라 재접속하면 유실된다 —
        /// 알려진 제한이다.
        ///
        /// `OnEvent`의 토스트 스위치와 완전히 별개로 구독된다(OnEnable 참고). 이유는
        /// 이 파일을 C-1b도 같이 만지기 때문 — 기존 case 안에 훅을 심으면 그쪽 수정과
        /// 충돌하기 쉽다. 별도 메서드로 붙이면 서로의 변경이 겹치지 않는다.
        /// </summary>
        /// <summary>일과 id → 마지막으로 본 상태. 완료·잠김은 이벤트로 안 오고
        /// 스냅샷 상태 변화로만 드러난다 — 일지가 이걸 안 보면 하루 중 가장 흔한
        /// 사건(일과를 끝냈다/놓쳤다)이 통째로 빠진다(실플레이 신고).</summary>
        private readonly Dictionary<string, string> _questStatusSeen = new Dictionary<string, string>();
        private bool _questJournalPrimed;

        private void CollectQuestJournal(Snapshot snapshot)
        {
            if (snapshot?.quests == null) return;

            if (snapshot.day != _journalDay)
            {
                _journal.Clear();
                _journalDay = snapshot.day;
                _questStatusSeen.Clear();
                _questJournalPrimed = false;
            }

            foreach (var quest in snapshot.quests)
            {
                if (quest == null || quest.id == null) continue;
                // 회복 행동(밥·세면)은 판정 대상이 아니다 — 일지에 쌓으면 소음이 된다
                if (quest.kind == SnapshotQuestsItemKindValues.Care) continue;

                _questStatusSeen.TryGetValue(quest.id, out var seen);
                _questStatusSeen[quest.id] = quest.status;
                // 첫 스냅샷은 기준선이다 — 재접속 때 이미 끝난 일과가 전부
                // "방금 완료"로 쏟아지면 일지가 거짓말을 한다
                if (!_questJournalPrimed || seen == quest.status) continue;

                var owner = quest.ownerId != null ? NameOf(quest.ownerId) : "합동";
                if (quest.status == SnapshotQuestsItemStatusValues.Done)
                {
                    PushJournal("완료", $"{quest.label} — {owner}"
                        + (quest.grade != null ? $" · {quest.grade}" : ""), HudTheme.Accent);
                }
                else if (quest.status == SnapshotQuestsItemStatusValues.Locked && quest.required)
                {
                    PushJournal("놓침", $"{quest.label} — 필수인데 시간대가 닫혔다", HudTheme.Alert);
                }
                else if (quest.status == SnapshotQuestsItemStatusValues.Failed)
                {
                    PushJournal("실패", $"{quest.label} — {owner}", HudTheme.Alert);
                }
            }

            _questJournalPrimed = true;
        }

        private void PushJournal(string tag, string text, Color color)
        {
            var clock = client?.Latest?.phase?.clock ?? "";
            var phaseLabel = HudTheme.PhaseLabel(client?.Latest?.phase?.id);
            _journal.Insert(0, new JournalEntry(clock, phaseLabel, tag, text, color));
            if (_journal.Count > 200) _journal.RemoveAt(_journal.Count - 1);
        }

        private void CollectJournal(ServerEvent item)
        {
            if (item == null) return;

            // 하루가 바뀌면 통째로 비운다 — "오늘의" 기록이지 어제 것이 아니다
            var day = client?.Latest?.day ?? _journalDay;
            if (day != _journalDay)
            {
                _journal.Clear();
                _journalDay = day;
            }

            string tag;
            string text;
            var color = HudTheme.Ink2;

            switch (item.type)
            {
                case ServerEventTypeValues.DisciplineChanged:
                    tag = "군기";
                    text = $"{item.to:0} · {item.band}";
                    color = HudTheme.Alert;
                    break;

                case ServerEventTypeValues.MemberEvacuated:
                    tag = "후송";
                    text = $"{NameOf(item.memberId)} 실려 나감";
                    color = HudTheme.Alert;
                    break;

                case ServerEventTypeValues.MemberReturned:
                    tag = "복귀";
                    text = item.asRecruit ? $"{NameOf(item.memberId)}, 이병으로" : $"{NameOf(item.memberId)} 복귀";
                    color = item.asRecruit ? HudTheme.Alert : HudTheme.Accent;
                    break;

                case ServerEventTypeValues.ConditionCritical:
                    tag = "위기";
                    text = ConditionCriticalText(item.stat, NameOf(item.memberId));
                    color = HudTheme.Alert;
                    break;

                case ServerEventTypeValues.DelegationRefused:
                    tag = "하달 거부";
                    text = $"{QuestLabel(item.questId)} — {HudTheme.DelegationRefusalText(item.reason)}";
                    color = HudTheme.Heat;
                    break;

                case ServerEventTypeValues.SupplyClaimed:
                    tag = "보급";
                    text = SupplySummary(item.items);
                    color = HudTheme.Accent;
                    break;

                case ServerEventTypeValues.RankReviewed:
                    tag = "승급";
                    text = RankReviewSummary(item.outcomes);
                    color = HudTheme.Accent;
                    break;

                case ServerEventTypeValues.DayJudged:
                    tag = "판정";
                    text = item.passed
                        ? "통과" + (item.reliefsUsed > 0d ? $" — 구제권 {item.reliefsUsed:0}장 사용" : "")
                        : $"미달 — 조건 {item.failedAt}";
                    color = item.passed ? HudTheme.Accent : HudTheme.Alert;
                    break;

                case ServerEventTypeValues.FrostbiteRelieved:
                    tag = "동상 해제";
                    text = $"{NameOf(item.memberId)} — 의무병 {NameOf(item.byId)}";
                    color = HudTheme.Accent;
                    break;

                case ServerEventTypeValues.ForcedSleep:
                    tag = "강제 취침";
                    text = NameOf(item.memberId);
                    color = HudTheme.Heat;
                    break;

                case ServerEventTypeValues.ChoreReassigned:
                    tag = "재배정";
                    text = $"분대장이 {NameOf(item.toId)}에게 — {QuestLabel(item.questId)}";
                    color = HudTheme.Heat;
                    break;

                case ServerEventTypeValues.Log:
                    // "delegationWindowClosed"는 화면 신호가 아니라 delegation.ts 내부
                    // 코드 문자열이다(Hud.cs OnEvent의 Log case와 같은 필터)
                    if (item.message == "delegationWindowClosed") return;
                    // E-2 잔여 — 군기 정산 항목별 델타(discipline.ts
                    // formatDisciplineDeltaLog)가 이 case로 들어온다. 태그·색을
                    // 군기 계열로 맞춰 위 DisciplineChanged 항목과 나란히 읽히게 한다
                    tag = item.message != null && item.message.StartsWith("군기 정산") ? "군기" : "경고";
                    text = item.message;
                    color = tag == "군기" ? HudTheme.Alert : HudTheme.Heat;
                    break;

                default:
                    return;
            }

            var clock = client?.Latest?.phase?.clock ?? "";
            var phaseLabel = HudTheme.PhaseLabel(client?.Latest?.phase?.id);
            _journal.Insert(0, new JournalEntry(clock, phaseLabel, tag, text, color));
            // 하루는 유한하다 — 안전판으로만 자른다
            if (_journal.Count > 200) _journal.RemoveAt(_journal.Count - 1);
        }

        /// <summary>E-3 — 승급 심사 요약. 전체 표는 승급 화면이 이미 보여주므로
        /// 일지에는 몇 명이 올랐는지만 한 줄로 남긴다</summary>
        private static string RankReviewSummary(ServerEventOutcomesItem[] outcomes)
        {
            if (outcomes == null || outcomes.Length == 0) return "";
            var promoted = 0;
            foreach (var outcome in outcomes)
            {
                if (outcome != null && outcome.promoted) promoted += 1;
            }
            return promoted > 0 ? $"{promoted}명 승급" : "승급 없음";
        }
    }
}
