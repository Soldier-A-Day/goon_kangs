Shader "SAD/SH_FrostFrame"
{
    // §4.3 동상 — "화면 4변에 결빙 프레임 스프라이트 페이드인".
    //
    // `_Coverage`는 **보온 게이지에 물린다**(§5.0 90초). 게이지가 줄어드는 동안
    // 서서히 차오르고 동상이 붙으면 가득 찬다. 그래야 얼기 전에 "들어가야 한다"를
    // 읽을 수 있다 — 다 얼고 나서 알려주는 화면은 알려주지 않는 것과 같다.
    //
    // 판정은 서버(`warmth.ts`)에 있고 여기서는 그 값을 그리기만 한다.
    Properties
    {
        _Coverage ("결빙 진행", Range(0, 1)) = 0
        _Tint ("결빙 색", Color) = (0.72, 0.84, 0.92, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_FrostFrame"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Coverage;
            float4 _Tint;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SadSample(uv);
                if (_Coverage <= 0.0) return src;

                // 변에서 안쪽으로 자라는 서리. 결정 모양은 노이즈로 들쭉날쭉하게
                // 만든다 — 매끈한 띠는 서리가 아니라 비네트로 보인다
                float edge = SadEdge(uv);
                // 결정은 잘고 촘촘해야 서리로 보인다. 굵게 하면 얼룩이 된다
                float crystal = SadNoise(uv * 48.0) * 0.16 + SadNoise(uv * 110.0) * 0.07;

                // 가장 두꺼울 때가 화면의 5분의 1. §4.3이 요구한 것은 "프레임"이고,
                // 그보다 깊이 들어오면 시야를 가려 게임을 못 하게 된다 —
                // 처음 0.42로 두었더니 화면 절반이 얼음이 됐다
                float reach = _Coverage * 0.20;
                float mask = saturate((reach - edge + crystal * _Coverage) * 6.0);

                // 서리가 낀 자리는 얼어붙어 흐려진다. 논리 픽셀 한 칸 번짐이면
                // 픽셀아트를 깨지 않고도 "유리에 성에가 꼈다"가 읽힌다
                float2 blur = float2(1.0 / SAD_REF.x, 1.0 / SAD_REF.y);
                float4 soft = (SadSample(uv + float2(blur.x, 0)) + SadSample(uv - float2(blur.x, 0))
                             + SadSample(uv + float2(0, blur.y)) + SadSample(uv - float2(0, blur.y))) * 0.25;

                float3 frost = lerp(src.rgb, soft.rgb, 0.7);
                frost = lerp(frost, frost * _Tint.rgb + _Tint.rgb * 0.35, 0.6);

                return float4(lerp(src.rgb, frost, saturate(mask)), src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
