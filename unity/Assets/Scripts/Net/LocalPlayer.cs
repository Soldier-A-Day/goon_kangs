using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>
    /// 내가 조작하는 분대원. 1인칭 · 마우스 시점 · WASD.
    ///
    /// **위치는 서버가 모른다.** sim은 좌표가 아니라 구역과 이동 소요만 갖는다
    /// (4.3) — `zones.ts`가 "좌표는 논리 좌표이며 렌더링과 무관하다. Unity는 실제
    /// 좌표를 zone으로 매핑해 보고할 뿐이다"라고 못박아뒀다.
    ///
    /// 그래서 맵 안을 걷는 것은 규칙이 아니다. 걷다가 **무엇 앞에 섰는지**만
    /// 의도로 바꿔 보내고, 되는지 안 되는지는 서버가 정한다(ARCH-02).
    /// 구역을 넘는 이동만 서버 판정을 거치며 그동안에는 조작을 잠근다 —
    /// 걸어서 다음 구역에 도착할 수는 없다. 6.1의 "동선이 멀다"가 시간 비용이기 때문이다.
    ///
    /// 마우스는 **포인터 락**으로 잡는다. 브라우저 Pointer Lock API를 Unity가
    /// `Cursor.lockState`로 감싼 것이라, 웹에서도 커서가 화면을 벗어나지 않는다.
    /// 락은 사용자 제스처에서만 걸리므로(브라우저 규칙) 클릭으로 건다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class LocalPlayer : MonoBehaviour
    {
        [Tooltip("걷는 속도(m/s)")]
        public float speed = 4.5f;

        [Tooltip("달리기 배수 (Shift)")]
        public float sprint = 1.8f;

        [Tooltip("마우스 감도")]
        public float sensitivity = 2.4f;

        [Tooltip("눈높이. 캐릭터가 1.75m라 그 조금 아래다")]
        public float eyeHeight = 1.62f;

        public Camera view;

        /// <summary>구역 이동 중에는 조작을 잠근다</summary>
        public bool Locked { get; set; }

        /// <summary>포인터 락이 걸려 있는가. HUD가 조준점을 띄울지 정한다</summary>
        public bool Looking => Cursor.lockState == CursorLockMode.Locked;

        private CharacterController _controller;
        private float _fall;
        private float _yaw;
        private float _pitch;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _yaw = transform.eulerAngles.y;

            // 1인칭이므로 내 몸은 그리지 않는다. T포즈 팔이 화면 한가운데를
            // 가로막고, 머리 안쪽이 보인다. 남들이 보는 내 모습은 SquadView가
            // 따로 세우므로 여기서 지워도 남의 화면에는 영향이 없다.
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            if (view != null)
            {
                view.transform.SetParent(transform, false);
                view.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
                view.transform.localRotation = Quaternion.identity;
                view.nearClipPlane = 0.05f;
            }
        }

        /// <summary>구역이 바뀌면 순간이동한다. 걸어서 넘어가는 게 아니다</summary>
        public void TeleportTo(Vector3 position)
        {
            _controller.enabled = false;
            transform.position = position;
            _controller.enabled = true;
        }

        private void Update()
        {
            Look();
            Move();
        }

        private void Look()
        {
            // 락은 사용자 제스처에서만 걸린다. 화면을 누르면 시점 조작이 시작되고
            // ESC로 풀린다 — 브라우저가 ESC를 가로채므로 우리가 처리할 필요는 없다.
            if (!Looking && Input.GetMouseButtonDown(0) && !IsOverHud())
            {
                Cursor.lockState = CursorLockMode.Locked;
            }

            if (!Looking || Locked) return;

            _yaw += Input.GetAxisRaw("Mouse X") * sensitivity;
            _pitch -= Input.GetAxisRaw("Mouse Y") * sensitivity;

            // 위아래로 넘어가지 않게 막는다. 넘어가면 화면이 뒤집혀 방향을 잃는다.
            _pitch = Mathf.Clamp(_pitch, -85f, 85f);

            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (view != null) view.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void Move()
        {
            var input = Locked || !Looking
                ? Vector2.zero
                : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            var move = transform.right * input.x + transform.forward * input.y;
            if (move.sqrMagnitude > 1f) move.Normalize();

            var pace = speed * (Input.GetKey(KeyCode.LeftShift) ? sprint : 1f);

            // 지면에 붙인다. 블록아웃에는 바닥 콜라이더만 있어 계단은 없다.
            _fall = _controller.isGrounded ? -1f : _fall + Physics.gravity.y * Time.deltaTime;
            _controller.Move((move * pace + Vector3.up * _fall) * Time.deltaTime);
        }

        /// <summary>
        /// HUD 위를 눌렀는지.
        ///
        /// HUD 버튼을 누르려던 클릭으로 포인터 락이 걸리면, 누르려던 버튼 대신
        /// 시점이 잡혀버린다. 화면 가장자리의 패널 영역은 락에서 뺀다.
        /// </summary>
        private static bool IsOverHud()
        {
            var mouse = Input.mousePosition;
            var margin = Screen.width * 0.26f;
            return mouse.x < margin || mouse.x > Screen.width - margin || mouse.y < Screen.height * 0.2f;
        }
    }
}
