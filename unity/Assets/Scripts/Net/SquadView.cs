using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 다른 분대원을 세운다 (SAD-ART-001 §1.3-A).
    ///
    /// **이 클래스가 통신병 시스템의 2D 대응 지점이다.** 서버는 분대원 전원의 구역을
    /// 보내주지만, 다른 구역에 있는 사람은 **그리지 않는다** — 2D 탑다운에서 화면 안이
    /// 전부 보이면 "무전이 끊겨서 정보가 안 온다"(§8.0)가 무의미해지기 때문이다.
    /// 판정은 `ZoneVisibility`가 하고 여기서는 그 답을 따른다.
    ///
    /// 위치는 서버가 모르므로(좌표가 없다) 구역 안 정해진 자리에 세운다. 실제로
    /// 걸어 다니는 것은 각자의 화면에서고, 남의 화면에서는 "그 구역에 있다"까지만
    /// 참이다 — 그 이상을 지어내면 화면과 서버가 갈라진다.
    /// </summary>
    public sealed class SquadView : MonoBehaviour
    {
        public GameClient client;
        public SpriteLibrary library;
        public ZoneVisibility visibility;
        public ZoneWorld world;

        private sealed class Member
        {
            public GameObject go;
            public CharacterRig rig;
            public string zone;
            public string role;
            public string rank;
            public Vector2 target;
            /// <summary>지금 그려지고 있는 자리. 목표로 미끄러진다</summary>
            public Vector2 at;
            public bool placed;
        }

        private readonly Dictionary<string, Member> _members = new Dictionary<string, Member>();
        private readonly List<string> _stale = new List<string>();

        /// <summary>HUD가 이름표를 그릴 때 쓴다 — 보이는 사람만 이름이 뜬다</summary>
        public IEnumerable<KeyValuePair<string, Transform>> Visible
        {
            get
            {
                foreach (var pair in _members)
                {
                    if (pair.Value.go != null && pair.Value.go.activeSelf)
                        yield return new KeyValuePair<string, Transform>(pair.Key, pair.Value.go.transform);
                }
            }
        }

        public void Apply(Snapshot snapshot)
        {
            if (snapshot?.members == null || library == null) return;

            var seen = 0;
            var total = CountOthers(snapshot);

            foreach (var member in snapshot.members)
            {
                if (member == null || member.id == client.MemberId) continue;

                if (!_members.TryGetValue(member.id, out var view))
                {
                    view = Spawn(member);
                    _members[member.id] = view;
                }

                // 보직·계급이 바뀌면(승급) 다시 입힌다. 매 스냅샷 갈아입히면
                // 10Hz로 시트를 다시 찾게 되고, 그건 힙에 톱질을 한다
                if (view.role != member.role || view.rank != member.rank)
                {
                    view.role = member.role;
                    view.rank = member.rank;
                    view.rig.SetLook(member.role, member.rank);
                }

                var canSee = visibility == null || visibility.CanSee(member.zone);
                view.go.SetActive(canSee);
                if (!canSee) continue;

                // **자리는 서버가 준 좌표를 쓴다.**
                //
                // 예전에는 구역이 바뀔 때만 그 방의 기본 자리로 옮겼다. 그래서
                // 같은 방 안에서는 아무도 안 움직이는 것으로 보였다 — 문 앞에
                // 있는지 관물대 앞에 있는지가 화면에 없었다. 지금은 좌표가
                // 스냅샷에 실려 온다(표시 전용이라 판정은 안 흔들린다).
                //
                // 아직 한 번도 안 보낸 사람은 0이다. 그때만 구역 기본 자리에 세운다
                var reported = new Vector2((float)member.x, (float)member.y);
                var known = reported.sqrMagnitude > 0.0001f;

                if (view.zone != member.zone)
                {
                    view.zone = member.zone;
                    if (!known && world != null) view.target = world.StandPoint(member.zone, seen, total);
                    // 구역이 바뀌는 것은 순간이동이다 — 방을 가로질러 미끄러지면
                    // 벽을 통과하는 것으로 보인다
                    view.placed = false;
                }

                if (known) view.target = reported;

                if (!view.placed)
                {
                    view.at = view.target;
                    view.placed = true;
                }

                seen += 1;
                // §5.5 19~20 — 분대원도 떨고 헐떡인다. 동상 판정은 서버가 내리고
                // (`warmth.ts`), 그게 보여야 "의무병이 가서 풀어줘야 한다"를
                // 말로 듣지 않고 눈으로 안다
                view.rig.SetDistress(
                    member.frostbitten,
                    member.stats != null &&
                    (member.stats.fatigue >= 90d || member.stats.hydration <= 30d));
            }

            Sweep(snapshot);
        }

        /// <summary>
        /// 스냅샷 사이를 잇는다.
        ///
        /// 스냅샷은 10Hz라 그대로 찍으면 남의 캐릭터가 초당 열 번 순간이동한다.
        /// **보간은 표시 계층의 몫**이고 판정에는 영향을 주지 않는다 —
        /// 서버가 준 좌표가 목표이고, 여기서는 거기까지 미끄러질 뿐이다.
        /// </summary>
        private void Update()
        {
            foreach (var pair in _members)
            {
                var view = pair.Value;
                if (view.go == null || !view.go.activeSelf || !view.placed) continue;

                var before = view.at;
                // 10Hz 간격(0.1초)을 조금 넘겨 잡는다. 딱 맞추면 매번 도착해서
                // 다시 튀고, 너무 느리면 남이 계속 뒤처져 보인다
                view.at = Vector2.Lerp(view.at, view.target, 1f - Mathf.Exp(-14f * Time.deltaTime));
                view.go.transform.position = view.at;

                // 걷는 것으로 보이게 — 다리가 안 움직이면 미끄러지는 것으로 읽힌다
                var velocity = (view.at - before) / Mathf.Max(0.0001f, Time.deltaTime);
                view.rig?.Step(velocity, velocity.magnitude > 6f);
            }
        }

        private static int CountOthers(Snapshot snapshot)
        {
            var n = 0;
            foreach (var member in snapshot.members)
            {
                if (member != null) n += 1;
            }
            return Mathf.Max(1, n - 1);
        }

        private Member Spawn(SnapshotMembersItem member)
        {
            var go = new GameObject($"분대원:{member.name}");
            go.transform.SetParent(transform, false);

            var rig = go.AddComponent<CharacterRig>();
            rig.Bind(library);
            rig.SetLook(member.role, member.rank);
            rig.Play("idle");

            return new Member { go = go, rig = rig, role = member.role, rank = member.rank };
        }

        /// <summary>
        /// 스냅샷에서 사라진 사람을 치운다.
        ///
        /// 매 스냅샷마다 전부 지우고 다시 만들지 않는다 — 10Hz로 오브젝트를 만들고
        /// 부수면 힙이 톱질을 하고, M0에서 확인한 "누수 0"이 무의미해진다.
        /// </summary>
        private void Sweep(Snapshot snapshot)
        {
            _stale.Clear();
            foreach (var pair in _members)
            {
                var alive = false;
                foreach (var member in snapshot.members)
                {
                    if (member != null && member.id == pair.Key) { alive = true; break; }
                }
                if (!alive) _stale.Add(pair.Key);
            }

            foreach (var id in _stale)
            {
                if (_members[id].go != null) Destroy(_members[id].go);
                _members.Remove(id);
            }
        }
    }
}
