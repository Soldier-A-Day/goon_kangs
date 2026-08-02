Shader "SAD/SH_HeatDistort"
{
    // §9.2 — UV 노이즈 왜곡. 혹서 이상 밴드, 그리고 §5.0 열사병 2단계에서
    // `_Strength`가 맥동한다 (0.6~1.0, 주기 1.2s — 맥동은 C# 쪽이 먹인다).
    //
    // 아지랑이는 **지면 근처에서만** 인다(§9.1 `VFX_HeatHaze` "지면 근처 왜곡
    // 마스크"). 화면 전체를 흔들면 더운 것이 아니라 카메라가 고장난 것으로 보인다.
    Properties
    {
        _Strength ("강도", Range(0, 1)) = 0
        _Speed ("속도", Float) = 1.4
        _Scale ("노이즈 배율", Float) = 9
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always

        Pass
        {
            Name "SH_HeatDistort"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "SAD_Fullscreen.hlsl"

            float _Strength;
            float _Speed;
            float _Scale;

            float4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                if (_Strength <= 0.0) return SadSample(uv);

                // 아래로 갈수록 강하다 — 지면에서 올라오는 열이다
                float ground = saturate(1.0 - uv.y * 1.6);

                float t = _Time.y * _Speed;
                float wave = SadNoise(float2(uv.x * _Scale, uv.y * _Scale * 0.5 + t)) - 0.5;

                // 최대 3 논리픽셀. 그 이상 밀면 글자와 얼굴이 무너진다
                float2 offset = float2(wave * 3.0 / SAD_REF.x, 0.0) * _Strength * ground;

                return SadSample(SadSnap(uv + offset));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
