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

                if (view.zone != member.zone)
                {
                    view.zone = member.zone;
                    view.target = world != null
                        ? world.StandPoint(member.zone, seen, total)
                        : view.target;
                    view.go.transform.position = view.target;
                }

                seen += 1;
                // §5.5 19~20 — 분대원도 떨고 헐떡인다. 동상 판정은 서버가 내리고
                // (`warmth.ts`), 그게 보여야 "의무병이 가서 풀어줘야 한다"를
                // 말로 듣지 않고 눈으로 안다
                view.rig.SetDistress(
                    member.frostbitten,
                    member.stats != null &&
                    (member.stats.fatigue >= 90d || member.stats.hydration <= 30d));
                view.rig.Step(Vector2.zero);
            }

            Sweep(snapshot);
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
