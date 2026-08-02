using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 내가 조작하는 분대원. 탑다운 · WASD (SAD-ART-001 §1.1).
    ///
    /// **위치는 서버가 모른다.** sim은 좌표가 아니라 구역과 이동 소요만 갖는다
    /// (`zones.ts`: "좌표는 논리 좌표이며 렌더링과 무관하다"). 그래서 맵 안을 걷는
    /// 것은 규칙이 아니고, 걷다가 **무엇 앞에 섰는지**만 의도로 바꿔 보낸다.
    /// 되는지 안 되는지는 서버가 정한다(ARCH-02).
    ///
    /// 구역을 넘는 이동만 서버 판정을 거치며, 그동안에는 조작을 잠그고 `ZoneWorld`가
    /// 데려간다 — §6.1의 "시간 비용의 대부분이 이동"이 그렇게 화면에 남는다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class LocalPlayer : MonoBehaviour
    {
        /// <summary>
        /// PLAN 01 축척 규칙 — **이동 10타일/초**.
        ///
        /// 최장 대각 110타일을 약 11초에 지나라는 예산이고, 그게 걸어서 구역을
        /// 옮기는 설계에서 §6.1의 동선 비용을 정한다. 느리면 부대가 넓기만 하고,
        /// 빠르면 이동이 비용이 아니게 된다.
        /// </summary>
        [Tooltip("걷는 속도(타일/초). PLAN 01 = 10")]
        public float speed = 10f;

        [Tooltip("구보 배수 (Shift). §5.5 run은 walk보다 fps가 1.4배다")]
        public float sprint = 1.4f;

        /// <summary>구역 이동 중에는 조작을 잠근다. `ZoneWorld`가 위치를 몬다</summary>
        public bool Locked { get; set; }

        /// <summary>전체화면 UI가 떠 있는 동안에도 잠근다 — 수첩을 보며 걷지 않는다</summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// §9.0 사이드뷰 코스 — 전진을 `LaneRun`이 몬다.
        ///
        /// 여기서 WASD를 그대로 두면 옆에서 본 화면인데 위아래로 걸어 다니게
        /// 된다. 사이드뷰에서 플레이어가 정하는 것은 **속도뿐**이고, 그건
        /// `LaneRun`이 좌우 입력으로 읽는다.
        /// </summary>
        public bool SideView { get; set; }

        private Rigidbody2D _body;
        private CharacterRig _rig;
        private Vector2 _input;
        private bool _running;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_rig == null) _rig = GetComponent<CharacterRig>();

            // 탑다운에는 중력이 없다. 벽에 부딪혀도 돌지 않아야 한다
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
        }

        public void Bind(CharacterRig rig) => _rig = rig;

        public CharacterRig Rig => _rig;

        public void TeleportTo(Vector2 position)
        {
            _body.position = position;
            transform.position = position;
        }

        private void Update()
        {
            var free = !Locked && !Suspended && !SideView;

            _input = free
                ? new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"))
                : Vector2.zero;
            if (_input.sqrMagnitude > 1f) _input.Normalize();
            _running = free && Input.GetKey(KeyCode.LeftShift);

            // 잠긴 동안(이동 연출)은 `ZoneWorld`가 옮긴다 — 그 속도로 애니를 돌린다
            if (free) _rig?.Step(_input * speed, _running);
        }

        /// <summary>이동 연출이 밖에서 위치를 몰 때 애니메이션을 맞추기 위한 통로</summary>
        public void AnimateAs(Vector2 velocity) => _rig?.Step(velocity, velocity.magnitude > 6f);

        private void FixedUpdate()
        {
            if (Locked || Suspended || SideView) return;
            var pace = speed * (_running ? sprint : 1f);
            _body.MovePosition(_body.position + _input * (pace * Time.fixedDeltaTime));
        }
    }
}
