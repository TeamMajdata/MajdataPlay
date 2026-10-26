Shader "Maj/Effects/MaimaiColorEffect"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
		_Saturation("Saturation", Range(0,1)) = 1
		_Brightness("Brightness", Range(0,1)) = 1
		_Alpha("Alpha", Range(0,1)) = 0.5
		_Speed("Speed", Range(0.1, 10)) = 1
		_InnerLB("Inner Lower Bound", Range(0, 1)) = 0
		_InnerUB("Inner Upper Bound", Range(0.0001, 1)) = 0
		_OuterLB("Outer Lower Bound", Range(0, 0.9999)) = 0.9999
		_OuterUB("Outer Upper Bound", Range(0, 1)) = 1
	}
		SubShader
		{
			Tags
			{
				"Queue" = "Transparent"
				"IgnoreProjector" = "True"
				"RenderType" = "Transparent"
				"PreviewType" = "Plane"
				"CanUseSpriteAtlas" = "True"
				"RenderPipeline" = "UniversalPipeline"
			}
			LOD 100

			Pass
			{
			    Name "MaimaiColorEffect"
				Tags { "LightMode" = "SRPDefaultUnlit" }
				ZWrite Off
				Blend SrcAlpha OneMinusSrcAlpha

				HLSLPROGRAM
				#pragma vertex vert
				#pragma fragment frag

			#include "Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/Library/MajCore.hlsl"

			struct appdata
			{
				float4 vertex : POSITION;
				float3 normal : NORMAL;
				float2 uv : TEXCOORD2;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			sampler2D _MainTex;
			float4 _MainTex_ST;
			half _Alpha;
			half _Speed;
			float _Saturation;
			float _Brightness;
			float _InnerLB;
			float _InnerUB;
			float _OuterLB;
			float _OuterUB;

			// hue 的周期运算继续使用 float；clamp 后 RGB 有界，转 half 做颜色计算。
			half3 hsb2rgb(float3 c) {
				half3 rgb = (half3)clamp(abs(fmod(c.x * 6.0 + float3(0.0, 4.0, 2.0), 6) - 3.0) - 1.0, 0, 1);
				rgb = rgb * rgb * (3.0h - 2.0h * rgb);
				return (half)c.z * lerp(half3(1.0h, 1.0h, 1.0h), rgb, (half)c.y);
			}

			float clamp01(float x, float lb, float ub) {
				return (clamp(x, lb, ub) - lb) / (ub - lb);
			}

			v2f vert(appdata v)
			{
				v2f o;
				o.vertex = TransformObjectToHClip(v.vertex.xyz);
				o.uv = TRANSFORM_TEX(v.uv, _MainTex);
				return o;
			}

			half4 frag(v2f i) : SV_Target
			{
				// sample the texture
				half4 col = MAJ_SAMPLE_TEX2D(_MainTex, i.uv);

				float x = i.uv.x - 0.5;
				float y = i.uv.y - 0.5;

				// 径向边界可窄至 0.0001，坐标、归一化除法、角度和时间分段保留 float。
				float radius = clamp((y * y + x * x) * 4, 0, 1) * _Alpha;
				float alpha = 1.0 * _Alpha * clamp01(radius, _InnerLB, _InnerUB) * clamp01(1 - radius, 1 - _OuterUB, 1 - _OuterLB);

				float angle = atan2(y, x);
				angle = angle + step(0, -angle) * 3.14159 * 2;
				angle = angle / 6.28;

				float hsb_h = ceil(fmod(angle * 3 + ceil(_Time.y * _Speed * 5) * 0.2, 1) * 5) / 5;

				half3 color = hsb2rgb(float3(hsb_h, _Saturation, _Brightness));

				half3 albedo = col.rgb;
				half3 diffues = color * albedo;

				return half4(diffues, col.a * alpha);
			}
			ENDHLSL
		}
		}
}
