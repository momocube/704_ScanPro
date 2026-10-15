Shader "704/Fullscreen Fit"
{
    Properties { _MainTex ("Venue atlas", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _ContentRect;
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = (i.uv - _ContentRect.xy) / _ContentRect.zw;
                if (any(uv < 0.0) || any(uv >= 1.0)) return fixed4(0,0,0,1);
                return tex2D(_MainTex, uv);
            }
            ENDCG
        }
    }
}
