using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SoldierADay.M0
{
    /// <summary>
    /// `M0_Real` 계측기 (docs/M0_SCENE.md §4).
    ///
    /// **이 씬은 게이트가 아니다.** 여기서 60fps가 나오는 건 당연하다 — 실제 에셋
    /// 범위는 목표 부하의 1/4이다(§0). 목적은 통과가 아니라 **합성 프록시와 다르게
    /// 구는 항목을 찾는 것**이다.
    ///
    /// 그래서 fps만 찍지 않는다. 삼각형·렌더러·재질·스킨드 수를 함께 낸다 —
    /// 프록시와 같은 폴리에서 무거우면 원인이 그 셋 중에 있다.
    ///
    /// 드로우콜은 WebGL 빌드에서 런타임으로 읽을 수 없다(UnityStats는 에디터 전용).
    /// 대신 **정적 배칭이 안 되는 렌더러 수**를 센다 — §2가 캐릭터 28 드로우콜을
    /// 셀 때 쓴 것과 같은 셈이고, 그게 배칭이 깨졌는지 알려주는 값이다.
    /// </summary>
    public sealed class RealProbe : MonoBehaviour
    {
        [Tooltip("합성 씬 목표 부하의 실측 삼각형 (M0_SCENE.md §6.5 2차)")]
        public int syntheticTris = 629_000;

        [Tooltip("합성 씬 목표 부하에서 나온 fps")]
        public float syntheticFps = 60f;

        private string _overlay = "";
        private float _next;
        private float _accum;
        private int _frames;
        private float _fps;
        private bool _wasHidden;

        private Stats _stats;

        private struct Stats
        {
            public int triangles;
            public int meshRenderers;
            public int skinnedRenderers;
            public int materials;
            /// <summary>정적 배칭 대상이 아닌 렌더러. 개수만큼 드로우콜이 그대로 나간다</summary>
            public int unbatchable;
        }

        private void Start()
        {
            _stats = Measure();
            Debug.Log(
                $"[M0_Real] 삼각형 {_stats.triangles:N0} · 렌더러 {_stats.meshRenderers} + " +
                $"스킨드 {_stats.skinnedRenderers} · 재질 {_stats.materials} · " +
                $"배칭 불가 {_stats.unbatchable}");
        }

        private static Stats Measure()
        {
            var stats = new Stats();
            var materials = new HashSet<Material>();

            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;

                stats.meshRenderers += 1;
                stats.triangles += filter.sharedMesh.triangles.Length / 3;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material != null) materials.Add(material);
                }

                // 정적으로 표시되지 않은 것은 배칭 대상이 아니다.
                // 실제 게임에서 움직이는 물건이 늘면 이 수가 그대로 드로우콜이 된다(§2).
                if (!renderer.gameObject.isStatic) stats.unbatchable += 1;
            }

            foreach (var skinned in FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
            {
                if (skinned.sharedMesh == null) continue;

                stats.skinnedRenderers += 1;
                stats.triangles += skinned.sharedMesh.triangles.Length / 3;
                foreach (var material in skinned.sharedMaterials)
                {
                    if (material != null) materials.Add(material);
                }

                // 스킨드는 정적 배칭이 원리적으로 불가능하다(ASSETS.md §0)
                stats.unbatchable += 1;
            }

            stats.materials = materials.Count;
            return stats;
        }

        private void Update()
        {
            var frameMs = Time.unscaledDeltaTime * 1000f;

            // 창이 가려지면 브라우저가 rAF를 멈추고 쉰 시간이 한 프레임에 실린다.
            // M0 측정에서 8.9분짜리 '프레임'을 본 적이 있다 — 값으로 걸러낸다.
            if (frameMs > 1000f) _wasHidden = true;

            _accum += frameMs;
            _frames += 1;
            if (_accum >= 500f)
            {
                _fps = 1000f / (_accum / _frames);
                _accum = 0f;
                _frames = 0;
            }

            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;
            Compose();
        }

        private void Compose()
        {
            var text = new StringBuilder();
            text.AppendLine("M0_Real — 실제 에셋 대조 (게이트가 아니다)");
            text.AppendLine();
            text.AppendLine($"삼각형      {_stats.triangles,10:N0}");
            text.AppendLine($"메시 렌더러 {_stats.meshRenderers,10}");
            text.AppendLine($"스킨드      {_stats.skinnedRenderers,10}");
            text.AppendLine($"재질        {_stats.materials,10}");
            text.AppendLine($"배칭 불가   {_stats.unbatchable,10}  ← 이만큼은 드로우콜이 그대로 나간다");
            text.AppendLine($"fps         {_fps,10:F1}");
            text.AppendLine();

            // 핵심 비교. 같은 폴리에서 프록시보다 무거우면 원인이 에셋 특성에 있다.
            var ratio = _stats.triangles > 0 ? (float)syntheticTris / _stats.triangles : 0f;
            text.AppendLine($"합성 씬: {syntheticTris:N0} tris · {syntheticFps:F0} fps");
            text.AppendLine($"이 씬은 폴리가 {ratio:F1}배 가볍다");

            if (_wasHidden)
            {
                text.AppendLine();
                text.AppendLine("※ 창이 가려진 적이 있다 — fps는 믿을 수 없다");
            }
            else if (_fps < 55f)
            {
                // 폴리가 훨씬 가벼운데 프레임이 안 나오면 그게 §4가 찾던 것이다.
                text.AppendLine();
                text.AppendLine("※ 폴리는 가벼운데 프레임이 낮다 — 원인은 폴리가 아니다.");
                text.AppendLine("   배칭 불가 수와 재질 수를 먼저 본다");
            }

            _overlay = text.ToString();
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;

            GUI.color = _fps < 30f ? Color.red : _fps < 55f ? Color.yellow : Color.green;
            GUI.Box(new Rect(10, 10, 520, 260), "");
            GUI.Label(new Rect(20, 16, 500, 250), _overlay);
            GUI.color = Color.white;
        }
    }
}
