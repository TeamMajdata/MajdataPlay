# AMF 录制码率检查补丁

`amf-rate-control.patch` 只适用于 `ffmpeg.lock.json` 固定的 FFmpeg `n9.0.1`、commit `bf1b838f2ab88b4f8fd83443325c782ea0e0f7fa`。补丁只修改内部 `libavcodec/amfenc.c`、`amfenc_h264.c`、`amfenc_hevc.c`、`amfenc_av1.c`，不修改公开绑定头文件、AMF 解码器或子模块。

原 AMF 编码包装器对多项 `AMF_ASSIGN_PROPERTY_*` 忽略 `SetProperty` 返回值，`avcodec_open2` 可能成功，而驱动没有接受请求的码率模式或限制。补丁对 H.264、HEVC、AV1 的以下属性检查设置与立即回读，并在 `Init` 和所有后续动态设置完成后再次回读最终值：

- RateControlMethod。
- VBV buffer size 与显式 initial fullness。
- TargetBitrate 与最终 PeakBitrate。
- 显式 EnforceHRD 与 FillerData。

数值属性必须为 AMF `INT64` 且与请求值相等；布尔属性必须为 AMF `BOOL`，非零值按真处理。未知类型、读回失败、驱动更改整数值或设置失败均记录属性名与原因并返回负的 `AVERROR_EXTERNAL`，使 `avcodec_open2` 失败。已有 `FF_CODEC_CAP_INIT_CLEANUP` 与编码器 close 路径负责释放初始化资源。没有将 FFmpeg 的负错误码写入 `AMF_RESULT` 枚举。

只检查调用方实际显式设置的可选项；未知/默认的 HRD、filler、零码率与未指定 VBV 初始状态保留原行为。整数驱动规范化会被明确拒绝，以避免外部属性虚报；布尔值的等价真值可以接受。VBV fullness 的量化沿用原来的 0–64 范围，并将乘法提升到 64 位以避免溢出。AV1 在已写入驱动模式后无条件把上下文改为 CBR 的代码被移除；原有自动选型分支继续工作。

补丁必须保留 LF，只应用于忽略的 `.build/source` 构建缓存；应用前应先检查固定 commit 与洁净状态，第三方绑定与子模块保持不变。补丁只定义内部 encoder helper，不改变公开 FFmpeg ABI；构建系统记录补丁 SHA256，并通过 extra-version 标记说明使用了这一检查。

在未修改的固定源码上检查适用性：

```powershell
$amfPatch = (Resolve-Path Tools/FFmpeg/patches/amf-rate-control.patch).Path
git -C Tools/FFmpeg/.build/source -c core.autocrlf=input apply --check $amfPatch
Get-FileHash -Algorithm SHA256 Tools/FFmpeg/patches/amf-rate-control.patch
```

重建后使用隔离 `.NET 9` 录制验证的 `--encode-hardware` 模式检查实际 H.264 AMF 的 CBR/VBR 打开、码率、像素、PTS 与排空；应同时检查 native 版本标记。`--encode-unchecked-amf` 使用没有此检查标记的旧 AMF 构建，验证拒绝旧实现的诊断、不替换请求格式及不生成/覆盖输出。仅编译不代表驱动上的 GetProperty 或编码已经通过；HEVC、AV1 仍需具备对应格式编码能力的设备单独验证。
