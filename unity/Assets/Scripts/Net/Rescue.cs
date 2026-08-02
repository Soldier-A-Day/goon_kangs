using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// B-2 위기 구조 — 쓰러진 동료 곁에서 E를 홀드해 살린다 (`docs/WORKORDER.md` B-2).
    ///
    /// **새 미니게임 판을 열지 않는다.** `QuestPlay`의 판 시스템과는 다른 길이다 —
    /// 위기는 조작을 겨루는 게임이 아니라 "곁에 있어 주는 시간"이어야 긴장이 산다.
    /// ChoreBoard처럼 붙잡은 시간이 그대로 진척이 되는 월드 상호작용이지만, 판을
    /// 열지 않으므로 `Minigame/` 폴더는 건드리지 않는다 — 그것이 이 발주의 경계다.
    ///
    /// 판정은 전부 서버(`packages/sim/src/crisis.ts`)가 한다. 여기서는 누가 곁에
    /// 있는지 알아내 홀드 여부만 보낼 뿐이다(ARCH-02).
    /// </summary>
    [DefaultExecutionOrder(46)]
    public sealed class Rescue : MonoBehaviour
    {
        public GameClient client;
        public LocalPlayer player;
        public SquadView squad;

        /// <summary>곁에 있다고 인정하는 거리 — `Interactable.radius` 기본값과 같다</summary>
        public const float Radius = 2.2f;

        /// <summary>지금 곁에 있는 위기의 동료 id — 없으면 null</summary>
        public string TargetId { get; private set; }
        public string TargetName { get; private set; }
        /// <summary>"stamina" · "hydration" — HUD 문구 분기에 쓴다</summary>
        public string TargetStat { get; private set; }
        /// <summary>0~1. 서버가 세는 구조 진척 — 화면은 표시만 한다</summary>
        public float Progress { get; private set; }
        /// <summary>위기 만료까지 남은 초 — 토스트가 예고한 숫자와 같은 시계다</summary>
        public float SecondsLeft { get; private set; }
        /// <summary>지금 E를 누르고 있는가 — HUD 프롬프트가 홀드 연출에 쓴다</summary>
        public bool Holding { get; private set; }

        private Snapshot _snapshot;

        private void OnEnable()
        {
            if (client != null) client.SnapshotReceived += Apply;
        }

        private void OnDisable()
        {
            if (client != null) client.SnapshotReceived -= Apply;
            Release();
        }

        private void Apply(Snapshot snapshot) => _snapshot = snapshot;

        private void Update()
        {
            // 나 자신이 쓰러져 있으면(후송·위기 둘 다) 남을 구할 수 없다 —
            // `Evacuation`이 이미 조작을 잠근다
            if (player == null || player.Locked)
            {
                Release();
                return;
            }

            var found = FindNearestCrisis();
            if (found == null)
            {
                Release();
                return;
            }

            TargetId = found.id;
            TargetName = found.name;
            TargetStat = found.crisisStat;
            Progress = (float)found.rescueProgress;
            SecondsLeft = Mathf.Max(0f, (float)found.crisisMsLeft / 1000f);

            var hold = Input.GetKey(KeyCode.E);
            if (hold != Holding)
            {
                Holding = hold;
                client?.Rescue(TargetId, hold);
            }
        }

        /// <summary>붙잡은 것을 놓는다 — 자리를 뜨거나 대상이 사라지면 서버에도 알려야 한다</summary>
        private void Release()
        {
            if (Holding)
            {
                Holding = false;
                client?.Rescue(TargetId, false);
            }
            TargetId = null;
            TargetName = null;
            TargetStat = null;
            Progress = 0f;
        }

        private SnapshotMembersItem FindNearestCrisis()
        {
            if (_snapshot?.members == null || player == null || squad == null) return null;

            SnapshotMembersItem best = null;
            var bestDistance = Radius;

            foreach (var member in _snapshot.members)
            {
                if (member == null || string.IsNullOrEmpty(member.crisisStat)) continue;
                if (client != null && member.id == client.MemberId) continue; // 자기 자신은 대상이 아니다

                var target = squad.Find(member.id);
                if (target == null) continue;

                var distance = Vector2.Distance(player.transform.position, target.position);
                if (distance > bestDistance) continue;

                best = member;
                bestDistance = distance;
            }

            return best;
        }
    }
}
