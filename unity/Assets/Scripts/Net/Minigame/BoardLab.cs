using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 미니게임 실험대 (`docs/WORKORDER.md` A-1 준비 작업).
    ///
    /// **A-1의 문제**: 원형 14종 중 13종을 사람이 한 번도 눌러보지 않았고
    /// 파라미터가 전부 추정값이다. 실전 경로(`QuestPlay`)로 확인하려면 서버에
    /// 붙고, 로비를 통과하고, 그 일과가 오늘 배정될 때까지 기다려야 한다 —
    /// 열넷을 다 보려면 하루를 몇 번을 돌아야 한다.
    ///
    /// 여기서는 그 경로를 통째로 건너뛴다. F9를 누르면 목록에서 하나를 골라
    /// **즉시** 판이 열린다. 서버 연결도, 스냅샷도, 퀘스트 상태도 필요 없다 —
    /// `Boards.Open`을 직접 불러 판 하나만 떼어내 돈다.
    ///
    /// ── 소유권 ───────────────────────────────────────────────────────────
    /// 이 파일이 실험대의 전부다. `Board` · `Boards` · `HudMinigame` ·
    /// `QuestPlay`는 건드리지 않는다 — 그쪽은 다른 작업이 동시에 손대고 있다.
    /// `Hud.cs`에는 이 클래스를 부르는 줄 하나만 얹는다.
    ///
    /// ── 합성 스펙 ────────────────────────────────────────────────────────
    /// 난이도 1~3마다 쓰는 파라미터는 `packages/sim/data/quests.json`의 실제
    /// 일과에서 최대한 그대로 가져왔다(`BuildSpec` 주석에 출처 표시). 그 파일에
    /// 없는 난이도 칸(예: SCRUB 난이도 1)은 이웃 값에서 손으로 미룬 추정치라
    /// **실전과 다를 수 있다** — A-1 실측의 시작점이지 정답이 아니다.
    /// </summary>
    public static class BoardLab
    {
        /// <summary>
        /// 원형 14종. `RANDOM`은 이 중 하나를 무작위로 불러오는 메타 판이라
        /// (`RandomBoard`) 그 자체로는 재미가 없어 뺐다. 합동(`JointBoard`)도
        /// 원형이 아니라 "누가 채우는가"를 씌우는 겉이라 뺐다 — 안쪽 원형은
        /// 여기서 이미 잰다.
        /// </summary>
        private static readonly string[] Types =
        {
            SnapshotQuestsItemMinigameTypeValues.SCRUB,
            SnapshotQuestsItemMinigameTypeValues.PLACE,
            SnapshotQuestsItemMinigameTypeValues.AUDIT,
            SnapshotQuestsItemMinigameTypeValues.MASH,
            SnapshotQuestsItemMinigameTypeValues.BALANCE,
            SnapshotQuestsItemMinigameTypeValues.HOLD,
            SnapshotQuestsItemMinigameTypeValues.TRACE,
            SnapshotQuestsItemMinigameTypeValues.SORT,
            SnapshotQuestsItemMinigameTypeValues.TIMING,
            SnapshotQuestsItemMinigameTypeValues.SEQ,
            SnapshotQuestsItemMinigameTypeValues.RHYTHM,
            SnapshotQuestsItemMinigameTypeValues.TRACK,
            SnapshotQuestsItemMinigameTypeValues.SEARCH,
            SnapshotQuestsItemMinigameTypeValues.REACT,
        };

        private static bool _open;
        private static int _index;
        private static int _difficulty = 2;
        private static bool _fast;
        private static int _seed;

        private static Board _board;
        private static bool _awaitingResult;

        /// <summary>이번 실 세션에서 지금까지 나온 결과. 새것이 위로 쌓인다</summary>
        private static readonly List<string> Log = new List<string>();

        /// <summary>
        /// 실제 게임 프레임과 맞춘다.
        ///
        /// `OnGUI`는 한 프레임에 여러 번(Layout · Repaint · 입력 이벤트) 불린다.
        /// 키 입력과 판 `Tick`을 매번 처리하면 한 프레임에 두 번 진행되거나
        /// 토글이 눌렸다가 바로 풀리는 일이 생긴다 — `QuestPlay`는 `Update`
        /// (프레임당 한 번)에서 입력을 처리하고 `OnGUI`는 그리기만 하는데, 이
        /// 클래스는 `Hud.OnGUI`에서만 불리므로 그 구분을 직접 흉내 낸다.
        /// </summary>
        private static int _lastFrame = -1;

        /// <summary>
        /// URL로도 연다 — `…/play/코드?lab=1`.
        ///
        /// F9는 일부 브라우저가 자기 단축키로 가로챈다. 키가 안 먹는 환경에서도
        /// 실험실에 들어올 뒷문이 하나는 있어야 한다. WebGL에서만 의미가 있고,
        /// 에디터에서는 absoluteURL이 비어 있어 조용히 넘어간다.
        /// </summary>
        private static bool _urlChecked;

        public static void Draw(HudTheme theme)
        {
            var freshFrame = Time.frameCount != _lastFrame;
            if (freshFrame) _lastFrame = Time.frameCount;

            if (!_urlChecked)
            {
                _urlChecked = true;
                var url = Application.absoluteURL;
                if (!string.IsNullOrEmpty(url) && url.Contains("lab=1")) _open = true;
            }

            if (freshFrame && Input.GetKeyDown(KeyCode.F9)) _open = !_open;
            if (!_open) return;

            if (_board == null) OpenBoard();

            if (freshFrame)
            {
                HandleKeys();
                TickBoard();
            }

            DrawPanel(theme);
        }

        /* ──────────────────────────────────────────────────────── 입력 */

        private static void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.R)) OpenBoard();
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) SetDifficulty(1);
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) SetDifficulty(2);
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) SetDifficulty(3);
            if (Input.GetKeyDown(KeyCode.T)) _fast = !_fast;

            // 화살표(↑↓)는 안 쓴다 — `LocalPlayer`가 `Vertical` 축(↑↓ · W/S)으로
            // 이동하는데, 실험대는 판이 열려도 그 입력을 막지 않는다(§소유권 —
            // `LocalPlayer.Suspended`는 건드리지 않는다). 겹치지 않는 키를 쓴다
            if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                _index = (_index + 1) % Types.Length;
                OpenBoard();
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                _index = (_index - 1 + Types.Length) % Types.Length;
                OpenBoard();
            }
        }

        private static void SetDifficulty(int level)
        {
            if (_difficulty == level) return;
            _difficulty = level;
            OpenBoard();
        }

        private static void Select(int index)
        {
            if (index == _index) return;
            _index = index;
            OpenBoard();
        }

        /// <summary>지금 고른 원형·난이도로 판을 새로 연다 — 시드도 새로 받는다</summary>
        private static void OpenBoard()
        {
            _seed += 1;
            var type = Types[_index];
            var (spec, limit) = BuildSpec(type, _difficulty);
            // 씨앗에 실행 카운터를 넣는다. 실전(`QuestPlay.Restart`)은 재시도마다
            // **같은** 씨앗을 써서 같은 판을 다시 내지만(§익힘), 실험대는 반대로
            // 한 원형을 여러 배치로 훑어봐야 파라미터가 맞는지 감이 온다
            _board = Boards.Open(spec, limit, $"lab:{type}:{_difficulty}:{_seed}");
            _awaitingResult = true;
        }

        private static void TickBoard()
        {
            if (_board == null) return;

            if (_board.State != BoardState.Running)
            {
                if (_awaitingResult)
                {
                    LogResult();
                    _awaitingResult = false;
                }
                // 자동 재시작하지 않는다 — 결과를 다 읽고 R을 누르는 것은 사람이다
                return;
            }

            // **`Time.timeScale`은 건드리지 않는다.** 멀티 동기화가 그 값을
            // 공유하므로, 여기서 바꾸면 다른 곳까지 같이 빨라진다. 판에 들어가는
            // `dt`만 불려서 이 판의 `Tick` 안에서만 시간이 빨리 간다
            var dt = _fast ? Time.deltaTime * 4f : Time.deltaTime;
            _board.Tick(dt, BoardInput.Read());
        }

        private static void LogResult()
        {
            var type = Types[_index];
            var head = $"{Boards.Name(type)}({type}) d{_difficulty}";
            var line = _board.State == BoardState.Cleared
                ? $"{head} — 통과 {_board.Grade()} · {_board.Elapsed:0.0}초 · Miss {_board.Mistakes}"
                : $"{head} — 실패 · {_board.Elapsed:0.0}초 · Miss {_board.Mistakes}";
            Log.Insert(0, line);
            if (Log.Count > 80) Log.RemoveRange(80, Log.Count - 80);
        }

        /* ──────────────────────────────────────────────────── 합성 스펙 */

        /// <summary>
        /// 난이도 1~3의 합성 스펙.
        ///
        /// 출처는 셋 중 하나다.
        ///   1) `quests.json` 실측값 그대로(가장 가까운 난이도 항목)
        ///   2) 실측값을 손으로 보간·외삽한 값(이웃 난이도가 있을 때)
        ///   3) 실측이 아예 없어 감으로 잡은 값(원형에 그 난이도 데이터가 없을 때)
        /// 어느 쪽인지는 A-1 실측 문서에 옮길 때 `quests.json`과 대조해서
        /// 다시 확인해야 한다 — 여기 숫자는 "판을 열 수 있게" 하는 것이 목적이지
        /// 밸런스 확정값이 아니다.
        /// </summary>
        private static (SnapshotQuestsItemMinigame spec, float limit) BuildSpec(string type, int difficulty)
        {
            var d = Mathf.Clamp(difficulty, 1, 3);
            var spec = new SnapshotQuestsItemMinigame { type = type, variant = "lab", difficulty = d };
            var limit = 15f;

            switch (type)
            {
                case SnapshotQuestsItemMinigameTypeValues.SCRUB:
                    if (d == 1) { spec.stains = 5; spec.brush = 22; spec.coverage = 0.82; limit = 14; }
                    else if (d == 3) { spec.stains = 5; spec.brush = 20; spec.coverage = 0.95; limit = 20; }
                    else { spec.stains = 7; spec.brush = 26; spec.coverage = 0.90; limit = 17; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.PLACE:
                    // 난이도별로 `snapDeg` 0 / 90 / 180을 다 보도록 일부러 흩었다 —
                    // 실전 분포(0이 압도적)와 다르니 등급 체감을 셋을 다 눌러 비교할 것
                    if (d == 1) { spec.pieces = 7; spec.snapDeg = 0; spec.tolerance = 0.15; limit = 12; }
                    else if (d == 3) { spec.pieces = 6; spec.snapDeg = 180; spec.tolerance = 0.08; limit = 18; }
                    else { spec.pieces = 9; spec.snapDeg = 90; spec.tolerance = 0.10; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.AUDIT:
                    if (d == 1) { spec.entries = 10; spec.mismatches = 3; spec.strikes = 3; limit = 14; }
                    else if (d == 3) { spec.entries = 14; spec.mismatches = 4; spec.strikes = 3; limit = 22; }
                    else { spec.entries = 14; spec.mismatches = 4; spec.strikes = 3; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.MASH:
                    if (d == 1) { spec.taps = 30; spec.drain = 0.020; spec.stamina = 1.0; limit = 16; }
                    else if (d == 3) { spec.taps = 46; spec.drain = 0.030; spec.stamina = 1.0; limit = 24; }
                    else { spec.taps = 40; spec.drain = 0.026; spec.stamina = 1.0; limit = 20; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.BALANCE:
                    if (d == 1) { spec.sway = 0.8; spec.segments = 3; spec.tolerance = 0.34; limit = 14; }
                    else if (d == 3) { spec.sway = 1.0; spec.segments = 6; spec.tolerance = 0.20; limit = 24; }
                    else { spec.sway = 1.1; spec.segments = 4; spec.tolerance = 0.28; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.HOLD:
                    if (d == 1) { spec.bandMin = 0.72; spec.bandMax = 0.90; spec.drift = 0.30; spec.slack = 1.5; limit = 14; }
                    else if (d == 3) { spec.bandMin = 0.45; spec.bandMax = 0.70; spec.drift = 0.40; spec.slack = 2.0; limit = 24; }
                    else { spec.bandMin = 0.55; spec.bandMax = 0.82; spec.drift = 0.36; spec.slack = 1.5; limit = 20; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.TRACE:
                    if (d == 1) { spec.nodes = 6; spec.branches = 0; spec.rotations = 3; limit = 14; }
                    else if (d == 3) { spec.nodes = 16; spec.branches = 3; spec.rotations = 8; limit = 22; }
                    else { spec.nodes = 12; spec.branches = 2; spec.rotations = 6; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.SORT:
                    if (d == 1) { spec.items = 14; spec.bins = 3; spec.ambiguous = 3; spec.strikes = 3; limit = 14; }
                    else if (d == 3) { spec.items = 18; spec.bins = 4; spec.ambiguous = 5; spec.strikes = 3; limit = 22; }
                    else { spec.items = 16; spec.bins = 3; spec.ambiguous = 4; spec.strikes = 3; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.TIMING:
                    if (d == 1) { spec.hits = 3; spec.window = 0.16; spec.speed = 0.70; limit = 14; }
                    else if (d == 3) { spec.hits = 5; spec.window = 0.08; spec.speed = 0.95; limit = 20; }
                    else { spec.hits = 4; spec.window = 0.10; spec.speed = 0.80; limit = 16; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.SEQ:
                    if (d == 1) { spec.length = 4; spec.steps = 1; spec.show = 4; limit = 14; }
                    else if (d == 3) { spec.length = 8; spec.steps = 2; spec.show = 2; limit = 22; }
                    else { spec.length = 6; spec.steps = 1; spec.show = 3; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.RHYTHM:
                    if (d == 1) { spec.beats = 12; spec.bpm = 60; spec.combo = 8; limit = 10; }
                    else if (d == 3) { spec.beats = 28; spec.bpm = 96; spec.combo = 16; limit = 22; }
                    else { spec.beats = 16; spec.bpm = 72; spec.combo = 10; limit = 15; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.TRACK:
                    // `variant == "post"`일 때만 졸음(반경 축소)이 겹친다 — 난이도
                    // 3에서만 켜서 그 갈래도 실험대에서 확인할 수 있게 한다
                    if (d == 1) { spec.speed = 0.35; spec.holdRatio = 0.60; spec.variant = "sector"; limit = 16; }
                    else if (d == 3) { spec.speed = 0.50; spec.holdRatio = 0.72; spec.variant = "post"; limit = 25; }
                    else { spec.speed = 0.45; spec.holdRatio = 0.66; spec.variant = "sector"; limit = 20; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.SEARCH:
                    if (d == 1) { spec.hidden = 2; spec.cells = 16; spec.signal = 0.28; limit = 14; }
                    else if (d == 3) { spec.hidden = 4; spec.cells = 32; spec.signal = 0.16; limit = 24; }
                    else { spec.hidden = 3; spec.cells = 20; spec.signal = 0.20; limit = 18; }
                    break;

                case SnapshotQuestsItemMinigameTypeValues.REACT:
                    if (d == 1) { spec.window = 1.4; spec.count = 3; limit = 14; }
                    else if (d == 3) { spec.window = 0.85; spec.count = 5; limit = 18; }
                    else { spec.window = 1.1; spec.count = 4; limit = 16; }
                    break;
            }

            return (spec, limit);
        }

        private static string ParamSummary(string type, SnapshotQuestsItemMinigame s)
        {
            switch (type)
            {
                case SnapshotQuestsItemMinigameTypeValues.SCRUB:
                    return $"stains {s.stains:0} · brush {s.brush:0} · coverage {s.coverage:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.PLACE:
                    return $"pieces {s.pieces:0} · snapDeg {s.snapDeg:0} · tolerance {s.tolerance:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.AUDIT:
                    return $"entries {s.entries:0} · mismatches {s.mismatches:0} · strikes {s.strikes:0}";
                case SnapshotQuestsItemMinigameTypeValues.MASH:
                    return $"taps {s.taps:0} · drain {s.drain:0.000} · stamina {s.stamina:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.BALANCE:
                    return $"sway {s.sway:0.00} · segments {s.segments:0} · tolerance {s.tolerance:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.HOLD:
                    return $"band {s.bandMin:0.00}~{s.bandMax:0.00} · drift {s.drift:0.00} · slack {s.slack:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.TRACE:
                    return $"nodes {s.nodes:0} · branches {s.branches:0} · rotations {s.rotations:0}";
                case SnapshotQuestsItemMinigameTypeValues.SORT:
                    return $"items {s.items:0} · bins {s.bins:0} · ambiguous {s.ambiguous:0} · strikes {s.strikes:0}";
                case SnapshotQuestsItemMinigameTypeValues.TIMING:
                    return $"hits {s.hits:0} · window {s.window:0.00} · speed {s.speed:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.SEQ:
                    return $"length {s.length:0} · steps {s.steps:0} · show {s.show:0}";
                case SnapshotQuestsItemMinigameTypeValues.RHYTHM:
                    return $"beats {s.beats:0} · bpm {s.bpm:0} · combo {s.combo:0}";
                case SnapshotQuestsItemMinigameTypeValues.TRACK:
                    return $"speed {s.speed:0.00} · holdRatio {s.holdRatio:0.00} · variant {s.variant}";
                case SnapshotQuestsItemMinigameTypeValues.SEARCH:
                    return $"hidden {s.hidden:0} · cells {s.cells:0} · signal {s.signal:0.00}";
                case SnapshotQuestsItemMinigameTypeValues.REACT:
                    return $"window {s.window:0.00} · count {s.count:0}";
                default:
                    return "";
            }
        }

        /* ──────────────────────────────────────────────────────── 그리기 */

        private static void DrawPanel(HudTheme theme)
        {
            var full = new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight);
            theme.Fill(full, HudTheme.Dim, 0.75f);

            var margin = 36f;
            var panel = new Rect(margin, margin,
                HudTheme.ViewWidth - margin * 2f, HudTheme.ViewHeight - margin * 2f);
            theme.Fill(panel, HudTheme.Paper2);
            // 실전 판(HudMinigame)과 헷갈리지 않도록 테두리 색을 다르게 쓴다
            theme.Border(panel, HudTheme.Heat, 3f);

            const float headerH = 48f;
            theme.Header(panel, headerH);
            GUI.Label(new Rect(panel.x + 16f, panel.y + 8f, 500f, 32f),
                "BoardLab — 미니게임 실험대  [F9]",
                theme.At(theme.Title, 22, HudTheme.Ink));
            GUI.Label(new Rect(panel.xMax - 760f, panel.y + 14f, 744f, 24f),
                "[ [ ] ] 원형  ·  [1][2][3] 난이도  ·  [R] 재시작  ·  [T] 배속 ×1/×4",
                theme.At(theme.Label, 13, HudTheme.Ink2, TextAnchor.MiddleRight));

            var top = panel.y + headerH + 16f;
            var bottom = panel.yMax - 16f;

            const float logH = 200f;
            var contentBottom = bottom - logH - 14f;

            /* ── 좌: 원형 14종 목록 ─────────────────────────────────── */
            var listW = 250f;
            var list = new Rect(panel.x + 16f, top, listW, contentBottom - top);
            DrawList(theme, list);

            /* ── 중앙: 판 + 실시간 판정값 ────────────────────────────── */
            var boardX = list.xMax + 16f;
            var debugW = 340f;
            var boardArea = new Rect(boardX, top, panel.xMax - 16f - debugW - 16f - boardX,
                contentBottom - top);
            DrawBoardArea(theme, boardArea);

            /* ── 우: 판정 내부값 · 난이도 · 결과 요약 ────────────────── */
            var debugArea = new Rect(boardArea.xMax + 16f, top, debugW, contentBottom - top);
            DrawDebug(theme, debugArea);

            /* ── 하단: 누적 결과 로그 ──────────────────────────────── */
            var logArea = new Rect(panel.x + 16f, contentBottom + 14f, panel.width - 32f, logH);
            DrawLog(theme, logArea);
        }

        private static void DrawList(HudTheme theme, Rect area)
        {
            theme.Fill(area, HudTheme.Paper3);
            theme.Border(area, HudTheme.Rule2);

            const float rowH = 30f;
            for (var i = 0; i < Types.Length; i += 1)
            {
                var row = new Rect(area.x + 4f, area.y + 4f + i * (rowH + 2f), area.width - 8f, rowH);
                if (row.yMax > area.yMax - 4f) break;

                var selected = i == _index;
                var hot = row.Contains(BoardInput.Read().Mouse);
                theme.Fill(row, selected ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper3);
                if (selected) theme.Border(row, HudTheme.Accent);

                var label = $"{i + 1,2}. {Boards.Name(Types[i])} · {Types[i]}";
                GUI.Label(row, label,
                    theme.At(theme.Small, 14, selected ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleLeft));

                if (hot && Input.GetMouseButtonDown(0)) Select(i);
            }
        }

        private static void DrawBoardArea(HudTheme theme, Rect area)
        {
            theme.Fill(area, HudTheme.Paper3);
            theme.Border(area, HudTheme.Rule);

            if (_board == null) return;

            var type = Types[_index];
            var headH = 30f;
            var chip = new Rect(area.x + 10f, area.y + 8f, 92f, 24f);
            theme.Chip(chip, Boards.Name(type), HudTheme.AccentW, HudTheme.Accent);
            GUI.Label(new Rect(chip.xMax + 10f, area.y + 6f, area.width - 220f, 28f),
                $"{type}  ·  d{_difficulty}  ·  제한 {_board.Limit:0}초{(_fast ? "  (×4 배속)" : "")}",
                theme.At(theme.Body, 16, HudTheme.Ink2));

            var footH = 54f;
            var body = new Rect(area.x + 10f, area.y + headH + 14f,
                area.width - 20f, area.height - headH - footH - 24f);
            if (body.width > 0f && body.height > 0f)
            {
                _board.Draw(theme, body);
            }

            var footY = body.yMax + 10f;
            GUI.Label(new Rect(area.x + 10f, footY, area.width - 20f, 22f),
                _board.Instruction, theme.At(theme.Small, 15, HudTheme.Ink));
            GUI.Label(new Rect(area.x + 10f, footY + 22f, area.width - 20f, 20f),
                _board.Status, theme.At(theme.Label, 13, HudTheme.Ink3));

            if (_board.State != BoardState.Running)
            {
                var cleared = _board.State == BoardState.Cleared;
                var banner = new Rect(area.center.x - 160f, area.center.y - 34f, 320f, 68f);
                theme.Fill(banner, cleared ? HudTheme.AccentW : HudTheme.AlertW, 0.95f);
                theme.Border(banner, cleared ? HudTheme.Accent : HudTheme.Alert, 2f);
                GUI.Label(banner,
                    cleared ? $"통과 — {_board.Grade()}   [R] 재시작" : "실패   [R] 재시작",
                    theme.At(theme.Heading, 22, cleared ? HudTheme.Accent : HudTheme.Alert,
                        TextAnchor.MiddleCenter));
            }
        }

        private static void DrawDebug(HudTheme theme, Rect area)
        {
            theme.Fill(area, HudTheme.Paper3);
            theme.Border(area, HudTheme.Rule2);

            var type = Types[_index];
            var y = area.y + 10f;
            const float lh = 24f;

            void Line(string label, string value, Color color)
            {
                GUI.Label(new Rect(area.x + 10f, y, 130f, lh), label,
                    theme.At(theme.Label, 13, HudTheme.Ink2));
                GUI.Label(new Rect(area.x + 130f, y, area.width - 140f, lh), value,
                    theme.At(theme.Mono, 15, color, TextAnchor.MiddleRight));
                y += lh;
            }

            if (_board != null)
            {
                Line("Fill", $"{_board.Fill * 100f:0}%", HudTheme.Ink);
                Line("남은 시간", $"{_board.Remaining:0.0}s / {_board.Limit:0}s", HudTheme.Ink);
                Line("경과", $"{_board.Elapsed:0.0}s", HudTheme.Ink3);
                Line("Miss", $"{_board.Mistakes}",
                    _board.Mistakes == 0 ? HudTheme.Accent : _board.Mistakes <= 1 ? HudTheme.Heat : HudTheme.Alert);
                Line("잔여율", $"{_board.TimeLeftRatio * 100f:0}%",
                    _board.TimeLeftRatio >= 0.2f ? HudTheme.Accent : HudTheme.Heat);
                Line("지금 등급", _board.Grade(),
                    _board.Grade() == SnapshotQuestsItemGradeValues.A ? HudTheme.Accent
                    : _board.Grade() == SnapshotQuestsItemGradeValues.B ? HudTheme.Heat
                    : HudTheme.Alert);
            }

            y += 6f;
            theme.Fill(new Rect(area.x + 10f, y, area.width - 20f, 1f), HudTheme.Rule);
            y += 10f;

            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 20f), "등급 컷 (Board.Grade 그대로)",
                theme.At(theme.Label, 12, HudTheme.Ink3));
            y += 20f;
            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 18f), "A  실수 0 · 잔여 20%↑",
                theme.At(theme.Small, 13, HudTheme.Accent));
            y += 18f;
            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 18f), "B  실수 ≤ 1",
                theme.At(theme.Small, 13, HudTheme.Heat));
            y += 18f;
            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 18f), "C  그 외 (통과는 했다)",
                theme.At(theme.Small, 13, HudTheme.Alert));
            y += 26f;

            theme.Fill(new Rect(area.x + 10f, y, area.width - 20f, 1f), HudTheme.Rule);
            y += 10f;

            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 20f), "합성 스펙 (난이도 " + _difficulty + ")",
                theme.At(theme.Label, 12, HudTheme.Ink3));
            y += 20f;
            var summary = _board != null ? ParamSummary(type, _board.Spec) : "";
            GUI.Label(new Rect(area.x + 10f, y, area.width - 20f, 40f), summary,
                new GUIStyle(theme.At(theme.Small, 13, HudTheme.Ink)) { wordWrap = true });
            y += 46f;

            /* 난이도 버튼 */
            var diffY = y;
            for (var i = 0; i < 3; i += 1)
            {
                var level = i + 1;
                var btn = new Rect(area.x + 10f + i * 70f, diffY, 62f, 30f);
                var on = level == _difficulty;
                var hot = btn.Contains(BoardInput.Read().Mouse);
                theme.Fill(btn, on ? HudTheme.AccentW : hot ? HudTheme.Paper2 : HudTheme.Paper);
                theme.Border(btn, on ? HudTheme.Accent : HudTheme.Rule);
                GUI.Label(btn, $"난이도 {level}", theme.At(theme.Small, 13,
                    on ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
                if (hot && Input.GetMouseButtonDown(0)) SetDifficulty(level);
            }
            y = diffY + 40f;

            /* 배속 · 재시작 버튼 */
            var fastBtn = new Rect(area.x + 10f, y, 100f, 30f);
            var fastHot = fastBtn.Contains(BoardInput.Read().Mouse);
            theme.Fill(fastBtn, _fast ? HudTheme.AccentW : fastHot ? HudTheme.Paper2 : HudTheme.Paper);
            theme.Border(fastBtn, _fast ? HudTheme.Accent : HudTheme.Rule);
            GUI.Label(fastBtn, _fast ? "배속 ×4  [T]" : "배속 ×1  [T]",
                theme.At(theme.Small, 13, _fast ? HudTheme.Accent : HudTheme.Ink, TextAnchor.MiddleCenter));
            if (fastHot && Input.GetMouseButtonDown(0)) _fast = !_fast;

            var restartBtn = new Rect(area.x + 120f, y, 100f, 30f);
            var restartHot = restartBtn.Contains(BoardInput.Read().Mouse);
            theme.Fill(restartBtn, restartHot ? HudTheme.Paper2 : HudTheme.Paper);
            theme.Border(restartBtn, HudTheme.Rule);
            GUI.Label(restartBtn, "재시작  [R]", theme.At(theme.Small, 13, HudTheme.Ink, TextAnchor.MiddleCenter));
            if (restartHot && Input.GetMouseButtonDown(0)) OpenBoard();
        }

        private static void DrawLog(HudTheme theme, Rect area)
        {
            theme.Fill(area, HudTheme.Paper3);
            theme.Border(area, HudTheme.Rule2);

            GUI.Label(new Rect(area.x + 10f, area.y + 6f, area.width - 20f, 18f),
                $"결과 누적 ({Log.Count}건) — 새 결과가 위로 쌓인다",
                theme.At(theme.Label, 12, HudTheme.Ink3));

            const float lh = 17f;
            var colW = (area.width - 20f) / 2f;
            var maxRows = Mathf.FloorToInt((area.height - 30f) / lh);

            for (var i = 0; i < Log.Count && i < maxRows * 2; i += 1)
            {
                var col = i / maxRows;
                var row = i % maxRows;
                var rect = new Rect(area.x + 10f + col * colW, area.y + 28f + row * lh, colW - 8f, lh);
                GUI.Label(rect, Log[i], theme.At(theme.Label, 13, HudTheme.Ink2));
            }
        }
    }
}
