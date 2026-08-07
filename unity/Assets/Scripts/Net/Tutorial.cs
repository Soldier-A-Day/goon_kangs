using System.Collections.Generic;
using System.Globalization;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 첫 런 온보딩 — F1 발주.
    ///
    /// **커리큘럼이 아니라 조작법을 가르친다.** "언제 무엇이 열리는가"는 이미
    /// `packages/sim/data/curriculum.json`이 정한다(D-01: 기본 조작 · 수첩). 이
    /// 파일은 그 첫날에 "그래서 어떻게 하는가"를 짧은 카드로 얹을 뿐이다 —
    /// 별도 튜토리얼 모드·맵을 만들지 않는다.
    ///
    /// ── 클라이언트 전용 ──────────────────────────────────────────────────
    /// 서버·프로토콜을 하나도 건드리지 않는다. 진행 판정은 스냅샷(퀘스트 상태·
    /// 시간대·날짜)과 입력(WASD·TAB·E)을 관찰해서 한다 — 서버가 "튜토리얼
    /// 단계"라는 개념을 알 필요가 없다.
    ///
    /// ── 막지 않는다 ──────────────────────────────────────────────────────
    /// 카드만 얹고 입력·이동을 가로채지 않는다. 순서를 안 지켜도 그 단계
    /// 조건이 충족되면 넘어간다 — 그래서 8개 플래그를 전부 독립적으로 매
    /// 프레임 검사하고, 화면에는 "아직 안 끝난 것 중 가장 앞선 것"만 보여준다.
    ///
    /// ── 겹치지 않는다 ────────────────────────────────────────────────────
    /// `Hud`가 이미 계산해 둔 "다른 전체화면이 떠 있다"(하달 창·마감 큐·
    /// 미니게임·통합 수첩 등)를 `busy`로 받아 그 동안은 그리지 않는다.
    ///
    /// ── 저장 ─────────────────────────────────────────────────────────────
    /// 완료·건너뜀은 `PlayerPrefs`에 저장한다(WebGL은 IndexedDB로 영속화된다고
    /// 알려져 있으나, 이 저장소는 지금까지 `PlayerPrefs`를 쓴 적이 없어
    /// **런타임 확인이 필요**하다 — 보고 참고).
    ///
    /// "튜토리얼 다시 보기"는 로비(Next.js `/settings`)에 있다 — 접근성 설정과
    /// 같은 이유다(`Accessibility.cs` 참고: 게임이 뜨기 전에 고를 수 있어야
    /// 한다). 그 화면이 이미 쓰는 `window.__sadSettings` 브리지에 값 하나
    /// (`tutorialResetAt`, 클릭 시각 ms)를 얹어 보내고, 여기서 그 값이 마지막으로
    /// 처리한 값보다 크면 초기화한다 — 새 저장 경로를 만들지 않고 이미 쓰는
    /// 경로를 그대로 썼다.
    /// </summary>
    public sealed class Tutorial : MonoBehaviour
    {
        public GameClient client;
        public Interactor interactor;
        public QuestPlay play;

        private const string DoneKey = "sad.tutorial.done";
        private const string ResetSeqKey = "sad.tutorial.resetSeq";

        private const int StepMove = 0;
        private const int StepSchedule = 1;
        private const int StepFindObjective = 2;
        private const int StepInteract = 3;
        private const int StepMinigame = 4;
        private const int StepRequiredOptional = 5;
        private const int StepTimeOfDay = 6;
        private const int StepRollCall = 7;

        // 본문은 HudTheme의 모든 스타일이 wordWrap=false라 자동 줄바꿈이 없다
        // (`HudTheme.Make` 참고) — 카드 폭(760, 건너뛰기 버튼이 있으면 실질
        // ~560)을 넘는 문장은 여기서 직접 "\n"으로 끊어 둔다
        //
        // G2(사용자 지시 — "각 키가 뭔지 알려줘야 될 거 아니야") — 각 카드에 그
        // 단계가 쓰는 키를 `keys`로 따로 얹는다. 문장 속에 섞인 "WASD"·"E" 같은
        // 글자와 달리 `HudTheme.KeyCap`으로 키캡 모양으로 그린다(`DrawKeyRow`).
        // 키 목록·출처는 `KeyGuideEntries`(F1 전체 안내) 주석에 파일:줄로 남겨
        // 뒀다 — 여기서는 그중 이 단계에 해당하는 것만 고른다.
        private static readonly (string title, string body, string[] keys)[] Steps =
        {
            ("이동", "WASD로 움직인다", new[] { "W", "A", "S", "D" }),
            ("일과표", "TAB을 눌러 오늘 할 일을 본다", new[] { "TAB" }),
            ("목표 찾기", "좌상단 나침반 · 우상단 미니맵으로 갈 곳을 확인한다", null),
            ("상호작용", "목표 앞에서 E로 일과를 시작한다", new[] { "E" }),
            ("미니게임", "판을 하나 깨 본다", new[] { "SPACE" }),
            ("필수와 선택", "TAB을 한 번 더 눌러 일과요약을 본다\n필수를 다 못 하면 점호에서 깨진다",
                new[] { "TAB" }),
            ("시간대", "하루는 6칸 — 시간이 흐르며 다음 시간대로 넘어간다\n(스킵 투표 가능)", null),
            ("점호 · 마감", "밤에 판정이 나면 요약을 확인해야 다음 날로 넘어간다", new[] { "SPACE" }),
        };

        private readonly bool[] _stepDone = new bool[Steps.Length];
        private bool _finished;
        private float _showCompleteUntil = -1f;
        private int _tabPresses;

        /// <summary>G2 — 조작 키 전체 안내(F1). 완료 여부·요일과 무관하게 언제든
        /// 열고 닫을 수 있다(요구 1) — 그래서 `_finished` 판정보다 앞에서 다룬다</summary>
        private bool _keyGuideOpen;
        /// <summary>첫 단계(이동)에서 딱 한 번 자동으로 연다. 리셋 시 다시 풀린다</summary>
        private bool _keyGuideAutoShown;
        /// <summary>지난 프레임 `Draw()`가 받은 `busy`. F1은 `Update()`에서 처리하는데
        /// `busy`는 `Draw()`(OnGUI)만 알아서, 한 프레임 지연된 값으로 "다른 전체화면이
        /// 떠 있으면 물러난다" 규칙을 지킨다 — 안내창 하나 여닫는 데 1프레임 지연은
        /// 티가 나지 않는다</summary>
        private bool _lastBusy;

        private readonly HashSet<string> _seenDoneQuestIds = new HashSet<string>();
        private bool _seenDoneBaseline;
        private string _basePhaseId;

        private void Awake()
        {
            if (PlayerPrefs.GetInt(DoneKey, 0) != 0) _finished = true;
        }

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += OnSnapshot;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= OnSnapshot;
        }

        /* ══════════════════════════════════════════════════ 진행 판정 (입력) */

        // 매 프레임 정확히 한 번만 돈다 — `OnGUI`(Layout·Repaint 이중 호출)에서
        // 키 입력을 세면 확인 한 번에 두 번 잡히는 것과 같은 이유로, 카운팅이
        // 필요한 입력 관찰은 전부 여기서 한다(`HudScreens.Update` 주석 참고).
        private void Update()
        {
            CheckResetSignal();

            // G2 요구 1 — "언제든 다시 볼 수 있게". `_finished`도 요일도 안 따진다.
            // 다른 전체화면이 떠 있는 동안은 새로 열지 않는다(`_lastBusy`) — 단,
            // 이미 열려 있으면 그 화면 위에서도 닫을 수는 있어야 하므로 닫는 쪽은
            // 막지 않는다
            if (Input.GetKeyDown(KeyCode.F1) && (_keyGuideOpen || !_lastBusy))
                _keyGuideOpen = !_keyGuideOpen;

            if (_keyGuideOpen)
            {
                // 안내가 열린 동안 ESC는 튜토리얼을 건너뛰지 않고 안내만 닫는다
                if (Input.GetKeyDown(KeyCode.Escape)) _keyGuideOpen = false;
                return;
            }

            if (_finished) return;

            if (Input.GetAxisRaw("Horizontal") != 0f || Input.GetAxisRaw("Vertical") != 0f)
                _stepDone[StepMove] = true;

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                _tabPresses += 1;
                _stepDone[StepSchedule] = true;
                // 두 번째 TAB — 합쳐진 수첩 창이 일과표→일과요약으로 넘어간다.
                // 필수/선택 구분은 그 탭에 있으므로, 거기까지 넘겨봤다는 관찰로 쓴다
                if (_tabPresses >= 2) _stepDone[StepRequiredOptional] = true;
            }

            if (interactor != null && interactor.Nearest != null) _stepDone[StepFindObjective] = true;
            if (play != null && play.QuestId != null) _stepDone[StepInteract] = true;

            if (Input.GetKeyDown(KeyCode.Escape)) Skip();

            if (AllStepsDone()) Complete(false);
        }

        private bool AllStepsDone()
        {
            for (var i = 0; i < _stepDone.Length; i += 1) if (!_stepDone[i]) return false;
            return true;
        }

        /* ══════════════════════════════════════════════════ 진행 판정 (스냅샷) */

        private void OnSnapshot(Snapshot snapshot)
        {
            if (snapshot == null || _finished) return;

            // D-01이 끝났다 — 남은 단계가 있어도 그날의 온보딩은 여기서 마친다.
            // 커리큘럼(§F1)은 조작 안내를 D-01에만 두므로, 날짜가 넘어간 뒤에도
            // 낡은 단계 카드가 남아 있으면 D-02 화면과 싸운다
            if (snapshot.day > 1d)
            {
                Complete(false);
                return;
            }
            if (snapshot.day != 1d) return;

            if (client != null && snapshot.quests != null)
            {
                if (!_seenDoneBaseline)
                {
                    // 재접속·재개 등으로 이미 끝나 있던 일과는 "내가 방금 깬 것"으로
                    // 잘못 세지 않는다 — 시작 시점 스냅샷을 기준선으로 잡는다
                    foreach (var quest in snapshot.quests)
                        if (quest.status == SnapshotQuestsItemStatusValues.Done) _seenDoneQuestIds.Add(quest.id);
                    _seenDoneBaseline = true;
                }
                else
                {
                    foreach (var quest in snapshot.quests)
                    {
                        if (quest.status != SnapshotQuestsItemStatusValues.Done) continue;
                        if (quest.ownerId != client.MemberId) continue;
                        if (_seenDoneQuestIds.Add(quest.id)) _stepDone[StepMinigame] = true;
                    }
                }
            }

            if (snapshot.phase != null)
            {
                if (_basePhaseId == null) _basePhaseId = snapshot.phase.id;
                else if (snapshot.phase.id != _basePhaseId) _stepDone[StepTimeOfDay] = true;

                if (snapshot.phase.id == SnapshotPhaseIdValues.Rollcall) _stepDone[StepRollCall] = true;
            }
        }

        /* ══════════════════════════════════════════════════ 완료 · 건너뛰기 */

        private void Skip() => Complete(true);

        private void Complete(bool skipped)
        {
            if (_finished) return;
            _finished = true;
            // 건너뛴 경우 축하 카드를 띄우지 않는다 — "다시 안 뜬다"는 조용한 종료다
            _showCompleteUntil = skipped ? -1f : Time.unscaledTime + 3f;
            PlayerPrefs.SetInt(DoneKey, 1);
            PlayerPrefs.Save();
        }

        /* ══════════════════════════════════════════════════ 웹 설정 브리지 */

        [System.Serializable]
        private sealed class ResetSignal
        {
            public double tutorialResetAt;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern string SadReadSettings();
#endif

        /// <summary>
        /// `Accessibility.Read()`와 같은 통로(`window.__sadSettings` · `SadReadSettings`
        /// jslib)를 다시 연다. 접근성 시스템(`Accessibility.cs`)은 이 발주의 소유
        /// 파일이 아니라 고치지 않는다 — 대신 이미 검증된 통로를 여기서 한 번 더
        /// 열어, 그 JSON에서 이 파일이 아는 필드(`tutorialResetAt`)만 골라 쓴다.
        /// </summary>
        private static string ReadWebSettings()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return SadReadSettings(); }
            catch { return null; }
#else
            return null;
#endif
        }

        /// <summary>
        /// 로비 설정 화면의 "튜토리얼 다시 보기" 버튼이 여기로 온다.
        ///
        /// 버튼을 누른 시각(ms)이 `tutorialResetAt`으로 실린다. 마지막으로 처리한
        /// 값(`ResetSeqKey`)보다 크면 진행 상태를 전부 지운다. 값 하나로만
        /// 판정하는 이유 — 이벤트가 아니라 값이라 Unity가 나중에 뜨는 이 구조에서
        /// 그 자리에 값이 이미 있어도(먼저 눌러두고 게임을 시작해도) 놓치지 않는다
        /// (`settings.ts`의 같은 논거 참고).
        /// </summary>
        private void CheckResetSignal()
        {
            var json = ReadWebSettings();
            if (string.IsNullOrEmpty(json)) return;

            ResetSignal signal;
            try { signal = JsonUtility.FromJson<ResetSignal>(json); }
            catch { return; }
            if (signal == null || signal.tutorialResetAt <= 0d) return;

            var consumed = 0d;
            var raw = PlayerPrefs.GetString(ResetSeqKey, "0");
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out consumed);
            if (signal.tutorialResetAt <= consumed) return;

            PlayerPrefs.SetString(ResetSeqKey,
                signal.tutorialResetAt.ToString("R", CultureInfo.InvariantCulture));
            PlayerPrefs.SetInt(DoneKey, 0);
            PlayerPrefs.Save();

            _finished = false;
            _showCompleteUntil = -1f;
            System.Array.Clear(_stepDone, 0, _stepDone.Length);
            _tabPresses = 0;
            _seenDoneQuestIds.Clear();
            _seenDoneBaseline = false;
            _basePhaseId = null;
            _keyGuideOpen = false;
            _keyGuideAutoShown = false;
        }

#if UNITY_EDITOR
        /// <summary>에디터 전용 수동 초기화 — 웹 브리지가 없는 Play 모드에서 검증할 때 쓴다</summary>
        [ContextMenu("튜토리얼 초기화 (에디터 테스트)")]
        private void ResetForEditorTesting()
        {
            PlayerPrefs.SetInt(DoneKey, 0);
            PlayerPrefs.Save();
            _finished = false;
            _showCompleteUntil = -1f;
            System.Array.Clear(_stepDone, 0, _stepDone.Length);
            _tabPresses = 0;
            _seenDoneQuestIds.Clear();
            _seenDoneBaseline = false;
            _basePhaseId = null;
            _keyGuideOpen = false;
            _keyGuideAutoShown = false;
            Debug.Log("[튜토리얼] 초기화됨 — 다음 스냅샷부터 다시 보인다");
        }
#endif

        /* ══════════════════════════════════════════════════ 그리기 */

        /// <summary>
        /// `Hud.OnGUI`가 매 프레임 부른다. `busy`는 하달 창·마감 큐·미니게임·
        /// 통합 수첩처럼 화면을 다 쓰는 것이 떠 있는지를 호출 쪽이 판단해 넘긴다 —
        /// 이 파일은 그 판정 기준(`HudScreens.BlocksMovement` 등)을 모른다.
        /// </summary>
        public void Draw(HudTheme theme, bool busy)
        {
            _lastBusy = busy; // Update()가 다음 프레임 F1 여닫이 판정에 쓴다

            // G2 요구 1 — 안내는 완료 여부·요일 판정보다 앞선다. `busy`만 따른다
            // (다른 전체화면이 떠 있으면 물러나는 기존 규칙, 품질 기준 참고)
            if (_keyGuideOpen)
            {
                if (busy) return;
                DrawKeyGuide(theme);
                return;
            }

            if (_finished)
            {
                if (_showCompleteUntil > Time.unscaledTime)
                    DrawCard(theme, "완료", "이제부터는 직접 진행한다 — 설정에서 다시 볼 수 있다", false, null);
                return;
            }

            var snapshot = client != null ? client.Latest : null;
            if (snapshot == null || snapshot.day != 1d) return;
            if (busy) return;

            var idx = CurrentStepIndex();
            if (idx < 0) return; // 다음 Update()에서 Complete()로 정리된다

            // 요구 1 — "첫 단계에서 한 번 보여주고". 이동 단계에 처음 닿는 프레임에
            // 안내를 자동으로 열고, 이번 프레임은 카드를 건너뛴다(다음 프레임부터
            // 위 `_keyGuideOpen` 분기가 그린다) — 눈에 띄지 않는 1프레임이다
            if (idx == StepMove && !_keyGuideAutoShown)
            {
                _keyGuideAutoShown = true;
                _keyGuideOpen = true;
                return;
            }

            var (title, body, keys) = Steps[idx];
            DrawCard(theme, $"{idx + 1}/{Steps.Length} · {title}", body, true, keys);
        }

        private int CurrentStepIndex()
        {
            for (var i = 0; i < _stepDone.Length; i += 1) if (!_stepDone[i]) return i;
            return -1;
        }

        /// <summary>나침반·주변 목록 패널(`Hud.DrawWhereAmI`)의 고정 우측 경계.
        /// 그 패널은 UI 배율과 무관하게 항상 x=48에서 폭 380으로 그려진다</summary>
        private const float CompassRight = 428f;

        /// <summary>
        /// 상단 시간대 바(480,48,960,72) 바로 아래, 그 빈 틈의 가운데.
        ///
        /// **폭을 고정하지 않는다.** UI 배율(§15.0 · `Accessibility.UiScale`
        /// 100~150%)이 오르면 `HudTheme.Fit()`이 `ViewWidth`를 그만큼 좁히는데,
        /// 좌상단 나침반(고정 x428까지)은 안 움직이고 우상단 미니맵·수첩 요약은
        /// `RightOf`로 **왼쪽으로 밀려온다** — 150%에서는 그 틈이 1920 기준
        /// 760px가 아니라 실측 460px까지 좁아진다. 고정 폭으로 두면 배율을
        /// 올렸을 때 나침반·미니맵과 겹친다(품질 기준 위반). 그래서 매 프레임
        /// 그 틈(`minimapLeft - CompassRight`)을 실측해 폭을 거기 맞춘다.
        ///
        /// 요구 2 — 키는 문장 속이 아니라 `HudTheme.KeyCap`(§7.1.5 상호작용
        /// 프롬프트와 같은 키캡 모양)으로 따로 그린다. `keys`가 비어 있으면(단계에
        /// 쓰는 키가 없으면) 그 줄은 그냥 비운다.
        /// </summary>
        private void DrawCard(HudTheme theme, string title, string body, bool showSkip, string[] keys)
        {
            var minimapLeft = HudTheme.RightOf(1592f, 280f);
            const float margin = 32f;
            var width = Mathf.Clamp(minimapLeft - CompassRight - margin * 2f, 400f, 760f);
            var centerX = (CompassRight + minimapLeft) * 0.5f;
            // 건너뛰기가 있는 단계 카드는 키캡 줄 한 칸(34px)을 더 확보한다.
            // 완료 카드(showSkip=false)는 키가 없어 예전 높이(118) 그대로다
            var height = showSkip ? 152f : 118f;
            var rect = new Rect(centerX - width * 0.5f, 132f, width, height);

            theme.Panel(rect, HudTheme.Paper2, HudTheme.Accent, 2f);
            theme.Spine(rect, HudTheme.Accent, 4f);

            // 건너뛰기는 머리띠 줄에 작은 칩으로 둔다 — 본문 옆에 큰 버튼을 두면
            // 틈이 좁아졌을 때(150%) 본문이 두 배로 줄어든다
            var skipWidth = showSkip ? 150f : 0f;
            GUI.Label(new Rect(rect.x + 24f, rect.y + 12f, width - 48f - skipWidth, 20f), "온보딩 · " + title,
                theme.At(theme.Label, 12, HudTheme.Accent));

            var bodyY = rect.y + 38f;
            if (showSkip)
            {
                DrawKeyRow(theme, new Rect(rect.x + 24f, rect.y + 36f, width - 48f, 30f), keys);
                bodyY = rect.y + 74f;
            }
            GUI.Label(new Rect(rect.x + 24f, bodyY, width - 48f, rect.yMax - bodyY - 12f), body,
                theme.At(theme.Body, 18, HudTheme.Ink));

            if (!showSkip) return;

            var skip = new Rect(rect.xMax - 24f - 134f, rect.y + 8f, 134f, 26f);
            theme.Fill(skip, HudTheme.Paper3);
            theme.Border(skip, HudTheme.Rule);
            GUI.Label(skip, "건너뛰기 [ESC]", theme.At(theme.Small, 12, HudTheme.Ink2, TextAnchor.MiddleCenter));
            // 코드번호 없는 투명 버튼 위에 라벨을 얹는 기존 관례를 그대로 쓴다
            // (`Hud.DrawPhraseWheel` · `HudScreens`의 `GUI.Button(rect, "", GUIStyle.none)`)
            if (GUI.Button(skip, "", GUIStyle.none)) Skip();
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

        /* ══════════════════════════════════════════ 전체 키 안내 (F1) */

        /// <summary>
        /// G2 요구 3 — "실제로 존재하는 키만" 적는다. 각 줄의 출처(파일:줄)는
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
        /// 닫기 ESC              — HudScreens.cs:194 · QuestPlay.cs:98 · Tutorial.cs(Skip)
        /// 시간대 스킵(키 아님)    — Hud.cs:737-763(마우스 클릭 버튼, GameClient.cs:180 VoteSkip)
        /// 미니게임 확인 SPACE/ENTER — Minigame/Board.cs:75-80(공통 Tap/Confirm) · HudScreens.cs:517-542(점호·승급·취침 확인)
        /// 방독면 훈련 G          — MaskDrill.cs:86
        /// 이 안내 F1             — Tutorial.cs(본 파일, Update의 F1 분기)
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
            (new[] { "F1" }, "이 안내", "언제든 열고 닫는다"),
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
            GUI.Label(new Rect(rect.x + 32f, rect.y + 10f, 400f, 20f), "온보딩 · 조작 키 안내",
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

            GUI.Label(new Rect(rect.x, rect.yMax - 34f, width, 24f), "닫기 — [F1] 또는 [ESC]",
                theme.At(theme.Small, 13, HudTheme.Ink2, TextAnchor.MiddleCenter));
        }
    }
}
