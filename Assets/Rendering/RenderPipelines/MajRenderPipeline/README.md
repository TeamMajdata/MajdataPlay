# Maj Render Pipeline 项目资源与集成记录

## 2026-11-12 结算接入与设置审计

`Result` 的两块主底板复用 Board / RT1，FastLate 和 NoteDiffGraph 的小底板复用 Category / RT2；`TotalResult` 的主底板复用 Board / RT1。保留原 Sprite、材质、GUID、布局与业务引用，未增加抓图阶段或工作 RT。普通半透明仍不启用捕获，Game 停止捕获但保留工作池，进入结算后由场景与面板注册恢复。

显式挂载 `UiBackdropPanel` 的独立 Sprite 也可使用烘焙轮廓。两张 Result 底板按实际图形配置裁切圆；TotalResult 的合并底板使用显式 `_liquidArtworkContour`，在编辑器按原图 Alpha 离线生成欧氏距离场，避免用单圆冒充复合轮廓。运行时均沿用同一 RGBA8 图集、一次硬件双线性采样及原解码，不增加实时距离生成。单个轮廓栅格长边上限 1020，源坐标、距离单位和两像素保护边不变；新增轮廓后距离图集为 4096×2048、32 MiB，工作 RT 池不变。修改此类源图或轮廓参数后需重新执行 Bake Glass Optics Atlas。

设置链保持五档 UI 风格独立于 MSAA、场景分辨率；精致档偏好与自定义保存值不相互覆盖，兼容模式仅临时覆盖。修正不支持系统缩放时菜单初始化仍可能吸附、回写保存值，以及 LiquidOnly 控件前景在高斯模式下被单独挪队列的问题；动态 TMP fallback 子材质随父控件同步刷新。清理旧 8192 次查找性能探针及过时实验说明，保留正式回归用例。

本轮仅做源码、资产与 diff 检查，并进行必要的隔离资源烘焙；未运行主工程场景、回归或性能测试。新结算页的视觉表现由用户验证，不将静态审计当作实机验收。

## 当前设置与本轮静态精简

精致档的“底板风格”现改名为“渲染风格”，保留高斯模糊／液态玻璃两个选择；自定义则由单一“UI风格”完整控制：

| 自定义 UI 风格 | 保存值 | 贴图与合成策略 | 背景抓取 |
| --- | --- | --- | --- |
| 不透明 | `None = 0` | 无描边、硬裁切 | 不抓取 |
| 混合透明 | `Performance = 1` | 原透明部分用描边 Alpha，其余保留硬裁切 | 不抓取 |
| 半透明 | `All = 2` | 描边 Alpha Blend | 不抓取 |
| 高斯模糊 | `GaussianBlur = 3` | 派生 All 贴图／合成，再应用高斯背景 | 三阶段抓取 |
| 液态玻璃 | `LiquidGlass = 4` | 派生 All 贴图／合成，再应用玻璃背景 | 三阶段抓取 |

节能／性能／美观分别选择 0／1／2，精致按保存的 `UIBackdropStyle` 偏好映射为 3／4；进入自定义继承当前有效设置。兼容模式临时使用 None，不覆盖保存选择。新／空配置默认美观，各命名预设默认 MSAA 关闭、100% 场景分辨率。保存过明确 `UIOutlineMode = All` 的旧配置保持普通半透明，忽略旧隐藏模糊标志；只有缺少该键的旧布尔配置才按显式 `UIBackdropBlur` 和旧 UI 模式迁移。`UIBackdropBlur` 仅兼容读取，不再写出。完整规则见 [UI 风格](Documentation/UI-Outline-Quality.md)。

前一轮静态精简去除了无 RectMask 变体的多余插值／顶点运算、高斯变体恒 1 的染色插值、死声明和重复表达式，统一了服务配置、资源命名、裁剪矩阵和材质释放，并缓存 Player 排序层名称；没有调整当时的外观配方。随后按用户明确要求，强高斯改为下述 quarter 网格及固定小核。两轮均未运行测试、编译、Unity 操作或 ADB；用户自行验证效果，不宣称已测得收益或绝对最优。

修改前已认可版本的用户实测：Pad 5 Pro 设置页高斯平均约 100 FPS、液态约 103 FPS；小米 15 两者约 121 FPS，高斯 1% low 约 100 FPS、液态约 92～100 FPS。用户确认画面正常、没有可见异常。这是用户提供的既有版本结果，**不是本轮精简后的复测**，也不是功耗或全设备保证。以下带日期的测试与实现记录只适用于各自当时的版本。

## 2026-11-12 Setting 加减按钮与圆外采样（当前）

仅 Setting 中央两只加减按钮将现有 `_liquidRefractionWidthScale` 设为 0.75。在当前同缩放基准下，折射带宽由 47.3985 降至 35.5489，介于顶部未选中分类的 34.56 和选中分类的 37.035 之间；对应模型峰值位移约 14.113。该倍率只缩小这两只按钮的折射范围及相应厚度，不改细高光宽度、颜色、交互、其他角色或 Gaussian 路径；不新增 Shader 字段，不重烘焙 profile / contour。数字描述当前几何与模型，不代替视觉或性能验证。

外围来源消费者已删除圆外原图填色替换：`PvGlass` 直接采样捕获结果，包括早期遮罩写入的黑色，再走原有 ArtworkBlend / tint 混合。不再发布或读取专用圆形投影 globals，不保留该几何判断或专用 `TryCalculateCaptureCircle`。早绘遮罩、Init 全局与 List / Setting 本地重复遮罩，以及保护副屏的 `MainDisplayUiClip` 显示隔离都保留。新修改的验证另行记录，旧补色方案用例不代表当前路径已通过。

## 2026-11-12 外围按键折射的中心衰减（当前）

仅 `UiBackdropPanel.IsButton`（外围八键，且不属于分类）启用 `_LiquidRefractionTail = 0.22`。保留原来的窄倒角宽度 `W = min(20 design units, 0.4Rui)` 及其表面倍率，在原 RGHalf 响应表的位移上补一条向实际可见 body 中心衰减的尾部。尾部终点使用原 Sprite 去除留白和阴影后的 body 最小半径，而不是完整 UI Rect 的 `Rui`，新增尾部在该中心的位移和一阶导数同时归零。复用外围 RT3 本身不等于启用尾部，Setting 加减按钮、分类、大底板和副屏卡片保留各自既有形态。

原位移响应的 `t = saturate(dLogical/W)` 保持不变；尾部结束位置独立使用 `u = saturate(dSource/RbodySource)`，其中 `dSource` 来自原始预计算距离读回及裁切后的 `sourceDistance`，`RbodySource = min(_LiquidGlassSpriteRect.zw)`。设 `q = t³(10-15t+6t²)`，新增归一化位移为 `0.22 × q × (1-u)² × (1-saturate(oldR/0.397008))`。逻辑距离、源 Sprite 距离分别归一化，非等比缩放时不能将二者当成一个 `d/R`。实际 `newui@3x_21` 示例的 UI 短半径为 100 逻辑单位，源 body 半径约 233.33 源像素；直接按完整 UI 半径结束会把留白算入折射。`0.397008` 是基底模型峰值的近似值，半精度 LUT 下不声称峰值逐位完全相等。

原响应表、轮廓图与编码不重烘焙；G 通道、细高光参数、颜色、透明度、阴影和 Gaussian 风格不变。尾部是少量运行时多项式乘加，不是又执行一遍 Snell，也不增加纹理读取、Blit、RT 或捕获切点。缺少有效轮廓／响应数据仍直接无折射。此尾部仍保留；此前独立的圆外原图色替换现已删除，见上方当前说明。不将旧像素精度、设备帧率或功耗数字当成本轮验收。

本轮定向验证：折射尾部 D3D11 五项用例通过，Vulkan 安全离屏真实按钮一项通过，Metal／GLES3 六个代表程序编译成功；透明留白中心归一化修正后，真实按钮中心新旧 RGB 最大差约 0.000214，纯色背景下颜色、高光和 Alpha 对照一致。半分辨率快照另通过 D3D11 三项用例：覆盖六 RT 复用／重建、两风格三阶段独立性、实际 TexelSize、64×48→32×24 及 97×65→49×33 的独立双线性参考。以上是隔离 GPU 和生命周期证据；本轮未重新打包或验证移动设备，未测帧率、带宽或功耗收益。

## 2026-11-12 三阶段菜单背景（当前）

美观、精致预设现在默认关闭 MSAA（场景附件为 1×）；自定义中仍可独立选择 2×／4×／8×，菜单采集相机也遵循该配置，不再暗中强制单采样。过滤和原图快照工作图始终单采样；若场景配置多采样，三个抓取切点仍需要相应 Resolve。它保留三个有序抓取切点及正常输出通路，不宣称零拷贝。

| 来源 | 抓取时机与消费者 | 高斯模糊 | 液态玻璃 |
| --- | --- | --- | --- |
| RT1 | 已绘制背景前缀（PV、绑定蓝环）之后，Live2D、大板及半张歌曲封面之前；主屏大板、副屏用户名条与图表长条、启动板共用 | 1/4 宽高，降采样及双轴固定高斯 | 相同 quarter 高斯，再叠加既有玻璃光学 |
| RT2 | 大板和半张歌曲封面后、分类前；Setting 无歌曲封面也抓取，中央深色值板同样使用此源 | 1/4 宽高，降采样及双轴固定高斯 | 半分辨率快照，一次双线性降采样，不做高斯 |
| RT3 | 分类及其他普通表层 UI 后，外围八键及 Setting 加减按钮之前 | 1/4 宽高，降采样及双轴固定高斯 | 独立半分辨率快照，一次双线性降采样，不做高斯 |

### 强高斯、半尺寸快照与池

强高斯结果及共享 scratch 现使用 `ceil(W/4) × ceil(H/4)`，不设长边 cap，固定 `sigma = 4、radius = 8`。每轴 17 个逻辑 tap 配成 4 对双线性读取，加中心共 9 次；降采样 4 次，加 H／V 各 9 次，一个完整阶段为 22 次按同等 quarter 输出面积累计的采样调用，并非同一线程执行 22 次，也不等同物理带宽。GL 借用 Point 源时降采样的每次线性调用可能展开为 4 次读取，H／V 的自有 Bilinear 输入不展开。

固定核在类型初始化时用纯数学预计算一次，`BuildPanelBlurKernel()` 无尺寸参数，四组权重／偏移由两轴共享；分辨率变化及 Reset 不重算、不清空该 CPU 核，只重新准备目标或清除 GPU 绑定。Shader 使用 `_MajUiGaussianCenterWeight` 与 `_MajUiGaussianPairs[4]`，固定展开四对。旧 half 网格的常规 sigma 8／radius 16、每轴 17 读及随来源尺寸增长的核、1024 参考 cap 均属历史。quarter sigma 4 与旧 half sigma 8 的连续模型都约为 16 个源像素的 sigma，但降采样／重建细节不同，不能声称画质或 PSF 相同；本次由用户自行判断效果。

液态 RT2/RT3 各自只做一次场景前缀 Blit，降到 `ceil(W/2) × ceil(H/2)`，结果使用 Bilinear / Clamp；不增加高斯、小范围滤波或 Mipmap，也不保留 small guard、ROI、核校准或小高斯 scratch。原场景是 Point 过滤时，复制 pass 显式提供线性采样而不改动借用源。玻璃消费端继续使用同一归一化 UV，并按实际结果发布 TexelSize；折射形态和颜色合成不随快照尺寸另作修改。半尺寸会减少细小线条和高频图案的信息，不能保证与原全尺寸快照逐像素相同。

六张持久工作图不增不减：`s_liquidCategoryTexture`、`s_liquidButtonTexture` 保持半宽半高，`s_boardBlurTexture`、`s_categoryBlurTexture`、`s_buttonBlurTexture`、`s_blurScratchTexture` 改为 quarter；均为 RGBA8、单采样、无深度、无 Mipmap。启动预建，风格切换、无可见消费者及进入 Game 只停止采集并清除 Ready；尺寸变化重建，退出或会话重置释放。`s_sourceSize`、`s_halfResolutionSize`、`s_gaussianResolutionSize` 分别保留源、液态快照和高斯网格尺寸，由 `GetSourceSize`、`GetHalfResolutionSize`、`GetGaussianResolutionSize` 计算。三源独立及 scratch 顺序复用保持。

宽高可被 4 整除时，工作图总面积为 `2×N/4 + 4×N/16 = 0.75N`，是上一版全 half 工作池 1.5N 的一半：1600×2560 约 11.71875 MiB，1080×1920 约 5.932617 MiB。任意尺寸严格按 `(2×ceil(W/2)×ceil(H/2) + 4×ceil(W/4)×ceil(H/4))×4` 字节计算。这只描述六张工作图，不代表游戏总显存或带宽减半；场景／MSAA 附件、视频 RT、原图及光学图集不变。实际 GPU 耗时、功耗及画质取舍未测。

全部角色可见时，高斯模式仍为三个切点各降采样＋H＋V，共九次过滤绘制；液态为 RT1 三次强过滤，RT2/RT3 各一次原图复制，共五次背景处理绘制。旧小高斯及其 ROI / guard 测例仅属历史，不再描述当前实现。绘制数量不等同实测帧率或功耗。

没有启用正式 Mipmap 模糊。隔离实验比较了直接 LOD、mip 后重建和少量加权采样：便宜候选改变细蓝环、斜边或 PSF，近似符合参考的 mip1＋完整高斯仍保留完整核，还增加快照与生成 mip 的工作，因此没有选用。mip1 本身就是半宽半高、mip2 就是 quarter；生成和存储并不免费，也不能把 mip 当成等价高斯。旧 quarter 快路径及其方差测试、旧设备帧率属于历史证据，不代表本次半尺寸路径的性能。

### 离线液态光学数据（当前）

`GlassOpticsAtlas.asset` 当前编码为 **v7 RGBA8 UNORM / Bilinear**。2048² 距离图仍为 16 MiB，线性颜色空间、无压缩、无 Mipmap。每个轮廓 tile 单独保存距离范围和最小值；RGB 保存三个单调粗值通道，A 保存细残差。一次原生双线性采样后，以 float 执行 `normalized = dot(rgb, 1/3) + a/765`，再乘该 tile 的 range 并加 minimum，恢复原 Sprite 坐标中的距离。它约有 17.6 bit 的距离离散分辨率，不能因为四通道合计 32 bit 就称为 32 bit 浮点精度。

当前路径使用一次原生采样和线性解码，不再需要 Gather、纹素格点 floor、四次 Point 或手动双线性重建；Shader 目标恢复为 3.0。采样和解码保持 float，GL / GLES 使用高精度 sampler。运行时检查 RGBA8 的 Sample / Linear 能力及 Shader 等级，不再按操作系统或后端白名单拒绝 GL。RGBA8 比 RFloat 线性过滤更普遍，但并不保证所有设备都能运行完整效果：独立的 256×1 RGHalf 光学响应表仍须支持 Sample / Linear。

距离及响应数据均不跟随图片纹理精度偏移，也不提供 UI 颜色。Sprite 路径在缺失、旧版本、不匹配轮廓或无效 range / minimum 时直接无折射；两条光学路径都仍要求原格式、过滤能力及响应表门控通过，不会换成四 Point 或实时 Snell 重算。coverage-only 处理继续保护原图颜色、Alpha、图标和阴影。原 Sprite 切图、九宫格、tile guard、裁圆板先采曲线后组合裁切的顺序，以及颜色、覆盖率、厚度和高光参数保持。通过 Tools → MajdataPlay → Rendering → Bake Glass Optics Atlas 重新生成运行时资源。

List / Setting 未选中的胶囊原本使用无 Sprite 的 `Maj/UI/Rounded Rect`，不属于 Sprite 图集轮廓。该精确源 Shader 仅在 `Image.Type.Simple`、`useSpriteMesh = false`、Rect 宽高为正、非外围按键且没有 ellipse / clipped-circle 形状时，才启用 `_LiquidNativeContourReady`。它复用当前 fragment 为原圆角 Alpha 裁边已经计算的距离、屏幕梯度和像素换算率，继续读取同一预计算光学响应表；不额外计算一次 SDF，不增加轮廓纹理读取、RT 或捕获。原 `roundedCoverage` 只裁边一次，额外 body coverage 为 1，保留原 Alpha、颜色、尺寸及四角半径动画。未知程序化图形、缺图的 Sprite 或不支持的格式／响应仍无折射；这是原生圆角控件的精确轮廓复用，不是通用解析轮廓回退。该修复的 GPU、视觉与设备验证需单独记录。

这次选择明确接受原生过滤的微小精度差，以用户要求的兼容性及原生双线性路径为优先。与相同光学代码的 v6 RFloat / Gather 参考相比，普通 Simple / Sliced / 裁切椭圆控件最大差为 1/255；薄蓝环在 1200／600 输入下最大差为 5/255、4/255，超过 2/255 的像素分别为 33、2 个（每帧 144 万输出像素）。原有最大 2/255 阈值仍记录为失败，不将该选择描述成完全等价或把旧失败改写为通过。v7 功能回归、跨编译和设备验证单独记录；桌面像素差不等于所有平台已实机通过或保证 120 FPS。

v7 编码迁移阶段的功能回归（恢复早期遮罩之前）：隔离 D3D11 EditMode 112/112、采集生命周期 21/21、光学 PlayMode 16/16，Vulkan 安全离屏光学 16/16；Metal／GLES3 共 6 个代表程序跨编译取得非空字节码。Android Release 构建 0 错误，在 Xiaomi Pad 5 Pro／Adreno 650／Vulkan 上确认 RGBA8 Sample／Linear 均可用，Title、List、Setting 冒烟未见黑屏或轮廓异常；配置前后逐字节一致，临时构建脚本已移除。上述功能通过不包含前述薄蓝环严格精度失败项，也不替代后续遮罩、外围采样和按钮范围修改的验证；不代表 Metal／GLES 实机、性能或功耗验证。

RHalf、RFloat v2、RGBA 硬件插值 v3、RGHalf hi/lo v4、RGB24 四 Point v5 及 RFloat / Gather v6 均保留为历史。v6 曾用一次 Gather 取四个原始 float 邻点，再用三次 float lerp，桌面严格对照可匹配原四点参考；它不是 RFloat 硬件双线性。旧 R16_UNorm 原生双线性薄蓝环差 13/255、RFloat 原生双线性差 3/255，属于此前按严格预算未采用的候选。v7 不是复用这些旧测试的精度结论，也不从采样指令减少推导显存带宽按相同比例下降。
Calibrator 的旧 LiquidOnly 配置已对齐现有角色：中央大板在两种风格都读 RT1，原图／透光配比与 Setting 主板一致；两只外围按钮读 RT3，原有图标子节点作为清晰前景。没有改原 Image、RectTransform、Sprite 或材质 GUID，也没有给该场景虚构不存在的蓝环绑定。

### 保留的外观与排序

颜色仍来自原 UI 贴图及 Graphic，不写死黄色、白色或蓝灰调色板；原图／处理后透光的配比、主体覆盖率、柔边、阴影与最后的 Max 高光不变。UserInfo 装订环的原图保护、搜索确认按钮读 RT3、Setting 深色板读 RT2／加减按钮读 RT3、副屏两板读 RT1 的规则保留。高斯模式直接采样过滤结果；液态模式在同一来源上添加原有折射与反光。

绘制区间为：圆外 Opaque 遮罩恢复原来的 2000 队列；背景及蓝环至 2899，大板 2900～2949，半张歌曲封面 2950～2989，分类 2990～2999，普通 UI 3000～3499，外围按钮 3500，清晰符号从 3501 起，转场 4000、传感器调试 5000。Alpha 遮罩恢复 Default 的原 Shader 队列（Transparent，实际 3000），保留原 Alpha 混合与 Canvas 内部排序。蓝环必须先进入 RT1，不能重新改为只取解码 PV。

Init 的持久全局 Mask 保持启用，使用原 Sprite 轮廓网格；List 和 Setting 的本地 CircleMask Image 也保持启用，按用户要求保留全局与场景本地两层遮罩及其重叠绘制。GameObject、Sprite、材质和序列化引用保持，单独预览这些场景仍有本地遮罩。前一轮只停用本地 Image、依赖全局遮罩的选择已撤销；其他场景的 Alpha 遮罩同样保留。传感器继续使用最上层队列及 Always 深度测试。

抓图内容保持原样，不对整张 RT 补边或换色。外围来源的 `PvGlass` 消费者（包括复用 RT3 的 Setting 加减按钮）现直接读取捕获中的圆外黑色，不按几何位置或 RGB 改写采样色；仍执行原有 ArtworkBlend / tint、装饰和高光。专用圆形投影、发布／绑定／清理 globals 和消费者替色判断均已删除，`MainDisplayUiClip` 对主屏 UI 与副屏的显示隔离继续保留。

复制、降采样及高斯 H／V、六张持久工作图、三次有序抓取和正常相机输出均不变，不新增 RT、Blit 或纹理读取。早期深度写入继续给后续受深度测试约束的圆外片元提供提前拒绝机会，实际 GPU 收益仍须测量，不能由队列或 Draw 数推算帧率和功耗。

已撤销圆外替色方案的历史验证：隔离 Unity 6000.3.17f1／D3D11 共 122 个唯一 EditMode 用例、40 个 PlayMode 用例通过；Vulkan 安全离屏 3 项通过，Metal／GLES3 六个代表程序跨编译成功。包含早期深度遮挡与关闭遮罩的负对照、旋转／偏移／镜像投影、当时的两风格圆外原图色、圆内黑色保留、有色 Graphic 仅乘一次，以及六 RT 池和 Ready 生命周期。Android 非开发构建 0 错误。这些记录保留其当时含义，不作为当前直接读取圆外颜色及 Setting 按钮范围修改的通过结果，也不能推导性能收益。

历史方案曾将两种遮罩都推迟到 4500，以避免抓图含黑色，但牺牲了早期深度遮挡；随后恢复早期队列并加入消费者几何替色，后者现也已删除。当前保留原来的早期队列、全局与 List／Setting 本地两层遮罩，不宣称减少重复绘制。旧晚遮罩、整图补边、消费者替色及停用本地 Image 的验证只描述各自当时的状态。

此前 v6 阶段的定向验证通过 D3D11 两批 112/112 与 38/38、Vulkan 17/17、离线 5/5；这些历史批次不作为 v7 重新验证的计数，也不等于所有平台或设备已实机通过。目标设备帧率与功耗单独报告，不沿用旧 quarter、4× 菜单或旧光学编码的测试数字。

## 2026-11-10 底板风格：高斯模糊 / 液态玻璃（三阶段前历史）

液态玻璃现为 Maj 自有实现，已移除之前直接移植的 Shader include。参考项目的分层思路保留为设计参考，当前不再使用它的圆弧镜头公式、振彩、像素取整细描边和片元九点模糊；来源及演变见[设计记录](Documentation/LiquidGlass-Design.md)。

首层背景在两种风格下都按表面角色分源，外围后层则保留各风格对应的模糊强度。首个背景切点可生成两份独立结果：半分辨率降采样 / 横向 / 纵向强高斯，供显式 `_useStrongBlur` 的六块大板使用；全分辨率横向 / 纵向小高斯，供横排分类及其他普通小表面使用。当前命令流先录制强高斯、再录制小高斯，两者读取同一份场景前缀并均在首层 UI 前完成，不依赖彼此结果。六块分别是 List 主板、Setting 主板及下方白条、搜索主板、顶部用户名卡和图表白卡；路由标记不覆盖原风格开关，Setting 白条在高斯风格下仍按 LiquidOnly 回退原 Alpha。Setting 中央深色 OptionBG、Practice / Calibrator 的 LiquidOnly 大板不扩大路由，液态时仍用全分辨率小高斯，高斯风格仍回退原 Alpha。外圈八键保留后一个切点：液态玻璃用已完成的普通 UI / 分类生成全分辨率小高斯；高斯风格在 3000～3499 队列消费完首层结果后，复用半分辨率一对生成强高斯。

全分辨率小高斯保留参考短边 1080 时 sigma 1.5 的归一化整数纹素核、相邻双线性合并、半径 2～8 及 sigma 上限 8/3；权重只在尺寸改变时计算。半分辨率强高斯保持旧高斯风格的屏幕空间强度，不把减半的纹理尺寸误当成模糊强度减半。玻璃片元按角色读取对应结果；没有逐控件抓取或片元九点补模糊，也没有深度或 Mipmap。

该历史阶段采用过内缩 Gaussian 峰及先 Max、再平均像素内四点的反光处理；其中的亮度门槛会削弱浅色表面的细光，现已由上方当前贴边实现替代。两版都只增加算术计算，不增加纹理读取，不向整数像素取整，也不强制一像素宽。UI 最终仍用预乘 Alpha Blend，遮挡、软边和淡出不变。液态模式在完成透射处理后混入原图色：List、Setting、搜索背景板按美观档实际约 42.3% 校准；顶部两张长条白卡为 50%；其余底板、分类和按钮为 25%。装饰再重建，细高光最后合成。

修正了大卡片没有高光的几何原因：五处黄色大板使用“大圆与上下水平裁切区相交”的真实轮廓；副屏两张白卡按实际主体内边距及圆角计算，排除装订环。原始 Sprite UV 轮廓结合实际导数转换到 UI 空间，兼容九宫格拉伸，覆盖率仍由原图 Alpha 决定。用户名卡片略斜的顶边仍属圆角矩形近似。

现有 36 个表面的开关、排序和前景符号保持：背景先画，六块大板读取首层半分辨率强模糊，分类和普通小表面读取同一背景切点的全分辨率小模糊，外围八键读取后一个切点的结果，液态为全分辨率小模糊、高斯风格为半分辨率强模糊；转场和传感器继续按既定顺序覆盖。十九个 LiquidOnly 表面只在液态模式参与，高斯风格保持原 Alpha 回退。分类和外圈按钮在两种风格下统一使用贴图驱动的透射乘色与原图占比；大板在高斯风格下仍保留原 `_tintStrength` / `_frostStrength` 颜色策略。

当时的“底板风格”仅在精致档、未开启兼容模式时显示，选择独立保存。当前名称已改为“渲染风格”，并由顶部五档 UI 风格规则统一派生捕获状态；此段其余内容保留为三阶段前的历史实现说明。

工作纹理按需保留一对全分辨率 RT 和一对半分辨率 RT，均为无深度、无 Mipmap 的 RGBA8。首切点先录制强高斯的降采样及两轴过滤共三次，再录制小高斯的两次绘制。液态外圈复用全分辨率一对执行两次小高斯，完整菜单合计七次；高斯外圈复用已经消费完的半分辨率一对执行降采样及两轴强高斯，合计八次。不额外执行场景 Copy，也不增加 RT。高斯外圈发布半分辨率结果为按钮来源，同时清除首层 / 大板 Ready，避免后续误读已被覆盖的阶段；只有高斯外圈可见时仅需半分辨率一对。四张纹理在整除尺寸下合计约 2.5 倍屏幕像素，未包含 MRP 自身为可采样场景颜色保留的附件；不是两个切点各分配一套纹理。可见表面只需一种来源时只分配对应一对。实际性能和功耗仍需测量，不能从绘制次数或片元采样次数直接推断省电。

前一版双源路径在隔离 Unity 6000.3.17f1 / D3D11 中通过 EditMode 27/27、PlayMode 2/2，均无跳过：包含实际 GPU 双源选择与独立 Ready 回退、两风格按钮同色、六面资产路由、双轴强模糊、后层复用不覆盖半分辨率结果，以及风格 / 尺寸变化时四张 RT 的释放。命令见 [UI 验证说明](../../../../Tools/Tests/Modules/UIOutlineValidation/README.md)。该版本后层仅复用全分辨率一对；这些结果不覆盖本次高斯外围改用半分辨率强模糊的修复。

本次高斯外围修复另通过隔离 Unity 6000.3.17f1 / D3D11 定向 PlayMode 3/3，零失败、零跳过：覆盖后层两风格分流、仅按钮 / 仅分类的按需分配、纹理复用、尺寸变化与释放。受控反相 XOR 背景中，高斯后层使用 32×24 半分辨率强模糊，液态后层使用 64×48 全分辨率小模糊；高斯覆盖已消费的半分辨率结果并清除旧 Ready，首层全分辨率内容保持。报告为 `gaussian-peripheral-strong-play.xml/log`，复现命令见 UI 验证说明。未操作主 Editor；主菜单外观、移动实机和功耗未验证。

历史单源路径验证：Unity 6000.3.17f1 / D3D11，定向 EditMode 50/50、PlayMode 27/27 通过且无跳过，包含 9 项实际 GPU 用例。覆盖深浅按钮的中性高光与乘色独立性、分数缩放连续性、裁圆大板水平边和圆弧、副屏白卡源 UV 与九宫格、同分辨率高斯能量和边界，以及双阶段 RT 复用与释放。执行 `ValidateLifecycle.ps1 -Mode All -Graphics`，限定相应渲染与资源用例；不代表完成主菜单或移动端视觉验收。 后续完整贴图乘色、去白雾和 Max 合成调整另复测 9/9 项 GPU 用例通过：包含低亮边缘目标不改变底色、先 Max 后像素平均保留浅色细反光，以及亮灰背景下不截成纯白。

## 2026-11-10 按钮折射范围与副屏白卡

独立的 `_liquidRefractionWidthScale` 在既有范围之后相乘：List、Setting、SortFind 三个界面的大底板设为 2；顶部 UserInfo、UI/ChartVisualDisplayer 两张长条白卡设为 1.5；2026-11-12 起，仅 Setting 中央两只加减按钮设为 0.75，见当前说明。其余表面默认 1，包括外圈八键、其他小按钮、Practice 与 Calibrator 大板。颜色混合、细高光宽度及抓取方式不随此倍率改变。

19 个横排分类及其他圆形、胶囊小按钮使用按短半径缩放的曲面折射区，宽度为短半径的 0.90，不再受原先 20 design units / 0.40 短半径的窄边上限限制。这些非外围控件的曲面和位移在内端平滑归零，中心仍留出 10% 短半径的平坦区；实际弯折会明显进入约三分之一短边的位置。外圈八键的基底倒角仍受原窄带限制，但 2026-11-12 起另有向中心衰减的位移尾部，见当前形态说明。倒角加宽不改变细白高光的宽度，也不增加抓取、RT 或纹理采样。大底板及带原图内边距的副屏卡保留原有的有限倒角范围。

副屏 UserInfo 与 UI/ChartVisualDisplayer 两张长条白卡设置 `_liquidArtworkBlend = 0.5`，将实际原图色与完成乘色、折射和宽反光的背景各取一半。它替代了此前 18% 的液态白幕，不再额外叠白。当前原图填色是白色，所以产生半白的透光效果；更换贴图颜色也会跟随。文字、装订环、蓝框、阴影和相对显隐动画保留，主体覆盖率及高斯风格的 `_frostStrength` 不变。

## 2026-11-10 液态底板颜色分层（当前）

美观档的 22 号大底板使用描边图的 `(255,249,231,127)` 与 Image Alpha `0.85`，原 Alpha 合成权重约为 42.3%；白背景结果约为 `#FFFCF5`。液态模式以 `_liquidArtworkBlend` 控制原图色占比，以 `_liquidTintStrength` 控制透射部分的 RGB 乘数；两者不改变主体覆盖率或细高光宽度。

| 表面 | 原图色 / 已混合透光 | 透射自身乘色强度 | 额外透射来源乘色 |
| --- | --- | --- | --- |
| List、Setting、SortFind、Calibrator 背景大板 | 42.33333% / 57.66667% | 0，避免重复染黄 | 无 |
| Practice 大底板 | 25% / 75% | 0.23 | 无 |
| 副屏用户名及图表白卡 | 25% / 75% | 0.42，白色填充保持中性 | 无 |
| Title 加载长条 | 42.33333% / 57.66667% | 0 | 无 |
| Setting 中间深色板、下方小白板 | 25% / 75% | 0.9 | Setting 大底板原填色的 0.14 |
| 外圈八键及其他分类、按钮 | 25% / 75% | 0.9 | 无 |

透射乘数为 `lerp(1, ownFill * ownGraphic, ownStrength) * lerp(1, sourceFill * sourceGraphic, sourceStrength)`，最终主体颜色为 `lerp(completedTransmission, ownFill * ownGraphic, artworkBlend)`，在当前渲染工作色空间计算，之后再重建原图装饰和细高光。List、Setting、搜索及校准背景板的权重直接取 `127/255 * 0.85 = 0.4233333`，并将自身透射染色强度设为 0，避免相同黄色算两遍。它们因此使用 `0.4233333 * 原图色 + 0.5766667 * 已处理背景`，与美观档保留同样的原图/背景颜色权重；区别在于背景经过了模糊、折射与玻璃反光处理，不能据此声称所有像素视觉完全相同。Practice 仍保留之前的 25%/75% 与 0.23 乘数。普通按钮在白底上的等效原色权重为 0.925，比美观档略浅。小板来源系数 0.14 只作用于透射部分，不重复染色原图层。所有颜色仍来自实际贴图，不保存黄色、白色或蓝灰色调色板。

Setting 大小板是同级对象，共用大板绘制前的背景抓取，所以使用显式 `UiBackdropPanel` 引用传递颜色来源。源纹理取 `QualityUiImage.mainTexture`，原 Sprite 只提供成对图集的切片 UV；源 Graphic 和材质色同步到实际绘制材质及 Stencil 副本。缺失、禁用或自引用的来源退回中性乘数。装饰分离仍使用原填色，不使用削弱后的乘数。

来源填色只在需要继承的表面顶点读取，新增一个条件顶点纹理采样和一个颜色插值量；这项颜色继承本身不新增 RT、Blit、抓取或片元背景采样。分类和外圈按钮在高斯风格下也使用相同的贴图乘色 / 原图占比，大板高斯风格继续使用原 `_tintStrength`、`_frostStrength`。双源资源成本以上方当前路径为准，验证范围见[设计记录](Documentation/LiquidGlass-Design.md)。

## 2026-11-10 高斯玻璃颜色自适应（实现历史）

本节保留首次自适应乘色、原两张 RT 资源及当时验证的记录。当前高斯大板仍使用 `_tintStrength`、`_frostStrength`；分类 / 外圈按钮颜色和双源过滤以最上方当前说明为准，以下旧资源数量与测试结果不代表当前路径。

高斯风格统一从实际 UI 贴图和控件颜色取得乘色，不保存黄色、灰蓝色等 RGB 调色板。其颜色策略仅使用 `_tintStrength` 与 `_frostStrength`；换色后的 UI 图集可直接改变玻璃颜色。分类与外围八键提高乘色强度，接近美观档原图的深浅；副屏白板使用较少的中性白雾，让模糊 RT 的细节贡献更多。

Shader 在顶点阶段对实际 `_MainTex` 的切片内部中心作一次 LOD0 采样，取得当前描边图的填色，供装饰分离和乘色共用；程序圆角使用白纹理与控件本身的颜色。原图与描边图的奶黄色中心 RGB / Alpha 不完全相同，不能把另一张图的固定 RGB 当作装饰分离基准。底色乘数为 `lerp(1, textureFill * GraphicRGB, tintStrength)`，玻璃 RGB 为 `(blur * (1 - frost) + frost) * 底色乘数`。插值只生成乘数，不把模糊结果向黄色或灰蓝纯色混合。

| 表面 | 乘色强度 | 中性白雾强度 |
| --- | --- | --- |
| List / Setting / SortFind 淡黄色大板 | 0.4 | 0.08 |
| 副屏用户名、图表白板 | 1 | 0.02 |
| List / Setting 分类与外围八键 | 1 | 0 |

主体覆盖率、柔边、阴影、相对显隐动画、模糊半径和捕获顺序不变。继续复用两张工作 RT 与原有两次捕获；没有新增 RT、Blit 或片元纹理采样。新增成本是每个顶点一次一致的纹理读取、倒数计算及插值数据传递，不能描述成零开销。

乘色会按通道衰减背景，仍保留模糊细节；例如深色 20 号按钮在强度 1 / 白雾 0 时仅保留约 31%～39% 的通道亮度。它因此显得更深，不是主体 Alpha 或背景抓取失效；是否进一步减弱乘色属于独立的外观调校。

本次验证：Unity 6000.3.17f1 / D3D11 定向 EditMode 36/36（34 项资产、1× / 4× 两项 GPU）与完整 UiPvBackdropLifecycle 15/15 通过，无跳过；独立 Vulkan Shader 像素测试 2/2 通过。外部实际 MRP / D3D11 原生 UGUI 图集测试 1/1 通过，内部覆盖 sprite6 Sliced、20 / 21 / 22 Simple × 实际场景附件 1× / 4× 共八组，CanvasRenderer 输出与独立取样的底色乘模糊背景一致。

实际 MRP / Vulkan 外部测试未完成：断言和读回验证之前先出现 `Missing Vulkan framebuffer attachment image?`，随后进入原生 `GfxDeviceVK::ReadbackImage` 栈，未生成结果 XML；隔离夹具与后端限制尚未区分，不能据此声称 Android / Vulkan 集成已验证。未测完整主菜单外观、移动实机或功耗，主项目临时探针脚本已清理。之前显式保存 `#FFFDF5` 的短期修正及以下各日期记录中的固定色、Alpha 混色比例均属于实现历史，以本节为准；历史捕获和性能测量保留原适用范围。

## 2026-11-10 List 背景封面纳入首层捕获

真实主 Editor 的 Frame Debugger 暴露了首层来源遗漏：Event 55 的 Gaussian Vertical 输出为 512×683，只包含默认白底花纹、蓝环和 Live2D；最终 Event 121 可见的大幅卡通图并不在其中。该图不是视频解码输出，而是 List 的 `Background/SongCover`，由 `CenterCoverDisplayer` 更新当前歌曲封面及 0～0.3 的渐显。其原生 Image 使用 `[UI]BlurCircleFade.mat`，原队列 3000，使它在首层抓取之后才绘制。此前玻璃实际模糊了不完整的背景。

修复只把这一张背景封面接入 `QualityUiImage._captureBeforeBackdrop`，保留同一 Image fileID、Sprite、源材质和动画。仅背景模糊启用时，用自有材质副本把队列改为 2999；关闭时恢复原材质和原顺序，不永久修改共享材质，也不移动其它封面。首层因此包含背景 PV、List 背景封面和蓝环；分类、大底板和副屏复用板共享它。原 Shader、染色和模糊参数不改，没有新增捕获、Blit 或 RT。

验证：Unity 6000.3.17f1 主 Editor / D3D11 重新从 Init 运行后，Frame Debugger Event 56 的首层 Gaussian 结果已包含大封面，最终分类能呈现该封面的颜色变化；背景阶段由 1 次绘制变为 2 次，后续普通 UI 少 1 次，总绘制仍为 121，捕获仍为两次。新增资产 1/1、PlayMode 材质切换与回收 2/2、实际 MRP GPU 1/1（内部 1× / 4×）通过；GPU 负对照的分类图案响应为 0，修复后为 0.105882，并确认同两张 RT 复用、关闭效果恢复原路径。完整 Core 三组平台定义编译通过。此前约 66.25% / 47.5% 的均匀背景响应只证明染色比例；本次另验证背景来源。临时主项目探针已删除，移动实机、Vulkan、GPU 耗时和功耗未测。

## 2026-11-09 横排分类复用首层模糊

List / Setting 的横排分类现在和白色、淡黄色大底板共用第一份背景模糊。绘制顺序为：背景 PV、启用时前置的 List 背景封面与蓝环 → 首次抓取 / 滤波 → 大底板和横排分类 → 外围按钮抓取 / 滤波 → 八个外围按钮 → 转场 → 传感器。分类仍保留自己的 3300 队列和原生内部顺序，只是不再为它们重新采集已完成的大底板。

移除了分类捕获接口、回调、独立 Ready 标志及对应的 MRP 绘制切点。完整菜单由三次捕获、九个低分辨率滤波 Pass，降为两次捕获、六个 Pass；仍顺序复用原两张无深度 / 无 Mipmap 的 RGBA8 工作 RT。只有分类可见时也能请求第一层捕获；副屏的复用板仍不能单独触发它。外围八个圆按钮保留后一次捕获，以便看到已完成的前景界面。

分类在自身片元内对首层 RT 做九点加权采样，步长为 8 个模糊纹素，不增加 Blit、RT 或 Mipmap 生成。分类底色的混合占比使用 a×0.75：未选中为 0.3375、选中为 0.525，分别保留 66.25% 和 47.5% 的模糊背景贡献，并混入 6% 淡奶色。此前 a+(1-a)×0.25 将选中背景压到 22.5%，叠加强模糊后近似实色，已撤销该增色方式。它只改变 RGB 混合；主体覆盖率、透明边缘、阴影、选中动画、文字和图标保持原有机制。

分类通透度补测使用真实选中 sprite6 / Default 材质和 0.7 染色、未选程序圆角和 0.45 染色，静止 Image alpha 均为 1。D3D11 1× / 4× 下，黑白背景变化传到分类中心的实测系数为未选 0.662745、选中约 0.475；Ready0 时背景响应为零并匹配原 Alpha 回退。此前统一用 0.45、部分控件 alpha 为 0.6 的测试不足以覆盖真实静态选中态，已修正。这些是受控背景响应测试，不验证主 List 首层包含全部真实背景对象；其来源盲区见上节。

List 左右导航箭头由 ±108 收到 ±96 个逻辑 UI 单位，保留独立 3301 队列、按下反馈以及切换时淡出 / 淡入。

近期代码审查另修复了 ChartVisualDisplayer 自有 1×1 占位纹理未释放，保留缓存提供的借用图表纹理；删除分类组件一个未使用字段；共享菜单按钮在高亮程度不变时只更新位置，跳过重复外观写入。其余相关材质所有权、TMP 子网格、切换和禁用路径未发现有实际调用证据的新增阻塞问题。

封面来源修复前验证：Unity 6000.3.17f1 窄 EditMode 6/6、PlayMode 4/4、真实 MRP D3D11 GPU 2/2（包含 1× / 4× 附件）通过；确认首层 / 按钮各一次、分类零次、同一对 RT 跨阶段与跨帧复用、缩放重建和 Ready0 回退。阶跃图案的 10–90% 扩散由基础模糊的 39px 增至分类的 72px，而第一张模糊纹理不变。占位纹理所有权 1/1、菜单 / 分类 19/19 通过；MRP Runtime 与完整 Core 的 Windows Editor / Android Editor / Android Player 定义编译通过。它们没有覆盖真实 List 背景封面的捕获归属，也不证明移动实机或功耗。以下旧记录是实现演变，若存在区别，以最上方当前说明为准。

## 2026-11-09 List 与 Setting 共用分类轮播

List 的每个分类池条目现在直接复用 Setting 的 `MenuTitleDisplayer`。分类底板、名称和类型图标作为同一条目平移；靠近当前游标时，按钮展开、切换蓝色选中底板并高亮文字。已删除原来固定在中间的长条及重复的名称 / 类型图标，不再将移动项送到另一层中央 UI 下面。左右箭头保留为独立 Navigation 组，沿用原按键反馈，位于展开后分类按钮的两端。原集合切换输入、类型图标、收藏夹、排序、难度选择和对象池继续由原管理器负责。

位置、宽度、选中颜色和文本缩放使用 Setting 同一套实现。移除 Main Prefab 中 9 个池实例的旧 100×100 根尺寸覆盖，避免它们抵消共享组件所需的 147.83×76.8 未选中尺寸；选中尺寸为 254.69×82.3。非当前项保持正常可见，只在可见范围外关闭对象，不再按屏幕位置或与长条的重叠程度渐隐。

此前为中央长条遮挡增加的全局范围发布、Shader 参与标记、额外插值和片元计算、遮挡材质副本及 `CollectionOcclusion.hlsl` 已全部删除。两个分类底板继续消费已有分类模糊采样，文字和图标保持清晰；没有新增 RT、抓取或模糊阶段。背景模糊和全局 UI 模式继续按当前画质设置生效。导航箭头使用独立的原生 UI Alpha 材质（3301），在分类玻璃（3300）之后、转场（4000）之前绘制；不参与分类图集质量替换，因此不会丢失这个前景队列。 Navigation 自己的 CanvasGroup 只控制两个箭头：沿用分类切换的同一段 250ms 时间轴，前 50ms 从当前透明度淡出，中间 150ms 隐藏，最后 50ms 淡入。分类游标保留原 OutQuad 曲线，箭头使用 SmoothStep；快速反向从当前状态接续，禁用时取消动画并恢复箭头，重新启用后归一化分类位置，避免遗留半透明状态。立即定位和首尾瞬间环绕不播放额外闪烁。

验证：Unity 6000.3.17f1 隔离 List / Setting 业务 70/70、分类资产 5/5、独立导航箭头资产 1/1、D3D11 分类 / 按钮生命周期 4/4 通过。完整 Core 的 Windows Editor / Android Editor / Android Player 定义编译通过。业务测试包含预期的场景切换替身异常，不能描述为日志完全无异常。轮播实际外观由用户确认；本次恢复箭头未追加主场景截图、移动端实机或性能测量。

箭头动画追加验证：同时间轴透明度与游标曲线、连续反向、禁用 / 重新启用、立即定位 / 环绕 / 重复选择等 4 项定向业务测试，以及 Navigation CanvasGroup 与管理器绑定的 1 项资产测试通过。此次未重新运行上述 70 项，也未追加主 Editor 画面或实机验证。

## 2026-11-09 副屏白板共享已有模糊

副屏用户名标签和图表白底板分别在 `UI/UserInfo.prefab` 与 `UI/ChartVisualDisplayer.prefab` 的原 QualityUiImage 上接入玻璃材质。两张原图的主体填色均为不透明白色，使用 92% 第一次模糊 RT + 8% 原白色染色；主体覆盖率为 1，保留原九宫格、蓝色边框、柔边和阴影。头像、姓名文字、图表和提示图标继续清晰绘制，不进入第一次背景采样。

两块板标记为 `_reusePanelCaptureOnly`，只消费主底板已请求的第一次模糊结果，不请求新的过滤、附件或 RT。没有第一层捕获时按 Ready0 恢复原 Alpha；单独留在游玩 / 结算界面时不会因此启用背景模糊。最后一个捕获发起者退出时释放两张工作 RT，即使副屏复用板仍可见。分类和外围按钮的采样顺序不变。

图表加载、帮助和错误状态的 1×1 占位图改为透明，底色统一由父白板负责，防止占位白矩形盖住玻璃。原提示图标及正常透明底图表保持。旧 `Assets/Prefabs/ChartVisualDisplayer.prefab` 不新增玻璃消费者。

验证：副屏资产 3 项、复用 / 释放生命周期 1 项、图表业务 PlayMode 6 项通过；实际 MRP D3D11 GPU 组通过 1x / 4x，加入两板后第一次捕获仍每帧一次、分类 / 按钮捕获不增加、工作 RT 实例不变。真实 Sprite 与相同比例的九宫格验证 92:8 主体合成、清晰前景不进入模糊源，以及只剩复用板时的 Ready0 原 Alpha 回退和释放。完整 Core 三组 Windows Editor / Android Editor / Android Player 定义编译通过。未操作主 Editor 或真机，未验证完整菜单、Vulkan 或功耗。

## 2026-11-09 玻璃主体覆盖与染色分离

“更通透”表示减少纯色染色、更多显示模糊后的背景，不表示降低玻璃主体的最终 Alpha。此前将底板 Image alpha 降至 0.85、玻璃参考透明度仍为 1，导致主体混回 15% 未模糊的原画面，削弱了磨砂效果。

List、Setting 和搜索界面的三块大玻璃底板现将 `_referenceOpacity` 与原 Image 的静止值 0.85 对齐。原图约 128/255 的主体 Alpha 归一化为完整 RT 覆盖，原图柔边随原 Alpha 连续过渡到零；新增阴影仍保留独立 Alpha。主体颜色仍为 92% 模糊 RT + 8% 淡奶黄色，染色比例不再借最终透明度调整。Image / CanvasGroup 动画相对静止值正常淡入淡出。

Image 本身的 0.85 不变，关闭模糊时沿用原 Alpha 合成；小白板、未启用玻璃的界面、分类和外围按钮的参考透明度不变。模糊核、采样顺序、两张工作 RT 和渲染 Pass 数量均不变。运行中的旧缓存材质须通过停止并重新进入 Play / 重新打开界面重建。

验证：三块真实资产的绑定检查 3/3、原生 UGUI 材质 / 淡出生命周期 1/1、实际 MRP D3D11 的 1x / 4x GPU 组通过。高频背景对照中，旧主体与模糊 RT 的最大通道差为 16/255，修正后该测点为零；柔边、Image / CanvasGroup 半淡出及组合四分之一淡出、独立染色和 Ready0 原 Alpha 回退通过。完整 Core 在 Windows Editor / Android Editor / Android Player 定义下编译通过。未运行本轮完整菜单、移动实机、Vulkan 或功耗测试。

## 2026-11-09 分类完整层与三次背景采样修复（历史）

上一轮将分类池底板和所有文字分别移到 3500 / 3501，遗漏 List 中央独立选中蓝底，破坏了中央蓝底遮住池中当前项的原生顺序，出现灰色小胶囊浮在大蓝胶囊上的回归。分类与外围按钮共用同一次采样也使外围磨砂背景缺少分类内容。孤立分类控件的验证未覆盖这一完整层级关系。

当前按「大底板 → 完整分类层 → 外围按钮」分别采样。分类底板、中央选中蓝底、文字、箭头、图标和 TMP 子网格统一进入 3300 队列，在内部沿用原 Canvas / Z / 层级顺序；分类绘制完成后，才为 3500 / 3501 的外围按钮抓取背景。Setting 选中蓝灰分类增加原色占比，保留白字对比。4000 转场和 5000 传感器仍在三次采样之后。

新增独立 `IMajUiCategoryBackdropCapture` 可选接口和分类 Ready 标志，旧单 / 双阶段消费者沿用原路径。三次采样顺序复用原两张小 RT，不新增常驻背景纹理；全部活跃时滤波从六个 Pass 增至九个，无可见消费者的阶段跳过。此前的底板透明度及传感器置顶修复保持。

验证：本轮隔离 EditMode 23/23、PlayMode 4/4、实际 MRP D3D11 GPU 两组通过，覆盖完整池条目 / 中央蓝底遮挡、三次采样复用相同 RT、三个 Ready 独立回退，以及旧单 / 双阶段接口。MRP 与完整 Core 的 Windows Editor / Android Editor / Android Player 定义编译通过。随后在主项目 Unity 6000.3.17f1 / DX11 / Android Device Simulator（Tablet Large）实际运行 Init → Title → List → Setting → List，检查 List 的 default1 / All 来回切换、Setting 的游戏 / 判定分类切换，未再看到灰色小胶囊越层；已停止 Play，恢复编辑状态。未运行本轮真机或 Vulkan 验证。

主工程停止 Play 时另观察到 FFmpeg 空纹理、HttpClient / Bass 音频对象的释放异常；这几处生命周期源码未在本轮改动，分类渲染验证不代表 Console 或这些释放路径已无错误。

## 2026-11-09 分类玻璃、调试置顶与底板透明度

Setting 的选中 / 未选中分类底板，以及 List 的集合分类底板，与八个外围按钮共同消费第二次背景抓取。底板在 3500 绘制，文字、图标和后续 TMP 子网格在 3501 保持清晰；程序圆角分类保留原来的四角半径及选中交叉渐变。沿用同两张工作 RT 和现有两次滤波，不增加第三次抓取。效果仍由精致 / 自定义的背景模糊开关控制，关闭时恢复原材质路径。

传感器调试材质使用队列 5000 和 `ZTest Always`，配合已有的最高 Debug 排序层，在按钮和 4000 转场之后绘制，不进入模糊源。网格、输入映射、透明度、风格、主副屏定位，以及关闭时禁用 MeshRenderer 的逻辑保持原样。

该阶段将直接使用 19 / 22 号底板的六处 Image alpha 统一为 0.85；涵盖 Setting、List、搜索、练习和校准。普通半透明填充从约 50.2% 降至 42.7%。当时也让精致玻璃透出 15% 原背景，此行为已由上方“玻璃主体覆盖与染色分离”修正；大板暖色 veil 仍为 8%，模糊核不变。外围按钮和中央 ± 胶囊透明度不改，节能档的不透明策略仍生效。练习 / 校准仅降低原底板 alpha，不额外加入模糊。

验证：隔离 D3D11 的本轮 28 项 EditMode、5 项 PlayMode 通过，覆盖资产绑定、传感器深度 / 透明混合、None 模式仍不透明、程序圆角和 TMP 子网格释放。实际 MRP 分类 GPU 一组通过实际 1x / 4x 附件，分类不进入第二次抓取，非白色 tint、清晰图标及 Ready0 原 Alpha 回退正确。完整 Core 在 Windows Editor / Android Editor / Android Player 定义下编译通过。没有运行主项目菜单或真机测试；主套传感器层级断言未执行，单独材质测试不冒充完整 MRP 传感器叠加验证。

## 2026-11-09 转场覆盖玻璃按钮

双层模糊把按钮移至普通 UI 之后，原来 3000 队列的转场因此提前绘制，造成按钮及图标浮在渐变动画上。本轮由 `SceneSwitcher` 显式绑定转场主遮罩、副屏渐变、加载文字和装饰，使用 `UiTransitionOverlay` 的原生材质副本仅将队列改为 4000，保留 GlobalMask 排序层、Canvas 层级、原 Shader、遮罩和动画。TMP 后续生成的子网格同步处理；永久副屏背景和其它全局 UI 不参与。

转场在玻璃按钮与符号之后合成，不进入两次模糊抓取。关闭模糊或使用旧单阶段接口时也保持覆盖顺序，不新增 RT、Blit 或管线绘制阶段。旧 Init 场景未指定独立转场 Canvas 时，仍按已有明确引用绑定，不再阻止初始化。

验证：新增原生 Image / TMP 子网格材质生命周期回归 2/2 通过；实际 MRP、转场 Shader 和 GlobalMask 排序层的 D3D11 GPU 检查通过实际附件 1x / 4x × 双阶段模糊 / 关闭模糊 / 旧单阶段接口共六种组合。起点无变化，终点覆盖按钮及图标，半程合成最大误差小于 1/255，加载标记仍在上层，转场不进入第二次抓取。完整 Core 的 Windows Editor / Android Editor / Android Player 定义编译通过。主项目 SceneSwitcher 新增回归未执行；未运行完整菜单转场、Player 或移动端验证。

## 2026-11-09 精致档背景 Blit 与底板分层（历史）

当时精致预设使用 100% 场景分辨率、4x MSAA、完整纹理精度、半透明 UI，开启旧 `UIBackdropBlur` 开关；该独立开关和 MSAA 默认值现已由顶部当前策略取代。以下保留当时的背景 Blit 分层及验证记录，不用于描述当前五档 UI 风格。

List / Setting / SortFind 搜索界面各一块大的奶黄色 22 号底板使用第一层模糊；Setting 小白色 19 号板保留原 Alpha。共享 `Buttons.prefab` 的八个外围圆底板另外使用第二层模糊，箭头、SVG 图标和长按进度最后清晰绘制。Setting 仍按原逻辑隐藏第 1 / 8 号外围按钮，中央 ± 胶囊未加入；List 显示八个。Practice / Result / TotalResult 中使用同一按钮 Prefab 的菜单在开启效果时一并生效，Game 不引用它。大板用 12% `#FFE4AD` 暖奶油色与 88% 模糊背景混合，透明度仍为 12%。

`MajUiBackdropBridge` 先画至 2999 队列（蓝环），Resolve 后第一次 Blit + 横纵 Gaussian，继续绘制普通 UI 3000..3499。可选按钮接口再 Resolve + 第二次滤波，画 3500 的八个圆底板及 3501 的清晰符号 / 长按进度。两阶段按命令顺序复用同两张实际场景四分之一边长、最长 1024 的 RGBA8 RT，半径 8 / sigma 4；同时开启时共六个低分辨率滤波 Pass，不为每个按钮开 RT。第二次抓取包含已绘制的普通 UI，不包含按钮或符号，避免反馈；两个独立 Ready 标志防止按钮抓取失败时误用第一层。没有可见大板时跳过第一次 Gaussian，没有可见按钮时沿用原单层前景路径。

按钮使用原来的浅 / 深灰蓝填充色做装饰分离，保留金色虚线和阴影，分别用 45% / 70% 原色混合模糊背景以保持符号对比。运行时 `UiBackdropForeground` 只在明确的 ButtonRoot 内绑定已有 Graphic（兼容 Image / SVG / TMP），保留原生材质和遮罩，仅改变队列；禁用后恢复并释放材质副本。按钮原圆形 alpha 轮廓不变、不使用大板的圆角 SDF 修饰。

为跨后端安全采样，本效果活跃时使用可 Store 的场景附件及 FinalCopy，MSAA 样本数不变；此时不使用 memoryless。它比直接取 PV 多一些带宽，但只影响开启该效果且大板可见的菜单。关闭或退出后恢复原路径，附件 / Shader 失败则原 Alpha 回退。详见 [背景 Blit 路径](Documentation/UI-Outline-Quality.md#refined-background-capture)。

验证：21 项 EditMode、7 项带图形设备的 PlayMode 通过。实际 MRP + 生产模糊服务的 D3D11 三组 GPU 检查通过，覆盖 1x / 4x、蓝环入源且前景不反馈、横竖屏与 50% / 150% 比例、遮罩、回退和释放；设置 1528 断言、翻译 481 断言通过。更新后的 MRP 与 Core 在 Windows Editor / Android Editor / Android Player 定义下源码编译通过。Vulkan 实际屏幕读回测试在关闭此功能的基线下也会原生崩溃，不能据此判定新功能成败；离屏分层合成通过，严格坐标检查尚未确认。手机 / iOS 与功耗未验证。

外围按钮双阶段修订另外通过 11 项资产 EditMode、9 项图形 PlayMode 及两组实际 MRP / D3D11 GPU 检查（含 1x / 4x、旧单阶段接口、纹理复用、清晰符号、第二层无自反馈和禁用恢复）。只有第一 Ready 时，按钮回到原 Alpha，未误读第一层模糊。更新后的完整 Core 在 Windows Editor / Android Editor / Android Player 定义下源码编译通过。未新增 Vulkan / 实机验证，也未测量 GPU 时间或功耗，不把通过图形检查等同于开销可以忽略。

## 2026-11-08 UI 模式与菜单背景分档

UI 模式统一为「不透明 / 混合透明 / 半透明」三档，保存为 `Rendering.UIOutlineMode`，内部枚举依次为 `None / Performance / All`。节能预设选「不透明」，性能预设选「混合透明」，美观 / 精致选「半透明」；预设不再分别配置普通 UI 与背景板。自定义可独立选择，兼容模式临时使用「不透明」，退出后恢复；MSAA 独立控制。旧布尔开关与背景策略仅作为配置读取迁移，新配置只写一个档位。

质量感知 UI 按作者原材质自动识别：`UI/Default`、`Maj/UI/Default`、`Maj/UI/Atlas Alpha` 属于原 Alpha 合成元素。「混合透明」让它们使用配对描边图，普通 Opaque 按钮仍保留无描边 Cutout；「不透明」将它们改用无描边图和支持 UGUI 遮罩的 `Maj/UI/Atlas Cutoff`，关闭混合与 A2C，主体实色、轮廓裁切、显隐硬切；「半透明」也让原 Opaque 的质量感知 UI 使用描边图和 Alpha Blend。原来只标记 List 五块底板的方案漏掉了 Setting 两块 RingCircle 与分类标题，本轮已删除这些手工标记，统一读取 `QualityUiImage.SourceMaterial`，防止把运行时转换后的材质误作分类依据。

Setting 未选中的分类标题使用程序圆角，已将 MenuTitleDisplayer 根 Image 接入质量感知。`Maj/UI/Rounded Rect` 保留原 Alpha 默认路径，并通过单独的运行时不透明材质副本硬裁圆角；原材质、圆角半径与 Stencil 保留。新混合属性使用 `_MajUISrcBlend/_MajUIDstBlend`，避免激活旧材质残留的 URP `_SrcBlend/_DstBlend` 值。普通 Image 黑色遮挡、TMP、视频、音符与 Live2D 不作全局材质替换；不新增 RT / Copy。

## 2026-11-08 UI 描边画质

独立的 List 紫色圆环、难度圆标、封面、收藏底圆及周边图标也随 UI 模式切换：「不透明 / 混合透明」保留原 Cutout，「半透明」使用连续 Alpha Blend、关闭 A2C。封面圆裁切使用约一像素的导数过渡，不额外模糊贴图；黑色遮挡材质保留。场景 MSAA 独立设置。D3D11 / Vulkan 已验证局部 Canvas 的前后叠加、主副屏裁边及深度偏移；完整菜单和移动端仍需实际复核。

List 圆周的 8 个按钮另有 9 个符号通过 Prefab 覆盖添加。它们必须放在各自底板之后绘制，不能依赖原 Opaque 的深度写入掩盖反向层级；当前仅修正这 9 条 List 局部插入顺序，保留坐标、大小、材质及长按填充参数。

节能 / 性能的原 Opaque UI 使用无描边图集与原 Cutout，场景保持无 MSAA；美观 / 精致的「半透明」模式使用有描边图集，原 Opaque UI 改为 Alpha Blend、关闭视觉 cutoff 和 A2C，场景仍保留 4x MSAA。原 Alpha 元素按上节 UI 模式规则处理。两套图集都位于 `Assets/Sprites/Ribbon`，保留所有 Sprite ID / GUID；25 片按实际阴影同步扩边，9 片保留原范围，并使用 FullRect 避免旧 Tight 轮廓裁影。通过保留旧逻辑 Sprite 的原生 UGUI 几何补偿保持按钮主体、排版、点击区域及按钮状态，不新增 RT / Copy。Setting 六个方向图标调整到背景之后绘制。资源契约与验证范围见 [UI outline quality](Documentation/UI-Outline-Quality.md)。

管线实现位于 [`Assets/Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline`](../../../Plugins/MajdataPlay/Rendering/RenderPipelines/MajRenderPipeline/README.md)，包含运行时、编辑器、管线 Shader 和共享 HLSL 库。本目录只保存 `Presets/` 中的项目配置资产，以及 `Documentation/` 和本文件中的项目集成、调优及历史验证记录。

主屏菜单使用原生 RectMask2D 限制到主屏范围，且上沿随副屏下沿调整，避免透明按钮越界盖住副屏；保留原黑色圆形遮挡、控件位置和全局排序，不增加 RT / Copy。裁剪控制器只在运行时由主屏的质量感知 Image 挂载。

下文的 `Runtime/`、`Editor/` 和 `Library/` 路径均相对上述插件库目录；`Presets/` 和 `Documentation/` 路径相对本目录。程序集名称、Shader 名称以及迁移资产的 GUID 保持不变。

## 2026-10-23 传感器调试位置与经典风格（当前）

「调试位置」保存为 `Debug.SensorPosition`，默认主屏；选择副屏后，读取 `InputManager.SubScreenEdge` 已有世界空间上下边缘，将整个分区图等比缩放到副屏高度并水平居中。位置在相机 / Canvas / 副屏边缘更新后的 LateUpdate 应用。仅 Shader 顶点与对应的 Renderer 剔除 bounds 使用该视觉映射；原 Transform、MeshCollider、输入索引与主屏触摸位置不变。副屏尚未提供有效边缘时暂不绘制，边缘恢复后自动显示。切回主屏恢复原始几何位置与自动 bounds；不增加 RT、Copy 或 Renderer 副本。

「调试风格」现有亮暗、红绿、经典三项，默认仍为红绿。经典使用完整分区，未触发 RGB 为 `#000D69`，触发为 `#5D0D06`，两态均使用 `SensorOpacity / 100`。共用透明度范围扩展为 10%～90%，步长 10%，默认 20%；亮暗继续使用闲置半倍、触发两倍并截断到 1 的 alpha，其余两种风格不改变设置 alpha。新位置、经典风格和扩展透明度已补四语言与模板。

验证：设置回归 1446 项断言、隔离 Unity 真实网格 / MPB / bounds / JSON 检查 17565 项断言通过；另以 Unity 6000.3.17f1 / D3D11 / RTX 3080 Ti 真实渲染生产 Shader，34 项 GPU 检查通过，覆盖镜头外原几何映射到副屏后的实际可见性、像素级上下对齐 / 居中、经典 20% / 90% 混合与主屏复位 / 隐藏。验证入口见 [SensorDebugValidation](../../../../Tools/Tests/Modules/SensorDebugValidation/README.md)。未运行主工程 Play、Player 或移动端真机。以下为同日上一轮记录，新的位置、风格与透明度范围以本节为准。

## 2026-10-23 传感器调试风格（前一轮）

「显示传感器」后新增「调试风格」，保存为 `Debug.SensorStyle`；旧配置缺项默认「红绿」，不改变现有外观。红绿沿用烘焙窄边网格，闲置红 / 触发绿，两态 alpha 均为 `SensorOpacity / 100`。亮暗改用 MeshCollider 已持有的原始完整分区网格，以黑色透明度区分状态；默认 20% 时闲置 alpha 0.1、触发 0.4，恢复旧 Editor 的浅 / 深效果。透明度调整对两态同比例缩放（闲置为设置值 × 0.5，触发为设置值 × 2，上限 1），所有平台一致，修复旧 Player 闲置 alpha 为 0 的不可见差异。

切换风格只替换 `MeshFilter.sharedMesh`，共享现有资源，不生成或复制网格；MeshCollider、Transform、输入索引不改。两种风格共用现有透明材质与简单颜色 Shader。Init 的 34 个分区仅取消 Batching Static 位（29 个场景对象与 FBX 实例中的 5 个 override），保留其他静态标志；否则 Player 静态合批可能冻结网格，导致运行时风格切换无效。显示关闭时禁用全部调试 MeshRenderer，跳过颜色和风格更新，重新开启时读取最新设置及触摸状态；稳定状态不重复提交 MPB。`DebugOptions` 拆为独立同名类型源文件以便测试直接链接生产设置，完整类型名与原 JSON 结构不变。四语言及模板键已补齐。

验证：`SettingValidation` 1265 项断言与 `IOValidation` 300 项断言通过。Unity 6000.3.17f1 隔离 headless 工程导入真实 FBX、5 份边框 Mesh、材质与 Shader，运行生产控制器和 DebugOptions，共 3081 项断言通过：覆盖 34 个输入映射、全部强度 / 风格 / 状态、真实 MPB 颜色、Renderer 开关、共享材质不变和 Newtonsoft 字符串枚举往返。场景检查确认 29＋5 个节点无 BatchingStatic，还原这 34 个标志后场景文本与修改前精确相同。验证入口见 [SensorDebugValidation](../../../../Tools/Tests/Modules/SensorDebugValidation/README.md)。未运行主工程 Play、Player 或真机；headless 测试不证明最终混合画面、触摸或功耗。以下「当前」标记均为当时记录，本节优先。

## 2026-10-18 传感器边框透明度（历史）

保留 5 份静态窄边 Mesh、34 个可视绑定与原碰撞数据；颜色仍为闲置红 / 触发绿。Shader 恢复 Transparent 队列（3000）、标准 Alpha Blend、ZWrite Off、ZTest LEqual，不再使用下一节历史记录中的 Opaque 状态。

调试菜单紧跟「显示传感器」新增「传感器透明度」，保存为 Debug.SensorOpacity。默认 20，范围 10～50，步长 10，菜单显示百分比；值越大越不透明，实际 alpha 为值 / 100。旧配置缺项默认 20%；外部输入按范围与档位规范化。红绿两态使用同一 alpha，调节即时更新，稳定状态不重复写 MPB；隐藏时仍禁用 Renderer 并跳过颜色更新。四种语言及模板键已补齐。

本轮 16 项程序集编译通过；四后端 8 个 Shader stage 和静态资源读回共 97 项检查通过，透明队列读回为 3000；实际新编译 Windows / Android DLL 各 25 项默认值、范围、步长和 JSON 保存读取检查通过；实际控制器源码加窄 stub 的 Editor / Player 各 24 项状态 / alpha / 缓存检查通过。未运行主工程 Play、Player 或真机，不宣称已验证最终混合画面或功耗。

## 2026-10-18 静态烘焙传感器边框（历史）

边框最终顶点已烘焙到 `Assets/Models/JudgeAreaBorders/A～E.asset`，固定对象空间宽度 0.02。Init 的 29 个独立 MeshFilter 和 FBX 实例中的 5 个 MeshFilter override 均替换为这 5 份共享资源，Renderer 材质也统一为 JudgeAreas。MeshCollider 继续使用原始完整 FBX 分区，Camera / Canvas / Transform / 输入索引未改。

未触发红色，触发绿色；关闭显示传感器仍禁用 Renderer。已移除运行时几何生成、网格所有权清理和 Shader 宽度偏移；材质 Border Width 不再是生效参数。Shader 只变换位置并输出颜色，使用 Geometry 队列 2000、Blend Off、ZWrite Off、ZTest LEqual。Opaque 队列使其早于后续透明音符，关闭深度写入避免边框给后续元素制造深度遮挡；实际与背景 / Live2D / 所有 UI 的画面仍由用户 Play 观察。

本轮 16 项程序集编译通过；隔离 headless Unity 的四后端 8 个 Shader stage 编译、5 份原生 Mesh 资源顶点 / 拓扑读回及 Opaque queue 共 97 项检查通过；实际控制器源码加窄 stub 的 Editor / Player 各 20 项红绿切换、关闭 Renderer、缓存写入和输入映射检查通过。静态验证 34 个可视绑定，原 29 份显式 Collider 和全部 Camera / Canvas / Transform 文档保持不变。未运行主工程 Play、Player 或真机，没有实测 GPU 时间或功耗。下节运行时生成 / 黑色触发 / 动态宽度记录为已替代的历史方案。

## 2026-10-18 传感器向内边框

`Assets/Models/JudgeAreas.mat` 使用新 `Maj/Debug/SensorBorder`。Init 的 DummyTouchPanelRenderer 指向此材质，原 Sensor.mat 不改。未触发红色，触发黑色，关闭显示传感器时禁用 Renderer；碰撞网格、GameObject 和输入索引不变。

五种可读平面网格各构建一次边框并共享：焊接导入时分裂的顶点，去掉内部三角形边，沿单轮廓生成外内顶点带。派生网格 UV0 存单位宽度向内偏移；原 FBX / UV / Collider 不改。顶点 Shader 用 Border Width（对象单位，默认 0.02，范围 0.001～0.05）调宽；片元只输出颜色，无纹理采样、无循环求边距、无透明内部。宽度随对象与相机缩放，不是固定屏幕像素。几何边缘使用已有 MSAA，不增加无 MSAA 的软边方案。

组件拥有派生 Mesh，销毁时恢复 MeshFilter 并释放，不释放源网格。只支持实际模型的单轮廓 XY 平面，异常时禁用对应可视绘制，保留输入。新材质需配合派生边框网格，直接给完整原网格换 Shader 不会自动产生边框。

16 项程序集编译、8 组四后端 Shader stage 编译通过。隔离 headless Unity 导入真实 FBX，复用生产构建算法并在 Upload 前捕获数据，5 网格 / 5 档宽度与分裂顶点去内部边共 18940 项断言通过。默认宽度的几何覆盖面积约为原分区 8%～16%，不能等同 GPU 时间或功耗。未运行主工程 Play、完整触发交互或真机测试。

> 2026-10-18 VSync 条件修正：仅 `(UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR` 隐藏输入；Windows Player 与 Editor（包括 Android 构建目标）均保留常规区垂直同步。Editor 启动和切画质同步应用保存值。16 项程序集编译、4 组实际 DLL 可见性检查、1212 项菜单断言通过；未运行主工程 Play 或 Player。

> 2026-10-18 最新菜单可见性：普通档位仅显示画质、帧率、桌面垂直同步和图形兼容模式；两种分辨率缩放与全部实时调试信息只在自定义且非兼容模式显示。兼容模式保留帧率、桌面 VSync 和兼容开关。自定义中，系统缩放不支持的平台仍显示「不支持」并拒绝调节。以下旧记录中普通档可见的描述已被本条替代。本轮设置回归 1212 项断言通过，未运行主游戏或真机。

> 2026-10-18 菜单修订：系统缩放不再按平台隐藏；Windows / Editor / 尚未接入的 iOS 显示「不支持」，拒绝左右调节，不改保存值、不提交请求。复用现有四语言「不支持」标签。当前 16 项程序集编译、1228 项设置 UI 断言通过；未运行主游戏或真机。Unity 的 Display.SetRenderingResolution 支持 Android / iOS，项目目前仅接入 Android Player，不能将此限制描述为 Windows 硬件不具备缩放能力。

## 2026-10-18 实时尺寸诊断与帧率设置

渲染菜单常规区现有帧率限制、桌面垂直同步，以及只读的当前输出路径、系统缩放状态、实际显示缓冲和实际场景分辨率。无需进入自定义；兼容模式也保留常用控制和诊断。系统缩放输入仅 Android Player 可调；Windows / Editor / iOS 保留该行并显示「不支持」，拒绝调节且保留保存值；移动端沿用原行为，不提供不起作用的 VSync 输入。

FPSLimit / VSync 现在保存到 Rendering。旧 Display 键逐项迁移；如果新旧两边都有键，新 Rendering 值优先，JSON 顺序不影响结果。画质预设不覆盖帧率或垂直同步；录制器及启动配置统一读取新位置。

系统缩放「已生效」仅表示实际显示缓冲尺寸匹配请求；尚未匹配显示「请求未确认」，恢复未匹配显示「正在恢复」，其它平台显示「不支持」。当前尺寸只发布成功提交的主屏相机结果：显示缓冲为实际目标大小，场景为实际绘制 viewport / 场景附件大小，附件回退时使用原输出大小。主屏不可用时显示 `--`。不变尺寸复用文本，不每帧拼接字符串；动态尺寸数字预热字体。

本轮 Windows / Android、Editor / Player 共 16 项程序集编译通过；设置 UI 回归 1226 项断言；实际新编译 Windows / Android DLL 各 36 项 JSON 迁移 / 平台可见性检查；实际系统缩放源码加窄 stub 的 Android / Windows 状态检查 52 / 6 项通过。未操作主工程 Play、设备或 APK，不代表原生 API、触摸、画面与功耗已通过。

## 2026-10-18 两种独立缩放

- 自定义菜单「渲染精度」：10%～200%，步长 5%；非 100% 使用 MRP RT＋Copy。实际附件仍受硬件最大纹理尺寸限制。
- 「系统分辨率缩放（实验）」：10%～100%，步长 5%，默认 100%，独立保存到 settings.json，普通预设也可见，不自动切换自定义。仅 Android Player 生效；100% 恢复接管前缓冲，低于 100% 请求原生宽高的对应比例。暂不开放高于原生的系统缓冲请求。
- 两项可叠乘：系统 75%、场景 50% 时，场景约为原生宽高的 37.5%，最终输出缓冲仍为 75%；文字和 Overlay 也可能受系统缩放影响。兼容模式暂时恢复系统缓冲，保留保存值，关闭兼容后恢复请求。
- Android Blit Type 从 Always 改 Auto（2），需重新打包；Unity 文档说明 Vulkan 忽略此设置，Auto 也不保证无内部复制。实际尺寸、输入对齐、MSAA、恢复和功耗待真机验证。

下文固定 75% / 节能自动启用 / Always 的实验记录为历史，不代表当前控制方式。


# MajRenderPipeline

面向 Unity 6000.3.17f1 的最小无光照管线。目标是在同等实际分辨率、抗锯齿和帧率下减少 GPU 附件读写、功耗与带宽；不提供 Lit、灯光、阴影或后处理。

当前架构、旧路径的问题、优化前后流程及 2026-10-16 Frame Debugger 审计见 [MRP 渲染架构与优化审计](Documentation/MRP-Architecture-and-Optimization.md)。下文保留实现细节和各阶段验证记录。

2026-10-17 已对当前未提交的 C#、Shader / include、配置与资源迁移做一次全面复审，范围和修复见[全面复审记录](Documentation/MRP-Architecture-and-Optimization.md#2026-10-17-未提交代码全面复审)。本次修复游玩 BGA 的资源 / Media 引用释放、旋转视频反复重建、显示切换和过期异步准备，补齐两份 UI Shader 的运行时 AlphaClip 变体，以及 A8 连按被校准 / 退出按下打断的边界。没有为此增加场景 RT、合成 Pass 或更改视频时钟；各项验证与真机限制分列于架构文档，旧 GPU 记录不当作本次重新执行。

2026-10-17 前的直接 Resolve 调查已在 Pad 5 Pro / Adreno 650 的 Vulkan 真机验证显式显示缓冲句柄恢复输出，保留 memoryless MSAA 和无中间颜色 RT / FinalCopy。那一阶段的独立 Fixture 使用旧版完整 viewport，通过 2x / 4x、冷启动、后台恢复与 Canvas / Mask，4x 与复制参考仍有稳定边缘差异（8740 像素、最大 62/255）。这是历史实测，不能沿用为后续菜单、动态参数覆盖、超采样或长屏平移的真机验证。2026-10-17 前一轮手机回归修复区分逻辑尺寸与物理屏幕，重做 Copy 定位，并保留偏移 viewport 的兼容兜底；随后本轮取消 ScreenPosCameraMover 的自动 .12 / .22 上移，使长屏也默认使用完整 viewport，100% 且后端条件满足时可直接 Resolve。各轮验证在下文分开记录，不沿用为本轮设备通过。设备证据见 [Android MSAA 屏幕 Resolve 调查与修复](Documentation/Android-MSAA-Compatibility.md)。

## 2026-10-18 Android 节能显示缓冲 75%（实验）

仅 Android Player 的“节能”档请求 `Display.main.SetRenderingResolution`，宽高取原生 `systemWidth / systemHeight` 的 75%，按当前屏幕方向排列并向下取整。例如 1440×3200 请求 1080×2400，输出像素为原生的 56.25%。MRP 自身的 RenderScale 仍为 100%、MSAA 关闭、纹理采样 bias +1；不把显示缓冲再乘一次场景 75%。Editor、Windows、iOS 维持原行为，菜单和 settings.json 没有新增字段。

MRP 的 Android 主屏目标、附件尺寸、viewport、FinalCopy 映射和 Shader 尺寸参数统一使用实际读回的 `Display.renderingWidth / renderingHeight`，归一化 Camera.rect 只映射一次。API 请求值不冒充已落地尺寸；未获得有效 rendering 尺寸时暂用 Screen 值。普通全屏 / 1x / 100% 条件满足时，仍直接写当前 CameraTarget，不新增 MRP 中间颜色 RT 或 FinalCopy。离屏、其它 Display、Preview 及非 Android 路径保留原像素域。

切到其它档位、进入自定义或启用兼容模式时恢复接管前的 rendering 尺寸，而不是强制面板原生值。捕获基准在快速来回切档时保留，防止异步恢复期间重复乘 75%；屏幕方向改变会交换基准宽高，后台恢复时可重新请求。稳定帧不反复设置缓冲。切换本身由引擎异步应用，过渡帧可能短暂使用旧显示尺寸与新场景比例，待读回恢复后兼容模式才稳定为原缓冲的 50%。

相机布局、Canvas、触摸坐标 / 半径、音频和判定时钟均未写入补偿比例。等比变化时现有 Expand 布局公式保持比例；真正的 Android 原生输入、触摸半径和 Overlay 集成仍需设备验证，不能仅用数值公式证明。

**当前 `androidBlitType: 0` 是 Always。** 此实验未修改该兼容设置；Unity 仍可在 MRP 之外离屏再复制到显示 framebuffer。MRP 的 Direct 状态只说明没有它自己的 FinalCopy，不能宣称全链路零拷贝。请求后一次 `[MRP.DisplayResolution]` 日志记录请求、实际、Screen、原生尺寸及 requiresBlit，`confirmed` 只代表尺寸读回一致。API 被拒绝或未生效时沿用设备实际尺寸，不自动伪装为另一种缩放实现。参见 [显示分辨率 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Display.SetRenderingResolution.html)和 [Android Blit Type](https://docs.unity3d.com/6000.3/Documentation/Manual/class-PlayerSettingsAndroid.html)。

本次最终源码按 Windows / Android × Editor / Player 编译 MRP、Cubism SDK、桥接和业务四程序集，共 16 项通过，既有设置菜单 1065 项回归也重新通过。实际辅助类、映射函数、RenderingOptions 及抽取的 GameManager / Renderer 片段配窄 stubs，四配置共 131 项通过；包含原生与恢复基准不同、启动无尺寸、异步与快速切档、方向、后台恢复、拒绝 / 无效请求、稳定帧不重设、4x 档位不变及 RT 不重复缩放。这些不构成 Unity 原生显示 API 或设备画面通过。

验证记录见[架构文档](Documentation/MRP-Architecture-and-Optimization.md#2026-10-18-节能显示缓冲实验)。本次未操作主工程 Play、启动 Player、安装 APK 或测量手机画面 / 功耗；需重新打包 Android，在节能与性能间对比并测试传感器边缘。

## Code review 顺序

1. `Runtime/MajRenderPipelineAsset.cs`：预设默认比例、运行时覆盖、MSAA 与拷贝 Shader 引用。
2. `Runtime/MajRenderScale.cs`：实际比例的尺寸取整、Auto 过滤、Mip Bias；`Runtime/MajFinalCopyMapping.cs`：物理输出尺寸、逻辑矩形与 Copy 裁剪空间映射的纯函数。
3. `Runtime/MajRenderPipeline.cs`：相机调度、回调、Overlay UI 归属与全局状态恢复；`Runtime/MajAndroidDisplayResolution.cs`：Android 节能显示缓冲实验的请求、读回与恢复。
4. `Runtime/MajCameraRenderer.cs`：目标选择、缩放与非缩放路径、Shader globals、最终拷贝。
5. `Runtime/MajCameraAttachments.cs`：RT/设备能力缓存、原生和兼容附件、Load/Store/Resolve/Discard。
6. `Runtime/Shaders/`：场景颜色拷贝/放大，以及保留原材质属性的 MRP Unlit。
7. `Library/`：项目 Shader 共用的无光照 HLSL 基础库。
8. `Editor/`：Scene View 几何/Gizmos 与 MRP Unlit 材质状态编辑器。

`Runtime/MajdataPlay.MajRenderPipe.asmdef` 没有 URP/Core RP/Live2D 程序集引用。管线只选择 `SRPDefaultUnlit` Pass，沿用原生 RendererList 的 SortingLayer、CanvasOrder、透明排序、cullingMask 与 Instancing；不额外选择 Lit Pass。

`Editor/MajdataPlay.MajRenderPipe.asmref` 将编辑器代码并入同一个程序集，保持 `MajCameraRenderer` 的两个 partial 文件为同一类型。这里的 Editor 目录负责代码整理，Player 排除依靠每个编辑器文件完整的 `#if UNITY_EDITOR`；不能删掉条件编译后仅依赖目录名。移动现有脚本、Shader 和资产时保留原 `.meta`，不改变场景、材质或 Quality 的资源 GUID。

## Frame Debugger 命名

复用的 CommandBuffer 命名为 `MRP.RenderCamera`，Unity 为每次 Execute 自动配对采样范围；原先跨 Execute 的手动外层 sample 已移除。原生 Pass 的 UTF-8 名为 `MRP.Scene` / `MRP.FinalCopy`，静态编码并保留末尾 NUL。Editor / Development 内另标记 `MRP.Opaque`、`MRP.Skybox`、`MRP.CameraExtension`（当前为 Live2D）、`MRP.Transparent`、`MRP.FinalCopy`，每段在同一次 Execute 内通过 finally 配对结束。Release 不调用这些细分采样方法，未增加 Execute / Submit、RT 或绘制次数。

早期命名审计的 23 个项目 Shader Pass 均已命名；该轮仅给此前缺失名称的 11 个补上 Name，已有名称、LightMode、HLSL、属性、Stencil 和 GUID 保留。`Canvas.RenderSubBatch` / `Draw Mesh` 等引擎事件类型名、`Batch cause: Unknown reason`、MRP 范围外的 `<unknown scope>` 不由这些接口命名。兼容路径可能显示多个同名 `MRP.RenderCamera` 分组，表示同一缓冲的多次执行，不是多台相机或多张 RT。工具最终是否展示原生 Pass 名称取决于 Unity / 图形后端。

Windows 游玩视频的现有外部纹理 / RT 分别命名为 `Maj VLC Video Frame` / `Maj Video BGA`，仍用原 Graphics.Blit。命名修改通过 8 组管线源码编译与 4 组完整业务源码编译；11 个 Shader 去掉新增 Name 行后与修改前逐字节一致，meta 不变。没有重新运行 Unity 或捕获 Frame Debugger，实际显示需刷新捕获确认。

## Shader 精度

### 2026-10-17 Break 图集 UV 精度修正（当前）

手机截图中橙色 Break 的块状 / 断续边缘，对应到实际路径为 RuntimeDatabase → BreakMaterial → BrightnessAdjustAnimate；普通音符则使用 float2 UV 的 Maj/Sprites/Default。Break、HoldShineShader、ColorAdjustEffect 和 Notes/Common 的 v2f.uv 遗留为 half2，输入 appdata 已经是 float2。该 half2 在旧 URP 源码及此次 mip bias 改动前都已存在，并非前一轮 fixed → half 时新增；宏内再转 float4 无法恢复插值前后已经丢失的精度。性能档 bias 0 也会经过此路径，不能将截图直接归因于节能档的 +1 偏移。

这四处 UV 改为 float2；颜色仍用 half，时间 / 相位仍用 float，Properties、材质、meta / GUID、实例化和常量缓冲布局均不变。没有改采样次数、纹理 / RT 格式、过滤、图集尺寸或正常 LOD + bias 规则。皮肤图集上限是 min(8192, SystemInfo.maxTextureSize)，本次未测截图设备的实际打包尺寸；以 UV 0.5～1 范围的 binary16 为例，4096 / 8192 图集的相邻可表示值可相差 2 / 4 个纹素，足以使本应连续的采样出现块状跳变。默认 Tap 普通 / Break 均为 122×122，Slide 均为 70×94，未发现这两种 Break 源图被单独缩小。参见 [Unity 的精度与纹理坐标说明](https://docs.unity3d.com/cn/2022.3/Manual/SL-DataTypesAndPrecision.html)。

本轮旧新四 Shader 的 D3D / GLES3 / Vulkan / Metal、Vertex / Fragment 共 64 项代表编译成功且字节码非空。GLES3 导出明确由 mediump vec2 UV 改为 highp vec2；Metal VS / PS 的 half2 接口改为 float2；Vulkan UV 原有 RelaxedPrecision 标记移除，OpTypeFloat 本来就是 32 位，不能只看类型判断原先是否允许降精度。编译 New 与生产修改去掉注释 / 空白后语义一致；逐字节逆向检查确认生产只改四处 UV 类型和注释，meta 不变。本轮没有运行主工程、桌面 GPU 像素对照、手机或功耗测试；这些证据定位并修正了移动后端的精度风险，之后用户反馈修复后正常；这不是本次全面复审重新运行的手机画面，也不扩展为其它机型或功耗的通过结论。

### 2026-10-15 颜色精度审计（历史）

不能将全部 `float` 替换成 `half`。2026-10-15 审计覆盖 Assets 内 64 个 Shader 源文件 / ShaderGraph（45 Shader、9 cginc、6 hlsl、4 ShaderGraph），只缩减有界颜色中间结果，不改变贴图、RT 格式、采样次数或原有材质接口。

- Common 的 BrightnessAdjust / BrightnessAdjustOpaque：实例与材质中的 `_Brightness` 仍按原 float 布局上传，只在片元读取后转 half。
- Notes/Common、BrightnessAdjustAnimate、HoldShineShader：亮度 / 对比度系数改为 half；时间、相位和 sin 输入保持 float，常数饱和度 / 对比度 1 可精确表示。
- MaimaiColorEffect：色相分段完成后的有界 RGB 中间结果使用 half；UV、角度、时间分段、径向边界和 alpha 保持原 float 运算。
- Live2D / TMP 的 17 个文件统一 87 处 fixed 类型为 half。本机 Unity 6.3 的现代目标后端已把二者映射到相同精度，这是等价写法清理，不产生额外性能收益。
- UI 的 8 个 Shader 只补充精度保留注释。位置 / 矩阵、完整分辨率 UV、SDF / 导数 / AA / 遮罩边缘、角度 / floor / progress、Live2D 小 alpha 反预乘与复杂混合保留 float。UnityPerDraw、实例数组、材质缓冲的原字段类型与顺序保留。

ShaderGraph 序列化精度、Unity 内置 / Packages 源码和全局 Shader Precision Model 未调整。`half` 不保证所有平台都使用 16bit ALU；Unity 也可能忽略浮点字面量的 `h` 后缀，精度应以表达式类型和目标后端代码为准，不能仅凭源码中的 half 数量估算收益。half 参数也不会自动缩小 CPU 可见常量缓冲、纹理或 RT 的内存 / 带宽。参见 [Unity 16bit 精度说明](https://docs.unity3d.com/6000.3/Documentation/Manual/SL-Use16BitPrecisionInShaders.html)。

本轮保留 Properties、Shader 名称、meta / GUID、常量与实例缓冲布局。隔离 D3D11 Editor 的 Gamma / Linear、Batcher 开关和真实实例化共 224 组旧新 GPU 对照，最大 RGBA8 单通道差 1，实例绘制与逐实例参考差 0；四后端代表 stage / keyword 共 384 项编译通过。Live2D Mask 与 TMP 的修改和 include 依赖另覆盖 15 个 Shader、19 个 SubShader、23 个 Pass（含 2 个 Surface Shader 生成的 5 个 Pass），四后端另有 984 项代表 stage / pass / keyword 编译通过；上述编译均取得非空字节码，无警告或错误。另以 IEEE binary16 舍入、融合 / 非融合运算模型采样约 2.58 亿个标量结果，当前 LDR 输入范围内最大绝对差 0.001953125（约 0.498 个 8bit 码值），直接量化到 UNORM8 或经 Linear → sRGB 再量化，最大均只差 1；alpha 不变，无新增非有限值。这是数值采样与离屏图像证据，不是所有输入的穷举证明，也不替代 Android / iOS 真机画面与功耗测试；没有操作主工程 Play 或运行 Player。

## 保留的编辑器质量资产

资产位于 `Presets/`。初始五档从原 URP 资产复制；下表记录当前配置，包括之后在 Inspector 中调整的 Low 和单独的 Very High，不能再把所有档位都视为原 URP 数值。

| MRP 资产 | 对应旧 URP | 默认 RenderScale | MSAA | Unity Quality 绑定 |
| --- | --- | --- | --- | --- |
| MRP_UltraLow | URP_0_VLQ | 50% | Disabled / 1x | 无，保留预设 |
| MRP_Low | URP_1_LQ；当前已调整 | 100% | Disabled / 1x | Default，索引 0 |
| MRP_Medium | URP_2_Medium | 100% | 2x | 无，保留预设 |
| MRP_High | URP_3_HQ | 100% | 4x | 无，保留预设 |
| MRP_VeryHigh | 当前独立配置 | 100% | 8x | 无，保留预设 |
| MRP_Ultra | URP_4_UHQ | 100% | 8x | 无，保留预设 |

共同开启附件优化、SRP Batcher、GPU Instancing，关闭 Dynamic Batching。HDR、灯光、阴影和可采样 Depth/Opaque Texture 不在管线中生成。

Unity Quality 已合并为唯一 Default（索引 0），保留原 Low block 除名称外的全部参数，并继续引用 MRP_Low；各平台默认索引也统一为 0。六份 MRP 预设及 GUID 没有删除或重写，但其余五份不再绑定 Unity Quality。预设默认值不是新的游戏画质档位；功耗比较必须记录实际运行时比例、样本数和质量采样 bias。

MRP 固定关闭各向异性过滤，不提供该画质选项：Default 使用 `anisotropicTextures: 0`（Disable），管线启用时也显式应用全局 Disable，覆盖源纹理的 Aniso 设置。现有 `activeQualityLevelChanged` 防御逻辑保留，供外部代码改变 Unity Quality 时维持约束；游戏渲染菜单不再切换 Unity Quality。没有逐帧查询或反复改写导入设置。MRP 自建 RT 显式使用 `anisoLevel = 0`，FinalCopy 只按 Auto 选择 Point/Bilinear。退出 MRP 时取消订阅并恢复接管前的全局过滤设置；实际功耗收益仍需同条件设备对照。

Graphics 默认回退仍指向 `Presets/MRP_Low.asset`。游戏启动和修改渲染设置时使用 Unity Default 基线，再独立覆盖场景参数；不使用旧六档枚举索引 Unity Quality。早期独立的 `Presets/MajRenderPipeline.asset` 已不存在。

## 游戏设置与运行时覆盖

设置分类顺序为显示设定 → 渲染设定 → 音量设定。普通模式只显示渲染质量和图形兼容模式；画质只在四个预设名之间循环，不拼接参数，也不会循环选到自定义。新用户默认“美观”（100% 分辨率、4x MSAA、开启 UI 描边）。

| 游戏档位 | 场景比例 | MSAA 请求 | 纹理质量采样 bias | 输出偏好 |
| --- | --- | --- | --- | --- |
| 节能 | 100% | 关闭 | +1，偏向更粗一层 mip | 直接输出 |
| 性能 | 100% | 关闭 | 0，默认采样精度 | 直接输出 |
| 美观 | 100% | 4x | 0，默认采样精度 | 直接输出 |
| 精致 | 100% | 4x | 0，默认采样精度 | 直接输出 |
| 自定义 | 10%–200%，步长 5% | 用户选择关闭 / 2x / 4x / 8x | 低 +2 / 中 +1 / 高 0 | 100% 时可选择；其它比例强制 RT + Copy |

高级入口仅在渲染质量行、兼容模式关闭时生效：连续按下十次 8 号键 / A8 区，保留当前参数进入自定义，立即应用并通过现有保存入口写入 `settings.json`。入口直接读取按下边沿，不再使用约 0.2 秒延迟且会合并快速点击的动画完成事件。按钮与触摸聚合为一轮按压，直到两路都释放才接受下一次；同帧或跨帧重叠不会重复计数，长按不会累计。换项、切档、离页及其它导航 / 调节输入仍清零计数，原来的 1 号键 / A1 校准入口保留原行为。已保存的自定义下次仍显示全部细项；选择普通预设后立即隐藏，自定义也再次从正常循环中排除，需要重新使用隐藏入口。

自定义时显示渲染质量、抗锯齿、纹理精度、输出模式、兼容开关、渲染分辨率及只读的当前输出路径。比例显示百分数，非 100% 时强制切为兼容输出并拒绝直接输出；回到 100% 后可手动重新选择直接输出。兼容模式开启后只显示兼容开关，有效值临时变为 50% / MSAA 关闭 / 质量 mip bias +2 / RT + Copy；原档位与自定义参数不变，关闭后恢复。纹理精度现在控制已接入 Shader 的自动 LOD 采样偏移，不是 Half / Quarter Resolution 资源限制，也不是 Mesh LOD；没有 mip 的纹理不会因此增加新的 mip 层。

“当前输出路径”读取 `MajRenderDiagnostics.OutputStatus`，显示最近一次主显示器 Game 相机正常提交后的实际分支：直接绘制、MSAA 直接 Resolve，或 RT+Copy，并标明缩放、兼容输出、MSAA 直出不可用、附件兼容路径等原因。附件失败或屏幕缓冲切换而退回直绘时也单独标记。状态只读且 `[JsonIgnore]`，不保存到 settings.json、不修改输出偏好、不触发应用或保存设置。它报告 CPU 已提交的路径，不是 GPU 像素正确性或黑屏检测。新的渲染帧没有主屏提交时标为不可用；同一帧额外的 Scene View、Preview 或离屏 context 不覆盖已完成的主屏结果。

`RenderingOptions` 保存到 `SystemSetting.Rendering`（system-settings.json），新配置默认使用美观档 / 100% / 4x MSAA / UI 描边开启。旧 URP 配置的 `Display.RenderQuality / RenderScale` 统一移除，所有旧档位和旧渲染比例都迁移为美观，不再还原旧 MSAA、纹理限制或缩放比例。旧 `FPSLimit / VSync` 继续迁移，显示布局等其它设置按原逻辑保留。已存在的 MRP `Rendering` 分类优先，其画质与自定义参数不被旧 URP 字段覆盖；旧帧率和 VSync 只补它缺少的键。空的新对象也优先，值为 null 的分类按缺项处理。已有 MRP 自定义配置缺少抗锯齿或 UI 描边字段时，这些缺项仍按历史值关闭，显式保存值不变；具名预设应用自己的完整参数。

渲染菜单的显示规则由 `RenderingOptions` 上的 `[ShowInSettingUIWhen]` 声明：渲染质量在兼容模式关闭时显示；抗锯齿、纹理精度、输出模式、两种分辨率缩放和只读诊断要求兼容模式关闭且为自定义；帧率、桌面 VSync 与兼容开关始终显示。`Menu` 不再按属性名特判渲染选项，仅保留隐藏入口的计数逻辑。

`GameManager.ApplyRenderingSettings` 固定 `QualitySettings.globalTextureMipmapLimit = 0`，并通过 MRP 运行时接口覆盖 RenderScale / MSAA / 输出模式和纹理质量采样 bias。覆盖均非序列化，不写预设资产；已有管线下次绘制读取新值，仅在尺寸 / 样本 / 目标改变时重建附件。质量 bias 变化不重新上传或缩小纹理，也不改 `Texture.mipMapBias`。桌面 VSync 仍以独立显示选项为准。直接输出是偏好，后端不支持时仍自动使用兼容路径；显式选兼容输出则在 100% / 1x 时也插入颜色 RT + Copy。

### 2026-10-17 纹理采样 bias 与单一 Unity Quality（当前）

纹理低 / 中 / 高分别传入 `_MajTextureMipBias = +2 / +1 / 0`，节能使用 +1，兼容模式使用 +2，其余预设为 0。接入点在自动 LOD 采样：现代本地 MRP 采样宏同时保留原 RenderScale 的 `_GlobalMipBias` 补偿并叠加质量 bias；原先没有比例补偿的 legacy 采样只加入质量 bias。全局资源 mip limit 保持 0，不再通过反复更改资源限制实现这三个档位。高档 0 仍由纹理投影尺寸自动选择 mip，不表示永远强制采样原始 mip 0。

这项改动只影响带 mip 且 Shader 已接入的采样。当前接入范围为项目自定义的相关自动 LOD 采样、Live2D 主图及 TMP_Sprite 图像采样；实际 EmojiOne 图集带 mip。全部 TMP SDF 字体以及未发现资源引用的三份 TMP Bitmap Shader 不修改。共享采样宏也可能用于视频或 Composite RT，但只有一层 mip 的纹理最终仍采样 LOD 0，不能因此降低纹理精度；FinalCopy 文件及采样不改。外部输入是否带 mip、最终 Shader 是否已接入，以及其它内置或第三方 Shader，仍需按实际绘制链确认，不能称所有纹理统一生效。普通 Bilinear 是单层 mip 内的双线性过滤，不提供相邻 mip 之间的连续插值；[Trilinear](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/FilterMode.Trilinear.html) 才提供级间混合，当前没有为此批量修改导入过滤方式。

主业务场景 / Prefab 原先使用的默认 UI 与游戏 SpriteRenderer 材质已改为共享 `Maj/UI/Default` / `Maj/Sprites/Default`，让这一范围的图像也走接入的本地采样。RuntimeDatabase 的普通音符默认材质入口同步替换，因此使用此入口的外部皮肤不因来源在工程外而自动排除；仍取决于纹理是否有 mip 和最终材质。静态迁移记录为 91 份文件、1568 处引用，保留 Sprite、fileID 与 meta；未改 unused 场景和未配置的历史 Prefab，也不覆盖用户自行提供的任意 Shader。该记录不是当前帧或主游戏视觉验证。

质量采样 bias 不删除或阻止上传高分辨率 mip，因此不减少这部分纹理驻留容量；可能改变采样工作与缓存压力，实际带宽和功耗必须测量。它与 Unity 的 Global Mipmap Limit 不同，后者会限制最高 mip 的上传，并可能在运行时切换时重新上传受影响纹理。参见 [Unity Mipmap Limit](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/QualitySettings-globalTextureMipmapLimit.html)。

本轮通过 Windows / Android 的 Editor、Player 四组共 8 个程序集源码编译；真实生产程序集 / Newtonsoft 配置回归分别通过 6924 / 6243 项，设置 UI harness 通过 13 组 / 1057 项。四语各 383 条键合法无重复，template 键表未变。Quality 静态对照确认唯一 Default 除名称外保留旧 Low 的全部 52 个字段，平台默认索引统一为 0；资源迁移逐字节逆向校验保留原 Sprite、fileID、meta 与其它属性。

隔离 Unity Editor 在桌面 D3D11 的 Gamma / Linear 下执行 574 次绘制、322 次读回，167 组全图零容差对照均无像素差：彩色 mip 链验证自动 LOD 0.5 / 1.5 / 2.5 上叠加 +1 / +2 与 UV 频率乘 2 / 4 的隐式参考一致，单 mip 不受影响；新增默认 Sprite / UI 在 bias 0 时与本版 Unity 内置 Shader 对照一致，包含 tint、flip、外置 alpha、PixelSnap、UI 裁切 / 柔边、AlphaClip 与顶点色处理的代表组合。26 个 Shader 的 D3D / GLES3 / Vulkan / Metal 共 432 项代表 stage / pass / keyword 编译成功且字节码非空。这些不是全部变体或移动设备实绘。初次 UNorm 低 alpha 对照没有可见差异的诊断仍保留，补充浮点目标多次绘制确认 AlphaClip 实际改变像素后，新旧输出仍一致，未放宽阈值。

本轮没有运行主工程 Play、完整 Player 构建或真机，也没有验证实际 CanvasRenderer / SpriteRenderer 集成、屏幕 Present、功耗、带宽或 GPU 频率。以下旧质量清理和此前 GPU 记录不计为本轮验证。

前一轮旧代码清理（历史）删除了无绑定的 `EngineEnumSettingEnumerator`、旧 RenderScale 枚举器分支与 `GameManager.ApplyRenderScale`，以及 Display 的旧画质 / 比例公开属性和重复默认常量。FPS 限制与 VSync 继续使用现行枚举器，VSync 默认仍为 true。该轮当时保留索引 1 的 Low 引擎基线，尚未合并质量资产或改变纹理参数语义；这不是当前 Default / 采样 bias 改动的验证。

该历史清理通过 Windows / Android 的 Editor、Player 四组共 8 个程序集编译。实际生产程序集与真实 Newtonsoft 的 Windows / Android 两轮分别通过 6813 / 6132 项逻辑检查，包含既有参数 / 状态 / 比例回归，以及空配置默认、部分旧字段的平台补齐、新旧对象顺序、字符串 / 数字旧画质、只读旧键不再保存等迁移检查。设置 UI 的 13 组 / 1054 项断言通过，旧比例枚举器断言改为实际仍使用的 FPS 限制。上述为当时源码、配置读写和控制流验证，没有运行主工程 Play、完整 Player 构建或设备画面 / 功耗测试。

场景比例当前支持 10%–200%，超采样按硬件纹理尺寸上限同比例限制，使用 Bilinear 缩小且不施加负 mip bias。150% 的附件大于屏幕，现有直接 MSAA Resolve 不负责缩放，因此自定义超采样需要中间 RT + Copy；旧精致曾使用此路径，现已恢复 100% 与直接输出偏好。Screen Space Overlay 继续按原生屏幕分辨率由引擎绘制。Camera 投影、布局与输入坐标不随比例修改。

此前高级菜单改动通过 Windows / Android 的 Editor、Player 四组生产源码编译；实际新程序集的 57 项参数、JSON 迁移 / 顺序、自定义持久化及缩放数值检查通过。设置 UI 外部 harness 当时扩展至 11 组 / 802 项断言，覆盖 A8 十次解锁、A1 校准保留、按钮 / 触摸去重、长按不重复累计、导航打断、分辨率步长 / 上下限、输出限制、隐藏 / 恢复、池和四语标签；此前 43 / 688 项记录属于更早版本。

2026-10-17 输出状态项改动通过 Windows / Android 的 Editor、Player 四组共 8 个程序集编译。新生产程序集的 101 项设置 / JSON / 状态生命周期与 Copy 原因优先级检查通过；UI 外部 harness 更新为 12 组 / 934 项断言，覆盖十种状态自动刷新、只读输入、稳定状态不重写文本、同帧 context 保留 / 新帧失效、四语切换与预热及自定义 / 普通 / 兼容模式的 7 / 2 / 1 项可见性。未运行主工程 Play 或本次 GPU 绘制，不将此前 GPU / 真机结果计作此状态项的端到端验证。

随后修正快速 A8 输入与 5% 比例：使用真实 `InputControlState` 复现 60Hz 下十次快速按下仅产生两次旧动画完成通知；新入口改按下边沿及两通道释放锁存，按钮 / 触摸均在第十次解锁，不修改全局输入或 A1 校准。UI harness 扩展至 13 组 / 1043 项断言；实际新生产程序集通过 190 项逻辑检查，包含 50%～150% 全部 21 档的有效比例、尺寸递增和直接输出规则。1080x2400 的 95% / 100% / 105% 分别得到 1026x2280 / 1080x2400 / 1134x2520，未再吸附。Windows / Android 的 Editor、Player 四组 8 个程序集编译通过；以上为源码与外部 harness 检查，未重测主工程 Play、实际手机输入或 GPU 附件。

## Opaque 裁切材质的最小 A2C

当前实际 Opaque / AlphaTest 裁切材质统一采用标准最小 Alpha-to-Coverage：`Maj/Common/Opaque` 覆盖普通 UI、RingLine 与列表等级底板；`Maj/UI/List/CoverShader` 覆盖列表封面、缩略图与圆形封面。两者固定 `AlphaToMask On`，保留 Blend Off、ZWrite On 和原队列，不增加贴图采样、绘制 Pass 或 RT。纯实心背景继续输出 Alpha=1，不因背景贴图或视频的 Alpha 打孔。

实际场景附件为多采样时，Common/Opaque 直接返回原贴图 × 顶点/材质颜色的 RGBA；CoverShader 返回原 RGB × Brightness 和现有 `textureAlpha × circleAlpha`。不再围绕 cutoff 做 fwidth / coverage 重映射，也不在 MSAA 分支做 0.5 硬裁切；原有圆形形状本身的导数 AA / Feather 保留。因此 `_Cutoff` 现在仅在无 MSAA 时生效，MSAA 下的低 Alpha、软边与淡出直接决定样本覆盖，外观可能与旧的硬裁切阈值不同。

`_MajAlphaToMaskAvailable` 仍由 MRP 按已创建附件的实际采样数设置，三条相机路径提交前复位为 0，避免后续引擎 Overlay 误用场景状态；1x 或未知系统目标保留原 clip 和 Alpha=1。没有新增材质开关或 Shader 关键词变体。原 UnityPerMaterial / Instancing 布局、材质 GUID 与场景引用保留；先前为 RingLine 增加的材质资产仍有效，但其专用关键词与开关字段已清除，行为与原共享材质一致。

2026-10-16 当前直接 Alpha 版本通过隐藏 Gamma / D3D11 隔离验证：26 次真实 Cull / RendererList / Resolve 绘制、29 项检查全部通过。两种 Shader 的 1x 与原硬裁切逐像素 RGBA 一致；4x 形成部分样本覆盖，透明孔、前后层深度顺序与列表 Brightness 实例化对照通过。MSAA 下改变 cutoff 为 0.1 / 0.9 不改变 Common/Opaque 输出，确认没有残留阈值重映射；原共享材质与保留的 RingLine 材质结果一致。两种 Shader 的 D3D11 / Vulkan / GLES3 / Metal 基础 Vertex / Fragment 共 16 项代表编译通过。测试手动驱动 availability，全局运行时接线、真正 Canvas 发射、屏幕直出与 Android / iOS 真机未在本轮验证；早期 RingLine 专用导数 A2C 记录属于历史版本。

## 透明圆形 UI 的描边与阴影

`Maj/UI/CircleMaskWithOutlineShadow`（`UICircleOutlineShadow.shader`）保留 Transparent / ZWrite Off，使用预乘 Alpha 输出和 `Blend One OneMinusSrcAlpha`。主体与描边先按互补的圆形覆盖率合为一层，再整体覆盖阴影，RGB 与 Alpha 使用一致的 source-over 计算，避免软边重复乘 Alpha、内接缝漏出背景。描边宽度为 0 时覆盖率明确为 0。

圆周使用 `fwidth(dist)` 调整抗锯齿宽度，1x 与 MSAA 目标均可使用；阴影保留原 Softness，并至少使用一个像素的过渡，避免零 Softness 退化。此透明 Shader 不启用 A2C，避免在普通透明混合之外再次衰减覆盖率。材质属性、Stencil、深度和原有主体 Tint 语义保持，仍为一次纹理采样、一个 Pass，没有新增 RT。

当前引用是以完整 0～1 UV 绘制的圆形背景。此修改没有新增 SpriteAtlas 子区域到局部圆形 UV 的换算，也没有改变阴影／描边独立于主体 Tint 的既有行为。正常的黑色软阴影仍会保留。

2026-10-16 修复版本通过隐藏 D3D11 / Gamma 离屏 GPU 验证：40 次绘制、38 项检查，覆盖 1x / 4x、30 / 150 像素、零宽描边、半透明 Over、内接缝 Alpha、正常软阴影和零 Softness 的有限值。旧版黑边和重复乘 Alpha 的失败对照均已复现并保留；D3D11 / Vulkan / GLES3 / Metal 的 Vertex / Fragment 共 8 项代表编译通过。本轮没有运行主场景 Canvas 或 Android / iOS 真机，跨后端编译不等于跨设备画面验证。

## RenderScale 流程

- 只缩放场景附件，不改 `Screen.SetResolution`、Camera 输出矩形、游戏坐标、音符/判定时钟。
- 比例限制在 50%～150%，每个 5% 档位都按所选数值计算，不再沿用旧 URP 的 100% 附近吸附。此前 105% 的浮点差值可能略小于 5%，被误当作 100%；本轮移除该吸附，95% / 105% 均保留实际比例。
- 场景尺寸按相机逻辑输出宽高计算，为 `Max(1, (int)(逻辑输出尺寸 * 比例))`，向下取整。例如逻辑输出 1080x1920 的 50% 是 540x960，75% 是 810x1440。主屏 Copy Pass 的物理尺寸单独取 `Screen.width / Screen.height`，不把相机 `pixelWidth / pixelHeight` 当作物理 backbuffer 尺寸。
- 附件缓存按实际整数宽高比较，不把比例量化为 10% 或 20% 档位；只有整数像素取整或硬件纹理尺寸上限可能让相邻比例得到相同尺寸。100% 且附件尺寸等于输出时仍可直接输出，其余比例仍走实际缩放与 Copy，不修改 Camera 投影或输入坐标。
- `_ScreenParams` 保持输出尺寸，`_ScaledScreenParams` / `_ScreenSize` 使用场景尺寸；Mip Bias 对齐旧 URP。Overlay 前恢复全分辨率参数。
- 旧 URP 的 Upscaling Filter 为 Auto：整数倍放大且输出宽高整除时用 Point，否则 Bilinear。因此偶数尺寸的 50% 是 Point，75% 是 Bilinear。

仅缩放安全独立的主显示器 Game 相机，且必须完整清颜色。独立资格按同一 Display 的屏幕 Game 相机判断，额外的 RT / Preview / 其它 Display 相机不会让主相机的 50% / 75% 变回 100%；相机按 depth 排序。原生附件要求归一化 viewport 的宽高均为 1，且相机逻辑输出尺寸与物理屏幕尺寸一致。ScreenPosCameraMover 已取消按长宽比自动设置 y=.12 / .22 的旧逻辑，初始化统一使用 (0,0,1,1) 完整 viewport；长屏本身不再触发偏移 MSAA 的 Copy。手动显示设置 MainScreenScale / MainScreenOffset 继续通过相机 Transform / 正交 Size 和 Canvas 变换调整布局，不改变 rect，也不等于 RenderScale，因此不会单独强制 Copy。RenderScale 非 100% 或明确选择兼容输出仍需场景颜色 RT + Copy。

MRP 仍保留外部非原点 viewport 的兼容处理：100% / 1x 按原 pixelRect 直接绘制；偏移 MSAA 在本地场景附件绘制，Resolve 到普通 1x RT，再由 FinalCopy 定位。缩放和强制兼容输出同样在最终 Copy 应用一次逻辑矩形。Copy 的实际 viewport 固定为整张物理输出、零原点，Shader 将完整源图映射到原逻辑矩形，裁掉超出源 UV 的片元，并清除未覆盖的屏幕边缘；不把完整图像压入屏幕交集。宽或高不为 1、逻辑与物理尺寸不一致等情况保留兼容附件路径。Scene View、Preview、Reflection 保持 100%；现有 targetTexture、共享屏幕的相机叠加、XR 和动态分辨率保留原目标尺寸，属于本版兼容边界。

## 附件流程

| 场景条件 | 场景颜色 | 场景 Depth/Stencil | 最终输出 |
| --- | --- | --- | --- |
| 100% + 1x，原生路径 | CameraTarget：Clear → Store | Clear → DontCare | 直接输出，无中间颜色与 FinalCopy |
| 100% + 2x/4x/8x，完整零原点 viewport，允许直接 Resolve | MSAA RT：Clear → Resolve 到屏幕显示缓冲，不 Store 多采样结果 | Clear → DontCare | 无单采样中间颜色 RT、无 FinalCopy |
| 小于 100% + 1x，原生路径 | 单采样场景 RT：Clear → Store | Clear → DontCare | 按 Auto 过滤放大到原输出尺寸 |
| 2x/4x/8x，有缩放、viewport 偏移或不允许直接 Resolve | MSAA RT：Clear → Resolve 到 1x RT，只 Resolve，不 Store 多采样结果 | Clear → DontCare | 单采样 Copy；偏移时仍保留符合条件的原生 / memoryless 附件 |
| 关闭附件优化但有缩放 / MSAA、小视口需中间颜色，或 Game View Gizmos | 普通场景 RT：Clear → Store，按需显式 Resolve | Store，保留到 Gizmos 完成 | 普通拷贝/放大，RenderScale / MSAA 仍生效 |

不透明、Skybox、透明对象及 Camera/World Space Canvas 在同一场景目标中绘制。Stencil 保留到透明 UI 完成，原生 Pass 结束时才丢弃；支持时使用 memoryless 深度。兼容路径保留深度供 Gizmos 使用。

原生 Pass 的 MSAA 颜色在设备支持时请求 `RenderTextureMemoryless.MSAA`，只在 Pass 结束时 Resolve，不 Load / Store 多采样内容；不额外设置 `Color` 标记。100% 直接 Resolve 到屏幕与缩放后 Resolve 到普通 1x RT 都可使用；兼容路径跨 Pass 保存颜色，仍用普通 RT。若创建失败、实际样本数/格式不匹配或 memoryless 标记未生效，释放该附件并以相同尺寸、格式和 MSAA 重试普通 RT；同一配置缓存拒绝结果，避免逐帧试错。普通附件也创建失败时保留原兼容绘制回退。单采样 Resolve 结果始终不是 memoryless，供 FinalCopy 读取。

最终屏幕请求 1x，MSAA 只在场景附件中完成，避免屏幕再次保存/解析多采样颜色。支持该请求的平台即使 `Screen.msaaSamples` 为 0（跟随 Quality），也显式请求 1；首次改变样本数的一帧使用原分辨率兼容路径，退出时原样恢复请求，包括 0 的跟随语义。`Screen.msaaSamples` 不是硬件附件的实测结果。

**Optimize Attachments** 控制原生附件丢弃，不再顺带关闭显式 MSAA。关闭优化后，有缩放或 MSAA 时使用普通附件与 FinalCopy；100% / 1x 才保留原 CameraTarget 路径。独立主相机显式关闭 MSAA 时仍请求系统 1x，避免恢复跟随 Quality 后重新启用其旧 antiAliasing 值。

**Resolve To Screen** 的资产默认值当前开启，游戏运行时由输出模式覆盖；只有允许直出，且为 100%、独立 Game 相机、原生附件路径、完整零原点 viewport、逻辑输出与物理屏幕尺寸一致时才会尝试。非零 x/y 偏移不尝试 MSAA 直接屏幕 Resolve，改为 Resolve → 1x RT → FinalCopy，MSAA 画质请求与符合条件的 memoryless MSAA / 深度仍保留。必须满足 `SystemInfo.supportsMultisampledBackBuffer`，后端限定 Vulkan / iOS Player 的 Metal、LDR Gamma，并排除 Display.requiresBlitToBackbuffer、requiresSrgbBlitToBackbuffer 和 HDR 输出。Linear、GLES3 / GLCore / D3D11 / D3D12 及其它平台的 Metal 保留 Resolve 到普通单采样 RT + FinalCopy，MSAA 档位不变。本机 Windows D3D11 / Editor 的回退属于历史验证；代码没有对 Windows / Editor Vulkan 额外做平台排除，仍按上述能力条件筛选。不能用“能够 Resolve 到普通 RT”推断“能够 Resolve 到屏幕”：Windows D3D11 旧分支曾在 Player.log 中触发 multisampled texture targeting back buffer is not supported，导致场景 Pass 被拒绝和画面残留。直接路径仍检查实际 MSAA RT 格式，设备出现异常时可单独关闭此选项；条件筛选不替代各 Vulkan / iOS Metal 环境的实际画面验证。

Android Player Vulkan 的直接路径每次 `BeginScene` 取 `Display.main.colorBuffer` 并构造显式 RenderTargetIdentifier；只缓存平台 / API 判断，不缓存跨帧显示缓冲。其它后端保留 CameraTarget 句柄。历史 Pad 5 Pro 独立 Probe 和当时生产 Runtime 的完整 viewport Android Fixture 同尺寸对照已确认该替换恢复屏幕输出；它不新增 RT、额外绑定、Draw 或 Submit，也不改变创建失败回退。Fixture 的 4x 直出已显示普通几何、A2C、前后深度覆盖、Stencil Equal 形状、真实 UGUI Camera / World Canvas、Overlay 半透 Panel 与 UGUI Mask。首轮直接 / 拷贝路径的差异集中于几何和 A2C 边缘，所选深度 / Stencil / Canvas ROI 无差异，不能将这项历史结果扩大为当前长屏平移 viewport、全图逐像素一致或完整游戏回归。仍不能据此宣称所有 Android / iOS 设备或真实屏幕格式已被完整验证。

直接路径的颜色输出在场景 Pass 结束时已写入屏幕，不再分配 MRP Scene Color，也不执行全屏三角形；深度/Stencil 仍保持到所有透明内容完成。当前 MRP 没有可采样场景颜色或读取背景的后处理；若以后添加这类效果，应显式要求中间颜色并禁用直接路径，不能从屏幕读回代替。

FinalCopy 的 Game 屏幕输出保留原始源 UV 和恒等 UV scale/bias；targetTexture / Scene View 使用纹理目标方向，在 TopLeft API 上单独翻转源 UV。输出矩形与源方向分开处理：Copy 使用整屏零原点 viewport，`MajFinalCopyMapping` 将原逻辑 pixelRect 归一化为顶点裁剪空间的 scale / bias，Y 平移仅按 `GL.GetGPUProjectionMatrix(Matrix4x4.identity, false).m11` 的符号转换到屏幕 GPU 方向。该符号不翻转整张源图，也不写回相机投影。Fragment 按未翻转的局部 UV 裁剪到 0～1，避免负偏移或超出屏幕时 Clamp 采样把边缘拉进留白；有未覆盖区域时先清整张输出。场景仍使用原相机矩阵，在本地 RT 中不另加平移，避免矩形被重复应用。Screen Space Overlay 由 Unity 在 SRP 后绘制一次，保持完整输出分辨率；Camera/World Space Canvas 随场景缩放。

### 2026-10-17 传感器调试显示开关

`DummyTouchPanelRenderer` 的显示逻辑统一到 Editor / Player：开启 `Debug.DisplaySensor` 后，未触发分区使用黑色 alpha 0.1，触发使用 alpha 0.4。原 Player 分支的 0 / 0.3 已移除。关闭时禁用 34 个分区的 MeshRenderer，构造时也默认禁用，不再靠 alpha 0 隐藏网格；Collider、GameObject 及碰撞体索引映射保持启用，输入查表和触摸不因调试显示关闭而停止。独立 IO 测试页的红蓝显示不在该开关范围内。

开关状态不变且关闭时直接返回；Renderer.enabled 只在开关变化时更新，MaterialPropertyBlock 只在颜色变化时更新，并保留已有的其它属性。本轮 Windows / Android Editor、Player 共 8 个程序集编译通过；实际组件源码与窄 Unity / 输入 stubs 的 Editor、Player 各 20 项、共 40 项检查通过，覆盖默认关闭、淡色 / 触发 / 释放、重新开启、稳定帧不重复写入、35 位原始输入中额外位跳过及 Collider / 索引保留。未运行主工程、Frame Debugger、设备或功耗测试；不将减少的 Renderer 绘制资格描述为已测量的 GPU 毫秒或功耗降幅。

### 2026-10-17 长屏相机与传感器对齐

主 UI 的 Canvas 使用 1080×1920 / Expand，主屏 1080×1080 区域锚定底部；传感器、音符和触摸查表使用固定世界坐标。此前取消长屏 viewport 上移后，相机仍固定正交 Size 9.6 / Y 4.2，导致 1080×2400 时世界分区比 UI 大 25%、中心偏上 135 像素。这是相机与 UI 的适配比例不同；Editor 桌面分辨率覆盖是独立问题。

`ScreenPosCameraMover` 现在使用逻辑屏幕尺寸，与 Canvas Expand 同比例计算运行时正交范围。令 `f = min(W/1080, H/1920)`、序列化基线为 `B`，适配范围为 `S = B × (H/1920) / f`。关闭手调时使用 Size=S、Y=原始Y+(S−B)；开启手调时使用 Size=S/MainScreenScale、Y=1.5+2.7×MainScreenOffset+(S−B)，保留既有 UI 围绕屏幕中心的缩放语义。默认基线 B=9.6，1080×1920 仍为 Size 9.6 / Y 4.2，1080×2400 则为 Size 12 / Y 6.6。屏幕尺寸改变会重新应用相机参数，稳定尺寸不重复写入。

只修改相机布局组件，保留完整 rect，不改变 MRP 场景附件 / RenderScale / MSAA 或直接输出资格。传感器根、固定世界坐标查表、触摸半径和输入时间源不改；输入继续通过同一 Camera.ScreenToWorldPoint 做逆投影。传感器查表初始化后保持原世界位置，不通过独立移动传感器补偿。运行中切换方向 / Game View 比例时，Canvas 手调中心及副屏角点缓存仍有各自的旧刷新边界，本轮不将它们扩展成整体动态布局重构。

本轮 Windows / Android Editor、Player 共 8 个程序集编译通过。隔离 Unity 使用真实 Camera、ScreenSpaceCamera Canvas / CanvasScaler、修改前后实际 CameraMover 和实际 CanvasMover，设置接口用窄 stub，逻辑尺寸由已安装的 ScreenSimulation 提供，目标为同尺寸 RT。20 条前后结果中，修复后 10 组全部通过，覆盖 1080×1920、1080×2400、1220×2712、1920×1080，以及手调 0.8 / 1.2；中心和对角点最大误差小于 0.000159 像素，UI 中心逆投影最大误差小于 0.000001151 世界单位。原始测试因未让 Canvas 经相机帧更新而保留旧世界变换的失败记录仍保留；补充实际 Camera.Render 更新后再测，不修改比较阈值。这是原生几何 / 坐标验证，不是 MRP 像素或手机 Present、实际触摸命中、功耗测试；主工程 Play 和手机仍需用户复测。

组件控制流另用实际源码与窄 Unity / 设置 stubs 验证，5 组 / 50 个固定屏幕启动组合、1494 项检查通过：常规屏、长屏、平板、宽屏，手动缩放 / 偏移、延迟设置、关闭恢复、尺寸变化后的相机重应用及投影 / 逆映射均覆盖；稳定帧不重复写相机，也不写入保存设置。动态尺寸检查只证明相机侧重新应用，不证明未改的 Canvas 中心和副屏角点缓存已经刷新。

### 2026-10-17 Editor 长宽屏拉伸修复

`GameManager.ApplyScreenConfig` 原来的桌面条件编译也包含 Windows / macOS Editor，会把保存的桌面 `Display.Resolution`（本次为 1080×1920）写入 Game View / Device Simulator。Simulator 的内部渲染尺寸可以被这个请求改成 9:16，但外层设备屏幕仍按所选设备的原始比例铺满显示，造成长屏纵向、宽屏横向拉伸。现仅在桌面 Player 应用窗口、全屏和分辨率配置；Editor 尺寸交给 Game View / Simulator，FPS 设置仍执行。没有修改 MRP 附件、MSAA、RenderScale、相机投影或 Canvas 布局。

本轮 Windows / Android Editor、Player 共 8 个程序集编译通过。提取实际修改前后方法体、以窄 Unity 接口 stubs 在 Windows / macOS / Android 的 Editor、Player 六组执行 72 个场景、239 项断言：Editor 不再写屏幕配置，FPS 保留；桌面 Player 每例调用行为与修改前一致，Android 仍只设置 FPS。隔离 Unity 的真实 ScreenSimulation / DeviceView 网格观测确认：1080×2400 设备被请求 1080×1920 后，内部尺寸改变、外层网格仍为 1080×2400；1920×1080 设备同理，足以解释拉伸方向。隔离无窗口 RenderView 尝试未完成画面验证，原始错误日志保留，不能算作 GPU 或主游戏通过。用户随后重新运行主工程 Play，报告画面已恢复；没有本轮真机或功耗结论。

### 2026-10-17 全屏布局调整（历史验证，布局规则保留）

取消 ScreenPosCameraMover 对长屏 viewport 的自动 .12 / .22 上移，主相机初始化归零为完整 viewport。100% 且满足后端、显示缓冲和附件条件时，长屏也可使用 MSAA 直接 Resolve，保留原生 Resolve-only 和支持时的 memoryless；100% / 1x 继续直接绘制。默认构图将回到未自动上移的位置，这是一项布局变化，不承诺与旧默认位置相同。现有手动 MainScreenScale / MainScreenOffset 及保存值保留，它们不改变 viewport；RenderScale 非 100% 仍走 RT + Copy。MRP 的偏移 viewport 兜底没有删除，供其它调用者改变 rect 时使用。

该布局阶段 Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过。工程外将实际 ScreenPosCameraMover.cs 与最小 Unity / DisplayOptions stubs 组合执行，3 组共 739 项检查通过，覆盖 Awake / LateUpdate、延迟设置、旧 .12 / .22 归零、手调开关 / 缩放 / 偏移以及恢复和稳定状态；这些是控制流与赋值检查，不是 Unity 视觉、实际触摸、Canvas 布局或屏幕 Present 验证。MRP 渲染逻辑未在该布局阶段改变，没有重跑下一节的 420 次 GPU 捕获；这些记录均早于当前纹理采样 bias / 单一 Quality 改动，不能作为本轮重新通过，手机实际画面仍待用户复测。

### 2026-10-17 手机偏移修复与验证状态

本节保留取消自动 viewport 偏移前的上一轮修复与验证；其中源码编译、2034 / 1053 项检查和 420 次 GPU 捕获不计为本次全屏布局调整重新通过。

用户报告此前长屏路径在手机上出现 MSAA 下移、兼容 Copy 上移及比例变化时位置不一致。本轮修复针对输出坐标空间的混用：相机逻辑尺寸与物理屏幕尺寸分开；Copy 的矩形映射移入 Shader；非原点 viewport 不再直接 MSAA Resolve 到屏幕。没有修改 Camera / Canvas / 输入，也没有按 SoC 型号添加黑名单；尚不能据此认定 Unity 或驱动内部某一环为已证实根因。偏移 MSAA 会增加一个可采样的 1x Resolve RT 和 Copy，但保留原生 Pass 的 Resolve-only、深度丢弃及支持时的 memoryless 优势。完整 viewport 的直接 Resolve 和 100% / 1x 无中间颜色路径保留。

本轮 Windows / Android 的 Editor、Player 四组共 8 个程序集编译通过；实际新程序集的 2034 项纯逻辑检查、设置 UI 的 1053 项断言通过。隐藏隔离 Editor 在 Windows D3D11 和 Vulkan 各完成 210 次 GPU 捕获，共 420 次：1080×2400 / 1220×2712，viewport y=0 / ±.12 / ±.22，50% / 95% / 100% / 105% / 150%，1x / 4x，覆盖原生附件和普通 Store 的 RT+Copy。每个后端的 100 对原生 / Store 输出全图零阈值一致，100% / 1x Copy 与原 pixelRect 直绘基准全图零阈值一致，留白内侧严格清空；其它缩放的预选梯度位置最大通道差 1/255，在预先保留的 2/255 采样误差内，不能据此称缩放后全图一致。测试使用当前生产附件代码和 FinalCopy Shader，仅把最终 CameraTarget 绑定替换为独立 RT，按纹理目标方向取 GPU 投影符号及源 UV；没有验证 Game 屏幕的原始 UV、真实 Screen / camera.pixelRect getter、Android CameraTarget / Display / Present、主游戏 Canvas 或功耗。手机画面仍由用户后续复测。原始日志、PNG 和对照结果保存在工程外 `mrp-mobile-viewport-repair`，未覆盖旧失败证据。

### 此前长屏离屏验证（历史）

2026-10-17 此前版本的长屏改动通过 Windows / Android 的 Editor / Player 四组源码编译，共 8 个程序集。该历史独立隐藏 D3D11 Editor GPU 检查共 60 次绘制：1080x2400、1220x2712，viewport y=.12 / .22，50% / 100% / 150%，1x / 4x，包含普通 Store、原生附件与 100% 原生直接输出。底部留白内侧无旧帧残留，选取的梯度位置误差为零；24 对原生 RT+Copy / 普通 Store+Copy 全图零阈值一致，4 对 1x 原生直绘与参考一致。4x 直接输出与 Copy 分别有 211 / 195 / 11 / 339 个像素不同，最大通道差 1/255，保留原图及差图，不计作逐像素一致。这些是修改前快照的结果，不算本次修复重新通过。

这 60 项历史 GPU 检查以显式输出 RT 替代 CameraTarget / Display，手动按给定宽高构造 viewport，最终拷贝采用纹理输出方向；没有读取 Android 实际 camera.pixelRect / pixelHeight 与 Screen 尺寸的关系，也没有覆盖 Game 屏幕的原始 UV、真实 backbuffer viewport / Y 方向或 Present。这正是本次手机回归的覆盖缺口。它们还未验证真实相机剔除、Canvas 或主游戏初始化。D3D11 不支持本项目的屏幕直接 Resolve 筛选，测试中的普通 RT Resolve 没有放宽生产后端门槛；本机未实际使用 memoryless，不能据此证明手机片上存储、长屏直出或功耗收益。

RT、CommandBuffer、NativeArray、Material 以及格式/MSAA 能力查询在配置稳定时复用；过滤变化只更新 sampler，不重建纹理。RT 丢失后重新准备，创建失败缓存输入条件以避免逐帧分配抖动。释放管线时统一释放并还原全局状态。

## Live2D MRP 适配

SDK 的中立数据与执行器 `CubismRenderingContext` / `CubismRenderingExecutor` 只依赖引擎类型；完整 SDK 执行器源码保留，但当前 MRP 只调用它的 `RenderSimple`。`Live2D.Cubism.MajRenderPipeline` 适配程序集反向引用 SDK 和 MRP；MRP 本体仍不引用 SDK、URP 或 Core RP。原 URP Feature 保留 GUID，位于独立且按包版本启用的 `Live2D.Cubism.URP` 程序集，卸载 URP 时不参与编译。

部分中立事件 / interceptor 保留 SDK 原有的 URP 命名空间，供 SDK 与可选适配器继续引用；命名空间不代表包依赖。SDK 的可选 URP Feature 在重建、释放时配对 Dispose，避免执行器持有的 Mesh / Material 遗留；三个旧业务 Cubism Feature 已删除，MRP 不调用它们。interceptor 编辑模式初始化直接取 sharedMaterial，运行时仍使用原 material 行为。

xxlb 的 MeshRenderer 使用 Picking 占位材质，实际绘制材质位于 DrawMaterial；直接跑普通 RendererList 不能替代 SDK 绘制。适配器在 Cull 前按当前相机过滤模型并暂时设置 `forceRenderingOff`，防止自动和手动画两次；在相机提交后的 finally 恢复原值，不改模型材质、enabled、layer 或判定时间源。

2026-10-16 起，背景视频不再挂接 Cubism interceptor。Init 的 BG 使用原 SpriteRenderer、BG 排序层及 Queue 2000 材质，在不透明阶段正常绘制；随后 SDK 直接绘制 Live2D，再绘制普通透明队列 / UI。BackgroundVideoController 不再按场景启用拦截器或关闭背景 Renderer，原播放 / 暂停与 Hide / Show 行为保留；仅供该分支使用的场景标签属性也已移除，场景枚举名称 / 数值保持不变。

- 当前桥接仅面向工程实际使用的 Normal/Over、无遮罩、无 Offscreen 模型；小小蓝白的 64 个部件全部满足这一条件。适配器不再包含 Common / Temporary / Mask 字段、RT 创建 / 缓存 / 释放、高级合成分支或 interceptor 调度，`RequiresIntermediateTarget` 恒为 false。未来换用遮罩 / 高级混合模型时需要另行适配，这些能力没有作为 MRP 回退预备。
- 无可绘制 Cubism 组时不添加绘制命令；可见模型按 SDK 内部排序直接 DrawMesh 到当前场景目标，不切换 RT、不复制、不因 Cubism 中断 Native Pass。相机视锥、layer、active、enabled、forceRenderingOff 和 opacity 过滤仍保留。
- 不创建 Cubism 工作 RT，也不执行 Common 的全屏合成；VideoPlayer 的解码纹理与 MRP 因 RenderScale / MSAA 需要的场景附件继续保留。当前模型绘制不会改变这些附件的分辨率、格式、MSAA 或 Resolve 策略。
- 绘制点与当前旧 URP 资产一致：opaque / Skybox 之后、普通透明队列之前。当前简单直绘不切换 RT，也不重新 Load 场景颜色或 Stencil；保持调用者当前 viewport、投影与尺寸 globals，Overlay 继续由引擎最后绘制。相机渲染器中仅在扩展显式请求中间目标时才执行恢复逻辑，当前 Cubism 桥接不走该分支。
- 不修改 Camera 本体的投影、viewport 或坐标换算；模型使用调用者当前目标与相机参数。

本轮最简直绘验证在隐藏 D3D11 Editor 中运行，未操作主项目 Play / Player。当前生产 MRP、SDK、桥接与完整业务程序集使用实际源文件通过 Windows / Android / iOS 的 Editor / Player 六组编译，共 24 项。GPU 使用与生产原文一致的相机渲染器与桥接器，通过实际 targetTexture 兼容路径比较原完整合成与新直绘：三种输出尺寸、1x / 4x，覆盖真实 64 部件模型、背景、透明 UI、额外前景深度试件、隐藏和无模型；60 次绘制及 54 项非像素语义断言通过，无 GPU 异常。这里改变的是离屏输出尺寸，没有放宽生产的相机资格条件来模拟屏幕 RenderScale。

当前工程为 Gamma：1x 旧新结果逐像素一致；去除额外前景试件后的背景 / 模型 / UI 组合，4x 的 50% / 75% 尺寸一致，100% 仅 2 个像素差 1 个 8bit 码值。Linear 使用屏幕代表性的 sRGB 目标时，1x 也一致，4x 不同像素少于约 0.1%，最大 RGB 差 12～15 码值、平均差小于 0.008 码值；alpha 均一致。额外不透明前景与模型相交的 MSAA 边缘也有差异：移除 Common 会改变提前 Resolve 再混合的覆盖计算，不能宣称所有遮挡边界逐像素一致。自定义 Linear UNorm8 输出还存在逐次颜色量化差异，原始失败对照保留，没有放宽阈值计为像素通过。原生屏幕路径、实际视频解码和移动真机功耗未在本轮运行；无效的原生离屏目标诊断不计为验证通过。

## URP 移除边界

资产仍返回 `UniversalPipeline` SubShader 标签，以兼容现有无光照 Shader；标签字符串不会创建 URP，也不是包引用。项目自定义 Shader 的 URP/Core include 已迁移到 `Library/`，MRP Unlit 代替直接使用的 URP Unlit，并保留 `_BaseMap`、`_BaseColor` 与透明混合等材质属性。库保留所需矩阵、UnityPerDraw ABI、传统实例化和采样约定，不移植 Lit、灯光、阴影或 DOTS 路径。

原命名整理阶段的 23 个项目 Shader 菜单名统一使用 `Maj/Common`、`Maj/UI`、`Maj/Notes`、`Maj/Effects` 和 `Maj/Unlit`；当前新增的默认 UI / Sprite 副本使用 `Maj/UI/Default` 与 `Maj/Sprites/Default`。内部 FinalCopy 使用 `Hidden/Maj/FinalCopy`，`MajRenderPipelineAsset.OnValidate` 的 `Shader.Find` 同步更新。只改变 Shader 声明名及对应字符串查找，保留 Shader 程序、Properties、文件位置和 `.meta` / GUID；现有材质按 GUID 继续引用同一 Shader。Live2D / TMP 的 SDK Shader 名称保留，以维持其自身查找约定。

初次迁移前，14 个启用场景的主程序/游戏内容静态可达 13 种依赖 URP 库的项目 Shader 和 1 种 URP Unlit，共 27 个材质；全量项目自定义 Shader 中共有 18 种包依赖。这部分在迁移后不再引用 URP/Core Shader 库。计入旧 Live2D SDK 时，场景初始完整统计为 16 种；项目 Shader 迁移后曾剩 2 种 SDK Shader 依赖，本次 Live2D 移植已将 SDK 全部 7 个包依赖 Shader 本地化。静态引用包含未激活对象和跨场景资源，不能等同于某一帧绘制数量。TMP 与 Unity 内置 Shader 继续使用引擎库。

逐 Shader、材质数量与场景/Resources 引用清单见 [ShaderDependencies](Documentation/ShaderDependencies.md)。

Live2D 的 7 个 URP 依赖 Shader 已改用本地 CubismMajCore / MRP Library，保留原 Shader 名称、属性和资源 GUID。SDK 主程序集不再引用任何 RP 程序集；MRP 与 Cubism 桥接也不需要 URP。GameManager / 画质设置中的 URP 分支已移除，三个旧业务 Cubism RenderFeature 及对应 meta 已删除；隐藏旧 Renderer 中的 Before Feature 子资产、列表和索引映射也同时清除，避免留下 Missing Script。当前 MRP 的设置、画质切换和 Live2D 绘制继续走原 MRP 分支。

共享 `Main Camera.prefab` 已单独移除 UniversalAdditionalCameraData，以及 Init 中仅针对该 Data 的启用覆盖项。Camera fileID、参数、AudioListener、ScreenPosCameraMover、Prefab / Scene meta 和原 Canvas 相机绑定全部保留；删除前后的四个非 Data 组件文档逐一相同。GlobalCamera 仍由 Init 场景添加并设置主相机。URP 的 Inspector 删除菜单会同时提议删除 Camera，不能使用该菜单完成单独清理。

Init 的主相机实例不能整体 Revert：`GlobalCamera` 是该实例额外添加的组件，不在公共 Prefab 中；撤掉后，相机在 Init 切换到 Title 时会被卸载。其他场景使用同一 Prefab 搭配 `PreviewOnlyCamera`，因此不要把 `GlobalCamera` 直接移入公共 Prefab。主相机应继承原 Camera（正交、Size 9.6、Depth -1），保留 GlobalCanvas 对它的引用，而不是再添加默认 Camera。

GraphicsSettings 中的 URP GlobalSettings 注册已清空。原可见 GlobalSettings / DefaultVolumeProfile 与其隐藏备份链均已备份到工程外后清理，其余隐藏旧管线预设保留。URP 包仍安装时，包内 Postprocessor 在域重载或导入 GraphicsSettings 时无条件 Ensure，因此可见资源和注册曾自动重生；确认用户卸载 URP 后，已再次清理重生的两份资产及 meta 和注册。包内共享资源没有手动删除。14 份材质中的 URP Editor AssetVersion 子文档已清理，保留 Material 主体、Shader、属性与 GUID。

`Assets/Rendering/Materials/Addctive.mat` 仍保存原 URP Particles/Lit 的 Shader GUID，卸包后成为未解析的 Shader 引用。全项目未发现其序列化引用、Resources / StreamingAssets 归属、AssetBundle 标签或代码按名加载；实际 `Hold_Effect` 粒子 Prefab 使用内置 Shader 的 `CircleMiss.mat`。按保留材质主体的要求，只删除 Addctive 的迁移标记，未更换其 Shader 或删除材质。卸包前，URP MaterialPostprocessor 确实在重新导入时又生成过 AssetVersion；卸包后已再次清掉该标记，当前材质主体与本次清理前保持相同。

URP 已从 manifest 和 packages-lock 移除；ShaderGraph 17.3.0 仍保留，并通过依赖保留 Core RP 17.3.0。主游戏 / MRP / 中立 Live2D Shader 没有 URP/Core 包路径 include。此前卸包检查的 Bee 编译产物有 MRP / Live2D MRP，已无 Universal 或 Live2D.Cubism.URP 程序集；SDK 的 `URP/Feature` 可选薄适配已由包版本约束停编。`URP/RenderingInterceptor` 保留于 SDK 完整 / 可选路径，当前 MRP 和背景业务已不调用它；不因目录名含 URP 就认定它有包依赖。未启用 SampleScene 的 CameraData / Light2D、未使用的 Addctive / URP Graph、隐藏旧预设和 URPProjectSettings 仍有遗留资源引用，尚未将这些闲置资源全部归档；不能把主程序脱离 URP 等同于全 Assets 零 Missing。`Library/` 的来源和使用条款见其许可证与说明。

23 个项目自定义 Shader 现已全部使用 HLSLPROGRAM。BrightnessAdjust、RadialMask、TouchHoldMask 在 Git 原版中原本使用 CGPROGRAM / UnityCG.cginc，后续已单独迁移到本地 MajCore：矩阵改为 TransformObjectToHClip，颜色由 fixed 改为 half，径向角度继续使用 float 及相同精度的 PI 常量；保持 Properties、Shader 名、采样方式、UV、Blend 和资源 GUID。Brightness 的 _Color 放入 UnityPerMaterial，_Brightness 仍通过 Props；Radial 的整组 Props 保留，用于 RawSpriteRenderer 的 MPB 颜色 / 进度；TouchHoldMask 保留 UnityPerDrawSprite 与 PerDrawSprite 的 SpriteRenderer 颜色 / Flip ABI，_RendererColor.a 仍为遮罩进度。没有改为乘顶点 alpha，也没有额外应用 Flip。该迁移不等于新增 SRP Batcher 优化或已测得功耗收益。

这三个 Shader 在隔离 Windows D3D11 Editor 的 Gamma / Linear、SRP Batcher 开关组合下，共 88 组旧 CG / 新 HLSL GPU A/B 逐字节一致；四种后端 D3D / GLES3 / Vulkan / Metal 的普通与 INSTANCING_ON 顶点 / 片元代表编译共 192 项通过。显式 DrawMeshInstanced 还与两个单独 DrawMesh 的相同矩阵 / 颜色 / 进度参考逐像素一致。保留的旧行为限制：TouchHoldMask 在 SRP Batcher 关闭的 SpriteRenderer 路径中，旧版与新版的 renderer alpha 都不驱动覆盖；开启时 alpha 0 / .25 / .5 / 1 的覆盖递增正常。当前实际 TouchHoldMask.mat 引用的是 RadialMask，旧 TouchHoldMask Shader 暂未发现资源引用。此次未运行主项目 Play、Player 或移动设备，移动端结果仅为编译验证。

迁移不改变视频解码、判定、对象池和游戏时间源。最初保留的背景视频拦截回调已在 2026-10-16 简化中移除，背景恢复普通 SpriteRenderer 绘制，Live2D 使用 SDK 简单直绘。

## Android 指定纹理压缩

Assets/Models/xxlb/小小蓝白.4096/texture_00.png、texture_01.png 和 Assets/Sprites/Game/EffectSprites/EffectAtlas.spriteatlasv2 配置显式 Android ASTC 4x4 平台覆盖。只改变 Android GPU 格式，保留原 Max Size 2048、透明通道、已有 Mipmap 设置及其他平台；图集继续不生成 Mipmap。细线、透明边缘与模型颜色需在设备画面中复核。

ASTC 4x4 的块为 16 字节 / 16 像素，基础容量约为 RGBA32 的 1/4，实际尺寸仍由纹理导入 / 图集打包结果决定。压缩比例不是逐帧带宽或功耗降低比例；不支持 ASTC 的设备可能解压到无压缩格式。外部皮肤继续走原运行时加载和打包路径，不受这三项导入配置影响。

## 验证与设备对照

2026-10-16 用户确认当前主游戏的视频与 Live2D 画面正常；最新最简直绘的隔离结果见上方 Live2D 节。以下是此前各阶段的历史记录，其中复杂合成、遮罩和 interceptor 测试不代表当前 MRP 仍接入这些能力，也不构成当前设备功耗数据。

布局回归的隔离 D3D11 Editor 验证通过 17 次 GPU 请求、57 个 UI / 世界几何 / Sprite 的 CPU-GPU 中心及尺寸对照、51 次屏幕射线与世界平面的反交。固定 SRP Batcher=true、Dynamic Batching=false；1080x1920、相机 Y4.2 / ortho9.6 的完整目标最大误差为 0.000061 像素，偏移 viewport 0 / .12 / .22 的复杂 1x / 4x 及视频绘制中心误差小于 .53 像素。真实 Sprite（980x980、PPU100、父世界 scale .01、子 localScale110）自动绘制和 interceptor 显式绘制均为 1078x1078 像素。旧版模型出视锥导致 BGA 漏画已在隔离测试重现并验证修复。这些测试显式驱动隔离 Editor 的 Canvas 布局，未复现用户主项目的所有界面缩放问题，不能代替其实际 Play 初始化上下文检查。

随后主项目的只读 Play 快照定位到另一项场景配置问题：实际主相机为新加的默认 Camera（透视、Size 5、Depth 0），Mover 的初始化缓存与相机一致，并未丢失。用户改回正交 / Size 9.6 后确认缩放恢复。之后 Revert 的 Title 快照显示 Camera.main=null、SceneSwitcher.MainCamera 已销毁、GlobalCamera 不存在；画面中的 UI 已转为 Overlay。恢复 Init 的原 Camera 场景绑定、Canvas 引用及实例上的 GlobalCamera 后，用户反馈恢复；新的 Game 快照确认主相机在 DontDestroyOnLoad、正交 9.6 / Depth -1、世界原点投到 (540,540)，GlobalCamera 与 Mover 缓存同一 Camera，Main_Display 世界中心回到 (0,0)。这些是本次再次移除 Data 之前的主项目 Editor Play 数据；Data 删除后的画面仍待重新载入 Init 回归。

后续五文件 URP 依赖清理使用主项目真实 Bee 编译配置，Windows / Android 的 Editor / Player 四组各编译 440 个业务源码，均通过；进一步剔除 RP / ShaderGraph / Live2D URP 薄适配等程序集引用后，四组再次通过，共 8 项最终编译、0 错误。三个已删除 Feature 的名称、脚本 GUID 与旧 Renderer 子资产 fileID 在 Assets（含隐藏目录）、Packages、ProjectSettings 中均无残留引用。此次为真实源码离线编译，没有启动 Unity 完整构建或游戏画面回归；不改变前一次 Live2D GPU 验证的适配器和 Shader 源码。

2026-10-15 Live2D 适配在隔离 Unity 6000.3.17f1 / Windows D3D11 Editor 中验证，未启动主工程、Player 或可见测试窗口。Gamma / Linear 各通过 32 次真实 GPU 请求、37 项深入断言，未出现渲染错误；覆盖实际 xxlb 的 64 个 drawable、普通 Native Pass 1x / 4x、复杂合成保留 4x、正常 / 反向遮罩、背景视频所用 interceptor 的合成 Sprite 绘制、隐藏 / 相机层 / 原 forceRenderingOff、禁用自动 Renderer 后显式绘制、128→256 工作 RT 重建，以及跨相机状态恢复。生产 MajCameraRenderer 真实 Cull / RendererList / Submit 路径同样参与验证；同帧上方不透明红标、SDK 模型和下方透明绿标的方向对照通过，Gamma 整图零差，Linear 复杂合成平均通道差小于 0.0003。

本地化 Live2D Shader 与原 URP Shader 在两种色彩空间合计 398 组 A/B、796 次 GPU 请求中逐像素相同。D3D / GLES3 / Vulkan / Metal 每种色彩空间的 192 个代表 Shader stage 均取得非空平台字节码。Windows / Android / iOS 的 Editor / Player 六组配置，分别编译 MRP、SDK 主程序集、MRP 桥接、URP 薄适配与三个业务 wrapper，共 30 项程序集编译通过；MRP / SDK 主程序集的编译引用不含 RP / Core / ShaderGraph 程序集。该轮验证时，210 个生产源码文件与隔离测试快照的 SHA256 一致。

后续资源 / 命名清理通过 14 份材质主体与 meta 不变、23 个 Shader 除声明名外原文与 meta 不变、44 个 Shader 声明名无重复、旧名称无生产字符串引用，以及 GlobalSettings / Profile GUID 无残留引用的静态检查；FinalCopy 的字符串查找已同步。该轮没有修改 Shader 程序，没有重复 GPU 验证，也没有启动主项目 Play 或完整构建。

当时完整桥接的验证限制：真实 xxlb 模型没有原生遮罩或 Offscreen 层级，遮罩使用实际 SDK Renderer 加隔离合成 fixture；Offscreen 的 Shader 数学与真实 RT 池分别验证，尚无实际嵌套 Offscreen 模型端到端覆盖。Scene View 只测 SceneView 类型相机的离屏请求，未测真实编辑器窗口合成。移动端编译与 Shader 字节码不代表 IL2CPP / Xcode 构建或真机 GPU 验证，也没有测当时游戏功耗、频率或带宽。当前最简桥接不接入模型遮罩 / 高级混合，不应把它们列为已经支持的回归项。

以下保留此前附件优化、纹理压缩与 Shader 库迁移阶段的验证记录。

Windows MSAA 重影修复以实际 Player.log 的 back-buffer MSAA 附件不支持错误，以及用户关闭 Resolve To Screen 后重影消失为依据；现在能力检查会让该平台自动保留普通 Resolve RT + FinalCopy。六组 Windows / Android / iOS Editor、Player C# 编译通过（iOS 为静态编译）。隔离 D3D11 Editor 通过公开 SubmitRenderRequest 取得真实 context 并提交 GPU 绘制，生产 BeginScene 的 1/2/4/8x MSAA 保持实际样本数，Resolve 到 1x RT 后使用 FinalCopy 输出到离屏目标；连续六帧、三个位置移动及半透明重复绘制没有旧位置残留，故意不清除的负对照能够检出残影。实测 supportsMultisampledBackBuffer 与 CanResolveToScreen 均为 false。此检查有真实 GPU 像素读回，但不是屏幕 Present 测试；此前仅附件描述/命令录制的检查未覆盖系统 back-buffer 的执行限制。

iOS Metal / memoryless MSAA 扩展通过 Windows / Android Editor、Player 及 iOS 的两组静态编译检查；iOS Player 使用已安装 iOS 模块的 arm64 IL2CPP UnityEngine 元程序集，不代表 Xcode / IL2CPP 构建完成。隔离 Windows D3D11 Editor 的 Gamma / Linear 各 48 组附件矩阵分别通过 1021 / 1028 项检查，包含 8 种受控创建结果、普通 RT 重试、失败清理、构造失败及拒绝缓存。本机不支持 memoryless，失败分支通过工厂控制验证，普通回退 RT 由 Unity 实际创建；尚未验证 Metal 真机的直接 Resolve、实际片上存储或功耗 / 带宽收益。本轮没有启动测试 Player。

直接 Resolve / ASTC 本轮通过 Windows / Android 的 Editor、Player 四组源码编译。隔离 Unity 的 Gamma 附件矩阵 48 组 / 827 项、Linear 回退矩阵 48 组 / 832 项通过，覆盖实际 RT 创建、缓存切换、Resolve 目标、Stencil 附件和直接路径不分配单采样颜色 RT。独立 Windows 验证 Player 构建成功，0 错误、0 警告。

Android ASTC 导入 / 打包检查 14 项通过：两张 Live2D 主图各为 2048x2048、12 mip、5,592,432 字节；EffectAtlas 保留 10 个 Sprite，打为一页 1024x1024、1 mip、1,048,576 字节。三项共约 11.67 MiB，同尺寸 / mip 的 RGBA32 为约 46.67 MiB。Windows Editor 软件解压后的 Live2D 静态边缘 alpha 平均误差约 1.26% / 1.15%，不是无损，也不代替 Android 设备显示验证。

该早期阶段的屏幕像素验证未完成：隐藏运行的测试 Player 没有触发任何相机绘制，不能把全黑截图计作通过；本轮新增的 Pad 5 Pro 独立 Probe 结果单独记录于 Android MSAA 调查文档，不倒算为该早期测试通过。直接路径的方向、Stencil 和 Overlay 由正常可见的 Player / 游戏构建复核；测试未修改主项目场景，也没有对 Android 功耗、频率或实际带宽作测量。

RenderScale 实现阶段已通过 Windows/Android 的 Editor、Player 四组源码编译；隔离 Unity 项目中的 98 项断言通过，覆盖五个真实预设加载、运行时覆盖与默认值隔离、尺寸/过滤/Mip Bias、实际 RT 创建与附件描述。该阶段的隔离小型 Windows Player 构建成功，0 errors、0 warnings。

Shader 库迁移后另验证了 Runtime/Editor 的实际程序集归属、六个管线资产加载、原 Shader 名称/属性与四个迁移材质的参数，以及 Unlit 编辑器的混合/裁剪状态。23 个 Rendering Shader 在 D3D、GLES3、Vulkan 的普通/声明支持的实例化和透明裁剪代表组合完成源码编译；移动片元还通过外部输出模式取得非空编译结果。SRP 环境初始化后，MRP Unlit 的 SRP Batcher 兼容查询返回 `0 / OK`。这些是编译与兼容信息，不是实际批处理命中率或帧耗时。

原 Shader 库迁移阶段的这些检查没有运行完整游戏、提交相机绘制或测试 Android 设备，不能证明最终画面、实际带宽或功耗已通过。尤其需要复核缩放后的画面方向、Camera Canvas 与 Overlay UI，以及不同设备支持的真实 MSAA 样本数。

功耗对照使用相同 Release 构建，关闭 Development Build、Autoconnect Profiler 和 Script Debugging，并统一实际分辨率、MSAA、帧率、谱面/BGA、亮度与热状态。先对照 50%/75%/100% 实际附件尺寸、UI 清晰度、Stencil 与画面方向，再比较稳定区间功耗和带宽。
