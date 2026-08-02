using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SoldierADay.Net
{
    /// <summary>
    /// 풀스크린 효과 패스 (SAD-ART-001 §9.2).
    ///
    /// 셰이더 6종을 **한 렌더러 피처가 순서대로** 돌린다. URP가 주는
    /// `FullScreenPassRendererFeature`를 6개 얹는 방법도 있지만, 그러면 렌더러
    /// 에셋에 항목이 6줄 생기고 전부 항상 켜져 있어 아무 일이 없는 프레임에도
    /// 블릿을 여섯 번 한다. 여기서는 **강도가 0인 효과를 건너뛴다** — 평시
    /// 낮에는 블릿이 한 번도 일어나지 않는다.
    ///
    /// 순서가 규칙이다.
    ///   1. 왜곡 (`HeatDistort`)    — 화면을 밀어놓고
    ///   2. 프레임 (`FrostFrame` · `MaskFrame`) — 그 위에 테두리를 얹고
    ///   3. 색 (`NightVision` · `VignettePulse` · `GrayscaleFade`) — 마지막에 물들인다
    ///
    /// 거꾸로 하면 테두리가 같이 일렁이고, 얼어붙은 서리가 왜곡을 타고 흐른다.
    /// </summary>
    public sealed class ScreenEffectsFeature : ScriptableRendererFeature
    {
        /// <summary>§9.2 표 순서. `ScreenEffects`가 이 순서로 강도를 채운다</summary>
        public enum Slot
        {
            HeatDistort,
            FrostFrame,
            MaskFrame,
            NightVision,
            VignettePulse,
            GrayscaleFade,
        }

        public const int SlotCount = 6;

        /// <summary>
        /// 지금 프레임의 재료.
        ///
        /// **정적이다.** 렌더러 피처는 에셋이고 씬 오브젝트를 참조할 수 없다 —
        /// 씬이 바뀌면 참조가 끊긴 채로 남는다. `ScreenEffects`가 매 프레임
        /// 여기에 값을 밀어넣고, 패스는 읽기만 한다.
        /// </summary>
        public static readonly Material[] Materials = new Material[SlotCount];

        private sealed class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        private sealed class Pass : ScriptableRenderPass
        {
            private readonly List<int> _active = new List<int>(SlotCount);

            public Pass()
            {
                // 2D 렌더러에서 후처리가 끝난 뒤에 얹는다. 앞에 넣으면
                // 온도 밴드 그레이딩(§4.3)이 우리 효과까지 다시 물들인다
                renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
            }

            public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
            {
                _active.Clear();
                for (var i = 0; i < SlotCount; i += 1)
                {
                    if (Materials[i] != null) _active.Add(i);
                }
                if (_active.Count == 0) return;

                var resources = frame.Get<UniversalResourceData>();
                if (resources.isActiveTargetBackBuffer) return;   // 백버퍼는 샘플할 수 없다

                var source = resources.activeColorTexture;
                var desc = graph.GetTextureDesc(source);
                desc.name = "SAD_ScreenFX";
                desc.clearBuffer = false;
                desc.depthBufferBits = 0;

                foreach (var slot in _active)
                {
                    // **핑퐁이 필요하다.** 같은 텍스처를 읽으면서 쓰면 GPU마다
                    // 다른 그림이 나온다 — 어떤 기기에서만 화면이 번지는 버그가 된다
                    var target = graph.CreateTexture(desc);

                    using (var builder = graph.AddRasterRenderPass<PassData>(
                               "SAD 화면 효과", out var data))
                    {
                        data.source = source;
                        data.material = Materials[slot];

                        builder.UseTexture(source);
                        builder.SetRenderAttachment(target, 0);
                        builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                        {
                            Blitter.BlitTexture(ctx.cmd, d.source,
                                                new Vector4(1f, 1f, 0f, 0f), d.material, 0);
                        });
                    }

                    source = target;
                }

                resources.cameraColor = source;
            }
        }

        private Pass _pass;

        public override void Create() => _pass = new Pass();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData data)
        {
            if (data.cameraData.cameraType != CameraType.Game) return;
            renderer.EnqueuePass(_pass);
        }
    }
}
