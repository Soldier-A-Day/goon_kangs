using UnityEngine;
using UnityEngine.Rendering;

namespace SoldierADay.Hd2dMock
{
    /// <summary>
    /// 이 씬만을 위한 URP 파이프라인 에셋을 활성화한다.
    ///
    /// 본선 3D 렌더러(Assets/M0/URP_Renderer.asset)는 <c>postProcessData</c>가
    /// 비어 있다(M0 벤치마크 씬이 후처리를 쓰지 않아서다) — 그 자산을 그대로
    /// 쓰면 Bloom·DoF·색 보정이 전부 조용히 무효화된다. 그렇다고 본선 자산을
    /// 고쳐 넣으면 "본선 파일 수정 0" 원칙이 깨진다.
    ///
    /// 그래서 Assets/Hd2dMock/Generated/Pipeline/ 아래 **복제본**을 만들어
    /// postProcessData만 채운 뒤, 이 스크립트가 런타임에 그 복제본을
    /// <see cref="GraphicsSettings.defaultRenderPipeline"/>으로 갈아 끼운다.
    /// 디스크 위의 본선 파일은 전혀 건드리지 않는다 — 이 빌드는 본선과
    /// 별도로 실행되는 독립 실행 파일이라 전역 상태를 바꿔도 새지 않는다.
    /// </summary>
    public sealed class Hd2dPipelineBootstrap : MonoBehaviour
    {
        public RenderPipelineAsset pipelineAsset;

        private void Awake()
        {
            if (pipelineAsset == null)
            {
                Debug.LogWarning("[HD2D] 파이프라인 에셋이 비어 있다 — 후처리가 안 보일 수 있다");
                return;
            }

            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            QualitySettings.renderPipeline = pipelineAsset;
        }
    }
}
