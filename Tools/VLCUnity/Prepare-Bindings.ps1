param(
    # Unity ships Cecil; no NuGet download or native SDK is required.
    [string]$CecilPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/Managed/Unity.Cecil.dll',
    [string]$SourceBinding,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/Plugins' }
if (-not $SourceBinding) { $SourceBinding = Join-Path $PSScriptRoot 'Bindings/LibVLCSharp.upstream.dll' }
if ((Get-FileHash -LiteralPath $SourceBinding -Algorithm SHA256).Hash -ne
    '770E8773FDFE738B3485AE0F323EDA3C05BCFFA55EC2BFDCC4E37F9E5D591101') {
    throw 'The input must be the unchanged, pinned upstream binding in Tools/VLCUnity/Bindings.'
}
if (-not (Test-Path -LiteralPath $CecilPath -PathType Leaf)) {
    throw 'Pass -CecilPath with the Unity editor Data/Managed/Unity.Cecil.dll (on macOS: Unity.app/Contents/Managed/Unity.Cecil.dll).'
}
Add-Type -Path $CecilPath
if (-not ('VlcBindingCanonicalMetadata' -as [type])) {
    Add-Type -Path (Join-Path $PSScriptRoot 'Bindings/CanonicalMetadata.cs')
}

function Get-AllTypes($Types) {
    foreach ($type in $Types) {
        $type
        Get-AllTypes $type.NestedTypes
    }
}

function Write-IfChanged($Assembly, [string]$Destination, [Guid]$ModuleId) {
    $stream = New-Object IO.MemoryStream
    try {
        $writer = New-Object Mono.Cecil.WriterParameters
        $writer.Timestamp = [uint32]0
        $Assembly.MainModule.Mvid = $ModuleId
        $Assembly.Write($stream, $writer)
        $bytes = $stream.ToArray()
        [VlcBindingCanonicalMetadata]::Canonicalize($bytes)
        $same = $false
        if (Test-Path -LiteralPath $Destination) {
            $previous = [IO.File]::ReadAllBytes($Destination)
            $same = [Convert]::ToBase64String($bytes) -ceq [Convert]::ToBase64String($previous)
        }
        if (-not $same) { [IO.File]::WriteAllBytes($Destination, $bytes) }
    }
    finally { $stream.Dispose() }
}

function Clear-Method($Method) {
    $Method.Body.Instructions.Clear()
    $Method.Body.ExceptionHandlers.Clear()
    $Method.Body.Variables.Clear()
    $Method.Body.InitLocals = $false
    $Method.Body.GetILProcessor().Emit([Mono.Cecil.Cil.OpCodes]::Ret)
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'iOS') -Force | Out-Null
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly([IO.Path]::GetFullPath($SourceBinding))
try {
    $version = $assembly.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AssemblyInformationalVersionAttribute' }
    if ($version.ConstructorArguments[0].Value -ne '4.0.0+a296e6f14b326bde2e7796439ea883b6c6040fb9') {
        throw 'This transformation supports only the bundled LibVLCSharp a296e6f14b326bde2e7796439ea883b6c6040fb9 ABI.'
    }
    $types = @(Get-AllTypes $assembly.MainModule.Types)
    $core = $types | Where-Object { $_.FullName -eq 'LibVLCSharp.Core' }
    $platform = $types | Where-Object { $_.FullName -eq 'LibVLCSharp.PlatformHelper' }
    $x64Predicate = $platform.Methods | Where-Object { $_.Name -eq 'get_IsX64BitProcess' }
    $arm64Predicate = $platform.Methods | Where-Object { $_.Name -eq 'get_IsArm64BitProcess' }
    $processArchitecture = ($arm64Predicate.Body.Instructions | Where-Object {
        $_.OpCode -eq [Mono.Cecil.Cil.OpCodes]::Call -and $_.Operand.Name -eq 'get_ProcessArchitecture'
    }).Operand
    if (-not $processArchitecture) { throw 'Missing expected RuntimeInformation.ProcessArchitecture reference.' }
    # A 64-bit pointer does not imply x64: selecting the x64 va_list layout on
    # Linux/Android ARM64 can corrupt log callback arguments. Reuse the binding's
    # existing framework reference rather than importing our PowerShell runtime.
    Clear-Method $x64Predicate
    $x64Il = $x64Predicate.Body.GetILProcessor()
    $x64Ret = $x64Predicate.Body.Instructions[0]
    $x64Il.InsertBefore($x64Ret, $x64Il.Create([Mono.Cecil.Cil.OpCodes]::Call, $processArchitecture))
    $x64Il.InsertBefore($x64Ret, $x64Il.Create([Mono.Cecil.Cil.OpCodes]::Ldc_I4_1)) # Architecture.X64
    $x64Il.InsertBefore($x64Ret, $x64Il.Create([Mono.Cecil.Cil.OpCodes]::Ceq))
    $initialize = $core.Methods | Where-Object { $_.Name -eq 'InitializeUnity' }
    if (-not $initialize.HasBody) { throw 'Missing expected Core.InitializeUnity body.' }
    if (@($assembly.MainModule.ModuleReferences | Where-Object { $_.Name -eq '__Internal' }).Count) {
        throw 'Use the shared/original binding as input, not the generated iOS binding.'
    }
    $il = $initialize.Body.GetILProcessor()
    $first = $initialize.Body.Instructions[0]
    # Idempotent prefix: keep the exact upstream Windows loader, while Unity and
    # VlcRuntime resolve portable libraries and module paths on other hosts.
    # The upstream macOS loader hardcodes obsolete paths and Windows separators.
    $alreadyPatched = $initialize.Body.Instructions.Count -gt 3 -and
        $initialize.Body.Instructions[1].OpCode -eq [Mono.Cecil.Cil.OpCodes]::Brtrue -and
        $initialize.Body.Instructions[2].OpCode -eq [Mono.Cecil.Cil.OpCodes]::Ret
    if (-not $alreadyPatched) {
        $windowsPredicate = $platform.Methods |
            Where-Object { $_.Name -eq 'get_IsWindows' }
        $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Call, $windowsPredicate))
        $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Brtrue, $first))
        $il.InsertBefore($first, $il.Create([Mono.Cecil.Cil.OpCodes]::Ret))
    }
    Write-IfChanged $assembly (Join-Path $OutputDirectory 'LibVLCSharp.dll') ([Guid]'b7ee130a-2b41-49de-855e-2271e2d878fc')

    # IL2CPP resolves these symbols from the linked executable/UnityFramework.
    # Do not change entry points or marshaling; only the native module changes.
    $internal = New-Object Mono.Cecil.ModuleReference('__Internal')
    $assembly.MainModule.ModuleReferences.Add($internal)
    $rewritten = 0
    foreach ($type in $types) {
        foreach ($method in $type.Methods) {
            if ($method.HasPInvokeInfo -and $method.PInvokeInfo.Module.Name -in @('libvlc', 'VLCUnityPlugin', 'libc', 'libSystem')) {
                $method.PInvokeInfo.Module = $internal
                $rewritten++
            }
        }
    }
    Clear-Method $initialize
    Clear-Method ($core.Methods | Where-Object { $_.Name -eq 'DisableMessageErrorBox' })
    # Unity iOS RuntimeInformation need not report OSPlatform.OSX. Route libc and
    # variadic logging through the Apple ABI explicitly for this iOS-only DLL.
    foreach ($predicate in @('get_IsMac', 'get_IsLinux', 'get_IsLinuxDesktop', 'get_IsWindows')) {
        $method = $platform.Methods | Where-Object { $_.Name -eq $predicate }
        Clear-Method $method
        $predicateIl = $method.Body.GetILProcessor()
        $constant = if ($predicate -eq 'get_IsMac') {
            [Mono.Cecil.Cil.OpCodes]::Ldc_I4_1
        } else {
            [Mono.Cecil.Cil.OpCodes]::Ldc_I4_0
        }
        $predicateIl.InsertBefore($method.Body.Instructions[0], $predicateIl.Create($constant))
    }
    Write-IfChanged $assembly (Join-Path $OutputDirectory 'iOS/LibVLCSharp.dll') ([Guid]'f995636d-5679-4e8e-9ea7-83fe4e9f8616')
    Write-Host "Prepared shared and iOS bindings ($rewritten iOS native imports)."
}
finally { $assembly.Dispose() }
