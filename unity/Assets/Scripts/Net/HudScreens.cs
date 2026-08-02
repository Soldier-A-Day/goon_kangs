using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 전체화면 UI (SAD-ART-001 §7.2 ~ §7.9).
    ///
    /// 상시 HUD(`Hud`)와 나눈 이유는 **월드를 가리느냐**로 갈리기 때문이다. 여기 있는
    /// 것들은 전부 배경을 암전시키고 조작을 잠근다 — 수첩을 보며 걷지 않는다.
    ///
    /// 화면이 겹칠 때의 우선순위가 규칙이다. 점호는 되돌릴 수 없으므로 무엇보다
    /// 위고, 하달 창은 20초 타이머가 도는 동안 다른 창이 덮으면 안 된다.
    /// </summary>
    public sealed class HudScreens
    {
        private readonly Hud _hud;

        private enum Screen { None, Notebook, Map, Schedule, Delegation, RollCall, Sleep, Rank }

        private Screen _screen = Screen.None;
        private bool _radialOpen;

        /* 연출 상태 */
        private float _rollCallStart;
        private ServerEvent _judgement;
        private ServerEvent _rankReview;
        private ServerEvent _sleepSettle;
        private Snapshot _beforeSleep;
        private double _lastDay = -1d;

        /// <summary>하루 마감에서 판정 다음으로 보여줄 화면들(승급 → 취침 정산 순).
        ///
        /// 서버는 하루 마감 이펙트를 한 WS 메시지로 묶어 보내고(room.ts:506),
        /// GameClient.cs가 그걸 한 프레임에 동기 for문으로 전부 발화시킨다. 그 순간
        /// OnEvent가 도착 즉시 `_screen`을 갈아치우면 마지막 이벤트만 화면에 남는다 —
        /// 판정·승급 화면이 단 한 프레임도 그려지지 못하고 취침 정산으로 직행했던
        /// 원인이 이거다. 그래서 여기서는 화면을 바로 바꾸지 않고 큐에 쌓아 두고,
        /// 지금 떠 있는 화면이 제 몫의 시간을 다 채운 뒤에야 하나씩 꺼낸다.</summary>
        private readonly Queue<Screen> _dayEndQueue = new();

        /// <summary>지금 떠 있는 승급/취침 정산 화면이 언제 떴는가(unscaledTime).
        /// 판정(RollCall) 화면은 기존 `_rollCallStart`를 그대로 쓴다 — 그 화면만
        /// 기획서에 박힌 6.4초 고정 연출이라 별도 필드로 이미 다뤄지고 있었다.</summary>
        private float _dayEndScreenStart;

        /// <summary>이 시간대의 하달을 내가 끝냈는가. 창을 다시 띄우지 않는다</summary>
        private string _delegationDone;

        /* 하달 창 선택 상태 (§7.3 접근성 — 키보드 경로) */
        private int _pickedChore;
        private int _pickedTarget;

        public HudScreens(Hud hud) => _hud = hud;

        /// <summary>이 창들이 떠 있으면 이동·상호작용을 잠근다</summary>
        public bool BlocksMovement => _screen != Screen.None || _radialOpen;

        private GameClient Client => _hud.client;

        /* ══════════════════════════════════════════════════════ 입력 */

        public void Update()
        {
            // §7.8 "표시 지연 0ms — 홀드 즉시 표시. 애니메이션 금지"
            _radialOpen = Input.GetKey(KeyCode.Q);
            if (_radialOpen)
            {
                for (var i = 0; i < 8; i += 1)
                {
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Send(i);
                }
                if (Input.GetKeyUp(KeyCode.Q)) { }
            }
            if (Input.GetKeyUp(KeyCode.Q))
            {
                var slot = SlotUnderCursor();
                if (slot >= 0) Send(slot);
            }

            // 점호·하달은 서버 상태가 열고 닫는다. 플레이어가 닫을 수 없다.
            // 승급·취침 정산도 하루 마감 연출의 한 토막이라 여기 묶는다 — Tab/M/Space
            // 같은 창 전환 키가 끼어들면 큐가 진행되는 도중에 다른 창으로 새 버린다.
            if (_screen is Screen.RollCall or Screen.Delegation)
                return;
            if (_screen is Screen.Rank or Screen.Sleep)
            {
                UpdateDayEndAdvance();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Tab))
                _screen = _screen == Screen.Notebook ? Screen.None : Screen.Notebook;
            if (Input.GetKeyDown(KeyCode.M))
                _screen = _screen == Screen.Map ? Screen.None : Screen.Map;
            // 일과표는 Space로 열고 닫는다. 하루에 몇 번씩 여는 화면이라
            // 손이 가장 먼저 닿는 키에 둔다
            if (Input.GetKeyDown(KeyCode.Space))
                _screen = _screen == Screen.Schedule ? Screen.None : Screen.Schedule;

            if (Input.GetKeyDown(KeyCode.Escape) && _screen != Screen.None)
                _screen = Screen.None;
        }

        /* ══════════════════════════════════════════════════ 상태 갱신 */

        public void OnSnapshot(Snapshot snapshot)
        {
            if (snapshot?.phase == null) return;

            // **하루 마감 연출이 도는 동안에는 스냅샷이 창을 열지 못한다.**
            //
            // 창을 여는 길이 둘인데 서로를 모른다 — 하달 창과 아침 일과표는 여기
            // 스냅샷이 열고, 판정·승급·취침은 `OnEvent`의 큐가 연다. 그래서 하달
            // 창이 연출 도중에 `_screen`을 가져가면 `Update()`가 하달 분기에서
            // 먼저 빠져나가 큐를 돌리는 곳에 영영 닿지 못하고, 남은 화면들이 다음
            // 판정이 올 때까지 갇힌다. 아침 일과표는 반대로 조용히 사라진다 —
            // 연출 도중에 날이 바뀌면 `_lastDay`만 갱신되고 창은 안 뜬다.
            //
            // 건너뛰어도 잃는 것이 없다. 스냅샷은 10Hz로 계속 오고 하달 창은 몇십
            // 초 열려 있으므로, 연출이 끝난 바로 다음 스냅샷이 그대로 연다.
            if (_screen is Screen.RollCall or Screen.Rank or Screen.Sleep ||
                _dayEndQueue.Count > 0)
            {
                if (snapshot.phase.id != SnapshotPhaseIdValues.Rollcall) _beforeSleep = snapshot;
                return;
            }

            // §7.3 하달 창은 서버가 연다 — `delegationWindowMsLeft`가 0보다 크면 열려 있다
            if (snapshot.phase.delegationWindowMsLeft > 0d && _delegationDone != snapshot.phase.id)
            {
                if (_screen != Screen.Delegation)
                {
                    _screen = Screen.Delegation;
                    _pickedChore = 0;
                    _pickedTarget = 0;
                }
            }
            else if (_screen == Screen.Delegation)
            {
                _screen = Screen.None;
            }

            // 창이 닫히면 확정 표시를 푼다 — 다음 시간대에 다시 열려야 한다
            if (snapshot.phase.delegationWindowMsLeft <= 0d) _delegationDone = null;

            // §7.4 일과표는 매일 아침 전면 표시
            if (snapshot.day != _lastDay)
            {
                if (_lastDay >= 0d && _screen == Screen.None) _screen = Screen.Schedule;
                _lastDay = snapshot.day;
            }

            // 취침 정산이 스탯 변화를 보여주려면 **정산 전 스냅샷**이 필요하다
            if (snapshot.phase.id != SnapshotPhaseIdValues.Rollcall) _beforeSleep = snapshot;
        }

        public void OnEvent(ServerEvent item)
        {
            switch (item.type)
            {
                case ServerEventTypeValues.DayJudged:
                    // 새 판정이 도착했다 = 이전 하루의 연출은 뭐가 남아 있었든 끝난 걸로
                    // 친다. 밀린 연출을 큐에 그대로 두면 다음 하루가 시작된 뒤에도
                    // 어제 화면이 뒤늦게 튀어나온다 — 화면이 하루 뒤처지는 쪽이
                    // 훨씬 큰 고장이므로 무조건 버리고 지금 판정을 최우선으로 보여준다.
                    _judgement = item;
                    _dayEndQueue.Clear();
                    _rollCallStart = Time.unscaledTime;
                    _screen = Screen.RollCall;
                    break;

                case ServerEventTypeValues.RankReviewed:
                    _rankReview = item;
                    // 판정 실패 날은 RollCall 화면이 그대로 남아 종료 흐름(퇴소)으로
                    // 가야 한다(§7.5, HudEnding 담당). 승급 화면을 끼워 넣으면 그 흐름을
                    // 밀어내 버리므로, SleepSettled와 똑같은 가드를 여기도 건다.
                    if (_judgement != null && !_judgement.passed) break;
                    EnqueueDayEnd(Screen.Rank);
                    break;

                case ServerEventTypeValues.SleepSettled:
                    _sleepSettle = item;
                    // 실패 시엔 RollCall 화면을 지키는 기존 동작 그대로 — 취침 정산을
                    // 보여주지 않고 버려서(§7.5) 판정 화면이 종료 흐름으로 이어지게 한다.
                    if (_judgement != null && !_judgement.passed) break;
                    EnqueueDayEnd(Screen.Sleep);
                    break;
            }
        }

        /// <summary>하루 마감 화면을 큐에 쌓는다. 지금 아무 화면도 안 떠 있으면(이론상
        /// 판정 없이 단독으로 도착한 경우) 즉시 보여준다 — 놀리지 않는다.</summary>
        private void EnqueueDayEnd(Screen screen)
        {
            if (_screen == Screen.None)
            {
                _screen = screen;
                _dayEndScreenStart = Time.unscaledTime;
            }
            else
            {
                _dayEndQueue.Enqueue(screen);
            }
        }

        /// <summary>지금 화면을 접고 큐에서 다음 화면을 꺼낸다. 큐가 비었으면 닫는다.
        /// RollCall(6.4초 고정 연출)이 끝나는 지점과, 아래 <see cref="UpdateDayEndAdvance"/>가
        /// Rank·Sleep 화면을 넘길 때 둘 다 이걸 거쳐서 화면이 큐 순서를 벗어나지 않는다.</summary>
        private void AdvanceDayEnd()
        {
            _screen = _dayEndQueue.Count > 0 ? _dayEndQueue.Dequeue() : Screen.None;
            _dayEndScreenStart = Time.unscaledTime;
        }

        /// <summary>승급·취침 정산 화면의 자동/수동 전환.
        ///
        /// 판정 화면은 §7.5 6.4초 고정 연출이라 스킵을 안 받는다 — DrawRollCall이
        /// 자체 타이머(`_rollCallStart`)로 알아서 <see cref="AdvanceDayEnd"/>를 부른다.
        /// 여기서는 그 뒤에 오는 두 화면만 다룬다.
        ///
        /// 최소 시간 동안은 무슨 입력이 와도 넘기지 않는다(내용을 최소한은 보게).
        /// 그 뒤로는 자동 전환 시각이 되거나 아무 키/클릭이 오면 즉시 다음으로
        /// 넘긴다 — 실시간 멀티라 서버는 계속 틱을 돌기 때문에, 입력을 기다리며
        /// 영영 멈춰 있는 경로를 만들면 안 된다(요구사항 4).
        ///
        /// 값 근거 — 기획서에 이 두 화면의 연출 시간이 명시돼 있지 않아 화면
        /// 내용량으로 정했다:
        /// - 승급(Rank): 분대원 4명짜리 표 하나, 단계식 리빌 없이 한 번에 그려진다.
        ///   최소 3.0s(표를 눈으로 훑는 데 필요한 시간), 자동 전환 6.0s(최소치의 2배 —
        ///   판정 화면 6.4s와 체감 속도를 맞췄다).
        /// - 취침 정산(Sleep): 스탯 6종 + 군기까지 숫자를 하나씩 대조해야 해서 더 준다.
        ///   최소 2.5s, 자동 전환 10.0s — 원래 "SPACE — 닫기"로 수동 닫기만 있던
        ///   화면이라(자동으로 안 넘어가도 됐다) 자동 하한을 가장 넉넉하게 잡았다.
        /// </summary>
        private void UpdateDayEndAdvance()
        {
            if (_screen != Screen.Rank && _screen != Screen.Sleep) return;

            var elapsed = Time.unscaledTime - _dayEndScreenStart;
            var floor = _screen == Screen.Rank ? 3.0f : 2.5f;
            var autoAdvance = _screen == Screen.Rank ? 6.0f : 10.0f;
            // `Input.anyKeyDown`은 키보드뿐 아니라 마우스 버튼도 포함한다 — 클릭도 스킵으로 친다
            var skipRequested = Input.anyKeyDown;

            if (elapsed >= autoAdvance || (elapsed >= floor && skipRequested))
                AdvanceDayEnd();
        }

        /* ══════════════════════════════════════════════════════ 그리기 */

        public void Draw(HudTheme theme, Snapshot snapshot)
        {
            if (_radialOpen) DrawRadial(theme);

            switch (_screen)
            {
                case Screen.Notebook: DrawNotebook(theme, snapshot); break;
                case Screen.Map: DrawBaseMap(theme, snapshot); break;
                case Screen.Schedule: DrawSchedule(theme, snapshot); break;
                case Screen.Delegation: DrawDelegation(theme, snapshot); break;
                case Screen.RollCall: DrawRollCall(theme, snapshot); break;
                case Screen.Sleep: DrawSleep(theme, snapshot); break;
                case Screen.Rank: DrawRankReview(theme, snapshot); break;
            }
        }

        /// <summary>암전 + 패널. 모든 전체화면이 같은 골격을 쓴다(목업 공통)</summary>
        private static Rect Backdrop(HudTheme theme, float w, float h, float dim = 0.68f)
        {
            // **암전은 화면 전체를 덮어야 한다.**
            //
            // HUD는 1920×1080 설계 좌표에 그리고 `Mathf.Min`으로 축소해 얹는다.
            // 화면이 16:9보다 넓으면 그 배율로는 설계 폭이 화면 폭에 못 미치고,
            // 암전이 딱 거기서 끝나 오른쪽에 월드가 그대로 드러난다 — 1820폭
            // 화면에서 실제로 400px 가까이 잘려 보였다.
            //
            // 설계 좌표로 되돌린 **실제 화면 크기**만큼 칠한다. 창은 그대로
            // 가운데 두므로 목업 실측 좌표는 건드리지 않는다.
            // `Screen`은 이 클래스의 전체화면 enum과 이름이 겹친다 — 정식 이름을 쓴다
            var pixels = new Vector2(UnityEngine.Screen.width, UnityEngine.Screen.height);
            var scale = Mathf.Min(pixels.x / HudTheme.ViewWidth,
                                  pixels.y / HudTheme.ViewHeight);
            var full = new Rect(0f, 0f,
                                Mathf.Max(HudTheme.ViewWidth, pixels.x / scale),
                                Mathf.Max(HudTheme.ViewHeight, pixels.y / scale));
            theme.Fill(full, HudTheme.Dim, dim);
            var rect = new Rect((HudTheme.ViewWidth - w) * 0.5f, (HudTheme.ViewHeight - h) * 0.5f, w, h);
            theme.Fill(rect, HudTheme.Paper);
            theme.Border(rect, HudTheme.Rule, 2f);
            return rect;
        }

        private static void Head(HudTheme theme, Rect panel, float height,
                                string code, string title, string subtitle)
        {
            theme.Header(panel, height);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 12f, 500f, 20f), code,
                theme.At(theme.Label, 12, HudTheme.Accent));
            GUI.Label(new Rect(panel.x + 32f, panel.y + 36f, 400f, 36f), title,
                theme.At(theme.Title, 26, HudTheme.Ink));
            if (!string.IsNullOrEmpty(subtitle))
            {
                GUI.Label(new Rect(panel.x + 32f + theme.Measure(title, theme.Title) + 24f,
                        panel.y + 38f, 520f, 30f), subtitle,
                    theme.At(theme.Body, 17, HudTheme.Ink2));
            }
        }

        /* ═══════════════════════════════════════════ §7.2 수첩 (Tab) */

        private void DrawNotebook(HudTheme theme, Snapshot snapshot)
        {
            // 목업 실측: 1440×840 @ 중앙
            var panel = Backdrop(theme, 1440f, 840f);
            var counts = CountQuests(snapshot, Client.MemberId);
            var left = counts.requiredTotal - counts.requiredDone;

            Head(theme, panel, 88f, "NOTEBOOK  [TAB]", "수 첩",
                snapshot == null ? "" :
                $"D-{snapshot.day:00} · {HudTheme.PhaseLabel(snapshot.phase?.id)} · 잔여 {Remaining(snapshot)}");

            // §7.2 "남은 필수 카운터 — 헤더 우측, 72px 초대형 숫자"
            var counter = new Rect(panel.xMax - 164f, panel.y + 14f, 132f, 58f);
            var counterColor = left == 0 ? HudTheme.Accent : left <= 2 ? HudTheme.Ink : HudTheme.Alert;
            theme.Fill(counter, left == 0 ? HudTheme.AccentW : HudTheme.AlertW);
            theme.Border(counter, counterColor, 2f);
            GUI.Label(counter, $"{left}", theme.At(theme.Display, 44, counterColor, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(counter.x - 100f, counter.y, 92f, 24f), "남은 필수",
                theme.At(theme.Small, 13, HudTheme.Ink2, TextAnchor.MiddleRight));

            // 4분할 — 각 720×376 (목업 실측)
            var top = panel.y + 88f;
            var midX = panel.x + panel.width * 0.5f;
            var midY = top + 376f;
            theme.Fill(new Rect(midX - 1f, top, 2f, panel.height - 88f), HudTheme.Rule);
            theme.Fill(new Rect(panel.x, midY, panel.width, 2f), HudTheme.Rule);

            Quadrant(theme, new Rect(panel.x + 40f, top + 30f, 680f, 346f),
                "내 필수", $"{counts.requiredDone} / {counts.requiredTotal}", HudTheme.Alert,
                snapshot, q => q.required && IsMine(q), true);

            Joint(theme, new Rect(midX + 40f, top + 30f, 640f, 346f), snapshot, counts);

            var opt = new Rect(panel.x + 40f, midY + 30f, 680f, 320f);
            Quadrant(theme, opt,
                "내 선택", $"{counts.optionalDone} / {counts.optionalTotal}", HudTheme.Optional,
                snapshot, q => !q.required && IsMine(q) &&
                               q.kind != SnapshotQuestsItemKindValues.Joint &&
                               q.kind != SnapshotQuestsItemKindValues.Care, false,
                reserve: 152f);
            Care(theme, new Rect(opt.x - 8f, opt.yMax - 142f, opt.width, 142f), snapshot);

            var handed = new Rect(midX + 40f, midY + 30f, 640f, 320f);
            Quadrant(theme, handed,
                "하달받은 것", HandedLabel(snapshot), HudTheme.Heat,
                snapshot, q => !string.IsNullOrEmpty(q.delegatedFrom) && IsMine(q), false, reserve: 82f);
            SquadProgress(theme, new Rect(handed.x - 8f, handed.yMax - 72f, handed.width, 72f), snapshot);

            bool IsMine(SnapshotQuestsItem q) => q.ownerId == Client.MemberId;
        }

        /// <summary>
        /// 회복 행동 (7.0 표 7-1).
        ///
        /// **일과가 아니라서 분면 넷 어디에도 안 들어간다.** 안 했다고 판정이
        /// 깨지지도 군기가 깎이지도 않으니 "내 선택"에 섞으면 승급 계산이 흐려지고,
        /// 그렇다고 안 보여주면 12시·18시 칸에 왜 아무것도 없는지 알 수가 없다.
        ///
        /// 지금 칸의 것만 세운다. 밥은 중식에, 세면은 개인정비에 하는 것이고,
        /// 지나간 칸의 회복은 다시 못 한다.
        /// </summary>
        private void Care(HudTheme theme, Rect box, Snapshot snapshot)
        {
            theme.Fill(box, HudTheme.Paper3);
            theme.Spine(box, HudTheme.Cold, 4f);
            GUI.Label(new Rect(box.x + 20f, box.y + 6f, 300f, 20f), "회복 — 몸 관리",
                theme.At(theme.Label, 13, HudTheme.Cold));

            var phase = snapshot?.phase?.id;
            var y = box.y + 32f;
            var any = false;

            if (snapshot?.quests != null)
            {
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null) continue;
                    if (quest.kind != SnapshotQuestsItemKindValues.Care) continue;
                    if (quest.ownerId != Client.MemberId || quest.phase != phase) continue;
                    if (y > box.yMax - 26f) break;
                    any = true;

                    var done = quest.status == SnapshotQuestsItemStatusValues.Done;
                    if (done) HudIcons.Check(new Rect(box.x + 20f, y + 2f, 18f, 18f), HudTheme.Cold);
                    else HudIcons.EmptyBox(new Rect(box.x + 21f, y + 3f, 16f, 16f), HudTheme.Ink2);

                    GUI.Label(new Rect(box.x + 48f, y, 260f, 22f), quest.label,
                        theme.At(theme.Body, 16, done ? HudTheme.Ink3 : HudTheme.Cold));
                    if (done)
                    {
                        var w = Mathf.Min(260f, theme.Measure(quest.label, theme.Body) + 4f);
                        theme.Fill(new Rect(box.x + 46f, y + 10f, w, 2f), HudTheme.Cold);
                    }

                    GUI.Label(new Rect(box.xMax - 264f, y, 220f, 22f), ZoneNames.Of(quest.zone),
                        theme.At(theme.Small, 14, HudTheme.Ink3, TextAnchor.MiddleRight));
                    y += 26f;
                }
            }

            if (!any)
            {
                GUI.Label(new Rect(box.x + 20f, y, box.width - 40f, 22f),
                    "이 칸에는 회복이 없다 — 중식(12:00) · 개인정비(18:00)에서 한다",
                    theme.At(theme.Small, 14, HudTheme.Ink3));
            }
        }

        /// <summary>§7.2 "0 / n" — 하달은 받은 것이 없으면 칸이 비므로 숫자로 말한다</summary>
        private string HandedLabel(Snapshot snapshot)
        {
            if (snapshot?.quests == null) return "";
            int total = 0, done = 0;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null || string.IsNullOrEmpty(quest.delegatedFrom)) continue;
                if (quest.ownerId != Client.MemberId) continue;
                total += 1;
                if (quest.status == SnapshotQuestsItemStatusValues.Done) done += 1;
            }
            return total == 0 ? "없음" : $"{done} / {total}";
        }

        /// <summary>이 시간대에 남은 시간 `M:SS`. 목업 헤더의 "잔여 02:41"</summary>
        private static string Remaining(Snapshot snapshot)
        {
            var phase = snapshot?.phase;
            if (phase == null) return "0:00";
            var left = Mathf.Max(0, Mathf.RoundToInt((float)(phase.durationMs - phase.elapsedMs) / 1000f));
            return $"{left / 60}:{left % 60:00}";
        }

        /// <summary>
        /// 분대 합동 (MOCKUP_04 우상).
        ///
        /// 다른 분면과 달리 **한 건을 크게 편다.** 합동은 하루에 하나뿐이고,
        /// 혼자서는 진척이 0이라 "지금 몇 명이 거기 있는가"가 전부다 — 목록으로
        /// 만들면 그 한 줄이 어디에도 안 들어간다.
        ///
        /// 인원은 스냅샷의 `members[].zone`을 세어 얻는다. 판정이 아니라 **읽기**다 —
        /// 진척을 올릴지 말지는 서버가 정한다(ARCH-02).
        /// </summary>
        private void Joint(HudTheme theme, Rect area, Snapshot snapshot, Counts counts)
        {
            theme.Fill(new Rect(area.x - 8f, area.y - 24f, 4f, 22f), HudTheme.Accent);
            GUI.Label(new Rect(area.x + 8f, area.y - 26f, 240f, 24f), "분대 합동",
                theme.At(theme.Heading, 19, HudTheme.Ink));
            GUI.Label(new Rect(area.x + 130f, area.y - 26f, 200f, 24f), counts.jointLabel,
                theme.At(theme.Mono, 17, counts.jointColor));
            theme.Fill(new Rect(area.x - 8f, area.y, area.width, 1f), HudTheme.Rule);

            var quest = FindJoint(snapshot);
            if (quest == null)
            {
                GUI.Label(new Rect(area.x + 12f, area.y + 40f, area.width, 28f),
                    "오늘 합동 일과가 없다", theme.At(theme.Body, 18, HudTheme.Ink3));
                return;
            }

            var card = new Rect(area.x - 8f, area.y + 30f, area.width, 246f);
            theme.Fill(card, HudTheme.Paper3);

            GUI.Label(new Rect(card.x + 20f, card.y + 12f, 200f, 30f), quest.label,
                theme.At(theme.Title, 21, HudTheme.Ink));
            theme.Chip(new Rect(card.x + 190f, card.y + 16f, 110f, 24f),
                ZoneNames.ShortOf(quest.zone), HudTheme.AccentW, HudTheme.Accent);
            GUI.Label(new Rect(card.xMax - 144f, card.y + 14f, 120f, 24f),
                HudTheme.PhaseLabel(quest.phase),
                theme.At(theme.Small, 14, HudTheme.Ink2, TextAnchor.MiddleRight));

            // 누가 거기 있는가 — 이름을 하나씩 세운다
            var need = (int)quest.minActors;
            var here = 0;
            // 네 줄 + 진척 막대 + 경고 띠가 카드 안에 다 들어가야 한다.
            // 30픽셀 간격으로 두었더니 막대가 마지막 이름 위에 얹혔다
            var y = card.y + 52f;
            if (snapshot.members != null)
            {
                foreach (var member in snapshot.members)
                {
                    if (member == null) continue;
                    var there = member.zone == quest.zone &&
                                member.presence != SnapshotMembersItemPresenceValues.Evacuated;
                    if (there) here += 1;

                    HudIcons.Dot(new Rect(card.x + 28f, y + 6f, 12f, 12f),
                        there ? HudTheme.Accent : HudTheme.Ink3);
                    GUI.Label(new Rect(card.x + 50f, y, 260f, 24f),
                        $"{member.name} · {HudTheme.RoleName(member.role)}",
                        theme.At(theme.Body, 17, there ? HudTheme.Ink : HudTheme.Ink3));
                    GUI.Label(new Rect(card.xMax - 204f, y, 180f, 24f),
                        there ? "도착" : ZoneNames.ShortOf(member.zone),
                        theme.At(theme.Small, 15, there ? HudTheme.Accent : HudTheme.Ink3,
                            TextAnchor.MiddleRight));
                    y += 26f;
                }
            }

            // 진척 막대 — 서버가 세는 값이다
            theme.Bar(new Rect(card.x + 20f, card.yMax - 62f, card.width - 40f, 10f),
                (float)quest.progress, here >= need ? HudTheme.Accent : HudTheme.Ink3);

            var bar = new Rect(card.x + 20f, card.yMax - 44f, card.width - 40f, 34f);
            if (here >= need)
            {
                theme.Fill(bar, HudTheme.AccentW);
                GUI.Label(new Rect(bar.x + 16f, bar.y, bar.width - 32f, bar.height),
                    $"요구 {need}인 · 현재 {here}인 — 진행 중",
                    theme.At(theme.Body, 16, HudTheme.Accent));
            }
            else
            {
                theme.Fill(bar, HudTheme.AlertW);
                GUI.Label(new Rect(bar.x + 16f, bar.y, bar.width - 32f, bar.height),
                    $"⚠  요구 {need}인 · 현재 {here}인 — 진행 게이지가 차오르지 않음",
                    theme.At(theme.Body, 16, HudTheme.Alert));
            }
        }

        /// <summary>
        /// SQUAD PROGRESS (MOCKUP_04 우하).
        ///
        /// 누가 몇 건을 넘겨받았는지를 나란히 둔다. §6.2가 하달을 "계급이 만드는
        /// 불균형"이라 부른 이상, **그 불균형이 화면에 숫자로 보여야** 한다.
        /// </summary>
        private static void SquadProgress(HudTheme theme, Rect box, Snapshot snapshot)
        {
            theme.Fill(box, HudTheme.Paper);
            theme.Border(box, HudTheme.Rule);
            GUI.Label(new Rect(box.x + 12f, box.y + 6f, 400f, 20f), "SQUAD PROGRESS — 받은 하달",
                theme.At(theme.Label, 13, HudTheme.Ink2));

            if (snapshot?.members == null) return;
            for (var i = 0; i < snapshot.members.Length && i < 4; i += 1)
            {
                var member = snapshot.members[i];
                if (member == null) continue;
                var got = (int)member.choresReceived;
                GUI.Label(new Rect(box.x + 12f + i * 108f, box.y + 34f, 104f, 24f),
                    $"{member.name} {got}",
                    theme.At(theme.Body, 15,
                        got == 0 ? HudTheme.Accent : got >= 2 ? HudTheme.Alert : HudTheme.Heat));
            }
        }

        /// <param name="reserve">
        /// 분면 바닥에 설명 띠가 앉을 자리. 목록이 여기까지 내려오면 글자가 겹친다 —
        /// 실제로 겹쳤다. 목록은 이 높이만큼 위에서 멈춘다
        /// </param>
        private void Quadrant(HudTheme theme, Rect area, string title, string count, Color accent,
                              Snapshot snapshot, System.Func<SnapshotQuestsItem, bool> filter,
                              bool markRequired, float reserve = 0f)
        {
            theme.Fill(new Rect(area.x - 8f, area.y - 24f, 4f, 22f), accent);
            GUI.Label(new Rect(area.x + 8f, area.y - 26f, 240f, 24f), title,
                theme.At(theme.Heading, 19, HudTheme.Ink));
            if (!string.IsNullOrEmpty(count))
            {
                GUI.Label(new Rect(area.x + 130f, area.y - 26f, 200f, 24f), count,
                    theme.At(theme.Mono, 17, accent));
            }
            theme.Fill(new Rect(area.x - 8f, area.y, area.width, 1f), HudTheme.Rule);

            if (snapshot?.quests == null) return;

            var y = area.y + 22f;
            var any = false;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null || !filter(quest)) continue;
                any = true;
                if (y > area.yMax - reserve - 30f) break;

                var done = quest.status == SnapshotQuestsItemStatusValues.Done;

                // **끝난 일과는 둘로 갈린다.** 해낸 것과 못 한 것을 같은 회색으로
                // 흐려두면 하루가 끝났을 때 무엇을 놓쳤는지 셀 수가 없다.
                //
                //   failed  판정에서 떨어진 것
                //   locked  시간대가 지나 잠긴 것 — 만회 경로가 없다(4.0)
                //
                // 둘 다 "이제 못 한다"는 점에서 같으므로 한 묶음으로 본다
                var lost = quest.status == SnapshotQuestsItemStatusValues.Failed ||
                           quest.status == SnapshotQuestsItemStatusValues.Locked;
                var closed = done || lost;
                var row = new Rect(area.x, y, area.width, 40f);

                // §7.2 "필수 미완료 — 좌측에 alert 3px 세로 바"
                if (markRequired && !closed) theme.Fill(new Rect(row.x - 8f, row.y, 4f, 34f), HudTheme.Alert);

                var previous = GUI.color;
                // §7.2 "완료 항목 — 취소선 + 알파 0.45 + accent 체크".
                // 못 한 것은 **흐리지 않는다** — 놓친 것이 눈에 덜 띄면 안 된다
                if (done) GUI.color = new Color(1f, 1f, 1f, 0.45f);

                if (done) HudIcons.Check(new Rect(row.x + 2f, row.y + 8f, 22f, 22f), HudTheme.Accent);
                else if (lost) HudIcons.Cross(new Rect(row.x + 2f, row.y + 8f, 22f, 22f), HudTheme.Alert);
                else HudIcons.EmptyBox(new Rect(row.x + 4f, row.y + 10f, 18f, 18f), HudTheme.Ink2);

                GUI.Label(new Rect(row.x + 36f, row.y + 4f, 300f, 32f), quest.label,
                    theme.At(theme.Body, 19, lost ? HudTheme.Ink2 : HudTheme.Ink));

                // 취소선 — 해낸 것은 초록, 놓친 것은 붉게.
                //
                // 주석에는 "취소선"이 적혀 있었는데 **선을 긋는 코드가 없었다.**
                // 완료가 알파로만 표시되니 목록을 훑어서는 어느 것이 끝난 것인지
                // 한눈에 안 잡혔다.
                if (closed)
                {
                    var width = Mathf.Min(300f, theme.Measure(quest.label, theme.Body) + 6f);
                    theme.Fill(new Rect(row.x + 34f, row.y + 19f, width, 3f),
                        done ? HudTheme.Accent : HudTheme.Alert);
                }

                // §7.2 "협동 필요 — 항목 우측에 ⚠ n인 필요 인라인"
                if (quest.minActors > 1d && !closed)
                {
                    var badge = new Rect(row.x + 340f, row.y + 8f, 118f, 24f);
                    theme.Fill(badge, HudTheme.AlertW);
                    GUI.Label(badge, $"{quest.minActors:0}인 필요",
                        theme.At(theme.Small, 14, HudTheme.Alert, TextAnchor.MiddleCenter));
                }

                // §7.2 "하달받은 항목 — 누가 넘겼는지 이름 필수 표기"
                if (!string.IsNullOrEmpty(quest.delegatedFrom))
                {
                    GUI.Label(new Rect(row.x + 36f, row.y + 24f, 320f, 20f),
                        $"← {NameOf(snapshot, quest.delegatedFrom)}이 넘김",
                        theme.At(theme.Small, 14, HudTheme.Heat));
                }

                // **오른쪽 끝에서 24px 들인다.** 여백 4px로 두었더니 구역 이름이
                // 4분할 가운뎃선에 딱 붙어, 옆 분면 글자와 한 덩이로 읽혔다
                GUI.Label(new Rect(row.xMax - 220f, row.y + 4f, 196f, 32f), ZoneNames.Of(quest.zone),
                    theme.At(theme.Small, 14, HudTheme.Ink2, TextAnchor.MiddleRight));

                if (quest.progress > 0d && !closed)
                {
                    theme.Bar(new Rect(row.x + 36f, row.yMax - 6f, 300f, 6f),
                        (float)quest.progress, HudTheme.Heat);
                }

                GUI.color = previous;
                y += 44f;
            }

            // 빈 분면은 **비었다고 적는다.** 아무것도 없는 칸은 아직 안 그려진
            // 칸과 구분되지 않는다 — 하달 칸은 대개 비어 있어서 특히 그랬다
            if (!any)
            {
                GUI.Label(new Rect(area.x + 4f, area.y + 24f, area.width, 26f),
                    title == "하달받은 것" ? "넘겨받은 일과 없음" : "없음",
                    theme.At(theme.Body, 17, HudTheme.Ink3));
            }

            // §7.2 거부 버튼 — 하달 항목에만. 1일 1회
            if (title == "하달받은 것" && snapshot != null)
            {
                var me = FindMember(snapshot, Client.MemberId);
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null || !filter(quest)) continue;
                    if (quest.status == SnapshotQuestsItemStatusValues.Done) continue;

                    var button = new Rect(area.x + 24f, area.yMax - reserve - 40f, 180f, 30f);
                    var used = me != null && me.vetoUsedToday;
                    theme.Fill(button, HudTheme.Paper);
                    theme.Border(button, used ? HudTheme.Rule : HudTheme.Ink2);
                    GUI.Label(button, used ? "거부  (소진)" : "거부  (1회 남음)",
                        theme.At(theme.Small, 14, used ? HudTheme.Ink3 : HudTheme.Ink2, TextAnchor.MiddleCenter));

                    if (!used && Event.current.type == EventType.MouseDown &&
                        button.Contains(Event.current.mousePosition))
                    {
                        Client.Send(new Intent { type = IntentTypeValues.VetoChore, questId = quest.id });
                        Event.current.Use();
                    }
                    break;
                }
            }
        }

        /* ═══════════════════════════════════════════ §7.3 하달 창 */

        private void DrawDelegation(HudTheme theme, Snapshot snapshot)
        {
            // 목업 실측: 1520×800, 암전 70%
            var panel = Backdrop(theme, 1520f, 800f, 0.72f);
            if (snapshot?.phase == null) return;

            theme.Header(panel, 86f);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 12f, 400f, 20f), "QST-04  HANDOVER",
                theme.At(theme.Label, 12, HudTheme.Accent));
            GUI.Label(new Rect(panel.x + 32f, panel.y + 36f, 400f, 36f), "일 과 하 달",
                theme.At(theme.Title, 27, HudTheme.Ink));
            GUI.Label(new Rect(panel.x + 220f, panel.y + 38f, 400f, 32f),
                $"D-{snapshot.day:00} · {HudTheme.PhaseLabel(snapshot.phase.id)}",
                theme.At(theme.Body, 16, HudTheme.Ink2));

            // §7.3 카운트다운 — 모노스페이스 56px. 5초 이하부터 alert + 1Hz 펄스
            var seconds = Mathf.CeilToInt((float)snapshot.phase.delegationWindowMsLeft / 1000f);
            var hot = seconds <= 5;
            GUI.Label(new Rect(panel.xMax - 360f, panel.y + 24f, 160f, 24f), "TIMER PAUSED",
                theme.At(theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleRight));
            GUI.Label(new Rect(panel.xMax - 360f, panel.y + 48f, 160f, 30f), "잔여",
                theme.At(theme.Body, 16, HudTheme.Ink2, TextAnchor.MiddleRight));
            GUI.Label(new Rect(panel.xMax - 200f, panel.y + 20f, 168f, 60f), $"{seconds}",
                theme.At(theme.Display, 56,
                    hot && !HudTheme.Pulse(1f) ? HudTheme.Ink2 : hot ? HudTheme.Alert : HudTheme.Ink,
                    TextAnchor.MiddleRight));

            // ── 좌측: 내 공통 일과 ──
            var chores = MyChores(snapshot);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 118f, 400f, 20f), "MY COMMON DUTIES",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            theme.Fill(new Rect(panel.x + 32f, panel.y + 142f, 340f, 1f), HudTheme.Rule);

            for (var i = 0; i < chores.Count && i < 3; i += 1)
            {
                var quest = chores[i];
                // 목업 실측: 일과 카드 340×120
                var card = new Rect(panel.x + 32f, panel.y + 162f + i * 142f, 340f, 120f);
                var picked = i == _pickedChore;

                theme.Fill(card, HudTheme.Paper3);
                theme.Border(card, picked ? HudTheme.Accent : HudTheme.Rule, picked ? 2f : 1f);
                theme.Spine(card, quest.required ? HudTheme.Alert : HudTheme.Ink2, 5f);

                GUI.Label(new Rect(card.x + 24f, card.y + 22f, 300f, 20f),
                    quest.required ? "필수" : "선택",
                    theme.At(theme.Label, 12, quest.required ? HudTheme.Alert : HudTheme.Ink2));
                GUI.Label(new Rect(card.x + 24f, card.y + 46f, 300f, 32f), quest.label,
                    theme.At(theme.Heading, 23, HudTheme.Ink));
                // §7.3 "동선 표기" — 시간 비용의 대부분이 이동이라(§6.1) 어디로 가야 하는지가 곧 비용이다
                GUI.Label(new Rect(card.x + 24f, card.y + 82f, 300f, 24f), ZoneNames.FullOf(quest.zone),
                    theme.At(theme.Body, 15, HudTheme.Ink2));

                GUI.Label(new Rect(card.x - 24f, card.y + 46f, 20f, 24f), $"{i + 1}",
                    theme.At(theme.Label, 12, HudTheme.Ink3));
            }

            // ── 우측: 분대원 카드 ──
            var targets = LowerRanked(snapshot);
            GUI.Label(new Rect(panel.x + 612f, panel.y + 118f, 400f, 20f), "SQUAD — 계급 하위",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            theme.Fill(new Rect(panel.x + 612f, panel.y + 142f, 876f, 1f), HudTheme.Rule);

            for (var i = 0; i < targets.Count && i < 3; i += 1)
            {
                var member = targets[i];
                // 목업 실측: 분대원 카드 300×220 (280 + 간격 24)
                var card = new Rect(panel.x + 612f + i * 304f, panel.y + 170f, 280f, 240f);
                var capacity = 2 - (int)member.choresReceived;
                var full = capacity <= 0;
                var picked = i == _pickedTarget && !full;

                var previous = GUI.color;
                // §7.3 "여력 0 — 카드 전체 알파 0.4, 드롭 불가"
                if (full) GUI.color = new Color(1f, 1f, 1f, 0.45f);

                theme.Fill(card, picked ? HudTheme.AccentW : HudTheme.Paper3);
                theme.Border(card, picked ? HudTheme.Accent : HudTheme.Rule, picked ? 3f : 1f);

                DrawRankPips(theme, new Rect(card.x + 24f, card.y + 24f, 14f, 12f), member.rank);
                GUI.Label(new Rect(card.x + 52f, card.y + 18f, 160f, 30f),
                    $"{member.name}{HudTheme.RankName(member.rank)}",
                    theme.At(theme.Heading, 22, HudTheme.Ink));
                theme.Chip(new Rect(card.xMax - 62f, card.y + 20f, 42f, 22f),
                    HudTheme.RoleTag(member.role), HudTheme.RoleColor(member.role), HudTheme.Paper);

                GUI.Label(new Rect(card.x + 24f, card.y + 60f, 160f, 24f), "수락 여력",
                    theme.At(theme.Small, 14, HudTheme.Ink2));
                GUI.Label(new Rect(card.xMax - 100f, card.y + 60f, 80f, 24f), $"{capacity} / 2",
                    theme.At(theme.Mono, 22, full ? HudTheme.Alert : HudTheme.Accent, TextAnchor.MiddleRight));

                theme.Bar(new Rect(card.x + 24f, card.y + 88f, 232f, 8f), capacity / 2f,
                    full ? HudTheme.Alert : HudTheme.Accent, HudTheme.Paper);

                // 이미 배정된 것
                var slot = 0;
                if (snapshot.quests != null)
                {
                    foreach (var quest in snapshot.quests)
                    {
                        if (quest == null || quest.ownerId != member.id) continue;
                        if (string.IsNullOrEmpty(quest.delegatedFrom)) continue;
                        if (slot >= 2) break;

                        var row = new Rect(card.x + 24f, card.y + 114f + slot * 52f, 232f, 46f);
                        theme.Fill(row, HudTheme.Paper);
                        theme.Spine(row, HudTheme.Alert);
                        GUI.Label(new Rect(row.x + 18f, row.y, 200f, row.height), quest.label,
                            theme.At(theme.Body, 16, HudTheme.Ink));
                        slot += 1;
                    }
                }

                GUI.color = previous;

                if (!full && Event.current.type == EventType.MouseDown &&
                    card.Contains(Event.current.mousePosition))
                {
                    Delegate(chores, member);
                    Event.current.Use();
                }
            }

            // §7.3 접근성 — "드래그 외에 키보드 경로 필수"
            HandleDelegationKeys(chores, targets);

            GUI.Label(new Rect(panel.x + 32f, panel.yMax - 40f, 900f, 24f),
                "[1-3] 일과 선택   [←→] 대상   [ENTER] 배정   미배정분은 20초 후 본인 소유로 남는다",
                theme.At(theme.Label, 13, HudTheme.Ink3));

            // 확인 — 내 하달을 끝냈다는 뜻이다.
            //
            // **타이머를 멈추지는 않는다.** 4인이 같은 창을 보고 있고 남은 사람은
            // 아직 배정 중일 수 있어서, 한 명이 눌렀다고 창을 닫으면 그 사람들의
            // 20초를 빼앗는다. 내 화면만 내리고 게임으로 돌아간다 —
            // 미배정분은 어차피 본인 소유로 남는다(§7.3).
            var confirm = new Rect(panel.xMax - 172f, panel.yMax - 106f, 140f, 52f);
            theme.Fill(confirm, HudTheme.Accent);
            GUI.Label(confirm, "확 정",
                theme.At(theme.Heading, 19, HudTheme.Paper, TextAnchor.MiddleCenter));

            if (Event.current.type == EventType.MouseDown && confirm.Contains(Event.current.mousePosition))
            {
                _delegationDone = snapshot.phase.id;
                _screen = Screen.None;
                Event.current.Use();
            }

            // §7.3 하단 요약 바 — 비용 구조 3겹의 가시화(§6.2)
            var bar = new Rect(panel.x + 612f, panel.yMax - 116f, 880f, 60f);
            GUI.Label(new Rect(bar.x, bar.y, 200f, 20f), "예상 군기 변동",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            GUI.Label(new Rect(bar.x, bar.y + 22f, 120f, 34f),
                snapshot.discipline != null ? $"{snapshot.discipline.value:0}" : "—",
                theme.At(theme.Display, 30, HudTheme.Alert));
            GUI.Label(new Rect(bar.x + 220f, bar.y, 200f, 20f), "내 복무 점수",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            GUI.Label(new Rect(bar.x + 220f, bar.y + 22f, 160f, 34f),
                $"{FindMember(snapshot, Client.MemberId)?.serviceScore ?? 0d:0}",
                theme.At(theme.Display, 30, HudTheme.Ink2));
            GUI.Label(new Rect(bar.x + 440f, bar.y, 400f, 20f), "미완료 시",
                theme.At(theme.Label, 12, HudTheme.Alert));
            GUI.Label(new Rect(bar.x + 440f, bar.y + 22f, 420f, 30f), "조건 A 붕괴 — 전원 게임오버",
                theme.At(theme.Heading, 17, HudTheme.Alert));
        }

        private void HandleDelegationKeys(List<SnapshotQuestsItem> chores, List<SnapshotMembersItem> targets)
        {
            for (var i = 0; i < 3; i += 1)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && i < chores.Count) _pickedChore = i;
            }
            if (Input.GetKeyDown(KeyCode.LeftArrow)) _pickedTarget = Mathf.Max(0, _pickedTarget - 1);
            if (Input.GetKeyDown(KeyCode.RightArrow))
                _pickedTarget = Mathf.Min(targets.Count - 1, _pickedTarget + 1);
            if (Input.GetKeyDown(KeyCode.Return) && _pickedTarget < targets.Count)
                Delegate(chores, targets[_pickedTarget]);
        }

        private void Delegate(List<SnapshotQuestsItem> chores, SnapshotMembersItem target)
        {
            if (_pickedChore >= chores.Count || target == null) return;
            Client.Send(new Intent
            {
                type = IntentTypeValues.DelegateChore,
                toId = target.id,
                questId = chores[_pickedChore].id,
            });
        }

        /// <summary>§7.3 "하달 대상은 공통 일과만" — 보직 전용은 넘겨도 수행이 불가능하다</summary>
        private List<SnapshotQuestsItem> MyChores(Snapshot snapshot)
        {
            var list = new List<SnapshotQuestsItem>();
            if (snapshot?.quests == null) return list;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                if (quest.kind != SnapshotQuestsItemKindValues.Chore) continue;
                if (quest.ownerId != Client.MemberId) continue;
                if (quest.status == SnapshotQuestsItemStatusValues.Done) continue;
                // §6.2 "재하달 금지 — 받은 일과는 다시 넘길 수 없다"
                if (!string.IsNullOrEmpty(quest.delegatedFrom)) continue;
                list.Add(quest);
            }
            return list;
        }

        /// <summary>§6.2 "계급이 1단계 이상 높은 사람만 하달할 수 있다"</summary>
        private List<SnapshotMembersItem> LowerRanked(Snapshot snapshot)
        {
            var list = new List<SnapshotMembersItem>();
            var me = FindMember(snapshot, Client.MemberId);
            if (me == null || snapshot.members == null) return list;

            var mine = RankIndex(me.rank);
            foreach (var member in snapshot.members)
            {
                if (member == null || member.id == me.id) continue;
                if (RankIndex(member.rank) < mine) list.Add(member);
            }
            return list;
        }

        private static int RankIndex(string rank) => rank switch
        {
            "sergeant" => 3, "corporal" => 2, "pfc" => 1, _ => 0,
        };

        /// <summary>§5.4 계급장 — 가로선 n개 + 병장은 상단 점. 실물 도안을 쓰지 않는다(§3.3)</summary>
        private static void DrawRankPips(HudTheme theme, Rect rect, string rank)
        {
            var (lines, dot) = rank switch
            {
                "sergeant" => (3, true),
                "corporal" => (3, false),
                "pfc" => (2, false),
                _ => (1, false),
            };
            var color = rank switch
            {
                "sergeant" => HudTheme.Hex("FFD98A"),
                "corporal" => HudTheme.Hex("E0D8B0"),
                _ => HudTheme.Hex("C9C7BC"),
            };

            if (dot) theme.Fill(new Rect(rect.x + rect.width * 0.4f, rect.y - 5f, 4f, 3f), color);
            for (var i = 0; i < lines; i += 1)
            {
                theme.Fill(new Rect(rect.x, rect.y + i * 5f, rect.width, 3f), color);
            }
        }

        /* ═══════════════════════════════════════════ §7.4 일과표 */

        /// <summary>
        /// 일과표 (MOCKUP_05).
        ///
        /// **여기는 개요다.** 한때 시간대 칸마다 분류 배지·이름·장소를 세 줄로 쌓아
        /// 놨는데, 목업이 칸에 넣어둔 것은 이름 한 줄뿐이다. 하루를 한눈에 훑는
        /// 자리에서 상세를 다 펼치면 훑을 수가 없다 — **상세는 수첩(Tab)이 맡는다.**
        /// 체크박스·요구 인원·하달한 사람·진척 막대는 전부 그쪽에 있다.
        ///
        /// 그래서 칸에는 이름만 적고, 무엇인지는 **색으로만** 가른다.
        ///   합동 accent · 필수 ink · 선택 ink2 · 끝난 것은 취소선
        /// </summary>
        private void DrawSchedule(HudTheme theme, Snapshot snapshot)
        {
            // 목업 실측: 1600×880 @ (160,100)
            var panel = Backdrop(theme, 1600f, 880f, 0.8f);
            if (snapshot == null) return;

            theme.Header(panel, 120f);
            GUI.Label(new Rect(panel.x + 36f, panel.y + 16f, 500f, 20f), "DAILY SCHEDULE — 06:00 기상",
                theme.At(theme.Label, 12, HudTheme.Accent));
            GUI.Label(new Rect(panel.x + 36f, panel.y + 48f, 500f, 52f), $"D-{snapshot.day:00}  일과표",
                theme.At(theme.Display, 40, HudTheme.Ink));

            // 목업의 "숙영 2일차" 자리 — 오늘 훈련이 있으면 그 이름이 온다
            var training = TrainingToday(snapshot);
            if (training != null)
            {
                GUI.Label(new Rect(panel.x + 320f, panel.y + 56f, 400f, 30f), training,
                    theme.At(theme.Body, 20, HudTheme.Ink2));
            }

            var band = snapshot.weather?.band ?? "normal";
            var bandColor = HudTheme.BandColor(band);
            var icon = new Rect(panel.x + 960f, panel.y + 32f, 60f, 60f);
            theme.Fill(icon, HudTheme.AccentW);
            HudIcons.Band(band, icon, bandColor);
            GUI.Label(new Rect(icon.xMax + 16f, panel.y + 36f, 200f, 32f), HudTheme.BandLabel(band),
                theme.At(theme.Title, 24, bandColor));
            GUI.Label(new Rect(icon.xMax + 16f, panel.y + 68f, 200f, 30f),
                $"{snapshot.weather?.feelsLike ?? 0d:0} °C",
                theme.At(theme.Mono, 20, bandColor));

            // 극단 밴드 경고 띠 (목업 우상단). 평시·온난에는 뜨지 않는다 —
            // 늘 떠 있는 경고는 경고가 아니다
            if (band == "extremeCold" || band == "extremeHot" || band == "hot" || band == "cold")
            {
                var warn = new Rect(panel.x + 1240f, panel.y + 40f, 330f, 44f);
                theme.Fill(warn, HudTheme.AlertW);
                GUI.Label(warn, $"⚠  {HudTheme.BandLabel(band)} — 체감 {snapshot.weather?.feelsLike ?? 0d:0}°C",
                    theme.At(theme.Body, 18, HudTheme.Alert, TextAnchor.MiddleCenter));
            }

            GUI.Label(new Rect(panel.x + 36f, panel.y + 156f, 300f, 20f), "TIMESLOT ×6",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            // 6칸 타임라인 — 목업 실측 240×300, 간격 16
            var phases = new[]
            {
                ("06:00", "reveille"), ("08:00", "morning"), ("12:00", "lunch"),
                ("14:00", "afternoon"), ("18:00", "personal"), ("22:00", "rollcall"),
            };

            for (var i = 0; i < phases.Length; i += 1)
            {
                var (clock, id) = phases[i];
                var card = new Rect(panel.x + 36f + i * 256f, panel.y + 186f, 240f, 300f);
                var now = snapshot.phase?.id == id;
                var judge = id == "rollcall";

                theme.Fill(card, judge ? HudTheme.AlertW : now ? HudTheme.AccentW : HudTheme.Paper3);
                theme.Border(card, judge ? HudTheme.Alert : now ? HudTheme.Accent : HudTheme.Rule3,
                    now || judge ? 2f : 1f);

                // 목업의 ★ 리본 자리. 강조할 칸은 언제나 **지금**이다
                var top = card.y;
                if (now)
                {
                    theme.Fill(new Rect(card.x, card.y, card.width, 26f), HudTheme.Accent);
                    // 별표를 달았다가 뺐다 — Pretendard에 `★`가 없어 앞에 빈칸만 남았다
                    GUI.Label(new Rect(card.x, card.y + 3f, card.width, 20f), "지금",
                        theme.At(theme.Label, 13, HudTheme.Paper, TextAnchor.MiddleCenter));
                    top += 22f;
                }

                GUI.Label(new Rect(card.x + 20f, top + 20f, 200f, 28f), clock,
                    theme.At(theme.Mono, 20, judge ? HudTheme.Alert : HudTheme.Accent));
                GUI.Label(new Rect(card.x + 20f, top + 52f, 200f, 28f), HudTheme.PhaseLabel(id),
                    theme.At(theme.Heading, 19, HudTheme.Ink));
                theme.Fill(new Rect(card.x + 20f, top + 86f, 200f, 1f), judge ? HudTheme.Alert : HudTheme.Rule);

                // ── 이름만. 상세는 수첩이다 ──
                var y = top + 102f;
                var required = 0;
                var optional = 0;
                var together = 0;
                var recover = 0;
                if (snapshot.quests != null)
                {
                    foreach (var quest in snapshot.quests)
                    {
                        if (quest == null || quest.phase != id) continue;

                        // 합동은 **주인이 없다**(`ownerId == null`). 내 것만 세던 탓에
                        // 일과표에서만 통째로 빠져 있었다 — 4인이 같이 하는 일이
                        // 하루 계획에서 사라지면 계획을 세울 수가 없다
                        var joint = quest.kind == SnapshotQuestsItemKindValues.Joint;
                        if (!joint && quest.ownerId != Client.MemberId) continue;

                        var care = quest.kind == SnapshotQuestsItemKindValues.Care;
                        if (care) recover += 1;
                        else if (joint) together += 1;   // 합동은 선택이 아니다
                        else if (quest.required) required += 1;
                        else optional += 1;
                        if (y > card.yMax - 58f) continue;

                        var done = quest.status == SnapshotQuestsItemStatusValues.Done;
                        var lost = quest.status == SnapshotQuestsItemStatusValues.Failed ||
                                   quest.status == SnapshotQuestsItemStatusValues.Locked;

                        // **수첩의 4분할과 같은 색을 쓴다.** 수첩은 필수를 빨강,
                        // 합동을 초록, 선택을 회색, 하달을 주황으로 나누는데 일과표만
                        // 필수를 흰 글자로 적어서, 두 화면을 오갈 때마다 색을 다시
                        // 맞춰봐야 했다. 중요한 것일수록 **굵게** 둔다
                        var handed = !string.IsNullOrEmpty(quest.delegatedFrom);
                        var ink = done || lost ? HudTheme.Ink3
                                : care ? HudTheme.Cold
                                : handed ? HudTheme.Heat
                                : joint ? HudTheme.Accent
                                : quest.required ? HudTheme.Alert
                                : HudTheme.Optional;

                        var loud = !done && !lost && (quest.required || joint || handed);
                        var text = new Rect(card.x + 20f, y, 200f, 24f);
                        GUI.Label(text, quest.label,
                            theme.At(loud ? theme.Heading : theme.Body, 16, ink));

                        if (done || lost)
                        {
                            var w = Mathf.Min(200f, theme.Measure(quest.label, theme.Body) + 4f);
                            theme.Fill(new Rect(text.x - 2f, text.y + 11f, w, 2f),
                                done ? HudTheme.Accent : HudTheme.Alert);
                        }

                        y += 28f;
                    }
                }

                // 칸 바닥 한 줄 — 목업이 여기 둔 것은 그 시간대의 **무게**다
                var foot = new Rect(card.x + 20f, card.yMax - 30f, 200f, 20f);
                if (judge)
                {
                    GUI.Label(foot, "되돌릴 수 없음", theme.At(theme.Small, 13, HudTheme.Alert));
                }
                else if (recover > 0 && required + optional + together == 0)
                {
                    // 중식·개인정비는 **일과가 없는 칸**이다. 몸을 되돌리는 칸이라고
                    // 적어야 "여기 아무것도 안 떴네"가 되지 않는다(7.0)
                    GUI.Label(foot, $"회복 {recover} — 일과 없음",
                        theme.At(theme.Small, 13, HudTheme.Cold));
                }
                else if (required + optional + together == 0)
                {
                    GUI.Label(foot, "배정 없음", theme.At(theme.Small, 13, HudTheme.Ink3));
                }
                else
                {
                    // **셈도 종류 색으로 적는다.** 위의 일과 이름은 색으로 갈라놨는데
                    // 바닥의 셈만 한 색이면, 빨간 줄이 몇이고 초록 줄이 몇인지를
                    // 다시 세어봐야 한다
                    var at = foot.x;
                    Tally(ref at, required, "필수", HudTheme.Alert);
                    Tally(ref at, together, "합동", HudTheme.Accent);
                    Tally(ref at, optional, "선택", HudTheme.Optional);

                    void Tally(ref float x, int count, string name, Color color)
                    {
                        if (count == 0) return;
                        if (x > foot.x)
                        {
                            GUI.Label(new Rect(x, foot.y, 12f, 20f), "·",
                                theme.At(theme.Small, 13, HudTheme.Ink3));
                            x += 12f;
                        }
                        // 스타일은 공유물이라 **크기를 먼저 맞춘 뒤에 재야** 한다 —
                        // 앞선 호출이 남긴 크기로 재면 폭이 어긋나 글자가 겹친다
                        var style = theme.At(theme.Small, 13, color);
                        var text = $"{name} {count}";
                        var w = theme.Measure(text, style) + 4f;
                        GUI.Label(new Rect(x, foot.y, w, 20f), text, style);
                        x += w;
                    }
                }
            }

            /* ── 하단 요약 세 칸 (목업 실측) ── */
            theme.Fill(new Rect(panel.x + 36f, panel.y + 520f, 1528f, 2f), HudTheme.Rule);

            var counts = CountQuests(snapshot, Client.MemberId);

            // 1. 오늘의 필수
            var box1 = new Rect(panel.x + 36f, panel.y + 560f, 360f, 90f);
            theme.Fill(box1, HudTheme.Paper3);
            GUI.Label(new Rect(box1.x + 20f, box1.y + 12f, 300f, 20f), "오늘의 필수",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            GUI.Label(new Rect(box1.x + 20f, box1.y + 34f, 60f, 48f), $"{counts.requiredTotal}",
                theme.At(theme.Display, 40, HudTheme.Alert));
            GUI.Label(new Rect(box1.x + 70f, box1.y + 46f, 280f, 24f),
                $"= 완료 {counts.requiredDone} · 남은 {counts.requiredTotal - counts.requiredDone}",
                theme.At(theme.Body, 16, HudTheme.Ink2));

            // 2. 합동 퀘스트 — 목업이 일과표에 따로 칸을 준 유일한 일과다.
            //    혼자 못 하는 일이라 **하루를 짜기 전에 알아야** 한다
            var box2 = new Rect(panel.x + 420f, panel.y + 560f, 440f, 90f);
            theme.Fill(box2, HudTheme.Paper3);
            GUI.Label(new Rect(box2.x + 20f, box2.y + 12f, 300f, 20f), "합동 퀘스트",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            var jointQuest = FindJoint(snapshot);
            if (jointQuest == null)
            {
                GUI.Label(new Rect(box2.x + 20f, box2.y + 44f, 400f, 28f), "오늘 없음",
                    theme.At(theme.Body, 18, HudTheme.Ink3));
            }
            else
            {
                GUI.Label(new Rect(box2.x + 20f, box2.y + 42f, 200f, 30f), jointQuest.label,
                    theme.At(theme.Title, 22, HudTheme.Ink));
                theme.Chip(new Rect(box2.x + 180f, box2.y + 46f, 150f, 26f),
                    $"{jointQuest.minActors:0}인 · {counts.jointLabel}",
                    HudTheme.AccentW, counts.jointColor);
                GUI.Label(new Rect(box2.x + 340f, box2.y + 46f, 84f, 26f),
                    ZoneNames.ShortOf(jointQuest.zone),
                    theme.At(theme.Small, 14, HudTheme.Ink2, TextAnchor.MiddleRight));
            }

            // 3. 보직 배정
            var box3 = new Rect(panel.x + 884f, panel.y + 560f, 680f, 90f);
            theme.Fill(box3, HudTheme.Paper3);
            GUI.Label(new Rect(box3.x + 20f, box3.y + 12f, 300f, 20f), "보직 배정",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            if (snapshot.members != null)
            {
                for (var i = 0; i < snapshot.members.Length && i < 4; i += 1)
                {
                    var member = snapshot.members[i];
                    if (member == null) continue;

                    // 이름은 **읽으라고** 있는 것이라 밝게 둔다. 보직 색은 왼쪽 점이
                    // 맡는다 — 소총병 색(#6E7A50)으로 글자를 쓰면 이 배경에서 안 읽힌다
                    var row = new Rect(box3.x + 20f + i * 168f, box3.y + 46f, 164f, 24f);
                    theme.Fill(new Rect(row.x, row.y + 7f, 10f, 10f), HudTheme.RoleColor(member.role));
                    GUI.Label(new Rect(row.x + 18f, row.y, row.width - 18f, 24f),
                        $"{member.name} · {HudTheme.RoleName(member.role)}",
                        theme.At(theme.Body, 15, HudTheme.Ink2));
                }
            }

            /* ── 필수 장비 ── */
            var me = FindMember(snapshot, Client.MemberId);
            var short2 = me != null && me.missingGear.Length > 0;
            var gear = new Rect(panel.x + 36f, panel.y + 680f, 1528f, 54f);
            theme.Fill(gear, HudTheme.Paper);
            theme.Border(gear, HudTheme.Alert);
            theme.Spine(gear, HudTheme.Alert, 6f);
            GUI.Label(new Rect(gear.x + 24f, gear.y, 120f, gear.height), "필수 장비",
                theme.At(theme.Label, 13, HudTheme.Alert));

            if (me != null)
            {
                // **한글 이름으로 적는다.** 여기 `combatUniform`이 그대로 떴었다 —
                // 이름표는 sim의 `supply.json`에만 있고 C#으로 생성된다(`ItemNames`)
                var x = gear.x + 140f;
                foreach (var item in me.inventory)
                {
                    theme.Chip(new Rect(x, gear.y + 14f, 124f, 28f), ItemNames.Of(item) + " ✓",
                        HudTheme.AccentW, HudTheme.Accent);
                    x += 134f;
                    if (x > gear.xMax - 420f) break;
                }
                foreach (var item in me.missingGear)
                {
                    theme.Chip(new Rect(x, gear.y + 14f, 134f, 28f), ItemNames.Of(item) + " ✗",
                        HudTheme.AlertW, HudTheme.Alert);
                    x += 144f;
                    if (x > gear.xMax - 280f) break;
                }
                if (short2)
                {
                    GUI.Label(new Rect(gear.xMax - 270f, gear.y, 250f, gear.height),
                        "— 미소지 시 조건 D 불합격", theme.At(theme.Body, 15, HudTheme.Alert));
                }
            }

            /* ── 오늘의 조건 ──
             *
             * 목업은 여기에 "금지 일과"를 뒀는데, **서버가 금지 목록을 보내지
             * 않는다.** 없는 값을 지어내 적으면 화면이 거짓말을 한다(ARCH-02).
             * 대신 스냅샷이 실제로 들고 있는 오늘의 조건을 같은 자리에 적는다.
             */
            var note = new Rect(panel.x + 36f, panel.y + 746f, 1528f, 54f);
            theme.Fill(note, HudTheme.Paper);
            theme.Border(note, HudTheme.Rule);
            theme.Spine(note, HudTheme.Cold, 6f);
            GUI.Label(new Rect(note.x + 24f, note.y, 120f, note.height), "오늘의 조건",
                theme.At(theme.Label, 13, HudTheme.Cold));
            GUI.Label(new Rect(note.x + 140f, note.y, 1300f, note.height), TodayNote(snapshot, me),
                theme.At(theme.Body, 17, HudTheme.Ink2));

            /* ── 하단 ── */
            GUI.Label(new Rect(panel.x + 36f, panel.y + 818f, 1040f, 44f),
                "상세는 수첩 [TAB] — 체크박스 · 요구 인원 · 하달 내역이 그쪽에 있다",
                theme.At(theme.Small, 14, HudTheme.Ink3));

            var confirm = new Rect(panel.x + 1264f, panel.y + 818f, 264f, 44f);
            theme.Fill(confirm, HudTheme.Accent);
            GUI.Label(confirm, "확인  ·  SPACE",
                theme.At(theme.Heading, 18, HudTheme.Paper, TextAnchor.MiddleCenter));
        }

        /// <summary>오늘의 합동 일과. 하루에 하나이므로 첫 건이면 된다</summary>
        public static SnapshotQuestsItem FindJoint(Snapshot snapshot)
        {
            if (snapshot?.quests == null) return null;
            foreach (var quest in snapshot.quests)
            {
                if (quest != null && quest.kind == SnapshotQuestsItemKindValues.Joint) return quest;
            }
            return null;
        }

        /// <summary>오늘 훈련이 잡혔으면 그 이름. 일과에 `training`이 실려 온다</summary>
        private static string TrainingToday(Snapshot snapshot)
        {
            if (snapshot?.quests == null) return null;
            foreach (var quest in snapshot.quests)
            {
                if (quest != null && !string.IsNullOrEmpty(quest.training)) return quest.label;
            }
            return null;
        }

        /// <summary>스냅샷이 실제로 들고 있는 오늘의 조건만 적는다 — 없는 값은 짓지 않는다</summary>
        private static string TodayNote(Snapshot snapshot, SnapshotMembersItem me)
        {
            var parts = new System.Collections.Generic.List<string>();

            var band = snapshot.weather?.band ?? "normal";
            if (band != "normal") parts.Add($"{HudTheme.BandLabel(band)} — 필수 장비가 밴드에 따라 바뀐다");
            if (snapshot.supply != null && snapshot.supply.isSupplyDay) parts.Add("보급일 — 오늘 청구할 수 있다");
            if (me != null && me.onGuardTonight) parts.Add("오늘 야간 경계 — 점호 뒤 초소");
            if (snapshot.reliefsRemaining <= 0d) parts.Add("구제 소진 — 조건 하나만 깨져도 게임오버");
            if (parts.Count == 0) parts.Add("특이사항 없음");

            return string.Join("  ·  ", parts);
        }

        /* ═══════════════════════════════════════════ §7.5 점호 판정 */

        /// <summary>§7.5 연출 순서 — 조건 A/B/C/D가 0.6초 간격으로 열린다</summary>
        private static readonly float[] Reveal = { 1.6f, 2.2f, 2.8f, 3.4f };

        private void DrawRollCall(HudTheme theme, Snapshot snapshot)
        {
            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight), HudTheme.Dim, 0.92f);
            if (_judgement == null || snapshot == null) return;

            var t = Time.unscaledTime - _rollCallStart;
            var failed = !_judgement.passed;
            var failedAt = _judgement.failedAt;

            // 실패 시 화면 전체가 alert 톤으로 전환된다(§7.5)
            if (failed && t > 4f)
                theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight),
                    HudTheme.Alert, 0.13f);

            // 목업 실측: 1200×556 @ (360, 452)
            var panel = new Rect(360f, 452f, 1200f, 556f);
            theme.Fill(panel, HudTheme.Paper);
            theme.Border(panel, failed ? HudTheme.Alert : HudTheme.Accent, 3f);

            theme.Header(panel, 82f);
            GUI.Label(new Rect(panel.x + 32f, panel.y + 24f, 500f, 20f), "JDG-01  ROLL CALL — 22:00",
                theme.At(theme.Label, 12, failed ? HudTheme.Alert : HudTheme.Accent));
            GUI.Label(new Rect(panel.x + 32f, panel.y + 46f, 400f, 32f), "야 간 점 호",
                theme.At(theme.Title, 24, HudTheme.Ink));
            GUI.Label(new Rect(panel.xMax - 200f, panel.y + 20f, 168f, 34f), $"D-{_judgement.day:00}",
                theme.At(theme.Display, 26, HudTheme.Ink, TextAnchor.MiddleRight));

            var conditions = new[]
            {
                ("A", "필수 퀘스트 100% 완료",
                    $"{snapshot.lastJudgement?.requiredDone ?? 0d:0} / " +
                    $"{snapshot.lastJudgement?.requiredTotal ?? 0d:0}"),
                ("B", "합동 퀘스트 완료", CountQuests(snapshot, Client.MemberId).jointLabel),
                ("C", "분대 군기 ≥ 40", $"{snapshot.discipline?.value ?? 0d:0} / 100"),
                ("D", "복장 · 장비 점검", "—"),
            };

            var y = panel.y + 114f;
            var stopped = false;

            for (var i = 0; i < conditions.Length; i += 1)
            {
                var (key, label, value) = conditions[i];
                var row = new Rect(panel.x + 32f, y, 1136f, i == 2 && failedAt == "C" ? 76f : 70f);

                var shown = t >= Reveal[i];
                var isFail = failed && failedAt == key;
                // §7.5 "하나라도 실패 → 그 줄에서 정지, 나머지 줄은 회색 처리"
                var skipped = stopped;

                var previous = GUI.color;
                if (!shown || skipped) GUI.color = new Color(1f, 1f, 1f, 0.3f);

                theme.Fill(row, isFail ? HudTheme.AlertW : HudTheme.Paper3);
                if (isFail) theme.Spine(row, HudTheme.Alert, 6f);

                GUI.Label(new Rect(row.x + 24f, row.y, 40f, row.height), key,
                    theme.At(theme.Display, 22, isFail ? HudTheme.Alert : HudTheme.Accent));
                GUI.Label(new Rect(row.x + 72f, row.y, 600f, row.height), label,
                    theme.At(theme.Heading, 21, skipped ? HudTheme.Ink3 : HudTheme.Ink));

                if (shown && !skipped)
                {
                    GUI.Label(new Rect(row.x + 620f, row.y, 240f, row.height), value,
                        theme.At(theme.Mono, 19, isFail ? HudTheme.Alert : HudTheme.Ink2,
                            TextAnchor.MiddleRight));

                    var mark = new Rect(row.x + 1078f, row.y + row.height * 0.5f - 13f, 26f, 26f);
                    if (isFail) HudIcons.Cross(mark, HudTheme.Alert);
                    else HudIcons.Check(mark, HudTheme.Accent);
                }
                else if (skipped)
                {
                    // §7.5 "실패한 조건 아래는 아예 판정하지 않고 —로 남긴다"
                    GUI.Label(new Rect(row.x + 1060f, row.y, 60f, row.height), "—",
                        theme.At(theme.Mono, 24, HudTheme.Ink3, TextAnchor.MiddleCenter));
                }

                GUI.color = previous;

                if (isFail && shown) stopped = true;
                y += row.height + 10f;
            }

            if (t < 4f) return;

            // 결과 — §7.5 "실패 원인을 단 한 줄로 지목한다"
            var result = new Rect(panel.x, panel.yMax - 108f, panel.width, 108f);
            theme.Fill(result, failed ? HudTheme.Hex("3A100C") : HudTheme.AccentW);
            theme.Fill(new Rect(result.x, result.y, result.width, 2f),
                failed ? HudTheme.Alert : HudTheme.Accent);

            GUI.Label(new Rect(result.x, result.y + 20f, result.width, 34f),
                failed ? $"조건 {failedAt} 미달 — 퇴소" : "전 조건 통과 — 취침",
                theme.At(theme.Title, 28, failed ? HudTheme.Alert : HudTheme.Accent, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(result.x, result.y + 56f, result.width, 26f),
                failed ? $"D-{_judgement.day:00} 까지 생존" : "다음 날로 넘어간다",
                theme.At(theme.Body, 17, HudTheme.Ink, TextAnchor.MiddleCenter));

            // 6.4초 = 기획서(SAD-GDD-002 §16 사운드 박스) "점호 판정 트랙은 6.4초
            // 연출에 큐 포인트 4개" — 그 시각에 다음 화면(승급이 있으면 승급, 없으면
            // 취침 정산)으로 넘긴다. 실패 시엔 절대 안 넘긴다 — RollCall 화면이 그대로
            // 남아 §7.5 종료 흐름(HudEnding)으로 이어져야 한다.
            if (!failed && t > 6.4f) AdvanceDayEnd();
        }

        /* ═══════════════════════════════════════════ §7.6 취침 정산 */

        private void DrawSleep(HudTheme theme, Snapshot snapshot)
        {
            var panel = Backdrop(theme, 720f, 620f);
            if (snapshot == null) return;

            Head(theme, panel, 88f, "COND-02  SETTLE", $"D-{snapshot.day:00} 일차 요약", "");

            var me = FindMember(snapshot, Client.MemberId);
            if (me?.stats == null) return;

            var y = panel.y + 120f;
            foreach (var (id, name) in Hud.Stats)
            {
                var value = Hud.Value(me.stats, id);
                var before = _beforeSleep != null
                    ? Hud.Value(FindMember(_beforeSleep, Client.MemberId)?.stats ?? me.stats, id)
                    : value;
                var delta = value - before;

                GUI.Label(new Rect(panel.x + 40f, y, 140f, 30f), name,
                    theme.At(theme.Body, 17, HudTheme.Ink));
                GUI.Label(new Rect(panel.x + 200f, y, 200f, 30f), $"{before:0} → {value:0}",
                    theme.At(theme.Mono, 17, HudTheme.Ink2));
                GUI.Label(new Rect(panel.xMax - 140f, y, 100f, 30f),
                    delta >= 0f ? $"+{delta:0}" : $"{delta:0}",
                    theme.At(theme.Mono, 17,
                        Mathf.Abs(delta) < 0.5f ? HudTheme.Ink3 :
                        delta > 0f ? HudTheme.Accent : HudTheme.Alert, TextAnchor.MiddleRight));
                y += 36f;
            }

            // §7.6 "경계 근무자는 회복량 50% 표시를 명시 — 다음 날의 빚"
            if (_sleepSettle?.guardIds != null)
            {
                foreach (var id in _sleepSettle.guardIds)
                {
                    var guard = FindMember(snapshot, id);
                    if (guard == null) continue;
                    var note = new Rect(panel.x + 40f, y + 12f, panel.width - 80f, 34f);
                    theme.Fill(note, HudTheme.AlertW);
                    GUI.Label(new Rect(note.x + 12f, note.y, note.width - 24f, note.height),
                        $"{guard.name} — 야간 경계 · 회복 50%",
                        theme.At(theme.Body, 15, HudTheme.Heat));
                    y += 42f;
                }
            }

            theme.Fill(new Rect(panel.x + 40f, y + 16f, panel.width - 80f, 1f), HudTheme.Rule);
            GUI.Label(new Rect(panel.x + 40f, y + 28f, 200f, 30f), "군기",
                theme.At(theme.Body, 17, HudTheme.Ink));
            GUI.Label(new Rect(panel.xMax - 200f, y + 28f, 160f, 30f),
                $"{snapshot.discipline?.value ?? 0d:0}",
                theme.At(theme.Mono, 17, HudTheme.Ink, TextAnchor.MiddleRight));

            GUI.Label(new Rect(panel.x, panel.yMax - 44f, panel.width, 24f), "SPACE — 닫기",
                theme.At(theme.Label, 13, HudTheme.Ink3, TextAnchor.MiddleCenter));
        }

        /* ═══════════════════════════════════════════ §7.7 승급 심사 */

        private void DrawRankReview(HudTheme theme, Snapshot snapshot)
        {
            var panel = Backdrop(theme, 1100f, 640f);
            if (_rankReview?.outcomes == null) return;

            Head(theme, panel, 88f,
                _rankReview.isRetry ? "RANK-01  재심사" : "RANK-01  승급 심사",
                $"D-{_rankReview.day:00} 승급 심사", $"요구 누적 {_rankReview.require:0}");

            // §7.7 "전원 공개가 핵심 설계 — 누가 무임승차했는지 드러나는 것이 사회적 압력 장치"
            var y = panel.y + 130f;
            var width = panel.width - 80f;

            var header = new Rect(panel.x + 40f, y, width, 34f);
            theme.Fill(header, HudTheme.Paper2);
            GUI.Label(new Rect(header.x + 16f, header.y, 200f, header.height), "분대원",
                theme.At(theme.Label, 12, HudTheme.Ink2));
            GUI.Label(new Rect(header.x + 460f, header.y, 160f, header.height), "누적",
                theme.At(theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleRight));
            GUI.Label(new Rect(header.xMax - 180f, header.y, 160f, header.height), "판정",
                theme.At(theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleRight));
            y += 38f;

            foreach (var outcome in _rankReview.outcomes)
            {
                var member = FindMember(snapshot, outcome.memberId);
                var row = new Rect(panel.x + 40f, y, width, 56f);
                var mine = outcome.memberId == Client.MemberId;

                theme.Fill(row, mine ? HudTheme.AccentW : HudTheme.Paper3);
                if (mine) theme.Spine(row, HudTheme.Accent);

                GUI.Label(new Rect(row.x + 20f, row.y, 200f, row.height),
                    member != null ? member.name : outcome.memberId,
                    theme.At(theme.Heading, 19, HudTheme.Ink));
                if (member != null)
                {
                    theme.Chip(new Rect(row.x + 140f, row.y + 17f, 42f, 22f),
                        HudTheme.RoleTag(member.role), HudTheme.RoleColor(member.role), HudTheme.Paper);
                }

                GUI.Label(new Rect(row.x + 200f, row.y, 240f, row.height),
                    $"{HudTheme.RankName(outcome.from)} → {HudTheme.RankName(outcome.to)}",
                    theme.At(theme.Body, 16, outcome.promoted ? HudTheme.Accent : HudTheme.Ink3));

                theme.Bar(new Rect(row.x + 460f, row.y + 24f, 200f, 8f),
                    (float)(outcome.score / Mathf.Max(1f, (float)outcome.require)),
                    outcome.promoted ? HudTheme.Accent : HudTheme.Alert);
                GUI.Label(new Rect(row.x + 670f, row.y, 120f, row.height),
                    $"{outcome.score:0} / {outcome.require:0}",
                    theme.At(theme.Mono, 17, HudTheme.Ink, TextAnchor.MiddleRight));

                GUI.Label(new Rect(row.xMax - 180f, row.y, 160f, row.height),
                    outcome.promoted ? "승급" : "보류",
                    theme.At(theme.Heading, 19,
                        outcome.promoted ? HudTheme.Accent : HudTheme.Alert, TextAnchor.MiddleRight));

                y += 60f;
            }

            GUI.Label(new Rect(panel.x, panel.yMax - 44f, panel.width, 24f),
                "승급은 선택 퀘스트로만 — 필수는 전원 100%라 변별력이 0이다   ·   SPACE",
                theme.At(theme.Label, 13, HudTheme.Ink3, TextAnchor.MiddleCenter));
        }

        /* ═══════════════════════════════════════════ §7.8 퀵 커맨드 라디얼 */

        private void DrawRadial(HudTheme theme)
        {
            var center = new Vector2(HudTheme.ViewWidth * 0.5f, HudTheme.ViewHeight * 0.5f);

            theme.Fill(new Rect(0f, 0f, HudTheme.ViewWidth, HudTheme.ViewHeight), HudTheme.Dim, 0.45f);

            // §7.8 라디얼 지름 360px, 슬롯 8개 각 96×96
            HudIcons.Circle(new Rect(center.x - 180f, center.y - 180f, 360f, 360f), 2f, HudTheme.Rule);
            HudIcons.Circle(new Rect(center.x - 62f, center.y - 62f, 124f, 124f), 1f, HudTheme.Rule);

            GUI.Label(new Rect(center.x - 60f, center.y - 20f, 120f, 20f), "HOLD",
                theme.At(theme.Label, 12, HudTheme.Ink2, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(center.x - 60f, center.y, 120f, 28f), "Q",
                theme.At(theme.Display, 22, HudTheme.Ink, TextAnchor.MiddleCenter));

            var hovered = SlotUnderCursor();

            for (var i = 0; i < 8; i += 1)
            {
                var (id, label) = HudIcons.Commands[i];
                var slot = SlotRect(center, i);
                var color = HudIcons.CommandColor(id);

                if (i == hovered)
                {
                    theme.Fill(new Rect(slot.x - 10f, slot.y - 10f, slot.width + 20f, slot.height + 20f),
                        HudTheme.Paper2);
                    theme.Border(new Rect(slot.x - 10f, slot.y - 10f, slot.width + 20f, slot.height + 20f),
                        color, 3f);
                }

                HudIcons.Command(id, new Rect(slot.x + 16f, slot.y + 8f, 64f, 64f), color);
                GUI.Label(new Rect(slot.x - 20f, slot.yMax - 8f, slot.width + 40f, 26f), label,
                    theme.At(theme.Heading, 17, color, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(slot.x + slot.width * 0.5f - 10f, slot.y - 26f, 20f, 20f), $"{i + 1}",
                    theme.At(theme.Label, 12, HudTheme.Ink3, TextAnchor.MiddleCenter));
            }
        }

        /// <summary>§7.8 표의 방향 — 1 집합 ↑, 2 대기 ↗, … 시계 방향</summary>
        private static Rect SlotRect(Vector2 center, int index)
        {
            var angle = index * 45f * Mathf.Deg2Rad;
            var x = center.x + Mathf.Sin(angle) * 158f - 48f;
            var y = center.y - Mathf.Cos(angle) * 158f - 48f;
            return new Rect(x, y, 96f, 96f);
        }

        /// <summary>마우스 방향 → 슬롯. 중앙 근처는 선택 없음(실수로 발신되지 않게)</summary>
        private static int SlotUnderCursor()
        {
            var scale = Mathf.Min(UnityEngine.Screen.width / HudTheme.ViewWidth,
                                  UnityEngine.Screen.height / HudTheme.ViewHeight);
            var mouse = new Vector2(Input.mousePosition.x / scale,
                                    (UnityEngine.Screen.height - Input.mousePosition.y) / scale);
            var center = new Vector2(HudTheme.ViewWidth * 0.5f, HudTheme.ViewHeight * 0.5f);
            var d = mouse - center;
            if (d.magnitude < 62f) return -1;

            var angle = Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.RoundToInt(angle / 45f) % 8;
        }

        private void Send(int slot)
        {
            if (slot < 0 || slot >= 8) return;
            Client.QuickCommand(HudIcons.Commands[slot].id);
        }

        /* ═══════════════════════════════════════════ §7.9 부대 지도 (M) */

        private void DrawBaseMap(HudTheme theme, Snapshot snapshot)
        {
            // 목업 실측: 지도 패널 + 우측 무전 상태 패널
            var panel = Backdrop(theme, 1180f, 940f);
            Head(theme, panel, 72f, "BASE MAP", "부대 배치도", "");

            var radio = _hud.visibility != null ? _hud.visibility.Radio : RadioState.Ok;
            var radioColor = _hud.visibility != null ? _hud.visibility.RadioColor : HudTheme.Accent;
            GUI.Label(new Rect(panel.xMax - 400f, panel.y + 24f, 368f, 26f),
                _hud.visibility != null ? "● " + _hud.visibility.RadioLabel : "",
                theme.At(theme.Body, 15, radioColor, TextAnchor.MiddleRight));

            // 범례가 3줄이 됐다(사람 형태/색 분리 때문에) — 지도 area를 그만큼 줄여서 자리를 낸다.
            // 아래 legend 쪽수와 짝을 맞춰서 바꿔야 한다.
            var area = new Rect(panel.x + 36f, panel.y + 106f, panel.width - 72f, panel.height - 300f);
            _hud.DrawSectorPlan(area, snapshot, radio, detailed: true);

            // 범례 — §1.3-A가 무엇을 하고 있는지 화면에서 읽혀야 한다.
            // 사람 마커는 두 축이 겹쳐 있다: 형태(속 찬 점/속 빈 원) = 같은 구역인지 아닌지,
            // 색(HudTheme.RoleColor) = 보직. 예전 범례는 이 둘을 색 하나로 뭉쳐 설명해서
            // 통신(Cold)·의무(White)가 아닌 보직은 범례와 다른 색으로 찍히는 거짓말이 됐다
            // (Hud.cs "5. 사람" 참고). 형태 줄과 색 줄을 분리해서 적는다.
            var legend = new Rect(panel.x + 36f, panel.yMax - 176f, panel.width - 72f, 140f);
            theme.Fill(new Rect(legend.x, legend.y, legend.width, 1f), HudTheme.Rule);
            GUI.Label(new Rect(legend.x, legend.y + 8f, 200f, 20f), "LEGEND",
                theme.At(theme.Label, 12, HudTheme.Ink2));

            theme.Fill(new Rect(legend.x, legend.y + 32f, 20f, 20f), HudTheme.AccentW);
            theme.Border(new Rect(legend.x, legend.y + 32f, 20f, 20f), HudTheme.Accent, 2f);
            GUI.Label(new Rect(legend.x + 30f, legend.y + 32f, 260f, 20f), "현재 구역 — 월드 렌더 활성",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            theme.Fill(new Rect(legend.x + 300f, legend.y + 32f, 20f, 20f), HudTheme.Paper3);
            theme.Border(new Rect(legend.x + 300f, legend.y + 32f, 20f, 20f), HudTheme.Rule2);
            GUI.Label(new Rect(legend.x + 330f, legend.y + 32f, 280f, 20f), "타 구역 — 스프라이트 미렌더",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            // 형태 = 가시성. 색이 아니라 속 찬 점/속 빈 원으로 구분한다 — 실제로 그리는 것과 맞춘다
            HudIcons.Dot(new Rect(legend.x + 640f, legend.y + 36f, 8f, 8f), HudTheme.Ink3);
            GUI.Label(new Rect(legend.x + 656f, legend.y + 32f, 220f, 20f), "같은 구역 — 속 찬 점(실시간)",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            HudIcons.Circle(new Rect(legend.x + 880f, legend.y + 36f, 8f, 8f), 2f, HudTheme.Ink3);
            GUI.Label(new Rect(legend.x + 896f, legend.y + 32f, 200f, 20f), "타 구역 — 속 빈 원(무전)",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            // 무전 두절이면 타 구역 마커는 파랗게 뜨는 게 아니라 아예 안 그려진다(Hud.cs §1.3-C).
            // 이 문장이 없으면 "왜 아무도 안 보이지"를 고장으로 읽는다
            GUI.Label(new Rect(legend.x, legend.y + 58f, legend.width, 20f),
                "무전 두절(Radio Down) 시 타 구역 인원 표시는 사라진다 — 고장이 아니다",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            // 색 = 보직. 완장 색과 같은 값(RoleColor)이라 월드와 UI가 일치한다
            var roleY = legend.y + 84f;
            var roleX = legend.x;
            foreach (var role in new[] { "comms", "medic", "admin", "" })
            {
                theme.Chip(new Rect(roleX, roleY, 44f, 22f),
                    HudTheme.RoleTag(role), HudTheme.RoleColor(role), HudTheme.Paper);
                roleX += 54f;
            }
            GUI.Label(new Rect(roleX + 10f, roleY, 170f, 22f), "— 마커 색 = 보직",
                theme.At(theme.Small, 14, HudTheme.Ink2));

            // 지도의 주황선은 사람이 아니라 문(HudTheme.Heat) — 사람으로 오독하는 신고가 있었다
            theme.Fill(new Rect(legend.x + 460f, roleY + 1f, 20f, 20f), HudTheme.Heat);
            GUI.Label(new Rect(legend.x + 490f, roleY, 280f, 22f), "주황선 — 문(사람 아님)",
                theme.At(theme.Small, 14, HudTheme.Ink2));
        }

        /* ══════════════════════════════════════════════════════ 헬퍼 */

        public struct Counts
        {
            public int requiredDone, requiredTotal, optionalDone, optionalTotal;
            public string jointLabel;
            public Color jointColor;
        }

        /// <summary>
        /// 수첩 요약과 4분할이 같은 숫자를 써야 한다. 두 곳에서 세면 반드시 갈라진다.
        /// </summary>
        public static Counts CountQuests(Snapshot snapshot, string memberId)
        {
            var counts = new Counts { jointLabel = "없음", jointColor = HudTheme.Ink3 };
            if (snapshot?.quests == null) return counts;

            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                var done = quest.status == SnapshotQuestsItemStatusValues.Done;

                if (quest.kind == SnapshotQuestsItemKindValues.Joint)
                {
                    counts.jointLabel = done ? "완료" : $"진행 {quest.progress * 100d:0}%";
                    counts.jointColor = done ? HudTheme.Accent : HudTheme.Heat;
                    continue;
                }

                // 회복 행동은 일과가 아니다 — 선택에 섞이면 "선택 0/8"처럼 보이고
                // 승급에 쓰는 선택 완료율이 밥 먹기로 희석된다(7.0)
                if (quest.kind == SnapshotQuestsItemKindValues.Care) continue;
                if (quest.ownerId != memberId) continue;

                if (quest.required)
                {
                    counts.requiredTotal += 1;
                    if (done) counts.requiredDone += 1;
                }
                else
                {
                    counts.optionalTotal += 1;
                    if (done) counts.optionalDone += 1;
                }
            }
            return counts;
        }

        public static SnapshotMembersItem FindMember(Snapshot snapshot, string memberId)
        {
            if (snapshot?.members == null || string.IsNullOrEmpty(memberId)) return null;
            foreach (var member in snapshot.members)
            {
                if (member?.id == memberId) return member;
            }
            return null;
        }

        private static string NameOf(Snapshot snapshot, string memberId)
        {
            var member = FindMember(snapshot, memberId);
            return member != null ? member.name : "누군가";
        }
    }
}
