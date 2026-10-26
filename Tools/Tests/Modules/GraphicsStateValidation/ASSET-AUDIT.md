# Capture asset audit and native union

`GraphicsStateAssetAudit.cs` is an Editor-only template. Copy it to `Assets/Editor/`
of an idle, isolated Unity **6000.3.17f1** project containing the production storage
assembly and the original shaders with their GUIDs preserved. Import each raw
capture into a separate asset path in that copy. Keep the raw pulled files outside
the project as independent evidence. Do not point another Editor at the open main
checkout, and coordinate Editor runs with device capture: Unity may restart its
ADB server during shutdown.

Example Unity command-line arguments (use absolute project, log and output paths):

```text
-batchmode -nographics -quit -projectPath <isolated-project>
-executeMethod MajdataPlay.Tests.GraphicsStates.GraphicsStateAssetAudit.Run
-gscAuditAssets "Assets/CaptureAudit/Matrix.graphicsstate;Assets/CaptureAudit/Baseline.graphicsstate"
-gscAuditOutput <ignored-report-directory>
-gscUnionOutput <ignored-report-directory>/Union.graphicsstate
-logFile <ignored-report-directory>/audit.log
```

Omit `-gscUnionOutput` for a read-only audit. With that argument, the first input is
the new matrix and subsequent inputs are previous captures whose unique coverage
must survive. Input metadata must have matching runtime platform, graphics API,
quality name and format version. The tool builds a separate native collection
with `AddVariant` and `AddGraphicsStateForVariant`, using Unity's native equality
to retain each full state. It never concatenates binary capture files or mutates
input assets. Repeating the native insertion over every input must add nothing,
which checks that the union contains all source data.

The union is saved only if a baseline added a missing variant or state. An existing
union destination is rejected. If the new matrix already covers the baseline,
`GSC_UNION_UNNECESSARY` is logged and no union file is created. Keep and ship the
new matrix in that case. `GSC_UNION_SAVED` reports exactly how many baseline
variants and states were added when a union is needed.

Reports:

- `collections.tsv`: platform/API/quality/version, variant and state counts,
  unique shader count, and actual sample-count distribution for every input and union.
- `variants.tsv`: shader name, persistent GUID/local ID, asset path, subshader/pass,
  keywords, and state count. Legacy graphics APIs can have variants without states.
- `states.tsv`: each variant's actual sample count, attachment formats/load/store/
  resolve targets, subpass input/output indices and flags, vertex declaration,
  topology, depth state, culling and orientation flags.
- `union-additions.tsv`: native additions from the matrix and each baseline.

Shader references must resolve to persistent assets, and Unity's enumerated counts
must match its reported totals. Attachment target identifiers are diagnostic data;
native collection equality, not string fingerprints, decides whether states match.
The audit does not call `WarmUp` in a desktop Editor for an Android capture.

After saving a union, import that new file as a separate asset in the isolated
project and rerun the audit. Compare its variant/state counts and sample
distribution with the in-memory union report, then include the selected final
asset in an Android APK and verify production warmup reaches completion on the
device. Preserve all original inputs and the pre-import report until those checks
pass. The Editor audit does not establish device warmup or scene rendering success.

## Matrix interpretation

For Vulkan, record the requested **1/2/4/8 samples × Direct/Intermediate** matrix,
and join its per-run lifecycle markers and `MajRenderDiagnostics.OutputStatus`
with the captured state distribution. An 8x request that produces only 4x states
is a device fallback, not proof of 8x PSO coverage. Query the actual color/depth
attachment combination using `GetRenderTextureSupportedMSAASampleCount`; screen
swapchain sample count alone does not identify MRP scene attachment samples.

Unity 6.3's [warmup API documentation](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Experimental.Rendering.GraphicsStateCollection.WarmUp.html)
states that full graphics-state warmup requires `SystemInfo.supportsParallelPSOCreation`;
otherwise it falls back to shader-variant warmup. The Unity Graphics team's
[platform support note](https://discussions.unity.com/t/prevent-shader-compilation-stutters-with-pso-tracing-in-unity-6/951031/1)
identifies OpenGLES as using that compatibility path since Unity 6.1. Query the
actual device capability and inspect the captured variants before reducing a GLES
matrix. In this project's current shaders MSAA is controlled by uniforms and
render state, without MSAA-specific shader keywords; Intermediate also exercises
the separate `Hidden/Maj/FinalCopy` shader. A proposed reduced GLES capture must
therefore cover all relevant scene/material/keyword paths in both output modes
and confirm that a higher-sample run adds no variants. A zero graphics-state count
alone does not prove full variant coverage.

## Audit tool verification

On 2026-10-24 the tool compiled and ran in the isolated `Temp/GSC` project using
Unity 6000.3.17f1. Two identical copies of the real Android Vulkan probe produced
4 variants / 6 states / `1x:6`; the second native union pass added 0 variants and
0 states, and no unnecessary union file was written. The report is retained at
`Temp/GSC/AuditDuplicate` and the Editor log at `Temp/GSC/audit-duplicate.log`.
Final matrix and nontrivial union verification are separate runs.
