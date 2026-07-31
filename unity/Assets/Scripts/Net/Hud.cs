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
        public Font font;

        /// <summary>디자인 기준 해상도. 창 크기가 달라도 비율을 지킨다</summary>
        private const float DesignWidth = 1600f;
        private const float DesignHeight = 900f;

        private static readonly (string id, string label)[] Zones =
        {
            ("barracks", "생활관"), ("drillGround", "연병장"),
            ("messHall", "식당"), ("storage", "창고"),
            ("guardPost", "초소"), ("infirmary", "의무실"),
            ("boilerRoom", "보일러실"), ("trainingField", "훈련장"),
        };

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

            GUI.Label(new Rect(rect.x + 20, rect.y + 14, 120, 16), "이동", _theme.Label);

            var chipWidth = (rect.width - 40 - 8) / 2f;
            for (var i = 0; i < Zones.Length; i += 1)
            {
                var chip = new Rect(
                    rect.x + 20 + (i % 2) * (chipWidth + 8),
                    rect.y + 36 + (i / 2) * 32,
                    chipWidth, 28);

                var here = snapshot != null && MyZone(snapshot) == Zones[i].id;
                if (Chip(chip, Zones[i].label, here)) client.Move(Zones[i].id);
            }

            var listTop = rect.y + 36 + 4 * 32 + 12;
            _theme.DrawFlat(new Rect(rect.x + 20, listTop - 6, rect.width - 40, 1), HudTheme.Divider);

            var done = 0;
            var total = 0;
            if (snapshot?.quests != null)
            {
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null) continue;
                    total += 1;
                    if (quest.status == SnapshotQuestsItemStatusValues.Done) done += 1;
                }
            }

            GUI.Label(new Rect(rect.x + 20, listTop + 6, 120, 16), "내 일과", _theme.Label);
            var counter = _theme.Meta;
            counter.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(rect.x, listTop + 6, rect.width - 20, 16), $"{done} / {total}", counter);
            counter.alignment = TextAnchor.MiddleLeft;

            if (snapshot?.quests == null) return;

            var view = new Rect(rect.x + 12, listTop + 28, rect.width - 24, rect.yMax - listTop - 40);
            var rows = 0;
            foreach (var quest in snapshot.quests)
            {
                if (quest != null && IsMine(quest)) rows += 1;
            }

            _taskScroll = GUI.BeginScrollView(
                view, _taskScroll, new Rect(0, 0, view.width - 16, rows * 58f));

            var y = 0f;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null || !IsMine(quest)) continue;
                DrawTask(quest, new Rect(0, y, view.width - 16, 52));
                y += 58f;
            }

            GUI.EndScrollView();
        }

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

        private static string ZoneLabel(string zone)
        {
            foreach (var (id, label) in Zones)
            {
                if (id == zone) return label;
            }
            return zone;
        }
    }
}
