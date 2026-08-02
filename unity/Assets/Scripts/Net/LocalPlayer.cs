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
        [Tooltip("내 자리를 올릴 곳. 없으면 안 보낸다 — 표시용이라 없어도 게임은 돈다")]
        public GameClient client;

        [Tooltip("걷는 속도(타일/초). PLAN 01 = 10")]
        public float speed = 10f;

        [Tooltip("구보 배수 (Shift). §5.5 run은 walk보다 fps가 1.4배다")]
        public float sprint = 1.4f;

        /// <summary>구역 이동 중에는 조작을 잠근다. `ZoneWorld`가 위치를 몬다</summary>
        public bool Locked { get; set; }

        /// <summary>전체화면 UI가 떠 있는 동안에도 잠근다 — 수첩을 보며 걷지 않는다</summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// B-3 정형 문구(quick-phrase) 8종 — id 순서가 곧 숫자 1~8과 `Hud.QuickPhrases`의
        /// 표시 순서다. 둘 중 하나만 바뀌면 번호와 문구가 어긋나므로 항상 같이 고친다.
        /// </summary>
        public static readonly string[] QuickPhraseIds =
        {
            "assist", "goingFirst", "gather", "here", "thanks", "comingNow", "danger", "wellDone",
        };

        /// <summary>C를 누르고 있는 동안 true — quick-phrase 목록이 열려 있다는 뜻이다.
        /// `Hud`가 이 값을 읽어 목록을 그린다(HudScreens가 아니라 Hud가 그리는 이유는
        /// B-3 소유 파일이 GameClient·Hud·LocalPlayer뿐이라서다).</summary>
        public bool PhraseWheelOpen { get; private set; }

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

        /// <summary>D-3 #7 — 내 캐릭터도 남들과 같은 멤버별 미세 변주를 받는다.
        /// `client.MemberId`는 서버 응답이 온 뒤에야 채워지므로 한 번만, 되는 대로 건다</summary>
        private bool _paceApplied;

        private void Awake()
        {
            _body = GetComponent<Rigidbody2D>();
            if (_rig == null) _rig = GetComponent<CharacterRig>();
            if (_rig != null) _rig.StepContact += PlayFootstep;

            // 탑다운에는 중력이 없다. 벽에 부딪혀도 돌지 않아야 한다
            _body.gravityScale = 0f;
            _body.freezeRotation = true;
        }

        public void Bind(CharacterRig rig) => _rig = rig;

        /// <summary>D-4 3단계 — 걷기 접지 프레임마다 발소리. `Vfx.HandleStepContact`가
        /// 같은 신호로 먼지를 뿌리는 것과 같은 자리다(`CharacterRig.StepContact`).
        /// 소리 한 줄만 붙인다 — 판정·이동 로직은 그대로 둔다</summary>
        private static void PlayFootstep() => Sfx.Play("walk", 0.5f);

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

            // B-3 정형 문구(quick-phrase) — C를 누르고 있으면 목록이 열리고 숫자
            // 1~8로 발화한다. Q는 이미 8.0 퀵 커맨드(전술 지시) 라디얼이 쓰고
            // 있어(`HudScreens.cs`) 겹치지 않는 C를 쓴다. 목록이 열린 동안은
            // 이동을 멈춘다 — 위 free 계산은 그대로 두고 여기서 입력만 덧씌운다.
            // 그래야 번호를 누르며 걷다가 엉뚱한 방향으로 튀어나가지 않는다.
            PhraseWheelOpen = free && Input.GetKey(KeyCode.C);
            if (PhraseWheelOpen)
            {
                _input = Vector2.zero;
                _running = false;
                if (client != null)
                {
                    for (var i = 0; i < QuickPhraseIds.Length; i += 1)
                    {
                        if (Input.GetKeyDown(KeyCode.Alpha1 + i)) client.QuickPhrase(QuickPhraseIds[i]);
                    }
                }
            }

            if (!_paceApplied && _rig != null && client != null && !string.IsNullOrEmpty(client.MemberId))
            {
                _rig.SetPaceSeed(CharacterRig.StableHash(client.MemberId));
                _paceApplied = true;
            }

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

        /// <summary>내 자리를 서버로 올리는 주기(초). 스냅샷이 10Hz라 그보다 성기게 보낸다</summary>
        private const float ReportEvery = 0.2f;
        /// <summary>이만큼 안 움직였으면 안 보낸다 — 가만히 서 있는 사람이 5Hz로 떠들 이유가 없다</summary>
        private const float ReportMoved = 0.15f;

        private float _reportAt;
        private Vector2 _reported = new Vector2(float.MinValue, float.MinValue);

        private void LateUpdate()
        {
            if (client == null) return;

            _reportAt -= Time.deltaTime;
            if (_reportAt > 0f) return;
            _reportAt = ReportEvery;

            var at = (Vector2)transform.position;
            if (Vector2.Distance(at, _reported) < ReportMoved) return;

            _reported = at;
            client.Position(at.x, at.y);
        }
    }
}
