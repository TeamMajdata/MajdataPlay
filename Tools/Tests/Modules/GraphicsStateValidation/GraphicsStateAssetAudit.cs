#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MajdataPlay.IO.Storage;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace MajdataPlay.Tests.GraphicsStates
{
    /// <summary>Audits imported captures and optionally creates a lossless union through Unity's native collection API.</summary>
    public static class GraphicsStateAssetAudit
    {
        /// <summary>Audits explicit asset paths and writes reports without changing the input assets.</summary>
        /// <remarks>The first input is the new matrix; subsequent inputs are captures whose previous coverage must be preserved.</remarks>
        /// <exception cref="ArgumentException">Required command-line arguments are absent.</exception>
        /// <exception cref="InvalidOperationException">An asset, reference, metadata or union invariant is invalid.</exception>
        /// <exception cref="System.IO.IOException">A report or new union file cannot be written.</exception>
        /// <exception cref="UnauthorizedAccessException">Access to an output location is denied.</exception>
        public static void Run()
        {
            var assetArgument = Argument("-gscAuditAssets") ?? throw new ArgumentException("Specify -gscAuditAssets with semicolon-separated imported asset paths.");
            var output = Argument("-gscAuditOutput") ?? throw new ArgumentException("Specify -gscAuditOutput with a report directory.");
            var paths = assetArgument.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            Require(paths.Length > 0, "At least one imported capture is required.");
            var directory = FileSystem.Directory.Create(output);
            var collections = new List<GraphicsStateCollection>();
            var overview = new StringBuilder("source\tplatform\tgraphicsApi\tquality\tversion\tvariants\tdeclaredStates\tenumeratedStates\tshaderCount\tsampleDistribution\n");
            var variantRows = new StringBuilder("source\tshader\tshaderGuid\tshaderLocalId\tshaderAsset\tsubshader\tpass\tkeywords\tgraphicsStates\n");
            var rows = new StringBuilder("source\tshader\tshaderGuid\tshaderLocalId\tshaderAsset\tsubshader\tpass\tkeywords\tstateIndex\tsamples\tdepthIndex\tsubpassIndex\tattachments\tsubpasses\tvertexAttributes\ttopology\trenderMask\tdepthWrite\tdepthCompare\tcullMode\tnegativeScale\tinvertCulling\tinvertProjection\n");
            var changes = new StringBuilder("source\taddedVariants\taddedGraphicsStates\n");
            foreach (var path in paths)
            {
                var collection = AssetDatabase.LoadAssetAtPath<GraphicsStateCollection>(path);
                Require(collection != null, "Cannot load imported capture: " + path);
                collections.Add(collection!);
                Audit(path, collection!, overview, variantRows, rows);
            }

            var unionOutput = Argument("-gscUnionOutput");
            if (!string.IsNullOrWhiteSpace(unionOutput))
            {
                Require(collections.Count >= 2, "A union requires the new matrix followed by at least one baseline capture.");
                Require(!FileSystem.File.Open(unionOutput!).Exists, "Refusing to overwrite a union file: " + unionOutput);
                var union = CreateEmptyLike(collections[0]);
                try
                {
                    var appendedVariants = 0;
                    var appendedStates = 0;
                    for (var index = 0; index < collections.Count; index++)
                    {
                        RequireCompatible(collections[0], collections[index]);
                        var additions = Append(union, collections[index]);
                        Row(changes, paths[index], additions.Variants, additions.States);
                        if (index == 0)
                        {
                            Require(union.variantCount == collections[0].variantCount
                                && union.totalGraphicsStateCount == collections[0].totalGraphicsStateCount,
                                "The initial native union copy did not preserve the matrix counts.");
                        }
                        if (index > 0)
                        {
                            appendedVariants += additions.Variants;
                            appendedStates += additions.States;
                        }
                    }
                    foreach (var source in collections)
                    {
                        var verification = Append(union, source);
                        Require(verification.Variants == 0 && verification.States == 0,
                            "A second native API union pass found missing source data.");
                    }
                    Audit("union", union, overview, variantRows, rows);
                    if (appendedVariants > 0 || appendedStates > 0)
                    {
                        Require(union.SaveToFile(unionOutput!), "Unity could not save the new union capture.");
                        Debug.Log("GSC_UNION_SAVED: " + unionOutput + "; baselineAddedVariants=" + appendedVariants + "; baselineAddedStates=" + appendedStates);
                    }
                    else
                    {
                        Debug.Log("GSC_UNION_UNNECESSARY: the new matrix already contains every baseline variant and state; no union file was written.");
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(union);
                }
            }
            FileSystem.File.Create(directory.Location, "collections.tsv", overwrite: true).WriteAllText(overview.ToString());
            FileSystem.File.Create(directory.Location, "variants.tsv", overwrite: true).WriteAllText(variantRows.ToString());
            FileSystem.File.Create(directory.Location, "states.tsv", overwrite: true).WriteAllText(rows.ToString());
            FileSystem.File.Create(directory.Location, "union-additions.tsv", overwrite: true).WriteAllText(changes.ToString());
            Debug.Log("GSC_ASSET_AUDIT_PASSED: " + directory.Location + "; inputs=" + collections.Count);
        }

        /// <summary>Exports every native state and validates shader references, state counts and pass attachment indices.</summary>
        /// <param name="source">The input label included in every report row.</param>
        /// <param name="collection">The imported or newly composed collection.</param>
        /// <param name="overview">The destination collection summary.</param>
        /// <param name="variantRows">The destination shader variant report, including legacy API variants without states.</param>
        /// <param name="rows">The destination per-state report.</param>
        /// <exception cref="InvalidOperationException">A shader reference or native collection invariant is invalid.</exception>
        private static void Audit(string source, GraphicsStateCollection collection, StringBuilder overview, StringBuilder variantRows, StringBuilder rows)
        {
            Require(collection.runtimePlatform == RuntimePlatform.Android, "Non-Android capture: " + source);
            Require(collection.variantCount > 0, "Empty shader capture: " + source);
            var variants = new List<GraphicsStateCollection.ShaderVariant>();
            var states = new List<GraphicsStateCollection.GraphicsState>();
            var shaders = new HashSet<string>(StringComparer.Ordinal);
            var samples = new SortedDictionary<int, int>();
            var stateCount = 0;
            collection.GetVariants(variants);
            Require(variants.Count == collection.variantCount, "Variant enumeration mismatch: " + source);
            foreach (var variant in variants)
            {
                var shader = variant.shader;
                Require(shader != null, "Missing shader reference in " + source);
                Require(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(shader, out string shaderGuid, out long localId),
                    "Shader reference has no persistent asset identity: " + shader.name);
                var shaderPath = AssetDatabase.GetAssetPath(shader);
                Require(!string.IsNullOrEmpty(shaderPath), "Shader reference has no asset path: " + shader.name);
                shaders.Add(shaderGuid + ":" + localId);
                states.Clear();
                collection.GetGraphicsStatesForVariant(variant, states);
                Require(states.Count == collection.GetGraphicsStateCountForVariant(variant), "State enumeration mismatch: " + shader.name);
                Row(variantRows, source, shader.name, shaderGuid, localId, shaderPath,
                    variant.passId.SubshaderIndex, variant.passId.PassIndex, Keywords(variant.keywords), states.Count);
                for (var index = 0; index < states.Count; index++)
                {
                    var state = states[index];
                    Require(state.sampleCount > 0, "Invalid sample count in " + shader.name);
                    samples.TryGetValue(state.sampleCount, out var count);
                    samples[state.sampleCount] = count + 1;
                    stateCount++;
                    var attachmentCount = state.attachments?.Length ?? 0;
                    Require(state.depthAttachmentIndex >= -1 && state.depthAttachmentIndex < attachmentCount,
                        "Invalid depth attachment index in " + shader.name);
                    var subpassCount = state.subPasses?.Length ?? 0;
                    Require(state.subPassIndex >= -1 && (subpassCount == 0 || state.subPassIndex < subpassCount),
                        "Invalid subpass index in " + shader.name);
                    Row(rows, source, shader.name, shaderGuid, localId, shaderPath,
                        variant.passId.SubshaderIndex, variant.passId.PassIndex, Keywords(variant.keywords), index,
                        state.sampleCount, state.depthAttachmentIndex, state.subPassIndex,
                        Attachments(state.attachments), Subpasses(state.subPasses), Vertices(state.vertexAttributes),
                        state.topology, state.renderState.mask, state.renderState.depthState.writeEnabled,
                        state.renderState.depthState.compareFunction, state.forceCullMode, state.negativeScale,
                        state.invertCulling, state.invertProjection);
                }
            }
            Require(stateCount == collection.totalGraphicsStateCount, "Total state count mismatch: " + source);
            var distribution = new List<string>();
            foreach (var pair in samples)
            {
                distribution.Add(pair.Key + "x:" + pair.Value);
            }
            Row(overview, source, collection.runtimePlatform, collection.graphicsDeviceType, collection.qualityLevelName,
                collection.version, collection.variantCount, collection.totalGraphicsStateCount, stateCount,
                shaders.Count, string.Join(",", distribution));
        }

        /// <summary>Copies a capture's platform metadata into an empty independent native collection.</summary>
        /// <param name="source">The capture whose metadata must remain unchanged.</param>
        /// <returns>A caller-owned empty collection with identical metadata.</returns>
        private static GraphicsStateCollection CreateEmptyLike(GraphicsStateCollection source)
        {
            return new GraphicsStateCollection
            {
                runtimePlatform = source.runtimePlatform,
                graphicsDeviceType = source.graphicsDeviceType,
                qualityLevelName = source.qualityLevelName,
                version = source.version
            };
        }

        /// <summary>Uses native equality and insertion to copy variants and their complete graphics states.</summary>
        /// <param name="destination">The independent collection receiving missing data.</param>
        /// <param name="source">The unchanged input capture.</param>
        /// <returns>The numbers of variants and graphics states actually added.</returns>
        /// <exception cref="InvalidOperationException">A source shader reference is missing.</exception>
        private static (int Variants, int States) Append(GraphicsStateCollection destination, GraphicsStateCollection source)
        {
            var variants = new List<GraphicsStateCollection.ShaderVariant>();
            var states = new List<GraphicsStateCollection.GraphicsState>();
            var addedVariants = 0;
            var addedStates = 0;
            source.GetVariants(variants);
            foreach (var variant in variants)
            {
                Require(variant.shader != null, "Cannot merge a missing shader reference.");
                if (destination.AddVariant(variant.shader, variant.passId, variant.keywords))
                {
                    addedVariants++;
                }
                Require(destination.ContainsVariant(variant.shader, variant.passId, variant.keywords),
                    "The native union does not contain a source shader variant.");
                states.Clear();
                source.GetGraphicsStatesForVariant(variant, states);
                foreach (var state in states)
                {
                    if (destination.AddGraphicsStateForVariant(variant, state))
                    {
                        addedStates++;
                    }
                }
            }
            return (addedVariants, addedStates);
        }

        /// <summary>Rejects metadata combinations that must remain separate capture assets.</summary>
        /// <param name="expected">The matrix capture's metadata.</param>
        /// <param name="candidate">A baseline capture to compare.</param>
        /// <exception cref="InvalidOperationException">The captures describe different platform, API, quality or format versions.</exception>
        private static void RequireCompatible(GraphicsStateCollection expected, GraphicsStateCollection candidate)
        {
            Require(expected.runtimePlatform == candidate.runtimePlatform && expected.graphicsDeviceType == candidate.graphicsDeviceType
                && expected.version == candidate.version && string.Equals(expected.qualityLevelName, candidate.qualityLevelName, StringComparison.Ordinal),
                "Cannot union captures with different platform/API/quality/version metadata.");
        }

        /// <summary>Formats attachment formats, load/store operations and resolve targets.</summary>
        /// <param name="attachments">The captured attachment array, when present.</param>
        /// <returns>A deterministic ordered description of the render-pass attachments.</returns>
        private static string Attachments(AttachmentDescriptor[]? attachments)
        {
            var values = new List<string>();
            if (attachments != null)
            {
                for (var index = 0; index < attachments.Length; index++)
                {
                    var attachment = attachments[index];
                    values.Add(index + ":" + attachment.graphicsFormat + "/load=" + attachment.loadAction +
                        "/store=" + attachment.storeAction + "/target=" + attachment.loadStoreTarget + "/resolve=" + attachment.resolveTarget);
                }
            }
            return string.Join(";", values);
        }

        /// <summary>Formats subpass color outputs, inputs and flags.</summary>
        /// <param name="subpasses">The captured subpasses, when present.</param>
        /// <returns>An ordered description preserving attachment index relationships.</returns>
        private static string Subpasses(SubPassDescriptor[]? subpasses)
        {
            var values = new List<string>();
            if (subpasses != null)
            {
                for (var index = 0; index < subpasses.Length; index++)
                {
                    var subpass = subpasses[index];
                    values.Add(index + ":outputs=" + Indices(subpass.colorOutputs) + "/inputs=" + Indices(subpass.inputs) + "/flags=" + subpass.flags);
                }
            }
            return string.Join(";", values);
        }

        /// <summary>Formats the populated portion of an attachment index array.</summary>
        /// <param name="indices">The native attachment-index container.</param>
        /// <returns>The ordered attachment indices.</returns>
        private static string Indices(AttachmentIndexArray indices)
        {
            var values = new List<string>();
            for (var index = 0; index < indices.Length; index++)
            {
                values.Add(indices[index].ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", values);
        }

        /// <summary>Formats the complete vertex declaration associated with a recorded pipeline state.</summary>
        /// <param name="attributes">The captured vertex attributes, when present.</param>
        /// <returns>The ordered attribute, format, dimension and stream entries.</returns>
        private static string Vertices(VertexAttributeDescriptor[]? attributes)
        {
            var values = new List<string>();
            if (attributes != null)
            {
                foreach (var attribute in attributes)
                {
                    values.Add(attribute.attribute + ":" + attribute.format + ":" + attribute.dimension + ":" + attribute.stream);
                }
            }
            return string.Join(";", values);
        }

        /// <summary>Formats variant keyword identity independent of keyword enumeration order.</summary>
        /// <param name="keywords">The variant's local keyword array.</param>
        /// <returns>The sorted local keyword names.</returns>
        private static string Keywords(LocalKeyword[]? keywords)
        {
            var names = new List<string>();
            if (keywords != null)
            {
                foreach (var keyword in keywords)
                {
                    names.Add(keyword.name);
                }
            }
            names.Sort(StringComparer.Ordinal);
            return string.Join(",", names);
        }

        /// <summary>Appends one escaped, culture-invariant tab-separated row.</summary>
        /// <param name="destination">The report buffer.</param>
        /// <param name="values">The ordered cell values.</param>
        private static void Row(StringBuilder destination, params object?[] values)
        {
            for (var index = 0; index < values.Length; index++)
            {
                if (index > 0)
                {
                    destination.Append('\t');
                }
                destination.Append((Convert.ToString(values[index], CultureInfo.InvariantCulture) ?? string.Empty)
                    .Replace("\t", " ").Replace("\r", " ").Replace("\n", " "));
            }
            destination.Append('\n');
        }

        /// <summary>Reads an explicit Unity command-line argument.</summary>
        /// <param name="name">The named argument.</param>
        /// <returns>The following value, or null if the argument is absent.</returns>
        private static string? Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index + 1 < arguments.Length; index++)
            {
                if (arguments[index] == name)
                {
                    return arguments[index + 1];
                }
            }
            return null;
        }

        /// <summary>Rejects an unmet audit invariant.</summary>
        /// <param name="condition">The invariant that must hold.</param>
        /// <param name="message">The actionable failure detail.</param>
        /// <exception cref="InvalidOperationException">The invariant is false.</exception>
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
