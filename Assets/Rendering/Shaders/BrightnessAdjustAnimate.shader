Shader "Maj/Effects/BrightnessAdjustAnimate"
{
	Properties
	{
		_MainTex("Albedo (RGB)", 2D) = "white" {}
	}

		SubShader
		{
			Tags
			{
				"Queue" = "Transparent"
				"RenderType" = "Transparent"
				"RenderPipeline" = "UniversalPipeline"
			}
			Pass
				{
			    Name "BrightnessAdjustAnimate"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			ZTest Always
			Cull Off
			ZWrite Off
			Blend SrcAlpha OneMinusSrcAlpha
			HLSLPROGRAM
			sampler2D _MainTex;
			half _Brightness;
			half _Saturation;
			half _Contrast;

			//vert和frag函数
			#pragma vertex vert
			#pragma fragment frag
			#include "Assets/Rendering/MajRenderPipeline/Library/MajCore.hlsl"


			struct appdata_t
			{
				float4 vertex : POSITION;
				half4 color : COLOR;
				float2 texcoord : TEXCOORD0;
			};
			//从vertex shader传入pixel shader的参数
			struct v2f
			{
				float4 pos : SV_POSITION; //顶点位置
				// 大皮肤图集的 UV 保持 float，避免移动端半精度造成块状采样。
				float2 uv : TEXCOORD0;
				half4 color : COLOR;
			};

			//vertex shader
			v2f vert(appdata_t v)
			{
				v2f o;
				//从自身空间转向投影空间
				o.pos = TransformObjectToHClip(v.vertex.xyz);
				o.color = v.color;
				//uv坐标赋值给output
				o.uv = v.texcoord;
				return o;
			}

			//fragment shader
			half4 frag(v2f i) : COLOR
			{
				//从_MainTex中根据uv坐标进行采样
				half4 renderTex = MAJ_SAMPLE_TEX2D(_MainTex, i.uv)*i.color;
				// 时间与 sin 的输入保留 float，避免长时间运行时相位丢失；颜色系数使用 half。
				float frames = _Time / 0.0008;
				half brightness = (half)(0.95 + max(sin(frames * 0.2) * 0.65, 0));
				half contrast = (half)(1 + min(sin(frames * 0.2) * -0.55, 0));
				half saturation = 1.0h;
				//brigtness亮度直接乘以一个系数，也就是RGB整体缩放，调整亮度
				half3 finalColor = renderTex * brightness;
				//saturation饱和度：首先根据公式计算同等亮度情况下饱和度最低的值：
				half gray = 0.2125 * renderTex.r + 0.7154 * renderTex.g + 0.0721 * renderTex.b;
				half3 grayColor = half3(gray, gray, gray);
				//根据Saturation在饱和度最低的图像和原图之间差值
				finalColor = lerp(grayColor, finalColor, saturation);
				//contrast对比度：首先计算对比度最低的值
				half3 avgColor = half3(0.5, 0.5, 0.5);
				//根据Contrast在对比度最低的图像和原图之间差值
				finalColor = lerp(avgColor, finalColor, contrast);
				//返回结果，alpha通道不变
				return half4(finalColor, renderTex.a);
			}
			ENDHLSL
		}
	}
	//防止shader失效的保障措施
	FallBack Off
}
