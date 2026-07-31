using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>가까이 가면 무언가 할 수 있는 지점</summary>
    public sealed class Interactable : MonoBehaviour
    {
        public enum Kind { Quest, Door }

        public Kind kind;
        public string questId;
        public string label;
        public string detail;
        public bool active;
        public float radius = 2.6f;

        private Transform _marker;

        /// <summary>
        /// 눈에 보이는 표식.
        ///
        /// 없으면 어디로 가야 하는지 알 수가 없다. 일과 목록에 "생활관"이라고
        /// 적혀 있어도 생활관 어디인지는 안 적혀 있고, 물건 앞으로 걸어가라는
        /// 설계에서 그건 **찾기 놀이**가 된다 — 6.1이 말한 시간 비용은 동선이지
        /// 수색이 아니다.
        /// </summary>
        public void RaiseMarker(Material material, Color color, float height, float width)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = "표식";
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            marker.transform.localScale = new Vector3(width, height, width);

            // 표식에 부딪히면 안 된다. 지나가려는 자리에 세우는 것이라서.
            Destroy(marker.GetComponent<BoxCollider>());

            var renderer = marker.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // 재질을 복제하지 않고 인스턴스 색만 바꾼다 — 복제하면 표식 수만큼
            // 재질이 늘고, 재질 수가 곧 드로우콜이다(§2).
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);

            _marker = marker.transform;
        }

        private void Update()
        {
            if (_marker == null) return;
            // 천천히 돈다. 정지한 흰 기둥은 블록아웃 지오메트리와 구분되지 않는다.
            _marker.localRotation = Quaternion.Euler(0f, Time.time * 45f, 0f);
        }
    }

    /// <summary>
    /// 가장 가까운 상호작용 지점을 고른다.
    ///
    /// **판정하지 않는다.** "지금 이걸 할 수 있다"를 정하는 것이 아니라
    /// "이 앞에 서 있다"를 알릴 뿐이다. 눌러서 서버가 거절하면 아무 일도
    /// 일어나지 않고, 그게 정답이다(ARCH-02).
    ///
    /// 목록에서 버튼을 누르는 대신 물건 앞으로 걸어가게 만든 이유는 6.1에 있다 —
    /// 공통 일과의 시간 비용 대부분이 이동이다. 목록 클릭으로는 그 비용이
    /// 화면에서 사라지고, 그러면 왜 시간이 모자란지가 읽히지 않는다.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        public Transform origin;

        private static readonly List<Interactable> Registry = new List<Interactable>();

        public Interactable Nearest { get; private set; }

        public static void Register(Interactable point)
        {
            if (!Registry.Contains(point)) Registry.Add(point);
        }

        public static void Unregister(Interactable point) => Registry.Remove(point);

        public static void Clear() => Registry.Clear();

        private void Update()
        {
            if (origin == null) { Nearest = null; return; }

            Interactable best = null;
            var bestDistance = float.MaxValue;

            for (var i = Registry.Count - 1; i >= 0; i -= 1)
            {
                var point = Registry[i];
                if (point == null) { Registry.RemoveAt(i); continue; }
                if (!point.gameObject.activeInHierarchy) continue;

                // 높이는 무시한다. 캐릭터는 바닥에 있고 앵커는 물건 높이에 있어서,
                // 3D 거리로 재면 키 큰 물건 앞에 서도 멀다고 나온다.
                var delta = point.transform.position - origin.position;
                delta.y = 0f;

                var distance = delta.magnitude;
                if (distance > point.radius || distance >= bestDistance) continue;

                best = point;
                bestDistance = distance;
            }

            Nearest = best;
        }
    }
}
