using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 스냅샷을 세계로 옮기는 층.
    ///
    /// 하는 일은 셋이다.
    /// 1. **내가 있는 구역의 맵만 켠다.** 8개를 다 띄우면 90m 밖의 빈 맵까지
    ///    그리게 되고, 무엇보다 내가 어디 있는지가 화면에서 안 읽힌다.
    /// 2. **퀘스트를 물건 옆에 세운다.** 서버가 그날 만든 퀘스트를 받아
    ///    `ZoneMap`이 찾아준 자리에 상호작용 지점을 붙인다.
    /// 3. **구역이 바뀌면 옮긴다.** 서버가 이동을 판정해 스냅샷의 zone이 바뀌면
    ///    그때 순간이동한다 — 걸어서 넘어가는 것이 아니다.
    ///
    /// 계산하는 것이 하나도 없다. 켜고 끄고 옮길 뿐이며, 무엇이 참인지는
    /// 전부 스냅샷이 정한다(ARCH-02).
    /// </summary>
    public sealed class ZoneWorld : MonoBehaviour
    {
        public GameClient client;
        public LocalPlayer player;
        public SquadView squad;

        [Tooltip("표식이 쓸 재질. 색은 인스턴스별로 바꾼다")]
        public Material markerMaterial;

        private readonly Dictionary<string, ZoneMap> _maps = new Dictionary<string, ZoneMap>();
        private readonly Dictionary<string, Interactable> _questPoints =
            new Dictionary<string, Interactable>();

        private Interactable _door;
        private string _current = "";

        /// <summary>서버가 알려준 이동 잔여 시간. 0보다 크면 조작을 잠근다</summary>
        public float TravelRemaining { get; private set; }

        public ZoneMap Current => _maps.TryGetValue(_current, out var map) ? map : null;

        private void Awake()
        {
            foreach (var map in GetComponentsInChildren<ZoneMap>(true))
            {
                _maps[map.zone] = map;
                map.gameObject.SetActive(false);
            }

            var doorGo = new GameObject("문");
            doorGo.transform.SetParent(transform, false);
            _door = doorGo.AddComponent<Interactable>();
            _door.kind = Interactable.Kind.Door;
            _door.label = "이동";
            // 생활관 깊이가 12m라 반경 6이면 스폰에서 바로 뜬다 — 들어오자마자
            // 다시 나가라고 묻는 꼴이 된다. 문 앞에 실제로 서야 뜨게 좁힌다.
            _door.radius = 3.5f;
            _door.RaiseMarker(markerMaterial, new Color(0.35f, 0.62f, 1f), 3.2f, 0.5f);
            _door.gameObject.SetActive(false);
            Interactor.Register(_door);
        }

        public void Apply(Snapshot snapshot)
        {
            if (snapshot?.members == null) return;

            var me = FindMe(snapshot);
            if (me == null) return;

            TravelRemaining = (float)me.travelRemainingMs / 1000f;
            if (player != null) player.Locked = TravelRemaining > 0f;

            if (me.zone != _current) EnterZone(me.zone);
            SyncQuests(snapshot, me.zone);

            // 다른 구역에 있는 분대원은 숨긴다. 안 그러면 꺼진 맵 자리의
            // 허공에 떠 있게 되고, 누가 여기 있는지가 오히려 헷갈린다.
            squad?.ShowOnly(me.zone);
        }

        private SnapshotMembersItem FindMe(Snapshot snapshot)
        {
            foreach (var member in snapshot.members)
            {
                if (member?.id == client.MemberId) return member;
            }
            return null;
        }

        private void EnterZone(string zone)
        {
            if (_maps.TryGetValue(_current, out var previous)) previous.gameObject.SetActive(false);
            _current = zone;

            if (!_maps.TryGetValue(zone, out var map))
            {
                Debug.LogWarning($"[world] 맵이 없는 구역: {zone}");
                return;
            }

            map.gameObject.SetActive(true);
            player?.TeleportTo(map.Spawn);

            _door.transform.position = map.Door;
            _door.detail = ZoneNames.Of(zone);
            _door.gameObject.SetActive(true);
        }

        /// <summary>
        /// 내 퀘스트를 물건 옆에 세운다.
        ///
        /// 매 스냅샷마다 전부 지우고 다시 만들지 않는다 — 10Hz로 오브젝트를
        /// 만들고 부수면 힙이 톱질을 하고, M0에서 확인한 누수 0이 무의미해진다.
        /// 있는 것은 갱신하고 사라진 것만 지운다.
        /// </summary>
        private void SyncQuests(Snapshot snapshot, string zone)
        {
            if (!_maps.TryGetValue(zone, out var map)) return;

            var alive = new HashSet<string>();

            foreach (var quest in snapshot.quests)
            {
                if (quest == null || quest.zone != zone) continue;
                if (quest.status == SnapshotQuestsItemStatusValues.Done) continue;

                var mine = quest.ownerId == client.MemberId || string.IsNullOrEmpty(quest.ownerId);
                if (!mine) continue;

                alive.Add(quest.id);

                if (!_questPoints.TryGetValue(quest.id, out var point) || point == null)
                {
                    var go = new GameObject($"일과:{quest.label}");
                    go.transform.SetParent(map.transform, true);
                    point = go.AddComponent<Interactable>();
                    point.kind = Interactable.Kind.Quest;
                    point.questId = quest.id;
                    point.transform.position = map.AnchorFor(quest.id, quest.label);
                    // 필수는 노랑, 선택은 흰색. 9.0의 필수 판정이 완주를 가르므로
                    // 멀리서도 갈라 보여야 한다.
                    point.RaiseMarker(markerMaterial,
                        quest.required ? new Color(1f, 0.78f, 0.2f) : new Color(0.85f, 0.9f, 0.95f),
                        2.4f, 0.28f);
                    Interactor.Register(point);
                    _questPoints[quest.id] = point;
                }

                point.label = quest.label;
                point.detail = $"{quest.zone} · {quest.phase}";
                point.active = quest.status == SnapshotQuestsItemStatusValues.Active;
            }

            // 끝났거나 사라진 것은 치운다
            var stale = new List<string>();
            foreach (var pair in _questPoints)
            {
                if (alive.Contains(pair.Key)) continue;
                stale.Add(pair.Key);
            }
            foreach (var id in stale)
            {
                if (_questPoints[id] != null)
                {
                    Interactor.Unregister(_questPoints[id]);
                    Destroy(_questPoints[id].gameObject);
                }
                _questPoints.Remove(id);
            }
        }

        private void OnDestroy() => Interactor.Clear();
    }

    /// <summary>구역 한글 이름. 표시용이며 규칙과 무관하다</summary>
    public static class ZoneNames
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            { "barracks", "생활관" }, { "drillGround", "연병장" },
            { "messHall", "식당" }, { "storage", "창고" },
            { "guardPost", "초소" }, { "infirmary", "의무실" },
            { "boilerRoom", "보일러실" }, { "trainingField", "훈련장" },
        };

        public static string Of(string zone) =>
            zone != null && Names.TryGetValue(zone, out var name) ? name : zone;

        public static IEnumerable<KeyValuePair<string, string>> All => Names;
    }
}
