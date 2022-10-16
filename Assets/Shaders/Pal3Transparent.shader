Shader "Pal3/Transparent"
{
    Properties
    {
        _MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
        _TintColor ("Tint color", Color) = (1.0, 1.0, 1.0, 1.0)
        _Threshold ("Transparent Threshold", Range(0, 1)) = 1.0
        
        _HasShadowTex ("Has Shadow Texture", Range(0, 1)) = 0.0
        _ShadowTex ("Shadow Texture", 2D) = "white" {}
        _Exposure("Exposure Amount", Range(0.1, 1.0)) = 0.4
        
        [Enum(UnityEngine.Rendering.BlendMode)]
        _BlendSrcFactor("Source Blend Factor", int) = 5    // BlendMode.SrcAlpha as Default
        
        [Enum(UnityEngine.Rendering.BlendMode)]
        _BlendDstFactor("Dest Blend Factor", int) = 10     // BlendMode.OneMinusSrcAlpha as Default
    }
    SubShader
    {
        Lighting Off
        
        Tags { "Queue" = "Transparent" }
        
        // Pass 2 ,transparent part
        Pass
        {
            Blend [_BlendSrcFactor] [_BlendDstFactor]
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                float2 shadowcoord : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                float2 shadowcoord : TEXCOORD1;
                
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Threshold;
            
            float _HasShadowTex;
            sampler2D _ShadowTex;
            float4 _ShadowTex_ST;
            float _Exposure;

            float4 _TintColor;
            
            v2f vert(appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.shadowcoord = TRANSFORM_TEX(v.shadowcoord, _ShadowTex);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half4 color = tex2D(_MainTex, i.texcoord);
                const float alpha = color.a;
                if(color.a >= _Threshold)
                {
                    discard;
                }
                
                if(_HasShadowTex > 0.5f)
                {
                    color *= tex2D(_ShadowTex, i.shadowcoord) / (1 - _Exposure);
                    color.a = alpha;
                }
                color *= _TintColor;
                return color;
            }
            ENDCG
        }
    }
}