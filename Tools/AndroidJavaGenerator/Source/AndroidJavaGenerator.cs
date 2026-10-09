#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace MajdataPlay.SourceGenerators.AndroidJava
{
    /// <summary>Generates descriptor-exact Android JNI wrappers for JavaClass-annotated partial classes.</summary>
    [Generator]
    public sealed class AndroidJavaGenerator : ISourceGenerator
    {
        /// <summary>Identifies the fully qualified runtime wrapper annotation.</summary>
        internal const string JavaClassAttributeName = "MajdataPlay.Platform.Android.JavaClassAttribute";

        /// <summary>Identifies the fully qualified assembly configuration annotation.</summary>
        internal const string ConfigurationAttributeName = "MajdataPlay.Platform.Android.JavaApiConfigurationAttribute";

        /// <summary>Identifies the runtime base class required by every generated wrapper.</summary>
        internal const string JavaObjectName = "MajdataPlay.Platform.Android.Runtime.Java.Lang.JavaObject";

        /// <summary>Registers compilation-local syntax discovery for attributed type declarations.</summary>
        /// <param name="context">The generator initialization context.</param>
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new DeclarationReceiver());
        }

        /// <summary>Resolves annotations, extracts grouped Java APIs, and emits fresh wrapper sources.</summary>
        /// <param name="context">The compilation, cancellation, output, and diagnostic context.</param>
        /// <exception cref="OperationCanceledException">The host cancels compilation.</exception>
        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                ExecuteCore(context);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (GeneratorException exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(exception.Descriptor, Location.None, exception.Message));
            }
            catch (Exception exception)
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.UnexpectedFailure, Location.None,
                    exception.GetType().Name + ": " + exception.Message));
            }
        }

        /// <summary>Runs generation without persisting metadata across compilations.</summary>
        /// <param name="context">The current compilation context.</param>
        /// <exception cref="OperationCanceledException">The host cancels compilation.</exception>
        private static void ExecuteCore(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is DeclarationReceiver receiver))
            {
                return;
            }
            var wrappers = Discover(context, receiver);
            if (wrappers.Count == 0)
            {
                return;
            }
            var global = new JavaApiOptions();
            foreach (var attribute in context.Compilation.Assembly.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ConfigurationAttributeName)
                {
                    global.Apply(attribute);
                }
            }
            var groups = new Dictionary<string, List<JavaWrapper>>(StringComparer.Ordinal);
            foreach (var wrapper in wrappers)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var options = JavaApiOptions.Merge(global, wrapper.Attribute);
                    var configuration = JavaApiConfiguration.Resolve(options, context.Compilation, wrapper.Declaration.SyntaxTree, context.CancellationToken);
                    wrapper.Configuration = configuration;
                    if (!groups.TryGetValue(configuration.Key, out var group))
                    {
                        group = new List<JavaWrapper>();
                        groups.Add(configuration.Key, group);
                    }
                    group.Add(wrapper);
                }
                catch (GeneratorException exception)
                {
                    Report(context, wrapper, exception.Descriptor, exception.Message);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
                {
                    Report(context, wrapper, GeneratorDiagnostics.InvalidConfiguration,
                        "Cannot resolve Java inputs for '" + wrapper.JavaName + "': " + exception.Message);
                }
            }
            var mappings = wrappers.ToDictionary(wrapper => wrapper.JavaName,
                wrapper => wrapper.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal);
            foreach (var group in groups.Values.OrderBy(items => items[0].Configuration!.Key, StringComparer.Ordinal))
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                IReadOnlyDictionary<string, JavaApiType> metadata;
                try
                {
                    metadata = JavaApiExtractor.Extract(group[0].Configuration!, group.Select(wrapper => wrapper.JavaName), context.CancellationToken);
                }
                catch (GeneratorException exception)
                {
                    foreach (var wrapper in group)
                    {
                        Report(context, wrapper, exception.Descriptor, exception.Message);
                    }
                    continue;
                }
                foreach (var wrapper in group.OrderBy(item => item.JavaName, StringComparer.Ordinal))
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var source = JavaWrapperEmitter.Emit(wrapper, metadata[wrapper.JavaName], mappings,
                            (descriptor, message) => Report(context, wrapper, descriptor, message), context.CancellationToken);
                        if (source != null)
                        {
                            context.AddSource(GetHintName(wrapper), SourceText.From(source, Encoding.UTF8));
                        }
                    }
                    catch (GeneratorException exception)
                    {
                        Report(context, wrapper, exception.Descriptor, exception.Message);
                    }
                }
            }
        }

        /// <summary>Discovers and validates annotated wrappers, rejecting duplicate binary mappings.</summary>
        /// <param name="context">The compilation context.</param>
        /// <param name="receiver">The attributed declarations collected during syntax traversal.</param>
        /// <returns>The valid, uniquely mapped wrapper declarations.</returns>
        /// <exception cref="OperationCanceledException">The host cancels compilation.</exception>
        private static List<JavaWrapper> Discover(GeneratorExecutionContext context, DeclarationReceiver receiver)
        {
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var candidates = new List<JavaWrapper>();
            foreach (var declaration in receiver.Declarations)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                var semantic = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(semantic.GetDeclaredSymbol(declaration, context.CancellationToken) is INamedTypeSymbol symbol) || !seen.Add(symbol))
                {
                    continue;
                }
                var attributes = symbol.GetAttributes().Where(attribute => attribute.AttributeClass?.ToDisplayString() == JavaClassAttributeName).ToArray();
                if (attributes.Length == 0)
                {
                    continue;
                }
                var attribute = attributes[0];
                var name = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                var wrapper = new JavaWrapper(symbol, declaration, attribute, name ?? string.Empty);
                var invalid = ValidateDeclaration(wrapper, context);
                if (attributes.Length > 1)
                {
                    invalid = "A wrapper must have exactly one JavaClass attribute.";
                }
                if (invalid != null)
                {
                    Report(context, wrapper, GeneratorDiagnostics.InvalidDeclaration, invalid);
                    continue;
                }
                if (!JavaDescriptors.IsBinaryName(wrapper.JavaName))
                {
                    Report(context, wrapper, GeneratorDiagnostics.InvalidDeclaration,
                        "JavaClass requires a nonempty binary class name, such as 'android.content.Intent' or 'package.Outer$Inner'.");
                    continue;
                }
                candidates.Add(wrapper);
            }
            var result = new List<JavaWrapper>();
            foreach (var mapping in candidates.GroupBy(wrapper => wrapper.JavaName, StringComparer.Ordinal))
            {
                var duplicates = mapping.ToArray();
                if (duplicates.Length > 1)
                {
                    var names = string.Join(", ", duplicates.Select(wrapper => wrapper.Symbol.ToDisplayString()).OrderBy(name => name, StringComparer.Ordinal));
                    foreach (var wrapper in duplicates)
                    {
                        Report(context, wrapper, GeneratorDiagnostics.DuplicateMapping,
                            "Java binary name '" + mapping.Key + "' is mapped by multiple wrappers: " + names + ".");
                    }
                }
                else
                {
                    result.Add(duplicates[0]);
                }
            }
            return result;
        }

        /// <summary>Validates class shape and its handwritten base declaration.</summary>
        /// <param name="wrapper">The annotated wrapper.</param>
        /// <param name="context">The compilation context.</param>
        /// <returns>An invalid-declaration explanation, or null when valid.</returns>
        /// <exception cref="OperationCanceledException">The host cancels compilation.</exception>
        private static string? ValidateDeclaration(JavaWrapper wrapper, GeneratorExecutionContext context)
        {
            var symbol = wrapper.Symbol;
            if (!(wrapper.Declaration is ClassDeclarationSyntax) || symbol.IsStatic || symbol.Arity != 0 || symbol.ContainingType != null)
            {
                return "JavaClass is supported only on nonstatic, nongeneric, top-level partial classes: '" + symbol.ToDisplayString() + "'.";
            }
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                if (!(reference.GetSyntax(context.CancellationToken) is ClassDeclarationSyntax declaration) || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    return "Every declaration of Java wrapper '" + symbol.ToDisplayString() + "' must be partial.";
                }
            }
            if (symbol.BaseType != null && symbol.BaseType.SpecialType != SpecialType.System_Object && symbol.BaseType.ToDisplayString() != JavaObjectName)
            {
                return "Java wrapper '" + symbol.ToDisplayString() + "' must derive directly from " + JavaObjectName +
                    " (or omit its base). Java inheritance is flattened, not represented as C# wrapper inheritance.";
            }
            if (symbol.IsAbstract)
            {
                return "The C# wrapper '" + symbol.ToDisplayString() + "' must be concrete so JNI references can be wrapped. Java abstract classes and interfaces remain supported.";
            }
            return null;
        }

        /// <summary>Creates a stable, filesystem-safe hint name without conflating qualified wrappers.</summary>
        /// <param name="wrapper">The wrapper whose source is being added.</param>
        /// <returns>The generated source hint name.</returns>
        private static string GetHintName(JavaWrapper wrapper)
        {
            byte[] hash;
            using (var algorithm = SHA256.Create())
            {
                hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(wrapper.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
            }
            var suffix = new StringBuilder();
            foreach (var value in hash)
            {
                suffix.Append(value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return wrapper.Symbol.Name + ".AndroidJava." + suffix + ".g.cs";
        }

        /// <summary>Reports a generator diagnostic at the relevant JavaClass attribute.</summary>
        /// <param name="context">The compilation diagnostic context.</param>
        /// <param name="wrapper">The affected wrapper.</param>
        /// <param name="descriptor">The diagnostic definition.</param>
        /// <param name="message">The actionable description.</param>
        private static void Report(GeneratorExecutionContext context, JavaWrapper wrapper, DiagnosticDescriptor descriptor, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, wrapper.Location, message));
        }
    }

    /// <summary>Collects attributed type syntax without retaining state between compilation executions.</summary>
    internal sealed class DeclarationReceiver : ISyntaxReceiver
    {
        /// <summary>Gets the declarations that require semantic annotation inspection.</summary>
        internal List<TypeDeclarationSyntax> Declarations { get; } = new List<TypeDeclarationSyntax>();

        /// <summary>Collects declarations with attributes for semantic validation.</summary>
        /// <param name="syntaxNode">The current node visited by Roslyn.</param>
        public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
        {
            if (syntaxNode is TypeDeclarationSyntax declaration && declaration.AttributeLists.Count != 0)
            {
                Declarations.Add(declaration);
            }
        }
    }

    /// <summary>Holds one annotated C# wrapper and its compilation-local extraction settings.</summary>
    internal sealed class JavaWrapper
    {
        /// <summary>Gets the annotated C# type symbol.</summary>
        internal INamedTypeSymbol Symbol { get; }

        /// <summary>Gets the declaration used to discover the annotation.</summary>
        internal TypeDeclarationSyntax Declaration { get; }

        /// <summary>Gets the semantic JavaClass attribute.</summary>
        internal AttributeData Attribute { get; }

        /// <summary>Gets the exact requested Java binary name.</summary>
        internal string JavaName { get; }

        /// <summary>Gets the annotation location used for diagnostics.</summary>
        internal Location Location { get; }

        /// <summary>Gets or sets the resolved extraction settings.</summary>
        internal JavaApiConfiguration? Configuration { get; set; }

        /// <summary>Creates one wrapper declaration record.</summary>
        /// <param name="symbol">The annotated type.</param>
        /// <param name="declaration">The originating syntax.</param>
        /// <param name="attribute">The JavaClass annotation.</param>
        /// <param name="javaName">The Java binary name.</param>
        internal JavaWrapper(INamedTypeSymbol symbol, TypeDeclarationSyntax declaration, AttributeData attribute, string javaName)
        {
            Symbol = symbol;
            Declaration = declaration;
            Attribute = attribute;
            JavaName = javaName;
            Location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? declaration.GetLocation();
        }
    }
}
