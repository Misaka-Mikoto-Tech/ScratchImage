Shader "Unlit/PaintOnRT"
{
    Properties
    {
        [PerRendererData][NoScaleOffset]
        _MainTex ("Texture", 2D) = "white" {}
        _BrushAlpha("BrushAlpha", Float) = 1
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }
        ZWrite Off
        ZTest Off
        Blend SrcAlpha One

        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                UNITY_VERTEX_INPUT_INSTANCE_ID
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float _BrushAlpha;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed alpha = tex2D(_MainTex, i.uv).a;    
                alpha *= _BrushAlpha;
                return fixed4(alpha, alpha, alpha, alpha);
            }
            ENDCG
        }
    }
}
