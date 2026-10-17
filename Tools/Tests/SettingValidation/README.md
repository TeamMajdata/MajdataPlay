> 2026-10-18 最新菜单可见性：普通档位仅显示画质、帧率、桌面垂直同步和图形兼容模式；两种分辨率缩放与全部实时调试信息只在自定义且非兼容模式显示。兼容模式保留帧率、桌面 VSync 和兼容开关。自定义中，系统缩放不支持的平台仍显示「不支持」并拒绝调节。以下旧记录中普通档可见的描述已被本条替代。本轮设置回归 1212 项断言通过，未运行主游戏或真机。

2026-10-18：菜单预期更新为普通预设 / 兼容模式保留帧率、桌面 VSync 和只读路径 / 尺寸 / 系统状态，Windows 保留系统缩放行并显示「不支持」。新增尺寸发布、缓存文本、只读输入及失效检查；本轮 1226 项断言通过。原生系统 API 状态用工程外窄 stub 另测，JSON 迁移用新编译实际 DLL 另测；本工具不证明真机渲染或功耗。

# Setting behavior validation

From the repository root, with .NET SDK 9 or newer installed:

```powershell
$validationRoot = 'C:/Users/iceCloud/Documents/Codex workspaces/2026-10-17/mrp-mip-bias/Tests'
dotnet run --project Tools/Tests/SettingValidation/SettingValidation.csproj `
  "-p:BaseIntermediateOutputPath=$validationRoot/obj/" `
  "-p:MSBuildProjectExtensionsPath=$validationRoot/obj/" `
  "-p:OutputPath=$validationRoot/bin/"
```

The project links the production `Menu`, `Option`, `SettingManager`, option
enumerators, `RenderingOptions`, `MajRenderDiagnostics`, settings attributes, and input repeat
implementation directly. It uses no NuGet packages. The command above writes all
build output outside the Unity project; omitting these properties retains the
older `Temp/SettingValidation` default. Rendering translations are copied from
the four actual project language tables into this external output directory.

The harness checks:

- One shared pool creates four option views while traversing settings and
  categories; stationary menus show at most three, with four during a slide.
- Rapid reversal, cancellation, menu disable/re-enable, and category transitions
  return and reuse the existing views.
- External numeric, boolean, enum, nullable boolean, and read-only value changes
  update the display. Input received before the next display poll uses the latest
  numeric value; offscreen changes are picked up on rebind.
- 120 unchanged frames perform no TMP text assignments; localization refreshes
  text without accumulating event subscriptions.
- Returning a view preserves its enumerator; destroying its menu disposes it.
- Changing offset units while the offset is zero updates step and description;
  FPS limit, VSync and audio enumerators synchronize external changes and preserve
  side effects. FPS covers a finite limit and the unlimited `-1` value. Display
  stubs no longer expose the obsolete quality or render-scale settings; the
  remaining legacy enum is linked from its actual production migration definition.
- Reflection arrays, option descriptors, attributes, and enum values are reused
  across scene recreation without retaining old settings instances. Every menu's
  enumerators are initialized before input; first traversal creates no additional
  enumerators or attribute instances.
- Eager preparation preserves a deserialized note-mask selection; activation
  callbacks do not reparent pooled views inside a changing hierarchy.
- Named rendering presets expose only quality and compatibility. Quality cycling
  in either direction skips Custom, while retaining its label for localization.
  Custom exposes seven rows, including RenderScale and read-only CurrentOutputPath. Initializing, refreshing,
  rebinding or localizing an option does not call ApplyRenderingSettings; a changed
  input calls it once.
- Compatibility mode shows only its switch and preserves the stored preset or
  custom values. Disabling it restores the seven Custom rows with focus on the switch.
  Repeated transitions, reactivation, and crossing category boundaries reuse the
  original four views without leaking localization subscriptions.
  Effective texture quality is now a sampling LOD bias: Low `+2`, Medium `+1`,
  High `0`, with compatibility using `+2`. This harness checks the model's
  MipmapBias value; it does not measure GPU LOD selection or memory use.
- Ten independent A8 / key 8 press edges on the rendering quality row enter Custom,
  apply immediately and request a save before leaving Setting. Button and sensor
  edges on the same frame count once; overlapping or directly handed-off channels
  in later frames remain one contact until both A8 channels release. A long held
  press contributes its initial edge without repeating into an unlock. Other input,
  a changed preset, option/category navigation, or disabling the menu interrupts
  the gesture. A1 / key 1 retains calibration behavior in every context and takes
  priority over simultaneous A8. A8 outside the available quality row does nothing.
- The production InputControlState reproduces the former fast-click loss: ten
  physical press/release pairs at 60 Hz yield only two delayed ClickCompleted
  events because its animation notification has one pending slot. The corrected
  setting entry unlocks on exactly the tenth observable press for both button and
  sensor input, with no global input change. Holding A8 while entering the quality
  row remains suppressed until both channels release; unrelated control releases
  do not reset the count. Navigation and A8 on the same frame discard that press
  instead of counting a late second-channel report after returning to the row.
- Custom RenderScale shows a percent suffix and changes in 5% steps between 50%
  and 150%, with no boundary wrap or repeated Apply. Non-100% forces intermediate
  output. Attempts to select Direct there leave the value and enumerator Current
  synchronized without Apply. Returning to 100% retains intermediate output until
  the user selects Direct explicitly. Compatibility restores the saved custom scale.
- English, Simplified Chinese, Traditional Chinese, and Japanese rendering names,
  descriptions and every enum label exist in the actual tables. Hidden options
  still participate in font warmup while compatibility mode is enabled, including
  all eleven output-status values. Line breaks are layout controls and do not require glyphs.
- CurrentOutputPath uses the actual diagnostics state and production default enum
  enumerator. Each accepted status refreshes its visible label; localization and
  pooled rebind retain the latest value. Either input direction is read-only and
  does not apply rendering settings or request a save. Unchanged frames do not
  rewrite TMP text; compatibility and named presets continue to hide this row.
  Beginning another context in the same frame preserves the accepted main-screen
  status; the next frame without a main-screen submission invalidates it.
- The production CurrentOutputPath property has no setter, ReadOnlyOption, and
  JsonIgnore metadata. This harness uses a narrow JsonIgnore attribute stub;
  actual Newtonsoft JSON exclusion is checked separately, not by this assertion.

Expected result: `SETTING_VALIDATION_PASSED` with an assertion count.
The 2026-10-17 sampling-bias run passed all thirteen groups, totaling 1057
assertions. Low / Medium / High return additive LOD biases `2` / `1` / `0`,
and compatibility returns `2`. All existing menu and language checks remain enabled.
The earlier 2026-10-17 legacy-quality cleanup run passed all thirteen groups, totaling
1054 assertions. The engine-number check now targets the live FPS setting;
obsolete Display quality/scale and pipeline stubs are removed. Existing menu,
rendering, compatibility, A8 input and language checks remain enabled.
The earlier 2026-10-17 input run passed all thirteen groups, totaling 1043 assertions,
including fast physical presses and cross-frame A8 deduplication. The earlier
934-assertion run predates this press-edge correction.
The earlier 2026-10-17 advanced-menu run passed eleven groups, totaling 802 assertions.
It predates CurrentOutputPath; its six-row Custom expectations are now seven rows.
The earlier nine-group / 688-assertion run predates the hidden Custom entry and
visible custom resolution control; its old visibility expectations were updated.

Unity objects, TMP, LitMotion scheduling, localization, and external game services
are narrow test doubles. The tests check production control flow and TMP setter
calls, not rendered pixels, Canvas rebuilds, real LitMotion timing, or Unity's
native lifecycle. GameSetting is a narrow category-layout stub here; actual JSON
migration and Unity rendering application require separate checks. The rendering
apply counter verifies call timing, not GPU state or output pixels. Compile the project with Unity and inspect Setting in Play Mode
for those integration checks.

[Setting text validation](../SettingTextValidation.md) additionally runs the
production tint, category title, and font warmup against real TMP in an isolated
Unity project, including dirty callbacks and fallback glyph meshes.
