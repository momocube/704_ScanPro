Shader "704/Output Atlas"
{
    Properties { _MainTex ("Editor canvas", 2D) = "black" {} }
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
            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 p = float2(i.uv.x * 2688.0, (1.0 - i.uv.y) * 3840.0);
                if (p.y >= 3328.0) return fixed4(0,0,0,1);
                float2 src = float2(p.y / 6.4, 420.0 - p.x / 6.4);
                return tex2D(_MainTex, float2(src.x / 520.0, 1.0 - src.y / 420.0));
            }
            ENDCG
        }
    }
}
