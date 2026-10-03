param([string]$CecilPath = 'C:/Program Files/Unity Editors/6000.3.17f1/Editor/Data/Managed/Unity.Cecil.dll')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$output = Join-Path $repoRoot 'Temp/VLCUnityBindingValidation'
& (Join-Path $repoRoot 'Tools/VLCUnity/Prepare-Bindings.ps1') -CecilPath $CecilPath -OutputDirectory $output
$assets = Join-Path $repoRoot 'Assets/Plugins/VLCUnity/Runtime/Plugins'
foreach ($relative in @('LibVLCSharp.dll', 'iOS/LibVLCSharp.dll')) {
    $generated = Join-Path $output $relative
    if ((Get-FileHash -LiteralPath $generated).Hash -ne (Get-FileHash -LiteralPath (Join-Path $assets $relative)).Hash) {
        throw "Generated binding is stale: $relative. Run Prepare-Bindings.ps1 with the same Cecil version."
    }
}

function Get-AllTypes($Types) {
    foreach ($type in $Types) { $type; Get-AllTypes $type.NestedTypes }
}
$shared = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $output 'LibVLCSharp.dll'))
$ios = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $output 'iOS/LibVLCSharp.dll'))
$upstream = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $repoRoot 'Tools/VLCUnity/Bindings/LibVLCSharp.upstream.dll'))

function Get-MethodAssociations($Binding) {
    foreach ($type in (Get-AllTypes $Binding.MainModule.Types)) {
        foreach ($property in $type.Properties) {
            'property:' + $property.FullName + '|' + $property.GetMethod.FullName + '|' + $property.SetMethod.FullName + '|' +
                (($property.OtherMethods | ForEach-Object FullName) -join ';')
        }
        foreach ($event in $type.Events) {
            'event:' + $event.FullName + '|' + $event.AddMethod.FullName + '|' + $event.RemoveMethod.FullName + '|' +
                $event.InvokeMethod.FullName + '|' + (($event.OtherMethods | ForEach-Object FullName) -join ';')
        }
    }
}
try {
    $expectedReferences = ($upstream.MainModule.AssemblyReferences | ForEach-Object FullName) -join "`n"
    $expectedAssociations = (Get-MethodAssociations $upstream) -join "`n"
    $sharedMethods = @{}
    foreach ($type in (Get-AllTypes $shared.MainModule.Types)) {
        foreach ($method in $type.Methods) {
            if ($method.HasPInvokeInfo) { $sharedMethods[$method.FullName] = $method }
        }
    }
    $rewritten = 0
    foreach ($type in (Get-AllTypes $ios.MainModule.Types)) {
        foreach ($method in $type.Methods) {
            if (-not $method.HasPInvokeInfo) { continue }
            $original = $sharedMethods[$method.FullName]
            if (-not $original -or $method.PInvokeInfo.EntryPoint -ne $original.PInvokeInfo.EntryPoint -or
                $method.PInvokeInfo.Attributes -ne $original.PInvokeInfo.Attributes) {
                throw "iOS ABI or marshaling changed: $($method.FullName)"
            }
            $module = $original.PInvokeInfo.Module.Name
            if ($module -in @('libvlc', 'VLCUnityPlugin', 'libc', 'libSystem')) {
                if ($method.PInvokeInfo.Module.Name -ne '__Internal') { throw "iOS import was not rewritten: $($method.FullName)" }
                $rewritten++
            }
            elseif ($method.PInvokeInfo.Module.Name -ne $module) { throw "Unexpected changed import: $($method.FullName)" }
        }
    }
    if ($rewritten -ne 290) { throw "Unexpected iOS import count: $rewritten" }
    foreach ($binding in @($shared, $ios)) {
        $references = ($binding.MainModule.AssemblyReferences | ForEach-Object FullName) -join "`n"
        if ($references -cne $expectedReferences) {
            throw 'Generated bindings must retain only the original assembly references; never import PowerShell host framework types.'
        }
        if (((Get-MethodAssociations $binding) -join "`n") -cne $expectedAssociations) {
            throw 'Metadata canonicalization changed a property or event accessor association.'
        }
        $platform = $binding.MainModule.Types | Where-Object FullName -eq 'LibVLCSharp.PlatformHelper'
        $x64 = ($platform.Methods | Where-Object Name -eq 'get_IsX64BitProcess').Body.Instructions
        if ($x64.Count -ne 4 -or $x64[0].OpCode.Name -ne 'call' -or
            $x64[0].Operand.DeclaringType.FullName -ne 'System.Runtime.InteropServices.RuntimeInformation' -or
            $x64[0].Operand.Name -ne 'get_ProcessArchitecture' -or $x64[1].OpCode.Name -ne 'ldc.i4.1' -or
            $x64[2].OpCode.Name -ne 'ceq' -or $x64[3].OpCode.Name -ne 'ret') {
            throw 'The x64 predicate must compare ProcessArchitecture with Architecture.X64, not pointer size.'
        }
        if ($x64[0].Operand.DeclaringType.Scope.Name -ne 'netstandard' -or
            $x64[0].Operand.ReturnType.Scope.Name -ne 'netstandard') {
            throw 'ProcessArchitecture and Architecture must use the original netstandard scope.'
        }
    }
    $sharedCore = $shared.MainModule.Types | Where-Object FullName -eq 'LibVLCSharp.Core'
    $prefix = ($sharedCore.Methods | Where-Object Name -eq 'InitializeUnity').Body.Instructions
    if ($prefix[0].Operand.Name -ne 'get_IsWindows' -or $prefix[1].OpCode.Name -ne 'brtrue' -or $prefix[2].OpCode.Name -ne 'ret') {
        throw 'The shared loader does not guard its desktop initialization by the host OS.'
    }
    $iosCore = $ios.MainModule.Types | Where-Object FullName -eq 'LibVLCSharp.Core'
    foreach ($name in @('InitializeUnity', 'DisableMessageErrorBox')) {
        $body = ($iosCore.Methods | Where-Object Name -eq $name).Body.Instructions
        if ($body.Count -ne 1 -or $body[0].OpCode.Name -ne 'ret') { throw "iOS still uses desktop initialization: $name" }
    }
    $iosPlatform = $ios.MainModule.Types | Where-Object FullName -eq 'LibVLCSharp.PlatformHelper'
    foreach ($predicate in @('get_IsMac', 'get_IsLinux', 'get_IsLinuxDesktop', 'get_IsWindows')) {
        $body = ($iosPlatform.Methods | Where-Object Name -eq $predicate).Body.Instructions
        $expected = if ($predicate -eq 'get_IsMac') { 'ldc.i4.1' } else { 'ldc.i4.0' }
        if ($body.Count -ne 2 -or $body[0].OpCode.Name -ne $expected -or $body[1].OpCode.Name -ne 'ret') {
            throw "iOS platform predicate must explicitly select the Apple ABI: $predicate"
        }
    }
    Write-Host 'Bindings verified: canonical reproducible outputs, original framework references/accessors, host-aware loader, architecture-aware x64 predicate, Apple iOS ABI, and 290 unchanged imports.'
}
finally { $shared.Dispose(); $ios.Dispose(); $upstream.Dispose() }
