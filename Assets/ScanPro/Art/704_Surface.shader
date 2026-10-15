Shader "704/Cave Surface"
{
    Properties { _MainTex ("Canvas", 2D) = "black" {} _SurfaceTint ("Empty Surface Tint", Color) = (0.12,0.14,0.19,1) }
    SubShader { Tags { "RenderType"="Opaque" } Cull Off
        Pass { CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            fixed4 _SurfaceTint;
            struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
            struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; return o; }
            fixed4 frag(v2f i):SV_Target
            {
                fixed4 surface = tex2D(_MainTex, i.uv);
                // Empty canvas cells are opaque black. Give cave geometry a subtle base
                // tint so blank wall planes remain visible against the clear background.
                if (surface.r < 0.005 && surface.g < 0.005 && surface.b < 0.005)
                    surface.rgb = _SurfaceTint.rgb;
                return surface;
            }
        ENDCG }
    }
}
