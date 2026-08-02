Shader "SAD/SH_NightVision"
{
    // §4.3 야시장비 — "#6BFF7A 단색화 + 스캔라인 + 원형 비네트 + 노이즈".
    //
    // 야간 경계근무에서 쓴다. 야간 그레이딩(§4.3 야간 중첩)이 화면을 어둡게
    // 깔아둔 상태에서 이걸 켜면 다시 보이게 되는 것이 이 장비의 전부이고,
    // 그래서 **밝기를 되돌리는 것**이 단색화보다 먼저다.
    Properties
    {
        _Amount ("적용", Range(0, 1)) = 0
        _Tint ("발광색", Color) = (0.42, 1.0, 0.478, 1)
        _ScanDensity ("스캔라인 밀도", Float) = 180
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_NightVision"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Amount;
            float4 _Tint;
            float _ScanDensity;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SadSample(uv);
                if (_Amount <= 0.0) return src;

                // 증폭이 먼저다 — 야간 그레이딩이 −0.90 EV로 눌러둔 것을 되돌린다
                float luma = dot(src.rgb, float3(0.299, 0.587, 0.114));
                float gain = saturate(pow(saturate(luma * 3.2), 0.65));

                float3 vision = _Tint.rgb * gain;

                // 스캔라인 — 가로줄. 논리 해상도가 360이라 밀도를 그보다 높이면
                // 격자가 서로 간섭해 지저분해진다
                float scan = 0.86 + 0.14 * sin(uv.y * _ScanDensity * 3.14159);
                vision *= scan;

                // 증폭관 노이즈. 시간에 따라 지글거려야 살아 있는 화면이 된다
                vision += (SadHash(floor(uv * SAD_REF) + floor(_Time.y * 24.0)) - 0.5) * 0.12;

                // 원형 비네트 — 관 밖은 안 보인다
                float2 c = uv - 0.5;
                c.x *= SAD_REF.x / SAD_REF.y;
                vision *= saturate(1.0 - smoothstep(0.34, 0.56, length(c)));

                return float4(lerp(src.rgb, saturate(vision), _Amount), src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
