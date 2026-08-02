using SoldierADay.Protocol;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 후송 연출 (SAD-GDD-001 §10.0 · SAD-ART-001 §9.1 `VFX_Collapse` · §9.2 `SH_Grayscale_Fade`).
    ///
    /// §10.0이 후송을 "**개인 게임오버가 개인에서 멈추게 하는 파이프라인**"이라
    /// 불렀다. 그 말이 이 연출의 전부다 — 쓰러진 사람의 화면은 색이 빠지고
    /// 멈추지만, 분대의 하루는 계속 돌아간다. 둘이 같은 화면에 있어야 한다.
    ///
    /// §3.3이 유혈을 금지했으므로 **색이 빠지는 것**이 그 자리를 대신한다.
    /// 방사형 블러가 중앙으로 빨려드는 방향인 이유도 같다 — 의식이 좁아지는 쪽.
    ///
    /// 판정은 서버다(`evacuation.ts`). 여기서는 `presence`가 바뀐 것을 보고
    /// 그릴 뿐이고, 클라가 누구를 후송하지 않는다(ARCH-02).
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class Evacuation : MonoBehaviour
    {
        public GameClient client;
        public ScreenEffects effects;
        public LocalPlayer player;
        public Vfx vfx;

        /// <summary>지금 후송 상태인가 — HUD가 안내를 띄운다</summary>
        public bool Down { get; private set; }

        /// <summary>0~1. 연출 진행. 다 차면 화면이 거의 검다</summary>
        public float Fade { get; private set; }

        /// <summary>
        /// 복귀는 **다음 날 아침**이다 (`evacuation.returnEvacuees`).
        ///
        /// 시간대 단위가 아니라 하루 단위라, 남은 칸을 세어 보여줄 것이 없다.
        /// 대신 "오늘은 끝났다"를 말한다 — 그게 §10.0이 후송에 붙인 값이다.
        /// </summary>
        public const string Notice = "후송됨 — 내일 아침 복귀한다";

        private bool _burst;

        private void OnEnable()
        {
            if (client != null)
            {
                client.SnapshotReceived += Apply;
                client.EventReceived += OnEvent;
            }
        }

        private void OnDisable()
        {
            if (client != null)
            {
                client.SnapshotReceived -= Apply;
                client.EventReceived -= OnEvent;
            }
        }

        private void OnEvent(ServerEvent evt)
        {
            if (evt?.type != ServerEventTypeValues.MemberEvacuated) return;
            if (evt.memberId != client.MemberId) return;

            // 쓰러지는 순간에만 터뜨린다. 스냅샷으로 알면 10Hz로 계속 터진다
            if (!_burst && vfx != null && player != null)
            {
                _burst = true;
                vfx.Collapsed(player.transform.position);
            }
        }

        private void Apply(Snapshot snapshot)
        {
            var me = HudScreens.FindMember(snapshot, client != null ? client.MemberId : null);
            if (me == null) return;

            var down = me.presence == SnapshotMembersItemPresenceValues.Evacuated;
            if (down != Down)
            {
                Down = down;
                if (!down) _burst = false;
            }

        }

        private void Update()
        {
            // 3초에 걸쳐 빠진다. §4.3의 밴드 전환과 같은 시간을 쓴다 —
            // 화면이 크게 바뀌는 일은 이 게임에서 전부 3초다
            Fade = Mathf.MoveTowards(Fade, Down ? 0.85f : 0f, Time.deltaTime / 3f);

            if (effects != null) effects.fadeOut = Fade;

            // 후송 중에는 걷지 못한다. 서버가 이미 일과를 막고 있지만,
            // 화면에서 움직일 수 있으면 "왜 아무것도 안 되지"가 된다
            if (player != null) player.Locked = Down;
        }
    }
}
