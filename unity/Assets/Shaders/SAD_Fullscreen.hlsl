#ifndef SAD_FULLSCREEN_INCLUDED
#define SAD_FULLSCREEN_INCLUDED

// ─────────────────────────────────────────────────────────────────────────────
// 풀스크린 효과 공용 (SAD-ART-001 §9.2)
//
// URP의 Blit.hlsl을 그대로 쓴다. `Blitter.BlitCameraTexture`가 넘겨주는
// `_BlitTexture` / `_BlitScaleBias`가 여기 들어 있고, XR·렌더 스케일·Y 뒤집힘
// 처리가 이미 다 되어 있다 — 직접 풀스크린 삼각형을 그리면 그 셋을 전부
// 다시 틀리게 된다.
//
// **§2.1이 정한 논리 해상도 640×360을 잊으면 안 된다.** 픽셀 퍼펙트로 그린
// 화면 위에 연속적인 왜곡을 얹으면 픽셀 격자가 어긋나 그 순간 픽셀아트가
// 아니게 된다. 그래서 왜곡 계열은 **논리 픽셀 단위로 스냅**한 뒤 샘플한다.
// ─────────────────────────────────────────────────────────────────────────────

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

//: §2.1 논리 해상도. 스냅 격자의 기준이다
static const float2 SAD_REF = float2(640.0, 360.0);

/// 논리 픽셀 격자에 UV를 붙인다. 왜곡을 얹어도 픽셀아트로 남게 하는 장치
float2 SadSnap(float2 uv)
{
    return (floor(uv * SAD_REF) + 0.5) / SAD_REF;
}

/// 값싼 해시 노이즈. 텍스처를 물리지 않으려고 쓴다 — 화면 효과 하나에
/// 노이즈 텍스처를 끼우면 그 로딩이 첫 프레임 히치가 된다
float SadHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float SadNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = SadHash(i);
    float b = SadHash(i + float2(1, 0));
    float c = SadHash(i + float2(0, 1));
    float d = SadHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

/// 화면 가장자리까지의 거리 (0 = 변, 1 = 한가운데). 프레임 계열이 전부 쓴다
float SadEdge(float2 uv)
{
    float2 d = min(uv, 1.0 - uv);
    return saturate(min(d.x, d.y) * 2.0);
}

float4 SadSample(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
}

#endif
