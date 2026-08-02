using System.Collections.Generic;
using UnityEngine;

namespace SoldierADay.Net
{
    /// <summary>가까이 가면 무언가 할 수 있는 지점</summary>
    public sealed class Interactable : MonoBehaviour
    {
        public string questId;
        public string label;
        public string detail;
        public bool active;
        public float radius = 2.2f;

        /// <summary>§7.1.5 프롬프트 2행이 왜 진행이 안 되는지를 말하려면 이 셋이 필요하다</summary>
        public bool required;
        /// <summary>요구 인원. 2 이상이면 §8.0 강제 협동 장치다</summary>
        public int minActors = 1;
        /// <summary>0~1. 서버가 세는 진척 — 클라는 표시만 한다</summary>
        public float progress;
        /// <summary>이 일과의 총 소요(초) — 곧 판의 제한 시간이다</summary>
        public float workSeconds = 20f;
        /// <summary>원형 이름(`SCRUB` · `PLACE` …). 판이 없는 일과면 null</summary>
        public string minigameType;

        private Transform _marker;
        private float _bobSeed;

        /// <summary>
        /// 눈에 보이는 표식.
        ///
        /// 없으면 어디로 가야 하는지 알 수가 없다. 일과 목록에 "생활관"이라고
        /// 적혀 있어도 생활관 어디인지는 안 적혀 있고, 물건 앞으로 걸어가라는
        /// 설계에서 그건 **찾기 놀이**가 된다 — 6.1이 말한 시간 비용은 동선이지
        /// 수색이 아니다.
        /// </summary>
        public void RaiseMarker(Sprite sprite, Color color)
        {
            var go = new GameObject("표식");
            go.transform.SetParent(transform, false);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            // 항상 모든 것 위에 뜬다. 물건 옆에 세우는 표식이 그 물건에 가려지면
            // 없는 것과 같다. sortingOrder는 16비트라 32767을 넘기면 래핑된다
            renderer.sortingOrder = 30000;

            _marker = go.transform;
            _bobSeed = (GetInstanceID() & 0xFF) * 0.13f;
        }

        /// <summary>플레이어가 이 안에 들어오면 마커를 숨긴다</summary>
        public Transform watcher;

        private void Update()
        {
            if (_marker == null) return;

            // 코앞에서는 감춘다. 상호작용 프롬프트가 이미 같은 말을 하고 있는데
            // 마커까지 머리 위에 겹치면 캐릭터가 안 보인다
            if (watcher != null)
            {
                var near = Vector2.Distance(watcher.position, transform.position) < radius * 0.6f;
                if (_marker.gameObject.activeSelf == near) _marker.gameObject.SetActive(!near);
                if (near) return;
            }

            // 위아래로 살랑인다. 정지한 그림은 맵 모듈과 구분되지 않는다
            var bob = Mathf.Sin(Time.time * 3f + _bobSeed) * 0.12f;
            _marker.localPosition = new Vector3(0f, 0.9f + bob, 0f);
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

                var distance = Vector2.Distance(point.transform.position, origin.position);
                if (distance > point.radius || distance >= bestDistance) continue;

                best = point;
                bestDistance = distance;
            }

            Nearest = best;
        }
    }
}
