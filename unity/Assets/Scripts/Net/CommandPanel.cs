using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 의도 보내기.
    ///
    /// **판단하지 않는다.** 버튼은 의도를 보낼 뿐이고, 되는지 안 되는지는 서버가
    /// 정한다. 클라가 "지금은 이동할 수 없다"를 스스로 막으면 두 가지가 무너진다 —
    /// 규칙이 두 곳에 살게 되고(ARCH-02), 서버가 거절할 상황을 클라가 다르게
    /// 판단하는 순간 화면과 실제가 갈라진다.
    ///
    /// 그래서 버튼을 회색으로 만들지 않는다. 눌러서 아무 일도 안 일어나면
    /// 그게 서버의 답이다. 대신 **내 현재 구역과 퀘스트 상태는 스냅샷에서 읽어**
    /// 표시한다 — 그건 판단이 아니라 서버가 보낸 사실이다.
    ///
    /// 퀵 커맨드 8종은 3.0의 협동 수단이다(음성 없이 4인이 맞춰야 한다).
    /// 라디얼 UI는 M1 범위이며(ASSETS.md §11), 여기서는 먼저 동작을 세운다.
    /// </summary>
    public sealed class CommandPanel : MonoBehaviour
    {
        public GameClient client;
        public Font font;

        [Tooltip("패널을 접는다. 측정 씬에서 화면을 가리지 않게")]
        public bool visible = true;

        /// <summary>
        /// 구역 목록은 표시용이다. 어디로 갈 수 있는지는 서버가 정한다 —
        /// 갈 수 없는 곳을 눌러도 서버가 무시할 뿐 화면이 어긋나지 않는다.
        /// </summary>
        private static readonly (string id, string label)[] Zones =
        {
            ("barracks", "생활관"),
            ("drillGround", "연병장"),
            ("messHall", "식당"),
            ("storage", "창고"),
            ("guardPost", "초소"),
            ("infirmary", "의무실"),
            ("boilerRoom", "보일러실"),
            ("trainingField", "훈련장"),
        };

        /// <summary>표 3-2의 퀵 커맨드 8종. 음성 없이 맞추는 유일한 수단이다</summary>
        private static readonly (string id, string label)[] Commands =
        {
            ("assemble", "집합"),
            ("wait", "대기"),
            ("allClear", "이상무"),
            ("needHelp", "도움"),
            ("done", "완료"),
            ("cannot", "불가"),
            ("overHere", "이쪽"),
            ("hurry", "서둘러"),
        };

        private Vector2 _questScroll;
        private GUIStyle _label;
        private GUIStyle _button;

        private void EnsureStyles()
        {
            if (_label != null) return;

            // 폰트를 지정하지 않으면 한글이 **빈칸**으로 나온다. Unity 기본 폰트에
            // 한글 글리프가 없어서인데, 값은 정상적으로 오므로 원인이 안 보인다.
            _label = new GUIStyle(GUI.skin.label);
            _button = new GUIStyle(GUI.skin.button);
            if (font != null)
            {
                _label.font = font;
                _button.font = font;
            }
        }

        private void OnGUI()
        {
            if (!visible || client == null) return;
            EnsureStyles();

            var snapshot = client.Latest;
            var width = 300f;
            var x = Screen.width - width - 10f;

            GUILayout.BeginArea(new Rect(x, 10, width, Screen.height - 20));

            GUILayout.Label(snapshot == null ? "서버 대기 중" : MyZoneLine(snapshot), _label);
            GUILayout.Space(6);

            DrawZones();
            GUILayout.Space(8);
            DrawCommands();
            GUILayout.Space(8);
            DrawQuests(snapshot);
            GUILayout.Space(8);
            DrawVotes();

            GUILayout.EndArea();
        }

        private string MyZoneLine(Snapshot snapshot)
        {
            foreach (var member in snapshot.members)
            {
                if (member?.id != client.MemberId) continue;

                // 이동 중이면 서버가 남은 시간을 준다. 클라가 세지 않는다.
                var moving = member.travelRemainingMs > 0
                    ? $" → 이동 중 {member.travelRemainingMs / 1000f:F1}초"
                    : "";
                return $"{member.name} · {member.zone}{moving}";
            }
            return "내 분대원을 찾지 못함";
        }

        private void DrawZones()
        {
            GUILayout.Label("이동", _label);
            for (var i = 0; i < Zones.Length; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (var j = i; j < i + 2 && j < Zones.Length; j += 1)
                {
                    if (GUILayout.Button(Zones[j].label, _button, GUILayout.Height(26)))
                    {
                        client.Move(Zones[j].id);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCommands()
        {
            GUILayout.Label("퀵 커맨드", _label);
            for (var i = 0; i < Commands.Length; i += 4)
            {
                GUILayout.BeginHorizontal();
                for (var j = i; j < i + 4 && j < Commands.Length; j += 1)
                {
                    if (GUILayout.Button(Commands[j].label, _button, GUILayout.Height(24)))
                    {
                        client.QuickCommand(Commands[j].id);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 내 퀘스트만 추린다.
        ///
        /// 이건 규칙이 아니라 **표시 필터**다 — 주인이 누구인지는 스냅샷에 이미
        /// 들어 있고, 남의 것을 안 보여주는 것은 판정과 무관하다. 반면 "이건
        /// 지금 시작할 수 없다"를 클라가 정하면 그건 규칙이다.
        /// </summary>
        private void DrawQuests(Snapshot snapshot)
        {
            GUILayout.Label("내 일과", _label);
            if (snapshot?.quests == null) return;

            _questScroll = GUILayout.BeginScrollView(_questScroll, GUILayout.Height(200));

            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                var mine = quest.ownerId == client.MemberId || string.IsNullOrEmpty(quest.ownerId);
                if (!mine) continue;

                var active = quest.status == SnapshotQuestsItemStatusValues.Active;
                var mark = quest.status == SnapshotQuestsItemStatusValues.Done ? "○"
                    : active ? "▶" : quest.required ? "●" : "△";

                // 구역을 함께 보여준다. 다른 구역의 일과를 누르면 서버가 거절하는데,
                // 이유가 안 보이면 "버튼이 고장났다"로 읽힌다. 막지는 않는다 —
                // 막는 순간 클라가 규칙을 갖게 된다. **읽을 거리를 줄 뿐이다.**
                GUILayout.BeginHorizontal();
                GUILayout.Label(
                    $"{mark} {quest.label} {quest.progress:P0}\n     {quest.zone} · {quest.phase}",
                    _label, GUILayout.Width(200));

                // 서버가 진행 상태를 알려주므로 버튼 글자는 그 반대를 가리킨다.
                // 눌렀는데 안 바뀌면 서버가 거절한 것이고, 그게 정답이다.
                if (GUILayout.Button(active ? "중단" : "수행", _button, GUILayout.Width(50)))
                {
                    client.Interact(quest.id, !active);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void DrawVotes()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("시간대 넘기기 투표", _button, GUILayout.Height(26)))
            {
                client.Send(new Intent { type = IntentTypeValues.VoteSkip, value = true });
            }
            GUILayout.EndHorizontal();
        }
    }
}
