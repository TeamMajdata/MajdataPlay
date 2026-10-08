import com.sun.source.doctree.DocCommentTree;
import com.sun.source.doctree.DocTree;
import com.sun.source.doctree.EndElementTree;
import com.sun.source.doctree.EntityTree;
import com.sun.source.doctree.ErroneousTree;
import com.sun.source.doctree.IndexTree;
import com.sun.source.doctree.LinkTree;
import com.sun.source.doctree.LiteralTree;
import com.sun.source.doctree.ParamTree;
import com.sun.source.doctree.ReturnTree;
import com.sun.source.doctree.StartElementTree;
import com.sun.source.doctree.SummaryTree;
import com.sun.source.doctree.SystemPropertyTree;
import com.sun.source.doctree.TextTree;
import com.sun.source.doctree.ThrowsTree;
import com.sun.source.doctree.UnknownInlineTagTree;
import com.sun.source.doctree.ValueTree;
import com.sun.source.tree.AnnotatedTypeTree;
import com.sun.source.tree.ArrayTypeTree;
import com.sun.source.tree.ClassTree;
import com.sun.source.tree.CompilationUnitTree;
import com.sun.source.tree.IdentifierTree;
import com.sun.source.tree.MemberSelectTree;
import com.sun.source.tree.MethodTree;
import com.sun.source.tree.ParameterizedTypeTree;
import com.sun.source.tree.PrimitiveTypeTree;
import com.sun.source.tree.Tree;
import com.sun.source.tree.TypeParameterTree;
import com.sun.source.tree.VariableTree;
import com.sun.source.util.DocTreePath;
import com.sun.source.util.DocTrees;
import com.sun.source.util.JavacTask;
import com.sun.source.util.TreePath;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStreamWriter;
import java.io.StringWriter;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.HashSet;
import java.util.IdentityHashMap;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.TreeSet;
import java.util.zip.ZipEntry;
import java.util.zip.ZipFile;

import javax.lang.model.SourceVersion;
import javax.lang.model.element.Element;
import javax.lang.model.element.ElementKind;
import javax.lang.model.element.ExecutableElement;
import javax.lang.model.element.Modifier;
import javax.lang.model.element.NestingKind;
import javax.lang.model.element.TypeElement;
import javax.lang.model.element.TypeParameterElement;
import javax.lang.model.element.VariableElement;
import javax.lang.model.type.ArrayType;
import javax.lang.model.type.DeclaredType;
import javax.lang.model.type.TypeKind;
import javax.lang.model.type.TypeMirror;
import javax.lang.model.type.TypeVariable;
import javax.lang.model.type.WildcardType;
import javax.lang.model.util.Elements;
import javax.lang.model.util.Types;
import javax.tools.Diagnostic;
import javax.tools.DiagnosticCollector;
import javax.tools.JavaCompiler;
import javax.tools.JavaFileObject;
import javax.tools.SimpleJavaFileObject;
import javax.tools.StandardLocation;
import javax.tools.ToolProvider;

/**
 * Extracts public Android Java API metadata using compiler symbols, never target
 * class loading or execution. Run this dependency-free file with a JDK 17 source
 * launcher; see the adjacent README for the command-line and XML contract.
 */
public final class JavaApiExtractor {
    /** Prevents construction of this command-line utility. */
    private JavaApiExtractor() {
    }

    /**
     * Writes one UTF-8 XML document on success, or an actionable stderr diagnostic
     * and a nonzero exit code on failure. No partial XML is published.
     *
     * @param arguments command-line option/value pairs
     */
    public static void main(String[] arguments) {
        try {
            var options = Options.parse(arguments);
            var compiler = ToolProvider.getSystemJavaCompiler();
            if (compiler == null) {
                throw new ExtractionException("No Java compiler is available. Use a full JDK 17, not a JRE.");
            }
            var xml = extract(compiler, options);
            var output = new OutputStreamWriter(System.out, StandardCharsets.UTF_8);
            output.write(xml);
            output.flush();
        } catch (ExtractionException | IOException | RuntimeException exception) {
            var message = exception.getMessage();
            if (message == null || message.isBlank()) {
                message = exception.getClass().getSimpleName();
            }
            try {
                var error = new OutputStreamWriter(System.err, StandardCharsets.UTF_8);
                error.write("JavaApiExtractor: " + message + "\n");
                error.flush();
            } catch (IOException ignored) {
                // The original failure remains represented by the exit status.
            }
            System.exit(1);
        }
    }

    /** Performs analysis without calling compilation/generation or loading classes. */
    private static String extract(JavaCompiler compiler, Options options) throws IOException, ExtractionException {
        var diagnostics = new CompilerDiagnostics();
        try (var files = compiler.getStandardFileManager(diagnostics.collector, Locale.ROOT, StandardCharsets.UTF_8)) {
            // Empty locations prevent accidental discovery through CLASSPATH or
            // source files adjacent to a classpath entry (including SDK sources).
            files.setLocationFromPaths(StandardLocation.CLASS_PATH, options.classpath);
            files.setLocationFromPaths(StandardLocation.SOURCE_PATH, List.of());
            Iterable<? extends JavaFileObject> sources = options.sources.isEmpty()
                    ? List.of(new MemorySource("JavaApiExtractorLookupAnchor", "final class JavaApiExtractorLookupAnchor {}"))
                    : files.getJavaFileObjectsFromPaths(options.sources);
            var flags = List.of("-proc:none", "-source", "8", "-target", "8",
                    "-bootclasspath", options.androidJar.toString(), "-encoding", "UTF-8", "-implicit:none", "-Xlint:none");
            var task = (JavacTask) compiler.getTask(diagnostics.output, files, diagnostics.collector, flags, null, sources);
            try {
                task.parse();
                task.analyze();
                diagnostics.check("Java source analysis failed. Supply all dependencies with --classpath or --source.");
                var elements = task.getElements();
                var types = task.getTypes();
                var trees = DocTrees.instance(task);
                try (var documentation = new DocumentationRepository(compiler, options.documentation, elements, types)) {
                    var extractor = new ApiModel(elements, types, trees, documentation, options.includeInherited);
                    var result = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<java-api version=\"1\">\n");
                    for (var name : options.typeNames) {
                        var type = extractor.resolveBinaryName(name);
                        if (type == null) {
                            throw new ExtractionException("Cannot resolve Java binary type '" + name
                                    + "'. Check --source, --classpath and --android-jar; nested types use '$', not '.'.");
                        }
                        extractor.appendType(result, type);
                    }
                    diagnostics.check("API symbol resolution failed. Check for missing classpath dependencies.");
                    return result.append("</java-api>\n").toString();
                }
            } catch (RuntimeException exception) {
                diagnostics.check("Java compiler symbol resolution failed. Check for missing classpath dependencies.");
                throw new ExtractionException("Java compiler symbol resolution failed: " + exception.getMessage()
                        + ". Check dependencies in --classpath and the selected --android-jar.", exception);
            }
        }
    }

    /** Validated inputs; source and type order is deterministic, classpath precedence is preserved. */
    private static final class Options {
        private Path androidJar;
        private final List<Path> documentation = new ArrayList<>();
        private boolean includeInherited;
        private final List<Path> sources = new ArrayList<>();
        private final List<Path> classpath = new ArrayList<>();
        private final Set<String> typeNames = new TreeSet<>();

        /** Parses strict option/value pairs and validates all paths before javac runs. */
        private static Options parse(String[] arguments) throws ExtractionException, IOException {
            var result = new Options();
            var sourceArguments = new ArrayList<Path>();
            var classpathArguments = new LinkedHashSet<Path>();
            var singletonOptions = new HashSet<String>();
            for (var index = 0; index < arguments.length; index += 2) {
                var option = arguments[index];
                if (!Set.of("--android-jar", "--source", "--classpath", "--type", "--documentation", "--include-inherited").contains(option)) {
                    throw new ExtractionException("Unknown option '" + option + "'. " + usage());
                }
                if (index + 1 == arguments.length || arguments[index + 1].startsWith("--") || arguments[index + 1].isBlank()) {
                    throw new ExtractionException("Missing value for " + option + ". " + usage());
                }
                var value = arguments[index + 1];
                if (Set.of("--android-jar", "--include-inherited").contains(option) && !singletonOptions.add(option)) {
                    throw new ExtractionException("Option " + option + " may be specified only once.");
                }
                switch (option) {
                    case "--android-jar" -> result.androidJar = readablePath(value, option);
                    case "--source" -> sourceArguments.add(readablePath(value, option));
                    case "--classpath" -> classpathArguments.add(readablePath(value, option));
                    case "--documentation" -> result.documentation.add(readablePath(value, option));
                    case "--include-inherited" -> {
                        if (!value.equals("true") && !value.equals("false")) {
                            throw new ExtractionException("--include-inherited must be 'true' or 'false'.");
                        }
                        result.includeInherited = Boolean.parseBoolean(value);
                    }
                    case "--type" -> {
                        if (!SourceVersion.isName(value, SourceVersion.RELEASE_8)) {
                            throw new ExtractionException("Invalid Java binary type name '" + value + "'.");
                        }
                        result.typeNames.add(value);
                    }
                    default -> throw new AssertionError("Option was not validated.");
                }
            }
            if (result.androidJar == null || result.typeNames.isEmpty()) {
                throw new ExtractionException("--android-jar and at least one --type are required. " + usage());
            }
            checkArchive(result.androidJar, "--android-jar", true);
            for (var path : classpathArguments) {
                if (!Files.isDirectory(path)) {
                    if (!path.toString().toLowerCase(Locale.ROOT).endsWith(".jar")) {
                        throw new ExtractionException("--classpath expects a .jar or a class directory, not '" + path
                                + "'. Normalize a .class file to its package root in the caller.");
                    }
                    checkArchive(path, "--classpath", false);
                }
                result.classpath.add(path);
            }
            for (var path : result.documentation) {
                if (!Files.isDirectory(path)) {
                    checkArchive(path, "--documentation", false);
                }
            }
            var sourcePaths = new TreeSet<Path>(Comparator.comparing(Path::toString));
            for (var path : sourceArguments) {
                if (Files.isDirectory(path)) {
                    try (var entries = Files.walk(path)) {
                        var matching = entries.filter(Files::isRegularFile)
                                .filter(entry -> entry.toString().endsWith(".java")).toList();
                        if (matching.isEmpty()) {
                            throw new ExtractionException("--source directory contains no .java files: " + path);
                        }
                        for (var entry : matching) {
                            sourcePaths.add(readablePath(entry.toString(), "--source"));
                        }
                    }
                } else if (Files.isRegularFile(path) && path.toString().endsWith(".java")) {
                    sourcePaths.add(path);
                } else {
                    throw new ExtractionException("--source expects a .java file or a source directory: " + path);
                }
            }
            result.sources.addAll(sourcePaths);
            return result;
        }

        /** Returns a canonical, readable path, retaining no caller-controlled traversal. */
        private static Path readablePath(String value, String option) throws ExtractionException, IOException {
            var path = Path.of(value).toAbsolutePath().normalize();
            if (!Files.exists(path) || !Files.isReadable(path)) {
                throw new ExtractionException(option + " path does not exist or is not readable: " + path);
            }
            return path.toRealPath();
        }

        /** Verifies archives without extracting entries or loading their classes. */
        private static void checkArchive(Path path, String option, boolean requireRuntime) throws ExtractionException {
            if (!Files.isRegularFile(path)) {
                throw new ExtractionException(option + " expects an archive file: " + path);
            }
            try (var archive = new ZipFile(path.toFile())) {
                if (requireRuntime && archive.getEntry("java/lang/Object.class") == null) {
                    throw new ExtractionException("--android-jar must contain Android runtime classes (java/lang/Object.class): " + path);
                }
            } catch (IOException exception) {
                throw new ExtractionException("Cannot read " + option + " archive '" + path + "': " + exception.getMessage(), exception);
            }
        }

        /** Returns the usage text; errors and usage never contaminate stdout. */
        private static String usage() {
            return "Usage: java JavaApiExtractor.java --android-jar <android.jar> --type <binary-name>"
                    + " [--type <binary-name> ...] [--source <file-or-root> ...] [--classpath <jar-or-directory> ...]"
                    + " [--documentation <source-root-or-zip>] [--include-inherited true|false]";
        }
    }

    /** A source object used for symbol lookup and parse-only documentation. */
    private static final class MemorySource extends SimpleJavaFileObject {
        private final String content;

        private MemorySource(String simpleName, String content) {
            super(URI.create("string:///" + simpleName + ".java"), Kind.SOURCE);
            this.content = content;
        }

        @Override
        public CharSequence getCharContent(boolean ignoreEncodingErrors) {
            return content;
        }
    }

    /** Collects compiler diagnostics in memory, never directly on stdout or stderr. */
    private static final class CompilerDiagnostics {
        private final DiagnosticCollector<JavaFileObject> collector = new DiagnosticCollector<>();
        private final StringWriter output = new StringWriter();

        /** Fails with source/line diagnostics if javac reported any errors. */
        private void check(String context) throws ExtractionException {
            var errors = collector.getDiagnostics().stream().filter(item -> item.getKind() == Diagnostic.Kind.ERROR).toList();
            if (errors.isEmpty()) {
                return;
            }
            var message = new StringBuilder(context);
            for (var error : errors) {
                message.append('\n');
                if (error.getSource() != null) {
                    message.append(error.getSource().getName()).append(':').append(error.getLineNumber()).append(": ");
                }
                message.append(error.getMessage(Locale.ROOT));
            }
            if (!output.toString().isBlank()) {
                message.append('\n').append(output);
            }
            throw new ExtractionException(message.toString());
        }
    }

    /** A controlled extraction failure with an actionable, user-visible message. */
    private static final class ExtractionException extends Exception {
        /** Keeps this internal exception serialization version stable. */
        private static final long serialVersionUID = 1L;

        private ExtractionException(String message) {
            super(message);
        }

        private ExtractionException(String message, Throwable cause) {
            super(message, cause);
        }
    }

    /** Compiler-backed member selection, JVM descriptors, constants and XML. */
    private static final class ApiModel {
        private final Elements elements;
        private final Types types;
        private final DocTrees trees;
        private final DocumentationRepository documentation;
        private final boolean includeInherited;

        private ApiModel(Elements elements, Types types, DocTrees trees, DocumentationRepository documentation, boolean includeInherited) {
            this.elements = elements;
            this.types = types;
            this.trees = trees;
            this.documentation = documentation;
            this.includeInherited = includeInherited;
        }

        /** Resolves binary nested names while preserving legal literal '$' identifiers. */
        private TypeElement resolveBinaryName(String name) {
            var direct = elements.getTypeElement(name);
            if (direct != null && binaryName(direct).equals(name)) {
                return direct;
            }
            for (var separator = name.lastIndexOf('$'); separator >= 0; separator = name.lastIndexOf('$', separator - 1)) {
                var outer = resolveBinaryName(name.substring(0, separator));
                if (outer == null) {
                    continue;
                }
                var simpleName = name.substring(separator + 1);
                for (var member : outer.getEnclosedElements()) {
                    if (member instanceof TypeElement nested && nested.getSimpleName().contentEquals(simpleName)
                            && binaryName(nested).equals(name)) {
                        return nested;
                    }
                }
            }
            return null;
        }

        /** Serializes one requested type and its deterministically selected API. */
        private void appendType(StringBuilder xml, TypeElement type) throws ExtractionException, IOException {
            if (type.getNestingKind() == NestingKind.LOCAL || type.getNestingKind() == NestingKind.ANONYMOUS) {
                throw new ExtractionException("Local and anonymous classes are unsupported public APIs: " + binaryName(type));
            }
            validateHierarchy(type);
            var isInterface = type.getKind().isInterface();
            var modifiers = type.getModifiers();
            var docs = getDocumentation(type);
            xml.append("  <type");
            attribute(xml, "name", binaryName(type));
            attribute(xml, "interface", isInterface);
            attribute(xml, "abstract", modifiers.contains(Modifier.ABSTRACT));
            attribute(xml, "final", modifiers.contains(Modifier.FINAL));
            attribute(xml, "deprecated", elements.isDeprecated(type));
            attribute(xml, "summary", docs.summary);
            attribute(xml, "documentationUrl", referenceUrl(type));
            xml.append(">\n");

            var members = includeInherited ? elements.getAllMembers(type) : type.getEnclosedElements();
            var fields = new ArrayList<VariableElement>();
            var methods = new ArrayList<ExecutableElement>();
            var nestedTypes = new TreeSet<String>();
            for (var member : members) {
                if (!member.getModifiers().contains(Modifier.PUBLIC) || elements.getOrigin(member) == Elements.Origin.SYNTHETIC) {
                    continue;
                }
                if (member instanceof VariableElement field && (member.getKind() == ElementKind.FIELD || member.getKind() == ElementKind.ENUM_CONSTANT)) {
                    fields.add(field);
                } else if (member instanceof ExecutableElement method && member.getKind() == ElementKind.METHOD && !elements.isBridge(method)) {
                    methods.add(method);
                } else if (member instanceof TypeElement nested) {
                    nestedTypes.add(binaryName(nested));
                }
            }
            if (!isInterface && !modifiers.contains(Modifier.ABSTRACT)) {
                // Constructors are never inherited, regardless of the global flag.
                for (var member : type.getEnclosedElements()) {
                    if (member.getKind() == ElementKind.CONSTRUCTOR && member.getModifiers().contains(Modifier.PUBLIC)
                            && elements.getOrigin(member) != Elements.Origin.SYNTHETIC) {
                        methods.add((ExecutableElement) member);
                    }
                }
            }
            fields.removeIf(field -> fields.stream().anyMatch(other -> other != field && elements.hides(other, field)));
            methods.removeIf(method -> method.getKind() != ElementKind.CONSTRUCTOR && methods.stream().anyMatch(other ->
                    other != method && other.getKind() != ElementKind.CONSTRUCTOR
                            && (elements.overrides(other, method, type) || elements.hides(other, method))));
            fields.sort(Comparator.comparing((VariableElement field) -> field.getSimpleName().toString())
                    .thenComparing(field -> descriptorUnchecked(field.asType())).thenComparing(field -> binaryName(declaringType(field))));
            methods.sort(Comparator.comparing((ExecutableElement method) -> method.getSimpleName().toString())
                    .thenComparing(this::methodDescriptorUnchecked).thenComparing(method -> binaryName(declaringType(method))));
            var fieldKeys = new HashSet<String>();
            for (var field : fields) {
                var descriptor = descriptor(field.asType());
                if (fieldKeys.add(field.getSimpleName() + ":" + descriptor)) {
                    appendField(xml, field, descriptor);
                }
            }
            var methodKeys = new HashSet<String>();
            for (var method : methods) {
                var descriptor = methodDescriptor(method);
                if (methodKeys.add(method.getSimpleName() + descriptor)) {
                    appendMethod(xml, method, descriptor);
                }
            }
            for (var nested : nestedTypes) {
                xml.append("    <nestedType");
                attribute(xml, "name", nested);
                xml.append("/>\n");
            }
            xml.append("  </type>\n");
        }

        /** Rejects missing hierarchy dependencies, including generic bound errors. */
        private void validateHierarchy(TypeElement root) throws ExtractionException {
            var pending = new ArrayDeque<TypeMirror>();
            var visited = new HashSet<String>();
            pending.add(root.asType());
            while (!pending.isEmpty()) {
                var type = pending.removeFirst();
                validateType(type, Collections.newSetFromMap(new IdentityHashMap<>()));
                if (type instanceof DeclaredType declared && declared.asElement() instanceof TypeElement element
                        && visited.add(binaryName(element))) {
                    for (var parameter : element.getTypeParameters()) {
                        for (var bound : parameter.getBounds()) {
                            validateType(bound, Collections.newSetFromMap(new IdentityHashMap<>()));
                        }
                    }
                    pending.addAll(types.directSupertypes(type));
                }
            }
        }

        /** Rejects error symbols before erasure can conceal a missing dependency. */
        private void validateType(TypeMirror type, Set<TypeMirror> visited) throws ExtractionException {
            if (type == null || !visited.add(type)) {
                return;
            }
            switch (type.getKind()) {
                case ERROR -> throw new ExtractionException("Unresolved Java API dependency '" + type
                        + "'. Add its jar/class directory with --classpath, or its sources with --source.");
                case ARRAY -> validateType(((ArrayType) type).getComponentType(), visited);
                case DECLARED -> {
                    var declared = (DeclaredType) type;
                    // getKind completes a lazy classfile symbol and detects absent dependencies.
                    declared.asElement().getKind();
                    validateType(declared.getEnclosingType(), visited);
                    for (var argument : declared.getTypeArguments()) {
                        validateType(argument, visited);
                    }
                }
                case TYPEVAR -> validateType(((TypeVariable) type).getUpperBound(), visited);
                case WILDCARD -> {
                    var wildcard = (WildcardType) type;
                    validateType(wildcard.getExtendsBound(), visited);
                    validateType(wildcard.getSuperBound(), visited);
                }
                case INTERSECTION -> {
                    for (var bound : ((javax.lang.model.type.IntersectionType) type).getBounds()) {
                        validateType(bound, visited);
                    }
                }
                default -> {
                    // Primitive, void, none and null types contain no class dependencies.
                }
            }
        }

        /** Returns a declaration-erased JVM type descriptor, not generic substitutions. */
        private String descriptor(TypeMirror type) throws ExtractionException {
            validateType(type, Collections.newSetFromMap(new IdentityHashMap<>()));
            var erased = types.erasure(type);
            return switch (erased.getKind()) {
                case BOOLEAN -> "Z";
                case BYTE -> "B";
                case CHAR -> "C";
                case SHORT -> "S";
                case INT -> "I";
                case LONG -> "J";
                case FLOAT -> "F";
                case DOUBLE -> "D";
                case VOID -> "V";
                case ARRAY -> "[" + descriptor(((ArrayType) erased).getComponentType());
                case DECLARED -> "L" + binaryName((TypeElement) ((DeclaredType) erased).asElement()).replace('.', '/') + ";";
                default -> throw new ExtractionException("Cannot encode JVM descriptor for '" + type + "' (" + erased.getKind() + ").");
            };
        }

        /** Adapts checked validation to sort comparators; the outer task reports its cause. */
        private String descriptorUnchecked(TypeMirror type) {
            try {
                return descriptor(type);
            } catch (ExtractionException exception) {
                throw new IllegalArgumentException(exception.getMessage(), exception);
            }
        }

        /** Includes the hidden enclosing-instance argument of a non-static member constructor. */
        private String methodDescriptor(ExecutableElement method) throws ExtractionException {
            var result = new StringBuilder("(");
            var enclosing = constructorEnclosingType(method);
            if (enclosing != null) {
                result.append(descriptor(enclosing.asType()));
            }
            for (var parameter : method.getParameters()) {
                result.append(descriptor(parameter.asType()));
            }
            result.append(')').append(method.getKind() == ElementKind.CONSTRUCTOR ? "V" : descriptor(method.getReturnType()));
            for (var exception : method.getThrownTypes()) {
                validateType(exception, Collections.newSetFromMap(new IdentityHashMap<>()));
            }
            for (var parameter : method.getTypeParameters()) {
                for (var bound : parameter.getBounds()) {
                    validateType(bound, Collections.newSetFromMap(new IdentityHashMap<>()));
                }
            }
            return result.toString();
        }

        /** Adapts method descriptor validation to deterministic sort comparators. */
        private String methodDescriptorUnchecked(ExecutableElement method) {
            try {
                return methodDescriptor(method);
            } catch (ExtractionException exception) {
                throw new IllegalArgumentException(exception.getMessage(), exception);
            }
        }

        /** Returns the required outer instance only for non-static member constructors. */
        private TypeElement constructorEnclosingType(ExecutableElement method) {
            var owner = declaringType(method);
            if (method.getKind() == ElementKind.CONSTRUCTOR && owner.getKind() == ElementKind.CLASS
                    && owner.getNestingKind() == NestingKind.MEMBER
                    && !owner.getModifiers().contains(Modifier.STATIC) && owner.getEnclosingElement() instanceof TypeElement outer) {
                return outer;
            }
            return null;
        }

        /** Serializes fields and only genuine compile-time scalar constants. */
        private void appendField(StringBuilder xml, VariableElement field, String descriptor) throws ExtractionException, IOException {
            var modifiers = field.getModifiers();
            xml.append("    <field");
            attribute(xml, "name", field.getSimpleName());
            attribute(xml, "descriptor", descriptor);
            attribute(xml, "static", modifiers.contains(Modifier.STATIC));
            attribute(xml, "final", modifiers.contains(Modifier.FINAL));
            attribute(xml, "deprecated", elements.isDeprecated(field));
            attribute(xml, "declaringType", binaryName(declaringType(field)));
            var constant = field.getConstantValue();
            if (constant != null && modifiers.contains(Modifier.STATIC) && modifiers.contains(Modifier.FINAL)) {
                var kind = switch (field.asType().getKind()) {
                    case BOOLEAN -> "boolean";
                    case BYTE -> "byte";
                    case CHAR -> "char";
                    case SHORT -> "short";
                    case INT -> "int";
                    case LONG -> "long";
                    case FLOAT -> "float";
                    case DOUBLE -> "double";
                    case DECLARED -> descriptor.equals("Ljava/lang/String;") ? "string" : null;
                    default -> null;
                };
                if (kind != null) {
                    attribute(xml, "constantKind", kind);
                    if (constant instanceof String string && !isXmlText(string)) {
                        attribute(xml, "constantEncoding", "base64-utf16be");
                        attribute(xml, "constantValue", base64Utf16Be(string));
                    } else {
                        attribute(xml, "constantValue", constant instanceof Character character ? Integer.toString(character) : constant.toString());
                    }
                }
            }
            attribute(xml, "summary", getDocumentation(field).summary);
            xml.append("/>\n");
        }

        /** Serializes declared parameters and documented/declared exception types. */
        private void appendMethod(StringBuilder xml, ExecutableElement method, String descriptor) throws ExtractionException, IOException {
            var docs = getDocumentation(method);
            xml.append("    <method");
            attribute(xml, "name", method.getSimpleName());
            attribute(xml, "descriptor", descriptor);
            attribute(xml, "static", method.getModifiers().contains(Modifier.STATIC));
            attribute(xml, "deprecated", elements.isDeprecated(method));
            attribute(xml, "declaringType", binaryName(declaringType(method)));
            attribute(xml, "summary", docs.summary);
            attribute(xml, "returns", docs.returns);
            xml.append(">\n");
            if (constructorEnclosingType(method) != null) {
                xml.append("      <parameter");
                attribute(xml, "name", "enclosingInstance");
                attribute(xml, "summary", "");
                xml.append("/>\n");
            }
            for (var index = 0; index < method.getParameters().size(); index++) {
                var parameter = method.getParameters().get(index);
                var name = docs.parameterNames.size() == method.getParameters().size()
                        ? docs.parameterNames.get(index) : parameter.getSimpleName().toString();
                xml.append("      <parameter");
                attribute(xml, "name", name);
                attribute(xml, "summary", docs.parameters.getOrDefault(name, ""));
                xml.append("/>\n");
            }
            var exceptions = new TreeMap<String, String>();
            for (var thrown : method.getThrownTypes()) {
                var erased = types.erasure(thrown);
                if (erased instanceof DeclaredType declared) {
                    exceptions.put(binaryName((TypeElement) declared.asElement()), "");
                }
            }
            for (var thrown : docs.exceptions) {
                if (thrown.binaryName != null) {
                    exceptions.put(thrown.binaryName, thrown.summary);
                } else {
                    // A simple @throws reference is usable only if exactly one
                    // declared exception matches; ambiguous documentation is omitted.
                    var matching = exceptions.keySet().stream().filter(name -> name.equals(thrown.reference)
                            || simpleBinaryName(name).equals(thrown.reference)).toList();
                    if (matching.size() == 1) {
                        exceptions.put(matching.get(0), thrown.summary);
                    }
                }
            }
            for (var exception : exceptions.entrySet()) {
                xml.append("      <exception");
                attribute(xml, "type", exception.getKey());
                attribute(xml, "summary", exception.getValue());
                xml.append("/>\n");
            }
            xml.append("    </method>\n");
        }

        /** Prefers comments on analyzed sources, falling back to matched optional docs. */
        private Documentation getDocumentation(Element element) throws ExtractionException, IOException {
            var path = trees.getPath(element);
            var comment = path == null ? null : trees.getDocCommentTree(path);
            if (comment != null) {
                var result = Documentation.read(trees, comment);
                var exceptions = new ArrayList<ExceptionDocumentation>();
                var commentPath = new DocTreePath(path, comment);
                for (var tag : comment.getBlockTags()) {
                    if (tag instanceof ThrowsTree thrown) {
                        var referenced = trees.getElement(DocTreePath.getPath(commentPath, thrown.getExceptionName()));
                        String name = null;
                        if (referenced instanceof TypeElement type) {
                            name = binaryName(type);
                        } else if (referenced instanceof TypeParameterElement parameter) {
                            var erased = types.erasure(parameter.asType());
                            if (erased instanceof DeclaredType declared) {
                                name = binaryName((TypeElement) declared.asElement());
                            }
                        }
                        exceptions.add(new ExceptionDocumentation(thrown.getExceptionName().getSignature(), name,
                                PlainText.read(trees, thrown.getDescription())));
                    }
                }
                return result.withExceptions(exceptions);
            }
            return documentation.lookup(element, this);
        }

        /** Returns the class declaring a member (or the type itself). */
        private TypeElement declaringType(Element element) {
            return element instanceof TypeElement type ? type : (TypeElement) element.getEnclosingElement();
        }

        /** Uses the compiler's binary name, including '$' for nested types. */
        private String binaryName(TypeElement type) {
            return elements.getBinaryName(type).toString();
        }

        /** Links Android runtime APIs to their canonical Android reference page. */
        private String referenceUrl(TypeElement type) {
            var name = type.getQualifiedName().toString();
            if (!name.startsWith("android.") && !name.startsWith("java.")) {
                return "";
            }
            var packageName = elements.getPackageOf(type).getQualifiedName().toString();
            var localName = name.substring(packageName.length() + 1);
            return "https://developer.android.com/reference/" + packageName.replace('.', '/') + "/" + localName;
        }
    }

    /** Searches ordered source roots without merging ambiguous declarations or executing their bodies. */
    private static final class DocumentationRepository implements AutoCloseable {
        private final List<DocumentationSource> sources = new ArrayList<>();

        /** Opens every optional documentation input, closing earlier archives if opening fails. */
        private DocumentationRepository(JavaCompiler compiler, List<Path> paths, Elements elements, Types types) throws IOException {
            try {
                for (var path : new LinkedHashSet<>(paths)) {
                    sources.add(new DocumentationSource(compiler, path, elements, types));
                }
            } catch (IOException exception) {
                try {
                    close();
                } catch (IOException closeException) {
                    exception.addSuppressed(closeException);
                }
                throw exception;
            }
        }

        /** Prefers the first exact signature match with actual prose over stripped SDK stubs. */
        private Documentation lookup(Element element, ApiModel model) throws IOException, ExtractionException {
            var fallback = Documentation.EMPTY;
            for (var source : sources) {
                var candidate = source.lookup(element, model);
                if (!candidate.summary.isEmpty() || !candidate.parameters.isEmpty()
                        || !candidate.returns.isEmpty()
                        || candidate.exceptions.stream().anyMatch(exception -> !exception.summary.isEmpty())) {
                    return candidate;
                }
                if (fallback == Documentation.EMPTY && candidate != Documentation.EMPTY) {
                    // Resolved parameter names remain useful even when the source has no Javadoc.
                    fallback = candidate;
                }
            }
            return fallback;
        }

        /** Closes every archive even when one source reports a close error. */
        @Override
        public void close() throws IOException {
            IOException failure = null;
            for (var source : sources) {
                try {
                    source.close();
                } catch (IOException exception) {
                    if (failure == null) {
                        failure = exception;
                    } else {
                        failure.addSuppressed(exception);
                    }
                }
            }
            if (failure != null) {
                throw failure;
            }
        }
    }

    /** Obtains optional source comments without analyzing SDK implementations. */
    private static final class DocumentationSource implements AutoCloseable {
        /** Bounds decompression and parser memory for any individual documentation file. */
        private static final int MAX_SOURCE_BYTES = 16 * 1024 * 1024;
        private final JavaCompiler compiler;
        private final Path root;
        private final ZipFile archive;
        private final Elements elements;
        private final Types types;
        private final Map<String, List<ZipEntry>> archiveEntries = new HashMap<>();
        private final Map<String, ParsedDocumentation> cache = new HashMap<>();

        private DocumentationSource(JavaCompiler compiler, Path path, Elements elements, Types types) throws IOException {
            this.compiler = compiler;
            this.elements = elements;
            this.types = types;
            root = path != null && Files.isDirectory(path) ? path : null;
            archive = path != null && root == null ? new ZipFile(path.toFile()) : null;
            if (archive != null) {
                var entries = archive.entries();
                while (entries.hasMoreElements()) {
                    var entry = entries.nextElement();
                    if (!entry.isDirectory() && safeEntryName(entry.getName()) && entry.getName().endsWith(".java")) {
                        archiveEntries.computeIfAbsent(entry.getName(), ignored -> new ArrayList<>()).add(entry);
                    }
                }
            }
        }

        /** Attaches comments only to a unique, fully resolved declaration signature. */
        private Documentation lookup(Element element, ApiModel model) throws IOException, ExtractionException {
            if (root == null && archive == null) {
                return Documentation.EMPTY;
            }
            var owner = model.declaringType(element);
            var topLevel = owner;
            while (topLevel.getEnclosingElement() instanceof TypeElement enclosing) {
                topLevel = enclosing;
            }
            var packageName = elements.getPackageOf(topLevel).getQualifiedName().toString();
            var relative = (packageName.isEmpty() ? "" : packageName.replace('.', '/') + "/") + topLevel.getSimpleName() + ".java";
            var parsed = cache.get(relative);
            if (parsed == null) {
                var source = readSource(relative);
                parsed = source == null ? ParsedDocumentation.EMPTY : parse(relative, source);
                cache.put(relative, parsed);
            }
            var matchingTypes = parsed.typeDeclarations.getOrDefault(model.binaryName(owner), List.of());
            if (matchingTypes.size() != 1) {
                return Documentation.EMPTY;
            }
            var declaration = matchingTypes.get(0);
            if (element instanceof TypeElement) {
                return declaration.documentation;
            }
            var context = new SourceContext(parsed.unit, declaration, elements, types);
            var matches = new ArrayList<Documentation>();
            for (var member : declaration.tree.getMembers()) {
                if (element instanceof VariableElement field && member instanceof VariableTree variable
                        && variable.getName().contentEquals(field.getSimpleName())) {
                    var signature = context.descriptor(variable.getType(), Map.of());
                    if (signature != null && signature.equals(model.descriptor(field.asType()))) {
                        matches.add(readMemberDocumentation(parsed, declaration, member, context, List.of()));
                    }
                } else if (element instanceof ExecutableElement method && member instanceof MethodTree methodTree
                        && methodTree.getName().contentEquals(method.getSimpleName())) {
                    var bounds = SourceContext.bounds(methodTree.getTypeParameters());
                    var signature = new StringBuilder("(");
                    var outer = model.constructorEnclosingType(method);
                    if (outer != null) {
                        signature.append(model.descriptor(outer.asType()));
                    }
                    var resolved = true;
                    var names = new ArrayList<String>();
                    for (var parameter : methodTree.getParameters()) {
                        var descriptor = context.descriptor(parameter.getType(), bounds);
                        if (descriptor == null) {
                            resolved = false;
                            break;
                        }
                        signature.append(descriptor);
                        names.add(parameter.getName().toString());
                    }
                    var returns = methodTree.getReturnType() == null ? "V" : context.descriptor(methodTree.getReturnType(), bounds);
                    if (resolved && returns != null && signature.append(')').append(returns).toString().equals(model.methodDescriptor(method))) {
                        matches.add(readMemberDocumentation(parsed, declaration, member, context, names));
                    }
                }
            }
            return matches.size() == 1 ? matches.get(0) : Documentation.EMPTY;
        }

        /** Reads a member's comment and resolves exception references lexically. */
        private Documentation readMemberDocumentation(ParsedDocumentation parsed, ParsedType owner, Tree member,
                SourceContext context, List<String> parameterNames) {
            var path = new TreePath(owner.path, member);
            var comment = parsed.trees.getDocCommentTree(path);
            if (comment == null) {
                return Documentation.EMPTY;
            }
            var docs = Documentation.read(parsed.trees, comment);
            var exceptions = new ArrayList<ExceptionDocumentation>();
            var bounds = member instanceof MethodTree method ? SourceContext.bounds(method.getTypeParameters()) : Map.<String, Tree>of();
            for (var thrown : docs.exceptions) {
                var descriptor = context.namedDescriptor(thrown.reference, bounds, new HashSet<>());
                var binaryName = descriptor != null && descriptor.startsWith("L")
                        ? descriptor.substring(1, descriptor.length() - 1).replace('/', '.') : null;
                exceptions.add(new ExceptionDocumentation(thrown.reference, binaryName, thrown.summary));
            }
            return new Documentation(docs.summary, docs.parameters, docs.returns, exceptions, parameterNames);
        }

        /** Selects exact or uniquely suffix-matching archive entries; never extracts zip paths. */
        private String readSource(String relative) throws IOException, ExtractionException {
            if (root != null) {
                var candidate = root.resolve(relative).normalize();
                if (!candidate.startsWith(root) || !Files.isRegularFile(candidate)) {
                    return null;
                }
                var actual = candidate.toRealPath();
                if (!actual.startsWith(root)) {
                    throw new ExtractionException("Documentation source symlink escapes --documentation root: " + candidate);
                }
                try (var input = Files.newInputStream(actual)) {
                    return readBounded(input, actual.toString());
                }
            }
            var matches = archiveEntries.get(relative);
            if (matches == null) {
                matches = new ArrayList<>();
                for (var entry : archiveEntries.entrySet()) {
                    if (entry.getKey().endsWith("/" + relative)) {
                        matches.addAll(entry.getValue());
                    }
                }
            }
            if (matches.size() != 1) {
                return null;
            }
            var entry = matches.get(0);
            if (entry.getSize() > MAX_SOURCE_BYTES) {
                throw new ExtractionException("Documentation source exceeds " + MAX_SOURCE_BYTES + " bytes: " + entry.getName());
            }
            try (var input = archive.getInputStream(entry)) {
                return readBounded(input, entry.getName());
            }
        }

        /** Rejects traversal-looking entries even though archives are never extracted. */
        private static boolean safeEntryName(String name) {
            if (name.startsWith("/") || name.contains("\\") || name.contains(":")) {
                return false;
            }
            for (var component : name.split("/", -1)) {
                if (component.equals("..") || component.equals(".") || component.isEmpty()) {
                    return false;
                }
            }
            return true;
        }

        /** Enforces a decompressed-byte bound for both directories and archives. */
        private static String readBounded(InputStream input, String name) throws IOException, ExtractionException {
            var bytes = input.readNBytes(MAX_SOURCE_BYTES + 1);
            if (bytes.length > MAX_SOURCE_BYTES) {
                throw new ExtractionException("Documentation source exceeds " + MAX_SOURCE_BYTES + " bytes: " + name);
            }
            return new String(bytes, StandardCharsets.UTF_8);
        }

        /** Uses parse(), never analyze(), for optional SDK source files. */
        private ParsedDocumentation parse(String relative, String source) throws IOException, ExtractionException {
            var diagnostics = new CompilerDiagnostics();
            try (var files = compiler.getStandardFileManager(diagnostics.collector, Locale.ROOT, StandardCharsets.UTF_8)) {
                files.setLocationFromPaths(StandardLocation.CLASS_PATH, List.of());
                files.setLocationFromPaths(StandardLocation.SOURCE_PATH, List.of());
                var simpleName = relative.substring(relative.lastIndexOf('/') + 1, relative.length() - ".java".length());
                var task = (JavacTask) compiler.getTask(diagnostics.output, files, diagnostics.collector,
                        List.of("-proc:none", "-encoding", "UTF-8", "-Xlint:none"), null, List.of(new MemorySource(simpleName, source)));
                var units = task.parse().iterator();
                diagnostics.check("Documentation syntax could not be parsed: " + relative);
                if (!units.hasNext()) {
                    return ParsedDocumentation.EMPTY;
                }
                var unit = units.next();
                var result = new ParsedDocumentation(unit, DocTrees.instance(task));
                var packageName = unit.getPackageName() == null ? "" : unit.getPackageName().toString();
                for (var declaration : unit.getTypeDecls()) {
                    if (declaration instanceof ClassTree type) {
                        result.addType(type, new TreePath(new TreePath(unit), type), null, packageName);
                    }
                }
                return result;
            }
        }

        @Override
        public void close() throws IOException {
            if (archive != null) {
                archive.close();
            }
        }
    }

    /** Parsed declaration trees; method bodies are never traversed or analyzed. */
    private static final class ParsedDocumentation {
        private static final ParsedDocumentation EMPTY = new ParsedDocumentation(null, null);
        private final CompilationUnitTree unit;
        private final DocTrees trees;
        private final Map<String, List<ParsedType>> typeDeclarations = new HashMap<>();

        private ParsedDocumentation(CompilationUnitTree unit, DocTrees trees) {
            this.unit = unit;
            this.trees = trees;
        }

        /** Records named top-level/member types but deliberately not local/anonymous classes. */
        private void addType(ClassTree tree, TreePath path, ParsedType outer, String packageName) {
            var name = outer == null ? (packageName.isEmpty() ? "" : packageName + ".") + tree.getSimpleName()
                    : outer.binaryName + "$" + tree.getSimpleName();
            var comment = trees.getDocCommentTree(path);
            var docs = comment == null ? Documentation.EMPTY : Documentation.read(trees, comment);
            var canonicalName = outer == null ? name : outer.canonicalName + "." + tree.getSimpleName();
            var declaration = new ParsedType(name, canonicalName, tree, path, outer, docs);
            typeDeclarations.computeIfAbsent(name, ignored -> new ArrayList<>()).add(declaration);
            for (var member : tree.getMembers()) {
                if (member instanceof ClassTree nested) {
                    addType(nested, new TreePath(path, member), declaration, packageName);
                }
            }
        }
    }

    /** A lexical source declaration and its enclosing type context. */
    private record ParsedType(String binaryName, String canonicalName, ClassTree tree, TreePath path, ParsedType outer, Documentation documentation) {
    }


    /** Conservative, compiler-symbol-backed type matching for parse-only documentation. */
    private static final class SourceContext {
        private final String packageName;
        private final ParsedType owner;
        private final Elements elements;
        private final Types types;
        private final Map<String, List<String>> explicitImports = new HashMap<>();
        private final Set<String> wildcardImports = new LinkedHashSet<>();
        private final Map<String, Tree> classBounds = new LinkedHashMap<>();

        private SourceContext(CompilationUnitTree unit, ParsedType owner, Elements elements, Types types) {
            packageName = unit.getPackageName() == null ? "" : unit.getPackageName().toString();
            this.owner = owner;
            this.elements = elements;
            this.types = types;
            for (var current = owner; current != null; current = current.outer) {
                for (var bound : bounds(current.tree.getTypeParameters()).entrySet()) {
                    if (!classBounds.containsKey(bound.getKey())) {
                        classBounds.put(bound.getKey(), bound.getValue());
                    }
                }
            }
            for (var imported : unit.getImports()) {
                var name = imported.getQualifiedIdentifier().toString();
                if (name.endsWith(".*")) {
                    wildcardImports.add(name.substring(0, name.length() - 2));
                } else {
                    explicitImports.computeIfAbsent(name.substring(name.lastIndexOf('.') + 1), ignored -> new ArrayList<>()).add(name);
                }
            }
        }

        /** Maps type variables to their first bound; null means java.lang.Object. */
        private static Map<String, Tree> bounds(List<? extends TypeParameterTree> parameters) {
            var result = new LinkedHashMap<String, Tree>();
            for (var parameter : parameters) {
                result.put(parameter.getName().toString(), parameter.getBounds().isEmpty() ? null : parameter.getBounds().get(0));
            }
            return result;
        }

        /** Computes erased signatures from syntax, never compiling documentation bodies. */
        private String descriptor(Tree tree, Map<String, Tree> methodBounds) {
            return descriptor(tree, methodBounds, new HashSet<>());
        }

        private String descriptor(Tree tree, Map<String, Tree> methodBounds, Set<String> resolving) {
            if (tree instanceof PrimitiveTypeTree primitive) {
                return switch (primitive.getPrimitiveTypeKind()) {
                    case BOOLEAN -> "Z";
                    case BYTE -> "B";
                    case CHAR -> "C";
                    case SHORT -> "S";
                    case INT -> "I";
                    case LONG -> "J";
                    case FLOAT -> "F";
                    case DOUBLE -> "D";
                    case VOID -> "V";
                    default -> null;
                };
            }
            if (tree instanceof ArrayTypeTree array) {
                var component = descriptor(array.getType(), methodBounds, resolving);
                return component == null ? null : "[" + component;
            }
            if (tree instanceof AnnotatedTypeTree annotated) {
                return descriptor(annotated.getUnderlyingType(), methodBounds, resolving);
            }
            if (tree instanceof ParameterizedTypeTree generic) {
                return descriptor(generic.getType(), methodBounds, resolving);
            }
            var name = qualifiedName(tree);
            return name == null ? null : namedDescriptor(name, methodBounds, resolving);
        }

        /** Removes type arguments from qualified nested type syntax. */
        private static String qualifiedName(Tree tree) {
            if (tree instanceof IdentifierTree identifier) {
                return identifier.getName().toString();
            }
            if (tree instanceof MemberSelectTree member) {
                var qualifier = qualifiedName(member.getExpression());
                return qualifier == null ? null : qualifier + "." + member.getIdentifier();
            }
            if (tree instanceof ParameterizedTypeTree generic) {
                return qualifiedName(generic.getType());
            }
            if (tree instanceof AnnotatedTypeTree annotated) {
                return qualifiedName(annotated.getUnderlyingType());
            }
            return null;
        }

        /** Resolves lexical type variables, enclosing types, imports and packages conservatively. */
        private String namedDescriptor(String name, Map<String, Tree> methodBounds, Set<String> resolving) {
            if (!SourceVersion.isName(name)) {
                return null;
            }
            if (methodBounds.containsKey(name) || classBounds.containsKey(name)) {
                if (!resolving.add(name)) {
                    return null;
                }
                var bound = methodBounds.containsKey(name) ? methodBounds.get(name) : classBounds.get(name);
                var result = bound == null ? "Ljava/lang/Object;" : descriptor(bound, methodBounds, resolving);
                resolving.remove(name);
                return result;
            }
            for (var current = owner; current != null; current = current.outer) {
                var enclosing = resolveCanonical(current.canonicalName);
                if (enclosing != null) {
                    var nested = resolveCanonical(enclosing.getQualifiedName() + "." + name);
                    if (nested != null) {
                        return typeDescriptor(nested);
                    }
                    if (name.equals(enclosing.getSimpleName().toString())) {
                        return typeDescriptor(enclosing);
                    }
                }
            }
            var firstSeparator = name.indexOf('.');
            var firstName = firstSeparator < 0 ? name : name.substring(0, firstSeparator);
            var imports = explicitImports.get(firstName);
            if (imports != null) {
                var matching = new TreeMap<String, TypeElement>();
                for (var imported : imports) {
                    var candidate = resolveCanonical(imported + (firstSeparator < 0 ? "" : name.substring(firstSeparator)));
                    if (candidate != null) {
                        matching.put(elements.getBinaryName(candidate).toString(), candidate);
                    }
                }
                return matching.size() == 1 ? typeDescriptor(matching.firstEntry().getValue()) : null;
            }
            var samePackage = resolveCanonical((packageName.isEmpty() ? "" : packageName + ".") + name);
            if (samePackage != null) {
                return typeDescriptor(samePackage);
            }
            if (name.contains(".")) {
                var fullyQualified = resolveCanonical(name);
                if (fullyQualified != null) {
                    return typeDescriptor(fullyQualified);
                }
            }
            var matching = new TreeMap<String, TypeElement>();
            var packages = new LinkedHashSet<>(wildcardImports);
            packages.add("java.lang");
            for (var imported : packages) {
                var candidate = resolveCanonical(imported + "." + name);
                if (candidate != null) {
                    matching.put(elements.getBinaryName(candidate).toString(), candidate);
                }
            }
            return matching.size() == 1 ? typeDescriptor(matching.firstEntry().getValue()) : null;
        }

        /** Resolves only real compiler symbols; unresolved doc-only names never get guessed. */
        private TypeElement resolveCanonical(String name) {
            var candidate = elements.getTypeElement(name);
            return candidate != null && candidate.asType().getKind() != TypeKind.ERROR ? candidate : null;
        }

        /** Uses actual symbol binary names and erasure for documentation type matching. */
        private String typeDescriptor(TypeElement type) {
            var erased = types.erasure(type.asType());
            if (!(erased instanceof DeclaredType declared)) {
                return null;
            }
            return "L" + elements.getBinaryName((TypeElement) declared.asElement()).toString().replace('.', '/') + ";";
        }
    }


    /** Extracted comment text; empty comments are not invented or inherited automatically. */
    private static final class Documentation {
        private static final Documentation EMPTY = new Documentation("", Map.of(), "", List.of(), List.of());
        private final String summary;
        private final Map<String, String> parameters;
        private final String returns;
        private final List<ExceptionDocumentation> exceptions;
        private final List<String> parameterNames;

        private Documentation(String summary, Map<String, String> parameters, String returns,
                List<ExceptionDocumentation> exceptions, List<String> parameterNames) {
            this.summary = summary;
            this.parameters = parameters;
            this.returns = returns;
            this.exceptions = exceptions;
            this.parameterNames = parameterNames;
        }

        /** Extracts body, value-parameter tags, return text and exception tags through DocTrees. */
        private static Documentation read(DocTrees trees, DocCommentTree comment) {
            var parameters = new LinkedHashMap<String, String>();
            var exceptions = new ArrayList<ExceptionDocumentation>();
            var returns = "";
            for (var tag : comment.getBlockTags()) {
                if (tag instanceof ParamTree parameter && !parameter.isTypeParameter()) {
                    parameters.put(parameter.getName().toString(), PlainText.read(trees, parameter.getDescription()));
                } else if (tag instanceof ReturnTree returned) {
                    returns = PlainText.read(trees, returned.getDescription());
                } else if (tag instanceof ThrowsTree thrown) {
                    exceptions.add(new ExceptionDocumentation(thrown.getExceptionName().getSignature(), null,
                            PlainText.read(trees, thrown.getDescription())));
                }
            }
            return new Documentation(PlainText.read(trees, comment.getFullBody()), parameters, returns, exceptions, List.of());
        }

        /** Replaces unresolved exception references after an exact declaration match. */
        private Documentation withExceptions(List<ExceptionDocumentation> resolved) {
            return new Documentation(summary, parameters, returns, resolved, parameterNames);
        }
    }

    /** An exception tag, with an optional genuinely resolved binary type name. */
    private record ExceptionDocumentation(String reference, String binaryName, String summary) {
    }

    /** Converts structured Javadoc to readable text without exposing HTML or inline tag syntax. */
    private static final class PlainText {
        private final DocTrees trees;
        private final StringBuilder text = new StringBuilder();
        private int suppressedHtml;

        private PlainText(DocTrees trees) {
            this.trees = trees;
        }

        /** Returns normalized plain text while retaining literal/code contents and link labels. */
        private static String read(DocTrees trees, List<? extends DocTree> content) {
            var converter = new PlainText(trees);
            converter.append(content);
            return normalizeWhitespace(converter.text.toString());
        }

        private void append(List<? extends DocTree> content) {
            for (var item : content) {
                if (item instanceof StartElementTree start) {
                    var name = start.getName().toString().toLowerCase(Locale.ROOT);
                    if (name.equals("script") || name.equals("style")) {
                        suppressedHtml++;
                    }
                    if (blockElement(name)) {
                        text.append(' ');
                    }
                } else if (item instanceof EndElementTree end) {
                    var name = end.getName().toString().toLowerCase(Locale.ROOT);
                    if ((name.equals("script") || name.equals("style")) && suppressedHtml > 0) {
                        suppressedHtml--;
                    }
                    if (blockElement(name)) {
                        text.append(' ');
                    }
                } else if (suppressedHtml == 0) {
                    if (item instanceof TextTree value) {
                        text.append(value.getBody());
                    } else if (item instanceof LiteralTree literal) {
                        text.append(literal.getBody().getBody());
                    } else if (item instanceof LinkTree link) {
                        if (link.getLabel().isEmpty()) {
                            text.append(link.getReference().getSignature());
                        } else {
                            append(link.getLabel());
                        }
                    } else if (item instanceof EntityTree entity) {
                        var characters = trees.getCharacters(entity);
                        text.append(characters == null ? "&" + entity.getName() + ";" : characters);
                    } else if (item instanceof SummaryTree summary) {
                        append(summary.getSummary());
                    } else if (item instanceof ReturnTree returned) {
                        append(returned.getDescription());
                    } else if (item instanceof UnknownInlineTagTree unknown) {
                        append(unknown.getContent());
                    } else if (item instanceof ValueTree value && value.getReference() != null) {
                        text.append(value.getReference().getSignature());
                    } else if (item instanceof SystemPropertyTree property) {
                        text.append(property.getPropertyName());
                    } else if (item instanceof IndexTree index) {
                        append(List.of(index.getSearchTerm()));
                        if (!index.getDescription().isEmpty()) {
                            text.append(' ');
                            append(index.getDescription());
                        }
                    } else if (item instanceof ErroneousTree error) {
                        text.append(error.getBody());
                    }
                    // Comments, inheritDoc, docRoot and unsupported structural tags
                    // contribute no invented text or inherited documentation.
                }
            }
        }

        /** Inserts word boundaries for common block HTML without breaking inline words. */
        private static boolean blockElement(String name) {
            return Set.of("p", "br", "div", "li", "pre", "table", "tr", "td", "th", "dt", "dd", "ul", "ol", "hr",
                    "h1", "h2", "h3", "h4", "h5", "h6", "blockquote").contains(name);
        }

        /** Collapses whitespace and sanitizes XML-invalid documentation, not constants. */
        private static String normalizeWhitespace(String value) {
            var result = new StringBuilder();
            var pendingSpace = false;
            for (var offset = 0; offset < value.length();) {
                var character = value.codePointAt(offset);
                offset += Character.charCount(character);
                if (Character.isWhitespace(character) || Character.isSpaceChar(character)) {
                    pendingSpace = result.length() != 0;
                } else {
                    if (pendingSpace) {
                        result.append(' ');
                        pendingSpace = false;
                    }
                    result.appendCodePoint(isXmlCharacter(character) ? character : 0xfffd);
                }
            }
            return result.toString();
        }
    }

    /** Returns a simple binary name for unambiguous exception-tag matching. */
    private static String simpleBinaryName(String name) {
        return name.substring(Math.max(name.lastIndexOf('.'), name.lastIndexOf('$')) + 1);
    }

    /** Tests XML 1.0 character legality, including unpaired UTF-16 surrogates. */
    private static boolean isXmlCharacter(int character) {
        return character == 0x9 || character == 0xa || character == 0xd
                || character >= 0x20 && character <= 0xd7ff
                || character >= 0xe000 && character <= 0xfffd
                || character >= 0x10000 && character <= 0x10ffff;
    }

    /** Determines whether an exact string can be carried directly in an XML attribute. */
    private static boolean isXmlText(String text) {
        for (var offset = 0; offset < text.length();) {
            var character = text.codePointAt(offset);
            offset += Character.charCount(character);
            if (!isXmlCharacter(character)) {
                return false;
            }
        }
        return true;
    }

    /** Encodes raw code units, not CharsetEncoder replacement of malformed surrogates. */
    private static String base64Utf16Be(String text) {
        var bytes = new byte[text.length() * 2];
        for (var index = 0; index < text.length(); index++) {
            var character = text.charAt(index);
            bytes[index * 2] = (byte) (character >>> 8);
            bytes[index * 2 + 1] = (byte) character;
        }
        return java.util.Base64.getEncoder().encodeToString(bytes);
    }

    /** Escapes XML 1.0 attributes and preserves attribute whitespace exactly. */
    private static void attribute(StringBuilder xml, String name, Object value) throws ExtractionException {
        xml.append(' ').append(name).append("=\"");
        var text = value.toString();
        for (var offset = 0; offset < text.length();) {
            var character = text.codePointAt(offset);
            offset += Character.charCount(character);
            switch (character) {
                case '&' -> xml.append("&amp;");
                case '<' -> xml.append("&lt;");
                case '>' -> xml.append("&gt;");
                case '"' -> xml.append("&quot;");
                case '\'' -> xml.append("&apos;");
                case '\n' -> xml.append("&#10;");
                case '\r' -> xml.append("&#13;");
                case '\t' -> xml.append("&#9;");
                default -> {
                    if (!isXmlCharacter(character)) {
                        throw new ExtractionException("Attribute '" + name + "' contains XML-invalid U+"
                                + Integer.toHexString(character).toUpperCase(Locale.ROOT) + ".");
                    }
                    xml.appendCodePoint(character);
                }
            }
        }
        xml.append('"');
    }
}
