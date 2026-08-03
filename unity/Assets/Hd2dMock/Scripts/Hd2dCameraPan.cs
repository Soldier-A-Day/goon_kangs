using UnityEngine;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// WASD로 카메라를 수평 평행 이동시킨다(§ HD-2D 목업 "체감용").
    ///
    /// 틸트(피치)는 씬 빌더가 고정해 둔 값 그대로 둔다 — 여기서는 카메라가
    /// 보는 평면(Y=고정) 위에서만 미끄러진다. 피치까지 흔들면 "옥토패스 앵글"이
    /// 매 프레임 달라져 프레임 비교가 의미 없어진다.
    /// </summary>
    public sealed class Hd2dCameraPan : MonoBehaviour
    {
        public float speed = 6f;

        private void Update()
        {
            var forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            var right = transform.right;
            right.y = 0f;
            right.Normalize();

            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;

            if (move.sqrMagnitude < 0.0001f) return;
            transform.position += move.normalized * (speed * Time.deltaTime);
        }
    }
}
