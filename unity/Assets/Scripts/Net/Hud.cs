using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// HUD.
    ///
    /// **판단하지 않는다.** 버튼은 의도를 보낼 뿐이고 되는지 안 되는지는 서버가
    /// 정한다. 클라가 "지금은 이동할 수 없다"를 스스로 막으면 규칙이 두 곳에
    /// 살게 되고(ARCH-02), 서버가 거절할 상황을 클라가 다르게 판단하는 순간
    /// 화면과 실제가 갈라진다.
    ///
    /// 그래서 버튼을 잠그지 않는다. 대신 **왜 안 되는지 읽을 거리를 준다** —
    /// 일과마다 구역과 시간대를 함께 띄운다. 이유가 안 보이면 "버튼이 고장났다"로
    /// 읽히고, 그건 막는 것만큼 나쁘다.
    ///
    /// 배치는 세 덩어리다. 왼쪽은 **상태**(지금 어떤가), 오른쪽은 **할 일**(무엇을
    /// 하나), 아래는 **말하기**(퀵 커맨드 8종 — 3.0에서 음성 없이 맞추는 유일한 수단).
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public GameClient client;
        public NetBootstrap boot;
        public Interactor interactor;
        public ZoneWorld world;
        public LocalPlayer player;
        public Font font;

        /// <summary>디자인 기준 해상도. 창 크기가 달라도 비율을 지킨다</summary>
        private const float DesignWidth = 1600f;
        private const float DesignHeight = 900f;

        /// <summary>표 3-2 퀵 커맨드 8종</summary>
        private static readonly (string id, string label)[] Commands =
        {
            ("assemble", "집합"), ("wait", "대기"), ("allClear", "이상무"), ("needHelp", "도움"),
            ("done", "완료"), ("cannot", "불가"), ("overHere", "이쪽"), ("hurry", "서둘러"),
        };

        private static readonly System.Collections.Generic.Dictionary<string, string> RoleLabel =
            new System.Collections.Generic.Dictionary<string, string>
            {
                { "rifle", "소총" }, { "comms", "통신" }, { "medic", "의무" }, { "admin", "행정" },
            };

        private HudTheme _theme;
        private Vector2 _taskScroll;

        private void OnGUI()
        {
            if (client == null) return;
            _theme ??= new HudTheme(font);

            // 기준 해상도로 스케일한다. 그러지 않으면 창이 커질수록 HUD가
            // 구석에 몰린 작은 글씨가 되고, 작아지면 화면을 다 덮는다.
            var scale = Mathf.Min(Screen.width / DesignWidth, Screen.height / DesignHeight);
            var matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            var width = Screen.width / scale;
            var height = Screen.height / scale;

            var snapshot = client.Latest;
            DrawStatus(snapshot, new Rect(24, 24, 296, 128));
            DrawSquad(snapshot, new Rect(24, 164, 296, 156));
            DrawTasks(snapshot, new Rect(width - 384, 24, 360, height - 156));
            DrawCommandBar(new Rect(width * 0.5f - 268, height - 92, 536, 68));
            DrawPrompt(width, height);
            DrawCrosshair(width, height);

            GUI.matrix = matrix;
        }

        /* ------------------------------------------------------------ 상태 */

        private void DrawStatus(Snapshot snapshot, Rect rect)
        {
            _theme.DrawPanel(rect);

            var connected = boot != null && boot.Connected;
            var dot = new Rect(rect.xMax - 26, rect.y + 16, 8, 8);
            _theme.DrawRounded(dot, 4f, connected ? HudTheme.Accent : HudTheme.Warn);

            if (snapshot == null)
            {
                GUI.Label(new Rect(rect.x + 20, rect.y + 20, 240, 24),
                    boot != null ? boot.Status : "연결 중", _theme.Title);
                GUI.Label(new Rect(rect.x + 20, rect.y + 48, 240, 20),
                    boot != null ? boot.Detail : "", _theme.Meta);
                return;
            }

            // 일차 — 가장 크게. 18일이 이 게임의 뼈대다(4.0)
            GUI.Label(new Rect(rect.x + 20, rect.y + 14, 120, 40), $"D-{snapshot.day:00}", _theme.Display);
            GUI.Label(new Rect(rect.x + 20 + Measure($"D-{snapshot.day:00}", _theme.Display) + 8, rect.y + 28, 120, 20),
                $"/ {snapshot.totalDays}", _theme.Meta);

            _theme.DrawFlat(new Rect(rect.x + 20, rect.y + 62, rect.width - 40, 1), HudTheme.Divider);

            if (snapshot.phase != null)
            {
                GUI.Label(new Rect(rect.x + 20, rect.y + 72, 180, 24), snapshot.phase.label, _theme.Title);
                GUI.Label(new Rect(rect.x + 20, rect.y + 94, 180, 18), snapshot.phase.clock, _theme.Meta);

                // 시간대 진행. 남은 시간이 눈에 보여야 6.1의 "시간이 모자란다"가 읽힌다
                if (snapshot.phase.durationMs > 0)
                {
                    var ratio = (float)(snapshot.phase.elapsedMs / snapshot.phase.durationMs);
                    _theme.DrawProgress(
                        new Rect(rect.x + 20, rect.y + 114, rect.width - 40, 3),
                        ratio, new Color(1f, 1f, 1f, 0.35f));
                }
            }

            if (snapshot.weather != null)
            {
                var color = HudTheme.BandColor(snapshot.weather.band);
                var text = $"{snapshot.weather.label}  {snapshot.weather.feelsLike:0}°";
                var chip = new Rect(rect.xMax - 20 - (Measure(text, _theme.ChipText) + 24), rect.y + 74, Measure(text, _theme.ChipText) + 24, 24);

                _theme.DrawRounded(chip, 12f, new Color(color.r, color.g, color.b, 0.16f));
                var style = _theme.ChipText;
                var previous = style.normal.textColor;
                style.normal.textColor = color;
                GUI.Label(chip, text, style);
                style.normal.textColor = previous;
            }
        }

        /* ------------------------------------------------------------ 분대 */

        private void DrawSquad(Snapshot snapshot, Rect rect)
        {
            _theme.DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 20, rect.y + 12, 200, 16), "분대", _theme.Label);

            if (snapshot?.members == null) return;

            var y = rect.y + 34;
            foreach (var member in snapshot.members)
            {
                if (member == null) continue;
                var me = member.id == client.MemberId;
                var row = new Rect(rect.x + 12, y, rect.width - 24, 26);

                if (me) _theme.DrawRounded(row, 8f, new Color(1f, 1f, 1f, 0.07f));

                // 보직 칩 — 4보직 1:1 대응(3.0)이라 색이 아니라 글자로 구분한다.
                // 색으로 하면 4가지를 외워야 하고, 두 글자면 바로 읽힌다.
                var badge = new Rect(row.x + 8, row.y + 4, 34, 18);
                _theme.DrawRounded(badge, 5f,
                    me ? new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.22f)
                       : HudTheme.SurfaceRaised);

                var badgeStyle = _theme.ChipText;
                badgeStyle.fontSize = 10;
                var previous = badgeStyle.normal.textColor;
                badgeStyle.normal.textColor = me ? HudTheme.Accent : HudTheme.TextSecondary;
                GUI.Label(badge, RoleLabel.TryGetValue(member.role, out var label) ? label : member.role, badgeStyle);
                badgeStyle.normal.textColor = previous;
                badgeStyle.fontSize = 12;

                GUI.Label(new Rect(row.x + 50, row.y, 120, 26), member.name, _theme.Body);

                // 이동 중이면 남은 시간을 보여준다. 서버가 준 값이며 클라는 세지 않는다.
                var zone = member.travelRemainingMs > 0
                    ? $"이동 중 {member.travelRemainingMs / 1000f:0.0}초"
                    : ZoneLabel(member.zone);
                var zoneStyle = _theme.Meta;
                zoneStyle.alignment = TextAnchor.MiddleRight;
                GUI.Label(new Rect(row.x, row.y, row.width - 10, 26), zone, zoneStyle);
                zoneStyle.alignment = TextAnchor.MiddleLeft;

                y += 30;
            }
        }

        /* ------------------------------------------------------- 이동·일과 */

        private void DrawTasks(Snapshot snapshot, Rect rect)
        {
            _theme.DrawPanel(rect);

            var done = 0;
            var total = 0;
            if (snapshot?.quests != null)
            {
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null || !IsMine(quest)) continue;
                    total += 1;
                    if (quest.status == SnapshotQuestsItemStatusValues.Done) done += 1;
                }
            }

            GUI.Label(new Rect(rect.x + 20, rect.y + 16, 120, 16), "내 일과", _theme.Label);
            var counter = _theme.Meta;
            counter.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(rect.x, rect.y + 16, rect.width - 20, 16), $"{done} / {total}", counter);
            counter.alignment = TextAnchor.MiddleLeft;

            if (snapshot?.quests == null) return;

            var ordered = Ordered(snapshot);
            var view = new Rect(rect.x + 12, rect.y + 40, rect.width - 24, rect.height - 52);

            _taskScroll = GUI.BeginScrollView(
                view, _taskScroll, new Rect(0, 0, view.width - 16, ordered.Count * 58f));

            for (var i = 0; i < ordered.Count; i += 1)
            {
                DrawTask(ordered[i], new Rect(0, i * 58f, view.width - 16, 52));
            }

            GUI.EndScrollView();
        }

        /// <summary>
        /// 일과 정렬.
        ///
        /// 서버는 생성 순서대로 보낸다 — 그 순서는 커리큘럼이 정하는 것이라
        /// 화면에서는 **뒤죽박죽으로 보인다.** 게다가 돌발 일과(6.0)가 끼어들면
        /// 목록이 통째로 밀려서, 방금 보던 줄이 어디로 갔는지 알 수 없다.
        ///
        /// 그래서 여기서 순서를 정한다. 기준은 "지금 손댈 수 있는 것이 위로"다.
        ///   1. 진행 중  — 이미 하고 있는 것
        ///   2. 지금 구역 — 걸어가지 않아도 되는 것
        ///   3. 필수     — 완주를 가르는 것 (9.0)
        ///   4. 시간대   — 일과표 순서 (4.0)
        ///   5. id       — 위 넷이 같으면 순서가 흔들리지 않게
        ///
        /// 이건 규칙이 아니라 정렬이다. 무엇을 할 수 있는지는 여전히 서버가 정하고,
        /// 순서를 바꾼다고 판정이 달라지지 않는다.
        /// </summary>
        private List<SnapshotQuestsItem> Ordered(Snapshot snapshot)
        {
            var here = MyZone(snapshot);
            var list = new List<SnapshotQuestsItem>();

            foreach (var quest in snapshot.quests)
            {
                if (quest != null && IsMine(quest)) list.Add(quest);
            }

            list.Sort((a, b) =>
            {
                var byDone = Rank(a) - Rank(b);
                if (byDone != 0) return byDone;

                var aHere = a.zone == here ? 0 : 1;
                var bHere = b.zone == here ? 0 : 1;
                if (aHere != bHere) return aHere - bHere;

                var aRequired = a.required ? 0 : 1;
                var bRequired = b.required ? 0 : 1;
                if (aRequired != bRequired) return aRequired - bRequired;

                var byPhase = PhaseOrder(a.phase) - PhaseOrder(b.phase);
                if (byPhase != 0) return byPhase;

                return string.CompareOrdinal(a.id, b.id);
            });

            return list;
        }

        private static int Rank(SnapshotQuestsItem quest) =>
            quest.status == SnapshotQuestsItemStatusValues.Active ? 0
            : quest.status == SnapshotQuestsItemStatusValues.Done ? 2 : 1;

        /// <summary>4.0 일과표 순서. 스냅샷의 phase 문자열을 그대로 받는다</summary>
        private static int PhaseOrder(string phase) => phase switch
        {
            "reveille" => 0,
            "morning" => 1,
            "afternoon" => 2,
            "personal" => 3,
            "evening" => 4,
            "lightsOut" => 5,
            _ => 6,
        };

        private void DrawTask(SnapshotQuestsItem quest, Rect rect)
        {
            var active = quest.status == SnapshotQuestsItemStatusValues.Active;
            var finished = quest.status == SnapshotQuestsItemStatusValues.Done;

            _theme.DrawRounded(rect, 10f,
                active ? new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, 0.10f)
                       : new Color(1f, 1f, 1f, 0.04f));

            // 필수는 왼쪽 띠로 표시한다. 아이콘을 쓰면 뜻을 외워야 하는데,
            // 띠는 "이건 다르다"가 즉시 읽힌다. 9.0의 필수 판정이 완주를 가른다.
            if (quest.required && !finished)
            {
                _theme.DrawRounded(new Rect(rect.x, rect.y + 8, 3, rect.height - 16),
                    1.5f, active ? HudTheme.Accent : HudTheme.Warn);
            }

            var titleStyle = _theme.Body;
            var previous = titleStyle.normal.textColor;
            titleStyle.normal.textColor = finished ? HudTheme.TextMuted : HudTheme.TextPrimary;
            GUI.Label(new Rect(rect.x + 14, rect.y + 6, rect.width - 90, 18), quest.label, titleStyle);
            titleStyle.normal.textColor = previous;

            // 구역·시간대를 함께 띄운다. 다른 구역의 일과를 누르면 서버가 거절하는데,
            // 막지 않는 대신 이유를 읽을 수 있어야 한다.
            GUI.Label(new Rect(rect.x + 14, rect.y + 24, rect.width - 90, 16),
                $"{ZoneLabel(quest.zone)} · {quest.phase}", _theme.Meta);

            _theme.DrawProgress(new Rect(rect.x + 14, rect.y + 42, rect.width - 90, 3),
                (float)quest.progress, finished ? HudTheme.TextMuted : HudTheme.Accent);

            if (finished)
            {
                var mark = _theme.ChipText;
                var before = mark.normal.textColor;
                mark.normal.textColor = HudTheme.TextMuted;
                GUI.Label(new Rect(rect.xMax - 66, rect.y, 56, rect.height), "완료", mark);
                mark.normal.textColor = before;
                return;
            }

            var button = new Rect(rect.xMax - 66, rect.y + 12, 56, 28);
            if (Chip(button, active ? "중단" : "수행", active)) client.Interact(quest.id, !active);
        }

        /* ---------------------------------------------------------- 조준점 */

        /// <summary>
        /// 화면 한가운데 점.
        ///
        /// 1인칭에서는 "어디를 보고 있는가"가 곧 "무엇에 다가가는가"다. 점이 없으면
        /// 상호작용 범위에 들어왔는지 감이 안 잡힌다. 시점을 잡지 않은 상태에서는
        /// 대신 어떻게 잡는지 알려준다 — 브라우저는 클릭 없이 커서를 못 숨긴다.
        /// </summary>
        private void DrawCrosshair(float width, float height)
        {
            if (player == null) return;

            if (!player.Looking)
            {
                var hint = new Rect(width * 0.5f - 150, height * 0.5f - 22, 300, 44);
                _theme.DrawPanel(hint);
                GUI.Label(hint, "화면을 클릭하면 시점 조작 · ESC로 해제", _theme.ChipText);
                return;
            }

            var dot = new Rect(width * 0.5f - 2.5f, height * 0.5f - 2.5f, 5, 5);
            _theme.DrawRounded(dot, 2.5f, new Color(1f, 1f, 1f, 0.75f));
        }

        /* -------------------------------------------------------- 프롬프트 */

        /// <summary>
        /// 눈앞의 것.
        ///
        /// 화면 가운데 아래에 뜬다 — 캐릭터가 거기 있고, 시선이 이미 가 있는 자리다.
        /// 목록 어딘가를 다시 찾게 만들면 "걸어가서 한다"는 감각이 깨진다.
        /// </summary>
        private void DrawPrompt(float width, float height)
        {
            if (world != null && world.TravelRemaining > 0f)
            {
                var moving = new Rect(width * 0.5f - 130, height - 168, 260, 44);
                _theme.DrawPanel(moving);
                var style = _theme.ChipText;
                var previous = style.normal.textColor;
                style.normal.textColor = HudTheme.Warn;
                GUI.Label(moving, $"이동 중  {world.TravelRemaining:0.0}초", style);
                style.normal.textColor = previous;
                return;
            }

            var near = interactor != null ? interactor.Nearest : null;
            if (near == null) return;

            if (near.kind == Interactable.Kind.Door) { DrawDoor(near, width, height); return; }

            var rect = new Rect(width * 0.5f - 170, height - 168, 340, 52);
            _theme.DrawPanel(rect);

            _theme.DrawRounded(new Rect(rect.x + 14, rect.y + 14, 24, 24), 6f,
                new Color(1f, 1f, 1f, 0.14f));
            GUI.Label(new Rect(rect.x + 14, rect.y + 14, 24, 24), "E", _theme.ChipText);

            GUI.Label(new Rect(rect.x + 48, rect.y + 8, rect.width - 60, 20),
                near.active ? $"{near.label} 중단" : near.label, _theme.Body);
            GUI.Label(new Rect(rect.x + 48, rect.y + 28, rect.width - 60, 16), near.detail, _theme.Meta);

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.E)
            {
                client.Interact(near.questId, !near.active);
                Event.current.Use();
            }
        }

        /// <summary>
        /// 문 앞. 여기서만 구역 이동이 뜬다.
        ///
        /// 어디서든 버튼으로 순간이동하면 6.1의 "동선이 멀다"가 사라진다 —
        /// 공통 일과의 시간 비용 대부분이 이동인데, 그게 화면에서 공짜가 되면
        /// 왜 시간이 모자란지가 읽히지 않는다.
        /// </summary>
        private void DrawDoor(Interactable door, float width, float height)
        {
            var rows = 4;
            var rect = new Rect(width * 0.5f - 200, height - 96 - rows * 34 - 44, 400, rows * 34 + 40);
            _theme.DrawPanel(rect);
            GUI.Label(new Rect(rect.x + 20, rect.y + 12, 200, 16),
                $"{door.detail} 출입문 · 어디로", _theme.Label);

            // 포인터 락 중에는 커서가 없다. 숫자키로도 고를 수 있어야 시점을
            // 풀지 않고 이동할 수 있다 — 문 앞에서 매번 ESC를 누르게 하면
            // 걸어다니는 감각이 끊긴다.
            var index = 0;
            var chipWidth = (rect.width - 40 - 8) / 2f;
            foreach (var pair in ZoneNames.All)
            {
                var here = pair.Key == MyZoneOrEmpty();
                var chip = new Rect(
                    rect.x + 20 + (index % 2) * (chipWidth + 8),
                    rect.y + 34 + (index / 2) * 34,
                    chipWidth, 28);

                var picked = Chip(chip, $"{index + 1}  {pair.Value}", here);

                if (Event.current.type == EventType.KeyDown &&
                    Event.current.keyCode == KeyCode.Alpha1 + index)
                {
                    picked = true;
                    Event.current.Use();
                }

                if (picked && !here) client.Move(pair.Key);
                index += 1;
            }
        }

        private string MyZoneOrEmpty()
        {
            var snapshot = client.Latest;
            return snapshot != null ? MyZone(snapshot) : "";
        }

        /* -------------------------------------------------------- 퀵 커맨드 */

        private void DrawCommandBar(Rect rect)
        {
            _theme.DrawPanel(rect);

            var width = (rect.width - 32 - 7 * 6) / 8f;
            for (var i = 0; i < Commands.Length; i += 1)
            {
                var chip = new Rect(rect.x + 16 + i * (width + 6), rect.y + 14, width, 30);
                if (Chip(chip, Commands[i].label, false)) client.QuickCommand(Commands[i].id);
            }

            GUI.Label(new Rect(rect.x + 16, rect.yMax - 18, 200, 14), "퀵 커맨드", _theme.Label);
        }

        /* ------------------------------------------------------------ 조각 */

        /// <summary>
        /// 칩 버튼.
        ///
        /// Unity 기본 버튼 스타일을 쓰지 않는 이유는 그 베벨 테두리가 정확히
        /// "옛스러움"의 정체이기 때문이다. 배경을 직접 그리고 라벨만 얹는다.
        /// </summary>
        private bool Chip(Rect rect, string label, bool highlighted)
        {
            var hover = rect.Contains(Event.current.mousePosition);

            var background = highlighted
                ? new Color(HudTheme.Accent.r, HudTheme.Accent.g, HudTheme.Accent.b, hover ? 0.34f : 0.24f)
                : new Color(1f, 1f, 1f, hover ? 0.14f : 0.07f);
            _theme.DrawRounded(rect, 10f, background);

            var style = _theme.ChipText;
            var previous = style.normal.textColor;
            style.normal.textColor = highlighted ? HudTheme.Accent : HudTheme.TextPrimary;
            GUI.Label(rect, label, style);
            style.normal.textColor = previous;

            return GUI.Button(rect, GUIContent.none, GUIStyle.none);
        }

        private static float Measure(string text, GUIStyle style) =>
            style.CalcSize(new GUIContent(text)).x;

        private bool IsMine(SnapshotQuestsItem quest) =>
            quest.ownerId == client.MemberId || string.IsNullOrEmpty(quest.ownerId);

        private string MyZone(Snapshot snapshot)
        {
            foreach (var member in snapshot.members)
            {
                if (member?.id == client.MemberId) return member.zone;
            }
            return "";
        }

        private static string ZoneLabel(string zone) => ZoneNames.Of(zone);
    }
}
