using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 사이드뷰 횡스크롤 코스 (SAD-GDD-001 §9.0 재설계 · SAD-ART-001 §6.4 TR03·07·08).
    ///
    /// > 행군 구간에만 카메라를 사이드뷰로 전환. 4명이 좌→우로 자동 전진.
    /// > 각자 **페이스 게이지**를 유지 — 너무 앞서면 체력 과소모, 뒤처지면
    /// > 대열 이탈 판정. 구간 4개 = 체크포인트 4개 = 필수 퀘스트 4건.
    ///
    /// **전진은 자동이다.** 20km를 직접 걷게 하면 그냥 지루한 이동이 되고, 그게
    /// 3D 원안이 실패한 지점이다(§1.3-2). 플레이어가 정하는 것은 **속도**뿐이고,
    /// 그 하나가 체력과 대열이라는 두 개의 상반된 압력을 만든다.
    ///
    /// ── 판정은 여전히 서버에 있다 ─────────────────────────────────────
    /// 여기서 세는 것은 화면 위의 위치와 페이스뿐이다. 체크포인트를 지나면
    /// `interact` 의도를 보내고, 진척은 서버가 센다(`applyWork`) — 다른 일과와
    /// 똑같다. 코스를 완주했다고 클라가 선언하지 않는다(ARCH-02).
    /// </summary>
    [DefaultExecutionOrder(40)]
    public sealed class LaneRun : MonoBehaviour
    {
        public GameClient client;
        public ZoneWorld world;
        public LocalPlayer player;
        public CameraRig camera;

        /// <summary>씬 빌더가 채운다 — 코스별 지면 높이와 구간 이름</summary>
        [System.Serializable]
        public struct Lane
        {
            public string zone;
            public string name;
            public Rect area;
            /// <summary>열마다의 지면 높이(타일). 왼쪽 끝부터</summary>
            public int[] ground;
            public int segments;
            public string[] legs;
        }

        public Lane[] lanes = System.Array.Empty<Lane>();

        /// <summary>§9.0 페이스 — 0이 낙오, 1이 과속. 0.5 언저리가 대열이다</summary>
        public float Pace { get; private set; } = 0.5f;

        /// <summary>지금 달리는 코스. 없으면 사이드뷰가 아니다</summary>
        public bool Running => _lane >= 0;

        public string LegName =>
            _lane < 0 || _leg >= lanes[_lane].legs.Length ? "" : lanes[_lane].legs[_leg];

        /// <summary>0~1. 코스 전체 진행률 — HUD가 막대로 그린다</summary>
        public float Progress { get; private set; }

        /// <summary>지금까지 지난 체크포인트 수</summary>
        public int Checkpoints { get; private set; }

        /// <summary>지금 구간에 해당하는 체크포인트 일과. 스냅샷이 채운다</summary>
        public string QuestId { get; private set; }

        private int _lane = -1;
        private int _leg;
        private float _x;

        /// <summary>§9.0 "대열" — 분대 평균이 서 있어야 할 자리</summary>
        private float _column;

        private const float BaseSpeed = 7f;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
        }

        /// <summary>
        /// 지금 밟아야 할 체크포인트를 고른다.
        ///
        /// 훈련 체크포인트는 그날 4건이 한꺼번에 열려 있다(`quests.ts`). 지난
        /// 구간부터 순서대로 채우면 되므로, **아직 안 끝난 첫 건**이 지금 것이다.
        /// </summary>
        private void Apply(Snapshot snapshot)
        {
            QuestId = null;
            if (_lane < 0 || snapshot?.quests == null || client == null) return;

            var zone = lanes[_lane].zone;
            foreach (var quest in snapshot.quests)
            {
                if (quest == null || quest.zone != zone) continue;
                if (quest.ownerId != client.MemberId) continue;
                if (quest.status == SnapshotQuestsItemStatusValues.Done) continue;
                QuestId = quest.id;
                return;
            }
        }

        private void Update()
        {
            var here = world != null ? world.Here : null;
            var index = here == null ? -1 : IndexOf(here.id);

            if (index != _lane)
            {
                _lane = index;
                if (_lane >= 0) Begin();
                else End();
            }

            if (_lane < 0) return;
            Advance();
        }

        private int IndexOf(string zone)
        {
            for (var i = 0; i < lanes.Length; i += 1)
            {
                if (lanes[i].zone == zone) return i;
            }
            return -1;
        }

        private void Begin()
        {
            var lane = lanes[_lane];
            _x = 2f;
            _leg = 0;
            _column = 2f;
            Pace = 0.5f;
            Checkpoints = 0;
            Progress = 0f;

            // 사이드뷰는 **카메라를 가둔다.** 탑다운에서는 풀어뒀지만 여기서는
            // 코스 밖으로 나갈 일이 없고, 오히려 위아래로 흔들리면 달리는 느낌이 깨진다
            if (camera != null) camera.SetBounds(lane.area);
            if (player != null) player.SideView = true;
        }

        private void End()
        {
            if (player != null) player.SideView = false;
            if (client != null && QuestId != null) client.Interact(QuestId, false);
            QuestId = null;
        }

        private void Advance()
        {
            var lane = lanes[_lane];
            var dt = Time.deltaTime;

            // 대열은 일정하게 나아간다. 플레이어가 겨루는 상대가 이 선이다
            _column += BaseSpeed * dt;

            // §9.0 "너무 앞서면 체력 과소모, 뒤처지면 대열 이탈".
            // 위/아래가 아니라 **좌우**로 조절한다 — 옆에서 본 화면에서 속도는 가로다
            var push = Input.GetAxisRaw("Horizontal");
            var pace = BaseSpeed * (1f + push * 0.45f);
            _x += pace * dt;

            var gap = _x - _column;
            // ±6타일이 대열의 폭이다. 그 밖은 낙오이거나 과속이다
            Pace = Mathf.Clamp01(0.5f + gap / 12f);

            var span = lane.ground.Length;
            Progress = Mathf.Clamp01(_x / span);

            // 발밑 지면을 따라 오르내린다. 점프가 아니라 **지형을 밟는 것**이라
            // 중력을 쓰지 않는다 — 페이스가 이 게임의 조작이고 점프는 아니다
            var column = Mathf.Clamp(Mathf.FloorToInt(_x), 0, span - 1);
            var height = lane.ground[column];
            var foot = lane.area.yMin + (lane.area.height - height);

            if (player != null)
            {
                player.TeleportTo(new Vector2(lane.area.xMin + _x, foot));
                player.AnimateAs(new Vector2(pace, 0f));
            }

            // 구간을 넘을 때마다 체크포인트 하나 — §9.0 TRN-02가 필수 1건으로 센다
            var perLeg = span / Mathf.Max(1, lane.segments);
            var leg = Mathf.Clamp(column / Mathf.Max(1, perLeg), 0, lane.segments - 1);
            if (leg != _leg)
            {
                _leg = leg;
                Checkpoints += 1;
            }

            // 대열 안에 있는 동안만 진척을 보낸다. 낙오하거나 앞서 나가면
            // 그 구간은 인정되지 않는다 — 그게 "대열 유지"라는 판정의 전부다.
            //
            // 보내는 것은 **지금 구간의 체크포인트 일과**다. 다른 일과와 똑같이
            // `interact`로 나가고 진척은 서버가 센다(ARCH-02)
            var inColumn = Pace > 0.2f && Pace < 0.8f;
            if (client != null && QuestId != null) client.Interact(QuestId, inColumn);
        }
    }
}
