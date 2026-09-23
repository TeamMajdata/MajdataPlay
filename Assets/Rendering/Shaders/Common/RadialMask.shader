Shader "Common/RadialMask"
{
    Properties
    {
        // RawSpriteRenderer shares a material per texture; textures cannot be instanced.
        _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        
        _Progress ("Progress", Range(0.0, 1.0)) = 1.0
        _Softness ("Edge Softness", Range(0.0, 0.05)) = 0.005

        [Toggle] _InvertMask ("Invert Mask", Float) = 0
        [Toggle] _CounterClockwise ("Counter Clockwise", Float) = 0

        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }
    
    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off Lighting Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing 
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float2 localPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID 
            };

            sampler2D _MainTex;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Progress)
                UNITY_DEFINE_INSTANCED_PROP(float, _InvertMask)
                UNITY_DEFINE_INSTANCED_PROP(float, _CounterClockwise)
                UNITY_DEFINE_INSTANCED_PROP(fixed4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float, _Softness)

                UNITY_DEFINE_INSTANCED_PROP(fixed4, _RendererColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            v2f vert(appdata_t IN)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                
                OUT.vertex = UnityObjectToClipPos(IN.vertex);
                OUT.texcoord = IN.texcoord;
                OUT.localPos = IN.vertex.xy;

                fixed4 color = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
                fixed4 rendererColor = UNITY_ACCESS_INSTANCED_PROP(Props, _RendererColor);
                OUT.color = IN.color * color * rendererColor;
                return OUT;
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float currentProgress = UNITY_ACCESS_INSTANCED_PROP(Props, _Progress);
                float invertMask = UNITY_ACCESS_INSTANCED_PROP(Props, _InvertMask);
                float isCCW = UNITY_ACCESS_INSTANCED_PROP(Props, _CounterClockwise);
                float softness = UNITY_ACCESS_INSTANCED_PROP(Props, _Softness);

                fixed4 col = tex2D(_MainTex, IN.texcoord) * IN.color;
                float2 dir = IN.localPos;

                if (isCCW > 0.5) dir.x = -dir.x;

                float angle = atan2(dir.x, dir.y);
                
                if (angle < 0) angle += UNITY_PI * 2.0;
                
                float normalizedAngle = angle / (UNITY_PI * 2.0);
                float safeSoftness = max(softness, 0.0001);
                float fillEnd = currentProgress * (1.0 + safeSoftness);
                float mask = 1.0 - smoothstep(fillEnd - safeSoftness, fillEnd, normalizedAngle);

                if (invertMask > 0.5)
                {
                    mask = 1.0 - mask;
                }

                col.a *= mask;
                return col;
            }
            ENDCG
        }
    }
}
