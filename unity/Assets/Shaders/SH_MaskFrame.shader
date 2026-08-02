Shader "SAD/SH_MaskFrame"
{
    // §9.2 방독면 시야 프레임 — `_Coverage` 고정 0.2.
    //
    // 화생방 훈련(§7.10 방독면 컷인)에서 쓴다. 방독면을 쓰면 **시야가 실제로
    // 좁아지는 것**이 이 장비의 비용이고, 그 비용을 화면이 물지 않으면
    // 방독면은 그냥 컷인 한 장이 된다.
    //
    // 눈구멍 둘이 아니라 하나로 만든다. 게임 화면에서 쌍안 마스크는 어디를
    // 보라는 것인지 알 수 없고, 실제 방독면도 넓은 단일 시야창이 흔하다.
    Properties
    {
        _Coverage ("가림", Range(0, 1)) = 0.2
        _Amount ("적용", Range(0, 1)) = 0
        _Fog ("김서림", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_MaskFrame"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Coverage;
            float _Amount;
            float _Fog;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SadSample(uv);
                if (_Amount <= 0.0) return src;

                float2 c = uv - 0.5;
                c.x *= SAD_REF.x / SAD_REF.y;

                // 모서리가 둥근 사각 시야창. 방독면 렌즈는 원이 아니라
                // 가로로 넓은 창이다
                float2 half_size = float2(0.62, 0.40) * (1.0 - _Coverage);
                float2 d = abs(c) - half_size + 0.08;
                float box = length(max(d, 0.0)) + min(max(d.x, d.y), 0.0) - 0.08;

                float rim = smoothstep(-0.02, 0.03, box);      // 고무 테두리
                float outside = smoothstep(0.0, 0.05, box);

                // 창 안쪽 가장자리는 김이 서린다 — 숨 쉬는 것이 보인다
                float fog = smoothstep(-0.16, 0.0, box) * _Fog;
                float2 blur = float2(1.5 / SAD_REF.x, 1.5 / SAD_REF.y);
                float3 soft = (SadSample(uv + blur).rgb + SadSample(uv - blur).rgb) * 0.5;

                float3 col = lerp(src.rgb, soft * 1.06, fog);
                col = lerp(col, float3(0.05, 0.05, 0.06), rim * 0.85);
                col = lerp(col, float3(0.02, 0.02, 0.03), outside);

                return float4(lerp(src.rgb, col, _Amount), src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
