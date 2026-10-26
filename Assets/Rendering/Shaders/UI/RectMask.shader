Shader "Maj/UI/Rect Mask"
{
    Properties
    {
        [PerRendererData]
        _MainTex ("Sprite",2D)="white"{}

        _Color("Color",Color)=(1,1,1,1)


        _Width("Width",Float)=200
        _Height("Height",Float)=200


        _FadeX("Fade X",Float)=20
        _FadeY("Fade Y",Float)=20
    }


    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "CanUseSpriteAtlas"="True"
            "RenderPipeline"="UniversalPipeline"
        }


        Cull Off
        Lighting Off
        ZWrite Off

        Blend SrcAlpha OneMinusSrcAlpha


        Pass
        {
            Name "RectMask"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag


            #include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"



            sampler2D _MainTex;

            float4 _Color;


            float _Width;
            float _Height;

            float _FadeX;
            float _FadeY;



            struct appdata
            {
                float4 vertex : POSITION;

                float2 uv : TEXCOORD0;

                float4 color : COLOR;
            };



            struct v2f
            {
                float4 vertex : SV_POSITION;

                float2 uv : TEXCOORD0;

                float4 color : COLOR;


                // RectTransform local position
                float2 localPos : TEXCOORD1;
            };



            v2f vert(appdata v)
            {
                v2f o;


                o.vertex =
                    TransformObjectToHClip(v.vertex.xyz);


                o.uv =
                    v.uv;


                o.color =
                    v.color * _Color;


                // 这里就是RectTransform本地坐标
                o.localPos =
                    v.vertex.xy;


                return o;
            }




            // RectTransform coordinates can span thousands of pixels; keep fade boundaries in float.
            float AxisMask(
                float pos,
                float size,
                float fade
            )
            {

                float start =
                    min(0,size);


                float end =
                    max(0,size);



                float inside =
                    step(start,pos)
                    *
                    step(pos,end);



                if(fade <= 0)
                    return inside;



                float fadeStart =
                    saturate(
                        (pos-start)
                        /
                        fade
                    );


                float fadeEnd =
                    saturate(
                        (end-pos)
                        /
                        fade
                    );



                return inside *
                    min(
                        fadeStart,
                        fadeEnd
                    );
            }





            half4 frag(v2f i)
                :SV_Target
            {

                half4 col =
                    MAJ_SAMPLE_TEX2D(
                        _MainTex,
                        i.uv
                    )
                    *
                    i.color;



                float maskX =
                    AxisMask(
                        i.localPos.x,
                        _Width,
                        _FadeX
                    );


                float maskY =
                    AxisMask(
                        i.localPos.y,
                        _Height,
                        _FadeY
                    );



                col.a *=
                    maskX *
                    maskY;



                return col;
            }


            ENDHLSL
        }
    }
}
