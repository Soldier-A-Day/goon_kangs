Shader "SAD/SH_Vignette_Pulse"
{
    // §9.2 열사병 붉은 맥동 — `_Rate` 0.83Hz (§4.3의 "주기 1.2s"와 같은 말).
    //
    // §5.0 열사병 2단계(수분 ≤10)에서 60초 카운트다운과 함께 뜬다. 심장이
    // 뛰는 속도로 화면이 붉어지는 것이 요점이라 주기를 정확히 지킨다 —
    // 빠르면 경보음처럼 읽히고, 느리면 아무 일도 아닌 것처럼 보인다.
    Properties
    {
        _Amount ("강도", Range(0, 1)) = 0
        _Rate ("맥동 (Hz)", Float) = 0.83
        _Color ("맥동색", Color) = (0.72, 0.22, 0.12, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_Vignette_Pulse"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Amount;
            float _Rate;
            float4 _Color;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 src = SadSample(uv);
                if (_Amount <= 0.0) return src;

                // §4.3 "맥동(0.6~1.0)" — 0으로 떨어지지 않는다. 완전히 사라졌다
                // 나타나면 깜빡임이 되고, 그건 상태가 아니라 오류로 읽힌다
                float beat = 0.6 + 0.4 * (0.5 + 0.5 * sin(_Time.y * _Rate * 6.28318));

                float2 c = uv - 0.5;
                c.x *= SAD_REF.x / SAD_REF.y;
                float ring = smoothstep(0.22, 0.62, length(c));

                float k = ring * beat * _Amount;
                return float4(lerp(src.rgb, _Color.rgb, saturate(k * 0.75)), src.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
