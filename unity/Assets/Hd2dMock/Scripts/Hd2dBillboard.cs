using UnityEngine;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// HD-2D 목업 전용 원통형 빌보드.
    ///
    /// 소품·캐릭터 스프라이트를 3D 공간에서 "항상 카메라를 본다"로 세우는
    /// 옥토패스류 트릭이다. Y축만 돌린다(피치는 고정) — 소품이 카메라 각도에
    /// 따라 앞으로 눕거나 뒤로 젖혀지면 바닥에 서 있다는 착시가 깨진다.
    ///
    /// 본선 코드와는 완전히 분리된 실험용 스크립트다(Assets/Hd2dMock 전용).
    /// </summary>
    public sealed class Hd2dBillboard : MonoBehaviour
    {
        private Transform _camera;

        private void LateUpdate()
        {
            if (_camera == null)
            {
                var cam = Camera.main;
                if (cam == null) return;
                _camera = cam.transform;
            }

            var toObject = transform.position - _camera.position;
            toObject.y = 0f;
            if (toObject.sqrMagnitude < 0.0001f) return;

            transform.rotation = Quaternion.LookRotation(toObject);
        }
    }
}
