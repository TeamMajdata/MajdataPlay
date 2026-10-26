#ifndef MAJ_MIP_SAMPLING_INCLUDED
#define MAJ_MIP_SAMPLING_INCLUDED

// 纹理质量在自动 LOD 上追加偏移：低 +2、中 +1、高 0。
// 只声明采样参数，不导入矩阵 / UnityPerDraw，允许 UnityCG 的 TMP Shader 独立使用。
// 保留纹理自身的 authored sampler bias；不在 CPU 上重复改写 Texture.mipMapBias。
float _MajTextureMipBias;

// legacy 图像采样保留原自动导数和原 RenderScale 行为，仅追加纹理质量偏移。
// RT / 视频 / SDF / 显式 LOD 采样不应不加区分地替换为此宏；UV 与 bias 保持 float。
#define MAJ_SAMPLE_TEX2D(tex, uv) tex2Dbias(tex, float4(uv, 0.0, _MajTextureMipBias))

#endif // MAJ_MIP_SAMPLING_INCLUDED
