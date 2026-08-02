Shader "SAD/SH_Grayscale_Fade"
{
    // §9.2 후송 · 게임오버 전환 — `_Amount` 0~1.
    //
    // §9.1 `VFX_Collapse`가 "화면 채도 급락 + 방사형 블러 · 3.3 준수 — 유혈 없음"이라
    // 적어둔 것이 여기다. 쓰러지는 연출에 피를 쓰지 않기로 한 이상, **색이
    // 빠지는 것**이 그 자리를 대신해야 한다.
    //
    // 방사형 블러는 화면 중앙으로 빨려드는 방향이다 — 의식이 좁아지는 쪽.
    Properties
    {
        _Amount ("진행", Range(0, 1)) = 0
        _Radial ("방사형 블러", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_Grayscale_Fade"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Amount;
            float _Radial;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_Amount <= 0.0) return SadSample(uv);

                float2 toCenter = (0.5 - uv);
                float3 sum = 0;

                // 6탭이면 픽셀아트 해상도에서 충분히 뭉갠다. 더 늘려도
                // 640×360에서는 보이지 않고 프레임만 먹는다
                [unroll]
                for (int i = 0; i < 6; i += 1)
                {
                    float t = i / 5.0;
                    float2 at = uv + toCenter * t * 0.09 * _Amount * _Radial;
                    sum += SadSample(at).rgb;
                }
                float3 blurred = sum / 6.0;

                float luma = dot(blurred, float3(0.299, 0.587, 0.114));
                float3 gray = float3(luma, luma, luma);

                float3 col = lerp(SadSample(uv).rgb, lerp(blurred, gray, _Amount), _Amount);

                // 끝에서는 완전히 어두워진다 — 정산 화면이 그 위에 올라온다
                col *= lerp(1.0, 0.18, saturate((_Amount - 0.7) / 0.3));

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
