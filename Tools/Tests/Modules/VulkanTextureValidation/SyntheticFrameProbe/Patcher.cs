#nullable enable
using System;
using System.Collections.Generic;
using Path = System.IO.Path;
using FileSystem = MajdataPlay.IO.Storage.FileSystem;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MajdataPlay.Validation.SyntheticFrameProbe
{
    /// <summary>Patches exactly one BGManager entry point in a new isolated Mono Player.</summary>
    internal static class Patcher
    {
        /// <summary>Identifies the sole permitted background type.</summary>
        private const string BackgroundType = "MajdataPlay.Scenes.Game.BGManager";
        /// <summary>Identifies the helper assembly and installed static entry point.</summary>
        private const string HelperName = "MajdataPlay.SyntheticFrameProbe";

        /// <summary>Validates, patches, rereads and records the isolated assembly.</summary>
        /// <param name="args">The isolated Player directory, original Build directory, and compiled helper DLL.</param>
        /// <returns>Zero after successful validation; one when a precondition or verification fails.</returns>
        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--disable-video-prepares")
                {
                    DisableVideoPrepares(args);
                }
                else if (args.Length > 0 && args[0] == "--disable-native-render-pass")
                {
                    DisableNativeRenderPass(args);
                }
                else
                {
                    Run(args);
                }
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("PATCH FAIL: " + error);
                return 1;
            }
        }

        /// <summary>Applies the single-method patch after validating all source and destination constraints.</summary>
        /// <param name="args">The three required absolute filesystem paths.</param>
        /// <exception cref="InvalidOperationException">The ABI, path, reference, or final verification does not match.</exception>
        private static void Run(string[] args)
        {
            if (args.Length != 3)
            {
                throw new InvalidOperationException("Usage: Patcher <MainSynthetic> <original Build> <helper.dll>");
            }

            var player = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[0]));
            var original = Path.GetFullPath(args[1]);
            var helperFile = Path.GetFullPath(args[2]);
            var work = Path.GetDirectoryName(player);
            var module = work == null ? null : Path.GetDirectoryName(work);
            if (Path.GetFileName(player) != "MainSynthetic" || Path.GetFileName(work) != ".work"
                || Path.GetFileName(module) != "VulkanTextureValidation")
            {
                throw new InvalidOperationException("Only VulkanTextureValidation/.work/MainSynthetic may be patched.");
            }

            for (var ancestor = player; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            {
                if (FileSystem.GetEntry(ancestor)?.IsSymbolicLink == true)
                {
                    throw new InvalidOperationException("The target or an ancestor is a reparse point.");
                }
            }

            var managed = Path.Combine(player, "MajdataPlay_Data", "Managed");
            var target = Path.Combine(managed, "MajdataPlay.Core.dll");
            var source = Path.Combine(original, "MajdataPlay_Data", "Managed", "MajdataPlay.Core.dll");
            var destinationHelper = Path.Combine(managed, HelperName + ".dll");
            var evidence = Path.Combine(player, "patch-evidence");
            if (!FileExists(Path.Combine(player, "fixture-manifest.json")) || FileExists(Path.Combine(player, "patch-manifest.json"))
                || FileExists(destinationHelper) || FileExists(Path.Combine(player, "Runtime", "runtime.db")))
            {
                throw new InvalidOperationException("A fresh prepared fixture without runtime.db or a prior patch is required.");
            }

            var sourceHash = Hash(source);
            if (Hash(target) != sourceHash)
            {
                throw new InvalidOperationException("The copied Core assembly is not byte-identical to original Build.");
            }

            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            resolver.AddSearchDirectory(Path.GetDirectoryName(helperFile)!);
            var reader = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadSymbols = false };
            using var core = AssemblyDefinition.ReadAssembly(target, reader);
            using var helper = AssemblyDefinition.ReadAssembly(helperFile, reader);
            if (core.Name.HasPublicKey || helper.Name.HasPublicKey || helper.Name.Name != HelperName)
            {
                throw new InvalidOperationException("Strong names or unexpected helper identity are unsupported.");
            }

            if (core.MainModule.AssemblyReferences.Any(reference => reference.Name == HelperName))
            {
                throw new InvalidOperationException("The Core assembly was already patched.");
            }

            ValidateReferences(helper.MainModule, managed, reader);

            var background = core.MainModule.Types.Single(type => type.FullName == BackgroundType);
            var requiredFields = new Dictionary<string, string>
            {
                ["_videoPlayer"] = "MajdataPlay.FFmpeg.FFmpegVideoPlayer",
                ["_coverRenderer"] = "UnityEngine.UI.Image", ["_videoRenderer"] = "UnityEngine.UI.RawImage",
                ["_backgroundMaterial"] = "UnityEngine.Material", ["_usePictureAsBackground"] = "System.Boolean",
                ["_destroyed"] = "System.Boolean", ["_videoLoadVersion"] = "System.Int32"
            };
            foreach (var field in requiredFields)
            {
                if (!background.Fields.Any(candidate => candidate.Name == field.Key && candidate.IsPrivate
                    && candidate.FieldType.FullName == field.Value))
                {
                    throw new InvalidOperationException("Background field ABI mismatch: " + field.Key);
                }
            }

            var method = background.Methods.Single(candidate => candidate.Name == "SetMovieAsync");
            if (method.IsStatic || method.ReturnType.FullName != "Cysharp.Threading.Tasks.UniTask"
                || method.ReturnType.Scope.Name != "UniTask" || method.Parameters.Count != 2
                || method.Parameters[0].ParameterType.FullName != "System.String"
                || method.Parameters[1].ParameterType.FullName != "UnityEngine.Sprite"
                || !method.Body.Instructions.Any(instruction => instruction.Operand?.ToString()?.Contains("AsyncUniTaskMethodBuilder::Start") == true))
            {
                throw new InvalidOperationException("Unexpected SetMovieAsync signature or original entry IL.");
            }

            var install = helper.MainModule.Types.Single(type => type.FullName == "MajdataPlay.Validation.SyntheticFrameProbe.Helper")
                .Methods.Single(candidate => candidate.Name == "Install");
            if (!install.IsStatic || !install.IsPublic || install.ReturnType.FullName != "System.Void"
                || install.Parameters.Count != 1 || install.Parameters[0].ParameterType.FullName != "UnityEngine.MonoBehaviour")
            {
                throw new InvalidOperationException("Unexpected helper Install ABI.");
            }

            var before = Snapshot(core.MainModule);
            var oldReferences = core.MainModule.AssemblyReferences.Select(Identity).ToArray();
            var originalIl = method.Body.Instructions.Select(instruction => instruction.ToString()).ToArray();
            var methodName = method.FullName;
            FileSystem.Directory.Create(evidence);
            var backup = Path.Combine(evidence, "MajdataPlay.Core.original.dll");
            if (!FileExists(backup))
            {
                FileSystem.File.Copy(target, backup);
            }
            else if (Hash(backup) != sourceHash)
            {
                throw new InvalidOperationException("The existing preflight backup does not match original Core.");
            }
            var body = new MethodBody(method) { InitLocals = true };
            method.Body = body;
            var task = new VariableDefinition(method.ReturnType);
            body.Variables.Add(task);
            var helperReference = new AssemblyNameReference(helper.Name.Name, helper.Name.Version);
            core.MainModule.AssemblyReferences.Add(helperReference);
            var helperType = new TypeReference("MajdataPlay.Validation.SyntheticFrameProbe", "Helper", core.MainModule, helperReference);
            var installReference = new MethodReference("Install", core.MainModule.TypeSystem.Void, helperType)
            {
                HasThis = false
            };
            installReference.Parameters.Add(new ParameterDefinition(core.MainModule.GetTypeReferences()
                .First(type => type.FullName == "UnityEngine.MonoBehaviour")));
            var il = body.GetILProcessor();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, installReference);
            il.Emit(OpCodes.Ldloca_S, task);
            il.Emit(OpCodes.Initobj, method.ReturnType);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Ret);
            var temporary = Path.Combine(evidence, "MajdataPlay.Core.patched.dll");
            core.Write(temporary, new WriterParameters { WriteSymbols = false });
            using var verified = AssemblyDefinition.ReadAssembly(temporary, reader);
            var after = Snapshot(verified.MainModule);
            var changed = before.Keys.Where(key => !after.TryGetValue(key, out var value) || before[key] != value).ToArray();
            if (after.Count != before.Count || changed.Length != 1 || changed[0] != methodName)
            {
                throw new InvalidOperationException("Verification found changes outside the single permitted method.");
            }

            var addedReferences = verified.MainModule.AssemblyReferences.Select(Identity)
                .Except(oldReferences).ToArray();
            if (addedReferences.Length != 1 || !addedReferences[0].StartsWith(HelperName + ",", StringComparison.Ordinal)
                || !oldReferences.All(reference => verified.MainModule.AssemblyReferences.Any(candidate => Identity(candidate) == reference)))
            {
                throw new InvalidOperationException("Unexpected assembly reference changes.");
            }

            var patchedMethod = verified.MainModule.Types.Single(type => type.FullName == BackgroundType)
                .Methods.Single(candidate => candidate.Name == "SetMovieAsync");
            var patchedIl = patchedMethod.Body.Instructions.Select(instruction => instruction.ToString()).ToArray();
            // The absolute target was checked above and remains inside the fresh MainSynthetic managed directory.
            FileSystem.File.Copy(helperFile, destinationHelper, overwrite: false);
            FileSystem.File.Copy(temporary, target, overwrite: true);
            if (Hash(source) != sourceHash)
            {
                throw new InvalidOperationException("Original Build Core changed during patching.");
            }

            var manifest = new
            {
                PatchedAt = DateTimeOffset.Now, SourceCore = source, TargetCore = target,
                OriginalCoreSHA256 = sourceHash, BackupSHA256 = Hash(Path.Combine(evidence, "MajdataPlay.Core.original.dll")),
                PatchedCoreSHA256 = Hash(target), HelperSHA256 = Hash(destinationHelper),
                ChangedMethods = changed, AddedReferences = addedReferences,
                HelperReferences = helper.MainModule.AssemblyReferences.Select(Identity).ToArray(),
                OriginalEntryIL = originalIl, PatchedEntryIL = patchedIl,
                Width = 854, Height = 480, UploadHz = 60,
                Scope = "Only Game BGManager.SetMovieAsync is replaced; global FFmpeg backgrounds remain.",
                Limitation = "Video CurrentSec remains zero in diagnostic text; Canvas upload phase differs from player Update."
            };
            FileSystem.File.Create(Path.Combine(player, "patch-manifest.json")).WriteAllText(JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PATCH DONE: " + target);
            Console.WriteLine("Changed method count: " + changed.Length + "; original Build Core SHA256 unchanged: " + sourceHash);
        }

        /// <summary>Optionally disables all FFmpeg video preparation in the already patched synthetic fixture.</summary>
        /// <param name="args">The mode flag, synthetic fixture, original Build, and optional --verify-only flag.</param>
        /// <exception cref="InvalidOperationException">The fixture, process state, source hash, ABI, or single-method verification does not match.</exception>
        private static void DisableVideoPrepares(string[] args)
        {
            if ((args.Length != 3 && args.Length != 4) || (args.Length == 4 && args[3] != "--verify-only"))
            {
                throw new InvalidOperationException("Usage: Patcher --disable-video-prepares <MainSynthetic> <original Build> [--verify-only]");
            }

            var verifyOnly = args.Length == 4;
            var player = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
            var original = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2]));
            var work = Path.GetDirectoryName(player);
            var module = work == null ? null : Path.GetDirectoryName(work);
            if (Path.GetFileName(player) != "MainSynthetic" || Path.GetFileName(work) != ".work"
                || Path.GetFileName(module) != "VulkanTextureValidation")
            {
                throw new InvalidOperationException("Only VulkanTextureValidation/.work/MainSynthetic may be patched.");
            }

            var managed = Path.Combine(player, "MajdataPlay_Data", "Managed");
            var target = Path.Combine(managed, "MajdataPlay.FFmpeg.dll");
            for (var ancestor = target; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
            {
                if (FileSystem.GetEntry(ancestor)?.IsSymbolicLink == true)
                {
                    throw new InvalidOperationException("The target or an ancestor is a reparse point.");
                }
            }
            EnsureFixtureStopped(player);

            var fixtureManifestPath = Path.Combine(player, "fixture-manifest.json");
            var coreManifestPath = Path.Combine(player, "patch-manifest.json");
            using var fixtureManifest = JsonDocument.Parse(FileSystem.File.Open(fixtureManifestPath).ReadAllText());
            using var coreManifest = JsonDocument.Parse(FileSystem.File.Open(coreManifestPath).ReadAllText());
            var declaredSource = fixtureManifest.RootElement.GetProperty("Source").GetString();
            const string expectedCore = "84BFFF325022DE2E86CE2B5757FBEF9BDB159C70FBAEE36DD9CEB2525AB423FF";
            const string expectedSourceCore = "F016EC0F63AA7A502C06F11FB4D87D5AF5347E84C34E4243092115437E6B9F88";
            const string expectedFFmpeg = "9A7BA95585BA358D48CFC345615E771F8724275FF26D52CFE8B222B06B13F870";
            const string backgroundEntry = "Cysharp.Threading.Tasks.UniTask MajdataPlay.Scenes.Game.BGManager::SetMovieAsync(System.String,UnityEngine.Sprite)";
            var corePath = Path.Combine(managed, "MajdataPlay.Core.dll");
            var sourceManaged = Path.Combine(original, "MajdataPlay_Data", "Managed");
            var sourceCore = Path.Combine(sourceManaged, "MajdataPlay.Core.dll");
            var source = Path.Combine(sourceManaged, "MajdataPlay.FFmpeg.dll");
            var changedCoreMethods = coreManifest.RootElement.GetProperty("ChangedMethods");
            if (declaredSource == null || !string.Equals(original,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(declaredSource)), StringComparison.OrdinalIgnoreCase)
                || coreManifest.RootElement.GetProperty("PatchedCoreSHA256").GetString() != expectedCore
                || coreManifest.RootElement.GetProperty("OriginalCoreSHA256").GetString() != expectedSourceCore
                || changedCoreMethods.GetArrayLength() != 1 || changedCoreMethods[0].GetString() != backgroundEntry
                || Hash(corePath) != expectedCore || Hash(sourceCore) != expectedSourceCore)
            {
                throw new InvalidOperationException("The source Build or existing single-method Core patch evidence does not match.");
            }
            if (Hash(source) != expectedFFmpeg || Hash(target) != expectedFFmpeg)
            {
                throw new InvalidOperationException("The original Build and copied FFmpeg DLL must match the recorded unmodified source SHA256.");
            }
            var coreManifestHash = Hash(coreManifestPath);
            var helperPath = Path.Combine(managed, HelperName + ".dll");
            var helperHash = Hash(helperPath);

            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            var reader = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadSymbols = false };
            using var ffmpeg = AssemblyDefinition.ReadAssembly(target, reader);
            using var sourceAssembly = AssemblyDefinition.ReadAssembly(source, reader);
            if (ffmpeg.Name.HasPublicKey || sourceAssembly.Name.HasPublicKey
                || ffmpeg.Name.Name != "MajdataPlay.FFmpeg" || Identity(ffmpeg.Name) != Identity(sourceAssembly.Name))
            {
                throw new InvalidOperationException("Strong names or unexpected FFmpeg assembly identities are unsupported.");
            }
            var video = ffmpeg.MainModule.Types.Single(type => type.FullName == "MajdataPlay.FFmpeg.FFmpegVideoPlayer");
            var method = video.Methods.Single(candidate => candidate.Name == "PrepareAsync");
            if (!method.IsPublic || method.IsStatic || method.HasGenericParameters || !method.HasBody
                || method.ReturnType.FullName != "System.Threading.Tasks.Task" || method.ReturnType.Scope.Name != "mscorlib"
                || method.Parameters.Count != 1 || method.Parameters[0].ParameterType.FullName != "System.Threading.CancellationToken"
                || method.Parameters[0].ParameterType.Scope.Name != "mscorlib"
                || !method.Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Newobj
                    && instruction.Operand is MethodReference constructor
                    && constructor.DeclaringType.FullName == "MajdataPlay.FFmpeg.Internal.VideoDecodeSession")
                || !method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call
                    && call.Name == "CheckThread" && call.DeclaringType.FullName == video.FullName))
            {
                throw new InvalidOperationException("Unexpected PrepareAsync signature or original decoder-opening IL: "
                    + method.FullName + "; return scope=" + method.ReturnType.Scope.Name
                    + "; parameter scopes=" + string.Join(",", method.Parameters.Select(parameter => parameter.ParameterType.Scope.Name))
                    + "; constructors=" + string.Join(",", method.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Newobj)
                        .Select(instruction => instruction.Operand.ToString())));
            }

            using var bcl = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "mscorlib.dll"), reader);
            var bclReference = ffmpeg.MainModule.AssemblyReferences.Single(reference => reference.Name == "mscorlib");
            if (Identity(bcl.Name) != Identity(bclReference))
            {
                throw new InvalidOperationException("Actual Player mscorlib identity mismatch.");
            }
            var task = bcl.MainModule.Types.Single(type => type.FullName == "System.Threading.Tasks.Task");
            var completedTask = task.Methods.Single(candidate => candidate.Name == "get_CompletedTask"
                && candidate.IsPublic && candidate.IsStatic && candidate.Parameters.Count == 0
                && candidate.ReturnType.FullName == task.FullName);
            var before = Snapshot(ffmpeg.MainModule);
            var oldReferences = ffmpeg.MainModule.AssemblyReferences.Select(Identity).ToArray();
            var originalIl = method.Body.Instructions.Select(instruction => instruction.ToString()).ToArray();
            var methodName = method.FullName;
            method.Body = new MethodBody(method);
            var il = method.Body.GetILProcessor();
            var getter = ffmpeg.MainModule.ImportReference(completedTask);
            if (getter.DeclaringType.Scope is not AssemblyNameReference getterScope || Identity(getterScope) != Identity(bcl.Name))
            {
                throw new InvalidOperationException("CompletedTask getter must be imported from actual Player mscorlib metadata.");
            }
            il.Emit(OpCodes.Call, getter);
            il.Emit(OpCodes.Ret);
            using var buffer = new System.IO.MemoryStream();
            ffmpeg.Write(buffer, new WriterParameters { WriteSymbols = false });
            var bytes = buffer.ToArray();
            using var rereadBuffer = new System.IO.MemoryStream(bytes, writable: false);
            using var verified = AssemblyDefinition.ReadAssembly(rereadBuffer, reader);
            var after = Snapshot(verified.MainModule);
            var changed = before.Keys.Where(key => !after.TryGetValue(key, out var value) || before[key] != value).ToArray();
            var patchedMethod = verified.MainModule.Types.Single(type => type.FullName == video.FullName)
                .Methods.Single(candidate => candidate.FullName == methodName);
            var patchedIl = patchedMethod.Body.Instructions.Select(instruction => instruction.ToString()).ToArray();
            if (after.Count != before.Count || changed.Length != 1 || changed[0] != methodName
                || !oldReferences.SequenceEqual(verified.MainModule.AssemblyReferences.Select(Identity))
                || patchedMethod.Body.Instructions.Count != 2 || patchedMethod.Body.Instructions[0].OpCode != OpCodes.Call
                || patchedMethod.Body.Instructions[0].Operand is not MethodReference verifiedGetter
                || verifiedGetter.Name != "get_CompletedTask" || verifiedGetter.DeclaringType.FullName != task.FullName
                || verifiedGetter.DeclaringType.Scope is not AssemblyNameReference verifiedScope
                || Identity(verifiedScope) != Identity(bcl.Name) || patchedMethod.Body.Instructions[1].OpCode != OpCodes.Ret)
            {
                throw new InvalidOperationException("Verification found changes outside the single permitted PrepareAsync body or Player BCL getter.");
            }

            EnsureFixtureStopped(player);
            if (Hash(corePath) != expectedCore || Hash(sourceCore) != expectedSourceCore || Hash(source) != expectedFFmpeg
                || Hash(target) != expectedFFmpeg || Hash(coreManifestPath) != coreManifestHash || Hash(helperPath) != helperHash)
            {
                throw new InvalidOperationException("Fixture or original Build changed during verification.");
            }
            var candidateHash = Convert.ToHexString(SHA256.HashData(bytes));
            var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N");
            var evidence = Path.Combine(player, "patch-evidence");
            var backup = Path.Combine(evidence, "MajdataPlay.FFmpeg.original-" + suffix + ".dll");
            var candidate = Path.Combine(evidence, "MajdataPlay.FFmpeg.no-prepares-" + suffix + ".dll");
            var manifestPath = Path.Combine(player, "disable-video-prepares-" + suffix + ".json");
            if (!verifyOnly)
            {
                FileSystem.Directory.Create(evidence);
                FileSystem.File.Copy(target, backup, overwrite: false);
                FileSystem.File.Create(candidate).WriteAllBytes(bytes);
                if (Hash(backup) != expectedFFmpeg || Hash(candidate) != candidateHash)
                {
                    throw new InvalidOperationException("FFmpeg backup or candidate hash mismatch.");
                }
                EnsureFixtureStopped(player);
                FileSystem.File.Copy(candidate, target, overwrite: true);
            }
            if (Hash(target) != (verifyOnly ? expectedFFmpeg : candidateHash) || Hash(corePath) != expectedCore
                || Hash(sourceCore) != expectedSourceCore || Hash(source) != expectedFFmpeg
                || Hash(coreManifestPath) != coreManifestHash || Hash(helperPath) != helperHash)
            {
                throw new InvalidOperationException("Final FFmpeg, Core, original Build or preserved evidence hash mismatch.");
            }
            var manifest = new
            {
                CheckedAt = DateTimeOffset.Now, Applied = !verifyOnly, Target = target, Source = source,
                OriginalFFmpegSHA256 = expectedFFmpeg, CandidateFFmpegSHA256 = candidateHash,
                TargetFFmpegSHA256 = Hash(target), Backup = verifyOnly ? null : backup,
                BackupSHA256 = verifyOnly ? null : Hash(backup), Candidate = verifyOnly ? null : candidate,
                ChangedMethods = changed, OriginalEntryIL = originalIl, PatchedEntryIL = patchedIl,
                CompletedTaskReference = getter.FullName, CompletedTaskAssembly = Identity(bcl.Name),
                CoreSHA256Unchanged = expectedCore, SourceCoreSHA256Unchanged = expectedSourceCore,
                PreservedCoreManifestSHA256 = coreManifestHash, HelperSHA256Unchanged = helperHash,
                Scope = "All FFmpegVideoPlayer PrepareAsync calls return Player Task.CompletedTask; no other method or Core patch changes.",
                RuntimeValidation = "Not performed by this tool. No Player is launched."
            };
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (!verifyOnly)
            {
                FileSystem.File.Create(manifestPath).WriteAllText(json);
                Console.WriteLine("VIDEO PREPARES DISABLED: " + manifestPath);
            }
            else
            {
                Console.WriteLine("VERIFY ONLY: no fixture or evidence files written.");
            }
            Console.WriteLine(json);
        }

        /// <summary>Optionally forces the synthetic pipeline's attachment optimization argument to false.</summary>
        /// <param name="args">The mode flag, synthetic fixture, original Build, and optional --verify-only flag.</param>
        /// <exception cref="InvalidOperationException">Paths, process state, source evidence, or candidate verification do not match.</exception>
        private static void DisableNativeRenderPass(string[] args)
        {
            if ((args.Length != 3 && args.Length != 4) || (args.Length == 4 && args[3] != "--verify-only"))
            {
                throw new InvalidOperationException("Usage: Patcher --disable-native-render-pass <MainSynthetic> <original Build> [--verify-only]");
            }

            var verifyOnly = args.Length == 4;
            var player = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[1]));
            var original = Path.TrimEndingDirectorySeparator(Path.GetFullPath(args[2]));
            var work = Path.GetDirectoryName(player);
            var module = work == null ? null : Path.GetDirectoryName(work);
            if (Path.GetFileName(player) != "MainSynthetic" || Path.GetFileName(work) != ".work"
                || Path.GetFileName(module) != "VulkanTextureValidation" || module == null
                || !string.Equals(original, Path.GetFullPath(Path.Combine(module, "..", "..", "..", "..", "Build")),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Only VulkanTextureValidation/.work/MainSynthetic and its repository's original Build are supported.");
            }

            var managed = Path.Combine(player, "MajdataPlay_Data", "Managed");
            var sourceManaged = Path.Combine(original, "MajdataPlay_Data", "Managed");
            var target = Path.Combine(managed, "MajdataPlay.MajRenderPipe.dll");
            var source = Path.Combine(sourceManaged, "MajdataPlay.MajRenderPipe.dll");
            var fixturePath = Path.Combine(player, "fixture-manifest.json");
            var coreManifestPath = Path.Combine(player, "patch-manifest.json");
            var corePath = Path.Combine(managed, "MajdataPlay.Core.dll");
            var sourceCore = Path.Combine(sourceManaged, "MajdataPlay.Core.dll");
            var preservedPaths = new[] { fixturePath, coreManifestPath, corePath, sourceCore,
                Path.Combine(managed, HelperName + ".dll"), Path.Combine(managed, "MajdataPlay.FFmpeg.dll") };
            var evidence = Path.Combine(player, "patch-evidence");
            RequirePlainPaths(preservedPaths.Concat(new[] { target, source, evidence }));
            // Reject a live fixture before reading its manifests or assembly contents, including in verify-only mode.
            EnsureFixtureStopped(player);
            var preservedHashes = preservedPaths.ToDictionary(path => path, Hash);
            using var fixture = JsonDocument.Parse(FileSystem.File.Open(fixturePath).ReadAllText());
            using var coreManifest = JsonDocument.Parse(FileSystem.File.Open(coreManifestPath).ReadAllText());
            var declaredSource = fixture.RootElement.GetProperty("Source").GetString();
            const string expectedCore = "84BFFF325022DE2E86CE2B5757FBEF9BDB159C70FBAEE36DD9CEB2525AB423FF";
            const string expectedSourceCore = "F016EC0F63AA7A502C06F11FB4D87D5AF5347E84C34E4243092115437E6B9F88";
            const string backgroundEntry = "Cysharp.Threading.Tasks.UniTask MajdataPlay.Scenes.Game.BGManager::SetMovieAsync(System.String,UnityEngine.Sprite)";
            var changedCore = coreManifest.RootElement.GetProperty("ChangedMethods");
            if (declaredSource == null || !string.Equals(original,
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(declaredSource)), StringComparison.OrdinalIgnoreCase)
                || coreManifest.RootElement.GetProperty("PatchedCoreSHA256").GetString() != expectedCore
                || coreManifest.RootElement.GetProperty("OriginalCoreSHA256").GetString() != expectedSourceCore
                || changedCore.GetArrayLength() != 1 || changedCore[0].GetString() != backgroundEntry
                || preservedHashes[corePath] != expectedCore || preservedHashes[sourceCore] != expectedSourceCore)
            {
                throw new InvalidOperationException("The original Build or existing single-method Core patch evidence does not match.");
            }

            var sourceHash = Hash(source);
            var targetHash = Hash(target);
            var sourceBytes = FileSystem.File.Open(source).ReadAllBytes();
            var targetBytes = FileSystem.File.Open(target).ReadAllBytes();
            if (sourceHash != targetHash || !sourceBytes.SequenceEqual(targetBytes)
                || Convert.ToHexString(SHA256.HashData(sourceBytes)) != sourceHash
                || Hash(source) != sourceHash || Hash(target) != targetHash)
            {
                throw new InvalidOperationException("Source and target render pipeline DLLs must be stable and byte-identical; an existing patch is rejected.");
            }
            var bytes = CreateNativeRenderPassCandidate(targetBytes, managed, out var verification);
            var candidateHash = Convert.ToHexString(SHA256.HashData(bytes));
            var suffix = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N");
            var backup = Path.Combine(evidence, "MajdataPlay.MajRenderPipe.original-" + suffix + ".dll");
            var candidate = Path.Combine(evidence, "MajdataPlay.MajRenderPipe.no-native-render-pass-" + suffix + ".dll");
            var manifestPath = Path.Combine(player, "disable-native-render-pass-" + suffix + ".json");
            RequirePlainPaths(preservedPaths.Concat(new[] { target, source, backup, candidate, manifestPath }));
            EnsureFixtureStopped(player);
            if (Hash(source) != sourceHash || Hash(target) != targetHash
                || preservedHashes.Any(pair => Hash(pair.Key) != pair.Value))
            {
                throw new InvalidOperationException("The fixture or original Build changed during candidate verification.");
            }
            if (!verifyOnly)
            {
                FileSystem.Directory.Create(evidence);
                FileSystem.File.Copy(target, backup, overwrite: false);
                FileSystem.File.Create(candidate).WriteAllBytes(bytes);
                if (Hash(backup) != targetHash || Hash(candidate) != candidateHash)
                {
                    throw new InvalidOperationException("Render pipeline backup or candidate SHA256 mismatch.");
                }
                RequirePlainPaths(preservedPaths.Concat(new[] { target, source, backup, candidate, manifestPath }));
                EnsureFixtureStopped(player);
                if (Hash(target) != targetHash || Hash(source) != sourceHash
                    || preservedHashes.Any(pair => Hash(pair.Key) != pair.Value))
                {
                    throw new InvalidOperationException("The fixture or original Build changed before application.");
                }
                FileSystem.File.Copy(candidate, target, overwrite: true);
            }
            if (Hash(source) != sourceHash || Hash(target) != (verifyOnly ? targetHash : candidateHash)
                || preservedHashes.Any(pair => Hash(pair.Key) != pair.Value))
            {
                throw new InvalidOperationException("Final render pipeline, original Build, or preserved fixture SHA256 mismatch.");
            }
            var manifest = new
            {
                CheckedAt = DateTimeOffset.Now, Applied = !verifyOnly, Target = target, Source = source,
                OriginalRenderPipeSHA256 = sourceHash, TargetBeforeSHA256 = targetHash,
                CandidateRenderPipeSHA256 = candidateHash, TargetAfterSHA256 = Hash(target),
                Backup = verifyOnly ? null : backup, BackupSHA256 = verifyOnly ? null : Hash(backup),
                Candidate = verifyOnly ? null : candidate, Verification = verification,
                PreservedSHA256 = preservedHashes, SourceSHA256Unchanged = Hash(source),
                Scope = "Only the optimizeAttachments argument load feeding _optimizeAttachments is replaced with ldc.i4.0; NativeGraphicsJobs and output assets are unchanged.",
                RuntimeValidation = "Not performed. No Player is launched."
            };
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            if (!verifyOnly)
            {
                FileSystem.File.Create(manifestPath).WriteAllText(json);
                Console.WriteLine("NATIVE RENDER PASS DISABLED: " + manifestPath);
            }
            else
            {
                Console.WriteLine("VERIFY ONLY: no fixture or evidence files written.");
            }
            Console.WriteLine(json);
        }

        /// <summary>Builds and rereads a pipeline candidate entirely in memory, changing exactly one argument load.</summary>
        /// <param name="input">The complete unmodified render pipeline assembly bytes.</param>
        /// <param name="managed">The actual Player dependency directory for metadata resolution.</param>
        /// <param name="verification">The original/replacement IL and complete single-method verification evidence.</param>
        /// <returns>The verified candidate bytes; no file is written.</returns>
        /// <exception cref="InvalidOperationException">Assembly identity, constructor ABI, field assignment, or reread verification is unexpected.</exception>
        private static byte[] CreateNativeRenderPassCandidate(byte[] input, string managed, out object verification)
        {
            using var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managed);
            var reader = new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadSymbols = false };
            using var inputBuffer = new System.IO.MemoryStream(input, writable: false);
            using var assembly = AssemblyDefinition.ReadAssembly(inputBuffer, reader);
            if (assembly.Name.Name != "MajdataPlay.MajRenderPipe" || assembly.Name.HasPublicKey
                || assembly.Name.PublicKeyToken?.Length > 0 || assembly.Modules.Count != 1
                || (assembly.MainModule.Attributes & ModuleAttributes.StrongNameSigned) != 0)
            {
                throw new InvalidOperationException("Strong names or unexpected render pipeline assembly identities are unsupported.");
            }
            var pipeline = assembly.MainModule.Types.Single(type => type.FullName == "MajdataPlay.Rendering.MajRenderPipeline");
            var field = pipeline.Fields.Single(candidate => candidate.Name == "_optimizeAttachments");
            var method = pipeline.Methods.Single(candidate => candidate.IsConstructor && !candidate.IsStatic);
            var parameterTypes = new[] { "MajdataPlay.Rendering.MajRenderPipelineAsset", "System.Boolean", "System.Boolean",
                "System.Boolean", "System.Boolean", "UnityEngine.Shader" };
            var parameterNames = new[] { "asset", "useDynamicBatching", "useGPUInstancing", "useSRPBatcher", "optimizeAttachments", "finalCopyShader" };
            if (!pipeline.IsClass || !pipeline.IsSealed || !field.IsPrivate || field.IsStatic || !field.IsInitOnly
                || field.FieldType.FullName != "System.Boolean" || field.FieldType.Scope.Name != "mscorlib"
                || method.Name != ".ctor" || !method.IsAssembly || !method.HasThis || method.ExplicitThis || !method.HasBody
                || method.HasGenericParameters || method.ReturnType.FullName != "System.Void"
                || !method.Parameters.Select(parameter => parameter.ParameterType.FullName).SequenceEqual(parameterTypes)
                || !method.Parameters.Select(parameter => parameter.Name).SequenceEqual(parameterNames))
            {
                throw new InvalidOperationException("Unexpected pipeline type, private readonly Boolean field, or exact constructor signature.");
            }
            var stores = assembly.MainModule.Types.SelectMany(Types).SelectMany(type => type.Methods)
                .Where(candidate => candidate.HasBody).SelectMany(candidate => candidate.Body.Instructions
                    .Where(instruction => (instruction.OpCode == OpCodes.Stfld || instruction.OpCode == OpCodes.Stsfld)
                        && instruction.Operand is FieldReference stored && stored.FullName == field.FullName)
                    .Select(instruction => (Method: candidate, Instruction: instruction))).ToArray();
            if (stores.Length != 1 || stores[0].Method != method)
            {
                throw new InvalidOperationException("Exactly one constructor store to _optimizeAttachments is required.");
            }
            var load = stores[0].Instruction.Previous;
            var parameter = method.Parameters[4];
            if (load == null || (load.OpCode != OpCodes.Ldarg && load.OpCode != OpCodes.Ldarg_S)
                || load.Operand != parameter || load.Previous?.OpCode != OpCodes.Ldarg_0)
            {
                throw new InvalidOperationException("The unique field store must immediately follow ldarg.0 and the optimizeAttachments parameter load; already patched IL is rejected.");
            }
            var before = Snapshot(assembly.MainModule);
            var references = assembly.MainModule.AssemblyReferences.Select(Identity).ToArray();
            var originalIl = method.Body.Instructions.Select(instruction => instruction.ToString()).ToArray();
            var normalizedBefore = NormalizedInstructions(method);
            var originalShape = BodyShape(method);
            var loadIndex = method.Body.Instructions.IndexOf(load);
            var maxStack = method.Body.MaxStackSize;
            var methodName = method.FullName;
            load.OpCode = OpCodes.Ldc_I4_0;
            load.Operand = null;
            using var buffer = new System.IO.MemoryStream();
            assembly.Write(buffer, new WriterParameters { WriteSymbols = false });
            var bytes = buffer.ToArray();
            using var rereadBuffer = new System.IO.MemoryStream(bytes, writable: false);
            using var verified = AssemblyDefinition.ReadAssembly(rereadBuffer, reader);
            var after = Snapshot(verified.MainModule);
            var changed = before.Keys.Where(key => !after.TryGetValue(key, out var value) || before[key] != value).ToArray();
            var patchedMethod = verified.MainModule.Types.Single(type => type.FullName == pipeline.FullName)
                .Methods.Single(candidate => candidate.FullName == methodName);
            var normalizedAfter = NormalizedInstructions(patchedMethod);
            var changedInstructions = Enumerable.Range(0, normalizedBefore.Length)
                .Where(index => index >= normalizedAfter.Length || normalizedBefore[index] != normalizedAfter[index]).ToArray();
            if (before.Count != after.Count || changed.Length != 1 || changed[0] != methodName
                || !references.SequenceEqual(verified.MainModule.AssemblyReferences.Select(Identity))
                || normalizedBefore.Length != normalizedAfter.Length || changedInstructions.Length != 1 || changedInstructions[0] != loadIndex
                || normalizedAfter[loadIndex] != "ldc.i4.0|" || patchedMethod.Body.Instructions[loadIndex].Operand != null
                || BodyShape(patchedMethod) != originalShape || patchedMethod.Body.MaxStackSize != maxStack)
            {
                throw new InvalidOperationException("Reread must preserve references, locals, EH, stack size, and every instruction except the one argument load.");
            }
            verification = new
            {
                Assembly = Identity(verified.Name), Type = pipeline.FullName, Constructor = methodName, Field = field.FullName,
                ChangedMethods = changed, TotalMethods = before.Count, ChangedInstructionIndices = changedInstructions,
                OriginalEntryIL = originalIl, PatchedEntryIL = patchedMethod.Body.Instructions.Select(instruction => instruction.ToString()).ToArray(),
                NormalizedOriginalInstructions = normalizedBefore, NormalizedPatchedInstructions = normalizedAfter,
                AssemblyReferencesUnchanged = references, LocalsAndExceptionHandlersUnchanged = true, MaxStackSizeUnchanged = maxStack
            };
            return bytes;
        }

        /// <summary>Normalizes opcodes and operands without depending on byte offsets changed by instruction sizes.</summary>
        /// <param name="method">The method whose instructions are compared.</param>
        /// <returns>One opcode/operand description per instruction, with branch targets represented by instruction index.</returns>
        private static string[] NormalizedInstructions(MethodDefinition method)
        {
            var instructions = method.Body.Instructions;
            return instructions.Select(instruction => instruction.OpCode.Name + "|" + (instruction.Operand switch
            {
                null => "",
                Instruction target => "instruction:" + instructions.IndexOf(target),
                Instruction[] targets => "instructions:" + string.Join(",", targets.Select(target => instructions.IndexOf(target))),
                ParameterDefinition parameter => "parameter:" + parameter.Index + ":" + parameter.Name + ":" + parameter.ParameterType.FullName,
                VariableDefinition variable => "variable:" + variable.Index + ":" + variable.VariableType.FullName,
                _ => instruction.Operand.ToString()
            })).ToArray();
        }

        /// <summary>Normalizes method locals and exception boundaries using instruction indices.</summary>
        /// <param name="method">The method body to inspect.</param>
        /// <returns>The local initialization flag, local type sequence and complete exception handler sequence.</returns>
        private static string BodyShape(MethodDefinition method)
        {
            var body = method.Body;
            return body.InitLocals + "|" + string.Join(";", body.Variables.Select(variable => variable.VariableType.FullName)) + "|"
                + string.Join(";", body.ExceptionHandlers.Select(handler => handler.HandlerType + ":"
                    + body.Instructions.IndexOf(handler.TryStart) + ":" + body.Instructions.IndexOf(handler.TryEnd) + ":"
                    + body.Instructions.IndexOf(handler.HandlerStart) + ":" + body.Instructions.IndexOf(handler.HandlerEnd) + ":"
                    + body.Instructions.IndexOf(handler.FilterStart) + ":" + handler.CatchType?.FullName));
        }

        /// <summary>Rejects symbolic links and reparse points for each path and every ancestor.</summary>
        /// <param name="paths">The source, target and evidence paths that must remain local ordinary entries.</param>
        /// <exception cref="InvalidOperationException">A path or ancestor is a reparse point.</exception>
        private static void RequirePlainPaths(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                for (var ancestor = path; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
                {
                    if (FileSystem.GetEntry(ancestor)?.IsSymbolicLink == true)
                    {
                        throw new InvalidOperationException("A source, target or evidence path is a reparse point: " + ancestor);
                    }
                }
            }
        }

        /// <summary>Rejects a live Player loaded from the synthetic fixture without stopping any process.</summary>
        /// <param name="player">The validated synthetic fixture directory.</param>
        /// <exception cref="InvalidOperationException">The fixture Player is running or process ownership cannot be verified.</exception>
        private static void EnsureFixtureStopped(string player)
        {
            var executable = Path.Combine(player, "MajdataPlay.exe");
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("MajdataPlay"))
            {
                using (process)
                {
                    try
                    {
                        if (!process.HasExited && (process.MainModule?.FileName is not string processPath
                            || string.Equals(Path.GetFullPath(processPath), executable, StringComparison.OrdinalIgnoreCase)))
                        {
                            throw new InvalidOperationException("The synthetic Player is running or its executable path cannot be verified; PID=" + process.Id);
                        }
                    }
                    catch (System.ComponentModel.Win32Exception error)
                    {
                        throw new InvalidOperationException("Cannot verify Player process ownership; refusing to modify assemblies.", error);
                    }
                }
            }
        }

        /// <summary>Requires each permitted helper dependency to match a file shipped with the actual Player.</summary>
        /// <param name="module">The helper module whose references must be checked.</param>
        /// <param name="managed">The actual Player managed assembly directory.</param>
        /// <param name="reader">The metadata-only assembly reader settings.</param>
        /// <exception cref="InvalidOperationException">A dependency is unexpected, absent, or has a different identity.</exception>
        private static void ValidateReferences(ModuleDefinition module, string managed, ReaderParameters reader)
        {
            foreach (var reference in module.AssemblyReferences)
            {
                if (reference.Name is not ("netstandard" or "mscorlib" or "System" or "System.Core" or "System.Runtime"
                    or "UnityEngine.CoreModule" or "UnityEngine.UIModule" or "UnityEngine.UI"))
                {
                    throw new InvalidOperationException("Unexpected helper dependency: " + reference.FullName);
                }

                var referenceFile = Path.Combine(managed, reference.Name + ".dll");
                if (!FileExists(referenceFile))
                {
                    throw new InvalidOperationException("Helper dependency is absent from the actual Player: " + reference.FullName);
                }

                using var actual = AssemblyDefinition.ReadAssembly(referenceFile, reader);
                if (Identity(actual.Name) != Identity(reference))
                {
                    throw new InvalidOperationException("Player reference identity mismatch: " + reference.FullName
                        + "; actual: " + Identity(actual.Name));
                }
            }
        }

        /// <summary>Formats assembly identity without depending on Cecil's lazy FullName cache.</summary>
        /// <param name="name">The assembly name metadata to compare.</param>
        /// <returns>The name, version, culture and public key token.</returns>
        private static string Identity(AssemblyNameReference name)
        {
            var token = name.PublicKeyToken;
            return name.Name + ", Version=" + name.Version + ", Culture="
                + (string.IsNullOrEmpty(name.Culture) ? "neutral" : name.Culture) + ", PublicKeyToken="
                + (token == null || token.Length == 0 ? "null" : Convert.ToHexString(token));
        }

        /// <summary>Checks whether a storage entry exists and denotes a file.</summary>
        /// <param name="path">The local path to query.</param>
        /// <returns>True when the entry exists and is not a directory.</returns>
        /// <exception cref="UnauthorizedAccessException">Entry metadata access is denied.</exception>
        /// <exception cref="System.IO.IOException">Entry metadata cannot be read.</exception>
        private static bool FileExists(string path)
        {
            return FileSystem.GetEntry(path)?.IsDirectory == false;
        }

        /// <summary>Computes the SHA256 of a regular file.</summary>
        /// <param name="path">The file to hash.</param>
        /// <returns>The uppercase hexadecimal SHA256.</returns>
        private static string Hash(string path)
        {
            using var stream = FileSystem.File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        /// <summary>Enumerates every nested type without loading the game into the CLR.</summary>
        /// <param name="root">The root metadata type.</param>
        /// <returns>The root and all of its descendants.</returns>
        private static IEnumerable<TypeDefinition> Types(TypeDefinition root)
        {
            yield return root;
            foreach (var nested in root.NestedTypes)
            {
                foreach (var type in Types(nested))
                {
                    yield return type;
                }
            }
        }

        /// <summary>Captures all method bodies, locals and exception handlers for a single-method comparison.</summary>
        /// <param name="module">The module to inspect.</param>
        /// <returns>Normalized body descriptions indexed by full method signature.</returns>
        private static Dictionary<string, string> Snapshot(ModuleDefinition module)
        {
            return module.Types.SelectMany(Types).SelectMany(type => type.Methods).ToDictionary(method => method.FullName,
                method => !method.HasBody ? "NO BODY" : method.Body.InitLocals + "|"
                    + string.Join(";", method.Body.Variables.Select(variable => variable.VariableType.FullName)) + "|"
                    + string.Join(";", method.Body.Instructions.Select(instruction => instruction.ToString())) + "|"
                    + string.Join(";", method.Body.ExceptionHandlers.Select(handler => handler.HandlerType + ":"
                        + handler.TryStart?.Offset + ":" + handler.TryEnd?.Offset + ":" + handler.HandlerStart?.Offset
                        + ":" + handler.HandlerEnd?.Offset + ":" + handler.FilterStart?.Offset + ":" + handler.CatchType?.FullName)));
        }
    }
}
