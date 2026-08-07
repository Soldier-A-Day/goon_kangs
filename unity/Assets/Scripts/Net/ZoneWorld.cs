using System.Collections.Generic;
using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 스냅샷을 세계로 옮기는 층.
    ///
    /// 부대 전체가 **하나의 맵**이고(§6.4), 아트 구역 15개가 서버 구역 8개에 매핑된다.
    /// 하는 일은 넷이다.
    ///
    ///   1. **퀘스트를 물건 옆에 세운다.** 서버가 그날 만든 퀘스트를 `ZoneMap`이
    ///      찾아준 자리에 붙인다 — "관물대 정돈"은 관물대 앞에서 해야 말이 된다.
    ///   2. **구역 경계를 카메라에 준다.** §2.2 Confiner와 §1.3-A 시야 차단이
    ///      **같은 사각형**을 쓴다. 둘이 어긋나면 안 보이는 곳으로 카메라가 나간다.
    ///   3. **구역 이동을 연출한다.** 판정은 여전히 서버가 한다 —
    ///      `travelRemainingMs`가 도는 동안 조작을 잠근 채 목적지로 데려간다.
    ///   4. **내 보직 그림을 입힌다.**
    ///
    /// 계산하는 것이 하나도 없다. 세우고 옮기고 입힐 뿐이며, 무엇이 참인지는
    /// 전부 스냅샷이 정한다(ARCH-02).
    /// </summary>
    public sealed class ZoneWorld : MonoBehaviour
    {
        public GameClient client;
        public LocalPlayer player;
        public SquadView squad;
        public SpriteLibrary library;
        public CameraRig camera;
        public ZoneVisibility visibility;

        /// <summary>씬 빌더가 채운다. 아트 구역 25개</summary>
        public ZoneMap[] zones = System.Array.Empty<ZoneMap>();

        /// <summary>동 — 지도를 건물 단위로 묶어 그린다. 방 25개를 낱개로 늘어놓으면
        /// 어느 것이 한 건물인지 읽히지 않는다</summary>
        [System.Serializable]
        public struct Building
        {
            public string id;
            public string name;
            public Rect area;
        }

        /// <summary>문 — 걸어서 옮기는 유일한 통로라, 어디에 있는지가 곧 길 정보다</summary>
        [System.Serializable]
        public struct Door
        {
            public string zone;
            public string name;
            public string exitLabel;
            public bool isExit;
            public Rect area;
        }

        public Building[] buildings = System.Array.Empty<Building>();
        public Door[] doors = System.Array.Empty<Door>();

        /// <summary>
        /// 위병소 → 훈련장 길. 지도가 그린다 — 바닥 타일은 지도에 안 나오므로
        /// 이게 없으면 부대 밖이 통째로 빈 종이가 되고, 훈련장을 눈대중으로 찾게 된다
        /// </summary>
        public Rect[] roads = System.Array.Empty<Rect>();

        /// <summary>지금 서 있는 구역. 없으면(도로·공터) null</summary>
        public ZoneMap Here => _current;

        /// <summary>
        /// 화면 왼쪽 위에 띄울 현재 위치.
        ///
        /// 걸어서 이동하는 이상 "내가 어디 있나"가 상시 보여야 한다 —
        /// 구역이 25개고 문을 몇 개 지나면 금세 헷갈린다.
        /// </summary>
        public string HereName => _current != null ? _current.zoneName : "실외";

        /// <summary>
        /// 지금 실내인가 — §9.1 파티클이 쓴다.
        ///
        /// 어느 구역에도 안 서 있으면(도로·공터) 실외다. 생활관 천장에서 눈이
        /// 내리면 그 한 장면이 부대 전체를 가짜로 만든다.
        /// </summary>
        public bool HereIndoor => _current != null && _current.indoor;

        /// <summary>
        /// 지금 다가서 있는 문. 없으면 false.
        ///
        /// 바닥에 이름을 박는 대신 이걸로 HUD가 안내한다 — 다른 게임이
        /// 상호작용 프롬프트를 쓰는 것과 같은 자리다.
        /// </summary>
        public bool NearestDoor(Vector2 at, float radius, out string label, out bool isExit)
        {
            label = null;
            isExit = false;
            var best = float.MaxValue;

            foreach (var door in doors)
            {
                var d = Vector2.Distance(at, door.area.center);
                if (d > radius || d >= best) continue;
                best = d;

                // 안에서 볼 때와 밖에서 볼 때 할 말이 다르다. 나가는 문 앞에서는
                // "밖으로", 방 문 앞에서는 그 방 이름이 필요하다
                var inside = _current != null && _current.id == door.zone;
                label = door.isExit && inside && !string.IsNullOrEmpty(door.exitLabel)
                    ? door.exitLabel
                    : door.name;
                isExit = door.isExit;
            }

            return label != null;
        }

        private readonly Dictionary<string, Interactable> _questPoints =
            new Dictionary<string, Interactable>();
        private readonly List<string> _stale = new List<string>();

        private ZoneMap _current;
        private string _currentServerZone = "";

        /// <summary>
        /// 서버가 알려준 이동 잔여 시간.
        ///
        /// 걸어서 옮기면 0이다(`zones.ts` 인접 검증). NPC 대리와 헤드리스 시뮬에만
        /// 붙으므로 사람이 조작하는 동안에는 대개 0이고, HUD가 표시에만 쓴다.
        /// </summary>
        public float TravelRemaining { get; private set; }

        public ZoneMap Current => _current;

        private void Awake()
        {
            if (zones == null || zones.Length == 0) zones = GetComponentsInChildren<ZoneMap>(true);
        }

        /// <summary>
        /// 지금 발이 어느 구역을 밟고 있는가.
        ///
        /// 방이 복도보다 우선한다 — 문턱에서는 두 사각형이 겹치는데, 그때 복도로
        /// 읽으면 방에 들어서자마자 다시 나간 것이 되어 이동 보고가 튄다.
        /// </summary>
        public ZoneMap ZoneAt(Vector2 point)
        {
            ZoneMap loose = null;
            foreach (var zone in zones)
            {
                if (zone == null || !zone.area.Contains(point)) continue;
                if (zone.kind == "room") return zone;
                loose = zone;
            }
            return loose;
        }

        /// <summary>
        /// 걸어서 구역을 옮긴다 (SAD-ART-002).
        ///
        /// 메뉴로 목적지를 고르는 대신 **실제로 문을 지나간다.** 그래서 여기서
        /// 하는 일은 발밑을 보고 바뀌었으면 서버에 알리는 것뿐이다.
        ///
        /// 서버 구역이 같으면 보내지 않는다 — 생활관에서 복도로 나가는 것은
        /// 아트에서는 다른 방이지만 sim에서는 둘 다 `barracks`라, 보내봐야
        /// 같은 값이고 10Hz로 의도만 쌓인다.
        /// </summary>
        private void Update()
        {
            if (player == null || client == null || string.IsNullOrEmpty(client.MemberId)) return;

            var here = ZoneAt(player.transform.position);

            if (here == null)
            {
                // 문 밖이다 — 도로와 공터는 어느 구역에도 속하지 않는다.
                // **카메라를 반드시 풀어줘야 한다.** 안 그러면 방금 나온 방의
                // 경계에 화면이 갇힌 채 플레이어만 화면 밖으로 걸어 나가고,
                // 그건 "밖으로 나갈 수 없다"로 보인다 — 실제로 그랬다.
                if (_current != null)
                {
                    _current = null;
                    camera?.ClearBounds();
                }
                return;
            }

            if (here != _current)
            {
                _current = here;
                if (visibility != null) visibility.CurrentZone = here.id;
                // 야외 구역도 가두지 않는다. 연병장에서 도로로 걸어 나가는 것이
                // 이동의 전부인데 사각형에 갇히면 같은 일이 벌어진다
                if (here.kind == "outdoor") camera?.ClearBounds();
                else camera?.SetBounds(here.area);
            }

            if (here.id == _reportedZone) return;
            _reportedZone = here.id;
            client.Move(here.id, onFoot: true);
        }

        private string _reportedZone = "";

        public void Apply(Snapshot snapshot)
        {
            if (snapshot?.members == null) return;

            var me = FindMe(snapshot);
            if (me == null) return;

            visibility?.Apply(snapshot);

            if (player != null && library != null && player.Rig != null)
            {
                player.Rig.Bind(library);
                player.Rig.SetLook(me.role, me.rank, ClothFor(snapshot.weather?.band));
                // §5.5 19~20 — 상태 이상이 idle을 대체한다.
                //
                // 동상은 **서버 판정을 그대로 쓴다**(`warmth.ts`). 예전에는
                // "극혹한 + 스태미나 30 이하"로 클라가 짐작했는데, 그건 서버가
                // 이미 내린 판정을 옆에서 다시 세는 것이고 반드시 어긋난다 —
                // 이동 −30%는 걸려 있는데 화면은 멀쩡히 서 있는 식으로(ARCH-02).
                player.Rig.SetDistress(
                    me.frostbitten,
                    me.stats.fatigue >= 90d || me.stats.hydration <= 30d);
            }

            // 걸어서 이동하므로 조작을 잠그지 않는다. `travelRemainingMs`는 NPC 대리와
            // 헤드리스 시뮬에만 붙고(서버가 `onFoot`을 보면 0을 준다), 사람이 조작하는
            // 동안에는 실제로 걷는 시간이 곧 §6.1의 동선 비용이다
            TravelRemaining = (float)me.travelRemainingMs / 1000f;

            // 서버가 나를 다른 구역으로 옮겼다면(후송·강제 이동) 그 자리로 데려간다
            if (me.zone != _currentServerZone && me.zone != _reportedZone)
            {
                Teleport(me.zone);
            }
            _currentServerZone = me.zone;

            SyncQuests(snapshot);
            squad?.Apply(snapshot);
        }

        /// <summary>§4.3 밴드가 피복을 정한다 — 한랭 이하면 방한, 그 위는 전투복</summary>
        private static string ClothFor(string band) => band switch
        {
            SnapshotWeatherBandValues.ExtremeCold => "winter",
            SnapshotWeatherBandValues.Cold => "winter",
            _ => "field",
        };

        private SnapshotMembersItem FindMe(Snapshot snapshot)
        {
            foreach (var member in snapshot.members)
            {
                if (member?.id == client.MemberId) return member;
            }
            return null;
        }

        /* -------------------------------------------------------- 구역 진입 */

        /// <summary>
        /// 구역 id로 그 구역을 찾는다.
        ///
        /// 예전에는 서버 구역 하나에 아트 구역이 여럿이라 "대표를 고르는" 일이
        /// 필요했다 — `barracks` 하나에 생활관·복도·세면장이 다 걸려 있어서,
        /// 복도가 먼저 잡히면 하루를 복도 한가운데서 시작했다. 서버가 방 단위로
        /// 쪼개진 뒤로는 고를 것이 없다. id가 하나면 구역도 하나다.
        /// </summary>
        private ZoneMap FindZone(string zoneId)
        {
            if (string.IsNullOrEmpty(zoneId)) return null;
            foreach (var zone in zones)
            {
                if (zone != null && zone.id == zoneId) return zone;
            }
            return null;
        }

        /// <summary>
        /// 서버가 나를 옮겼을 때만 순간이동한다.
        ///
        /// 걸어서 가는 것이 정상 경로이므로 여기 오는 것은 예외뿐이다 — 후송으로
        /// 의무실에 눕거나, 훈련 씬에서 돌아오거나, 재접속했을 때.
        /// </summary>
        private void Teleport(string zoneId)
        {
            var zone = FindZone(zoneId);
            if (zone == null)
            {
                Debug.LogWarning($"[world] 맵이 없는 구역: {zoneId}");
                return;
            }

            _current = zone;
            _reportedZone = zoneId;
            if (visibility != null) visibility.CurrentZone = zone.id;

            player?.TeleportTo(zone.spawn);
            if (zone.kind == "outdoor") camera?.ClearBounds();
            else camera?.SetBounds(zone.area);
            camera?.SnapTo(zone.spawn);
        }

        /* ---------------------------------------------------------- 퀘스트 */

        /// <summary>분대원 표시가 설 자리 — 그 구역 스폰 주변에 나란히</summary>
        public Vector2 StandPoint(string zoneId, int index, int total)
        {
            var zone = FindZone(zoneId);
            if (zone == null) return Vector2.zero;
            if (total <= 1) return zone.spawn;
            return zone.spawn + new Vector2((index - (total - 1) * 0.5f) * 1.2f, 0f);
        }

        /// <summary>
        /// 내 퀘스트를 물건 옆에 세운다.
        ///
        /// 있는 것은 갱신하고 사라진 것만 지운다 — 10Hz로 오브젝트를 만들고 부수면
        /// 힙이 톱질을 하고, M0에서 확인한 "누수 0"이 무의미해진다.
        /// </summary>
        private void SyncQuests(Snapshot snapshot)
        {
            if (snapshot.quests == null) return;

            foreach (var quest in snapshot.quests)
            {
                if (quest == null) continue;
                // 끝났거나 시간대가 지나 잠긴 일과는 마커를 세우지 않는다.
                // 눌러도 서버가 거절할 뿐인데 표식만 떠 있으면 "고장난 버튼"이 된다
                if (quest.status != SnapshotQuestsItemStatusValues.Pending &&
                    quest.status != SnapshotQuestsItemStatusValues.Active) continue;

                // **다른 시간대의 일과도 마찬가지다.**
                //
                // 서버가 진척을 막게 했지만(4.0 · `canWork`) 마커가 그대로 떠
                // 있으면 오후 일과를 아침에 하러 갔다가 아무 일도 안 일어난다 —
                // 막는 것과 안 보이게 하는 것은 같이 가야 한다.
                //
                // 돌발은 뜬 자리에서 처리하는 것이라 시간대를 안 본다
                if (quest.kind != SnapshotQuestsItemKindValues.Surprise &&
                    quest.phase != snapshot.phase?.id) continue;

                var mine = quest.ownerId == client.MemberId || string.IsNullOrEmpty(quest.ownerId);
                if (!mine) continue;

                var zone = FindZone(quest.zone);
                if (zone == null) continue;

                if (!_questPoints.TryGetValue(quest.id, out var point) || point == null)
                {
                    var go = new GameObject($"일과:{quest.label}");
                    go.transform.SetParent(zone.transform, true);
                    point = go.AddComponent<Interactable>();
                    point.questId = quest.id;
                    point.transform.position = zone.AnchorFor(quest.id, quest.spot);
                    if (library != null)
                    {
                        // §3.2 "색은 정보다" — 필수는 경고색, 선택은 잉크색.
                        // 필수 판정이 완주를 가르므로 멀리서도 갈라 보여야 한다
                        point.RaiseMarker(library.markerQuest,
                            quest.required ? HudTheme.Alert : HudTheme.Accent);
                    }
                    point.watcher = player != null ? player.transform : null;
                    Interactor.Register(point);
                    _questPoints[quest.id] = point;
                }

                point.label = quest.label;
                point.detail = $"{ZoneNames.Of(quest.zone)} · {HudTheme.PhaseLabel(quest.phase)}";
                point.required = quest.required;
                point.minActors = (int)quest.minActors;
                point.active = quest.status == SnapshotQuestsItemStatusValues.Active;
                point.progress = (float)quest.progress;
                point.workSeconds = (float)quest.workSeconds;
                // 다가서기 전에 무슨 판인지 읽힌다 — 문지를 일과와 대조할 일과는
                // 마음가짐이 다르고, 그걸 E를 누른 뒤에 알면 늦다
                point.minigameType = quest.minigame?.type;
            }

            _stale.Clear();
            foreach (var pair in _questPoints)
            {
                var alive = false;
                foreach (var quest in snapshot.quests)
                {
                    if (quest == null || quest.id != pair.Key) continue;
                    alive = quest.status == SnapshotQuestsItemStatusValues.Pending ||
                            quest.status == SnapshotQuestsItemStatusValues.Active;
                    break;
                }
                if (!alive) _stale.Add(pair.Key);
            }

            foreach (var id in _stale)
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
}
