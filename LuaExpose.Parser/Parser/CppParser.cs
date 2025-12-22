// LuaExpose.Parser - Custom C++ Recursive Descent Parser

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CppAst;
using LuaExpose.Parser.Lexer;
using LuaExpose.Parser.Preprocessor;

namespace LuaExpose.Parser
{
    /// <summary>
    /// Parser options (compatible with CppAst.CppParserOptions interface where needed).
    /// </summary>
    public class CppParserOptions
    {
        public bool ParseComments { get; set; } = false;
        public bool ParseSystemIncludes { get; set; } = false;
        public bool ParseAttributes { get; set; } = true;
        public bool ParseAsCpp { get; set; } = true;
        public bool ParseMacros { get; set; } = false;
        public List<string> Defines { get; } = new List<string>();
        public List<string> IncludeFolders { get; } = new List<string>();
        public List<string> SystemIncludeFolders { get; } = new List<string>();
        public List<string> AdditionalArguments { get; } = new List<string>();
        public string TargetSystem { get; set; }
        public string TargetVendor { get; set; }
        public string TargetAbi { get; set; }

        // Preprocessing options
        public bool EnablePreprocessing { get; set; } = false;
        public string PreferredPreprocessor { get; set; } // "clang", "gcc", "msvc", or null for auto
        public string CppStandard { get; set; } = "c++20";
        public int PreprocessorTimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// C++ Recursive Descent Parser - drop-in replacement for CppAst.CppParser.
    /// </summary>
    public static class CppParser
    {
        /// <summary>
        /// Parse multiple files.
        /// </summary>
        public static CppCompilation ParseFiles(List<string> files, CppParserOptions options = null)
        {
            var compilation = new CppCompilation();
            options ??= new CppParserOptions();

            foreach (var file in files)
            {
                try
                {
                    if (!File.Exists(file))
                    {
                        compilation.Diagnostics.Error($"File not found: {file}");
                        continue;
                    }

                    string source;

                    if (options.EnablePreprocessing)
                    {
                        var ppOptions = new PreprocessorOptions
                        {
                            CppStandard = options.CppStandard,
                            PreferredPreprocessor = options.PreferredPreprocessor,
                            TimeoutMs = options.PreprocessorTimeoutMs
                        };

                        foreach (var define in options.Defines)
                            ppOptions.Defines.Add(define);
                        foreach (var include in options.IncludeFolders)
                            ppOptions.IncludeFolders.Add(include);
                        foreach (var sysInclude in options.SystemIncludeFolders)
                            ppOptions.SystemIncludeFolders.Add(sysInclude);

                        var ppResult = CppPreprocessor.PreprocessFile(file, ppOptions);
                        if (!ppResult.Success)
                        {
                            compilation.Diagnostics.Warning($"Preprocessing failed for {file}, parsing raw source. Error: {ppResult.ErrorOutput}");
                            source = ExpandSimpleMacros(File.ReadAllText(file));
                        }
                        else
                        {
                            source = ppResult.Output;
                        }
                    }
                    else
                    {
                        source = ExpandSimpleMacros(File.ReadAllText(file));
                    }

                    ParseSource(source, file, compilation, options);
                }
                catch (Exception ex)
                {
                    compilation.Diagnostics.Error($"Error parsing {file}: {ex.Message}");
                }
            }

            ResolveTypes(compilation);
            return compilation;
        }

        /// <summary>
        /// Parse a single source string.
        /// </summary>
        public static CppCompilation Parse(string source, CppParserOptions options = null, string filename = "input.h")
        {
            var compilation = new CppCompilation();
            options ??= new CppParserOptions();

            string effectiveSource = source;

            if (options.EnablePreprocessing)
            {
                var ppOptions = new PreprocessorOptions
                {
                    CppStandard = options.CppStandard,
                    PreferredPreprocessor = options.PreferredPreprocessor,
                    TimeoutMs = options.PreprocessorTimeoutMs
                };

                foreach (var define in options.Defines)
                    ppOptions.Defines.Add(define);
                foreach (var include in options.IncludeFolders)
                    ppOptions.IncludeFolders.Add(include);
                foreach (var sysInclude in options.SystemIncludeFolders)
                    ppOptions.SystemIncludeFolders.Add(sysInclude);

                var ppResult = CppPreprocessor.PreprocessString(source, ppOptions, filename);
                if (ppResult.Success)
                {
                    effectiveSource = ppResult.Output;
                }
                else
                {
                    compilation.Diagnostics.Warning($"Preprocessing failed, parsing raw source. Error: {ppResult.ErrorOutput}");
                    effectiveSource = ExpandSimpleMacros(source);
                }
            }
            else
            {
                effectiveSource = ExpandSimpleMacros(source);
            }

            ParseSource(effectiveSource, filename, compilation, options);
            ResolveTypes(compilation);
            return compilation;
        }

        private static void ParseSource(string source, string filename, CppCompilation compilation, CppParserOptions options)
        {
            var lexer = new CppLexer(source, filename);
            var tokens = lexer.Tokenize();
            var parser = new CppParserImpl(tokens, compilation, options, filename);
            parser.Parse();
        }

        private static string ExpandSimpleMacros(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            var macros = CollectSimpleMacros(source);
            if (macros.Count == 0)
            {
                return source;
            }

            var pattern = @"\b(?:" + string.Join("|", macros.Keys.Select(Regex.Escape)) + @")\b";
            var sb = new System.Text.StringBuilder();
            using var reader = new StringReader(source);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("#"))
                {
                    sb.AppendLine(line);
                    continue;
                }

                var replaced = Regex.Replace(line, pattern, match => macros.TryGetValue(match.Value, out var body) ? body : match.Value);
                sb.AppendLine(replaced);
            }

            return sb.ToString();
        }

        private static Dictionary<string, string> CollectSimpleMacros(string source)
        {
            var macros = new Dictionary<string, string>();
            using var reader = new StringReader(source);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("#define"))
                {
                    continue;
                }

                var match = Regex.Match(trimmed, @"^#\s*define\s+([A-Za-z_][A-Za-z0-9_]*)(.*)$");
                if (!match.Success)
                {
                    continue;
                }

                var name = match.Groups[1].Value;
                var rest = match.Groups[2].Value.TrimStart();
                if (rest.StartsWith("("))
                {
                    // Function-like macros are not handled here.
                    continue;
                }

                var bodyBuilder = new System.Text.StringBuilder();
                var current = rest;
                while (true)
                {
                    bool hasContinuation = current.EndsWith("\\", StringComparison.Ordinal);
                    if (hasContinuation)
                    {
                        current = current.Substring(0, current.Length - 1);
                    }
                    bodyBuilder.AppendLine(current);

                    if (!hasContinuation)
                    {
                        break;
                    }

                    var nextLine = reader.ReadLine();
                    if (nextLine == null)
                    {
                        break;
                    }
                    current = nextLine;
                }

                var body = bodyBuilder.ToString();
                if (body.Contains("[[LUA_", StringComparison.Ordinal))
                {
                    macros[name] = body;
                }
            }

            return macros;
        }

        private static void ResolveTypes(CppCompilation compilation)
        {
            if (compilation == null) return;

            var classIndex = BuildClassIndex(compilation);
            var shortIndex = BuildShortNameIndex(classIndex);

            void ResolveClassTree(CppClass cppClass)
            {
                if (cppClass == null) return;

                for (int i = 0; i < cppClass.BaseTypes.Count; i++)
                {
                    var baseType = cppClass.BaseTypes[i];
                    var resolvedType = ResolveBaseType(baseType.Type, cppClass.Parent as ICppContainer, classIndex, shortIndex);
                    if (!ReferenceEquals(resolvedType, baseType.Type))
                    {
                        var updated = new CppBaseType(resolvedType)
                        {
                            Visibility = baseType.Visibility,
                            IsVirtual = baseType.IsVirtual
                        };
                        cppClass.BaseTypes[i] = updated;
                    }
                }

                foreach (var nested in cppClass.Classes)
                {
                    ResolveClassTree(nested);
                }
            }

            foreach (var cppClass in compilation.Classes)
            {
                ResolveClassTree(cppClass);
            }

            foreach (var ns in compilation.Namespaces)
            {
                ResolveNamespace(ns, ResolveClassTree);
            }
        }

        private static void ResolveNamespace(CppNamespace ns, Action<CppClass> resolveClass)
        {
            foreach (var cppClass in ns.Classes)
            {
                resolveClass(cppClass);
            }

            foreach (var nested in ns.Namespaces)
            {
                ResolveNamespace(nested, resolveClass);
            }
        }

        private static Dictionary<string, CppClass> BuildClassIndex(ICppGlobalDeclarationContainer container)
        {
            var index = new Dictionary<string, CppClass>();

            void AddClass(CppClass cppClass)
            {
                if (cppClass == null || string.IsNullOrWhiteSpace(cppClass.Name)) return;
                var qualifiedName = GetQualifiedName(cppClass);
                if (!string.IsNullOrEmpty(qualifiedName))
                {
                    if (!index.TryGetValue(qualifiedName, out var existing))
                    {
                        index.Add(qualifiedName, cppClass);
                    }
                    else if (IsBetterClassCandidate(cppClass, existing))
                    {
                        index[qualifiedName] = cppClass;
                    }
                }

                foreach (var nested in cppClass.Classes)
                {
                    AddClass(nested);
                }
            }

            void WalkNamespace(CppNamespace ns)
            {
                foreach (var cppClass in ns.Classes)
                {
                    AddClass(cppClass);
                }

                foreach (var nested in ns.Namespaces)
                {
                    WalkNamespace(nested);
                }
            }

            foreach (var cppClass in container.Classes)
            {
                AddClass(cppClass);
            }

            foreach (var ns in container.Namespaces)
            {
                WalkNamespace(ns);
            }

            return index;
        }

        private static Dictionary<string, List<CppClass>> BuildShortNameIndex(Dictionary<string, CppClass> classIndex)
        {
            var index = new Dictionary<string, List<CppClass>>();
            foreach (var item in classIndex.Values)
            {
                if (string.IsNullOrWhiteSpace(item.Name)) continue;
                if (!index.TryGetValue(item.Name, out var list))
                {
                    list = new List<CppClass>();
                    index[item.Name] = list;
                }
                list.Add(item);
            }
            return index;
        }

        private static bool IsBetterClassCandidate(CppClass candidate, CppClass existing)
        {
            if (existing == null) return true;
            if (candidate == null) return false;

            if (!existing.IsDefinition && candidate.IsDefinition) return true;

            var existingAttrs = existing.Attributes?.Count ?? 0;
            var candidateAttrs = candidate.Attributes?.Count ?? 0;
            if (existingAttrs == 0 && candidateAttrs > 0) return true;

            var existingMembers = (existing.Fields?.Count ?? 0) + (existing.Functions?.Count ?? 0) + (existing.Constructors?.Count ?? 0);
            var candidateMembers = (candidate.Fields?.Count ?? 0) + (candidate.Functions?.Count ?? 0) + (candidate.Constructors?.Count ?? 0);
            if (candidateMembers > existingMembers) return true;

            return false;
        }

        private static string GetQualifiedName(ICppMember member)
        {
            if (member == null) return string.Empty;

            var names = new List<string>();
            var current = member as CppElement;
            while (current != null)
            {
                if (current is ICppMember currentMember && !string.IsNullOrWhiteSpace(currentMember.Name))
                {
                    names.Add(currentMember.Name);
                }
                current = current.Parent as CppElement;
            }

            names.Reverse();
            return string.Join("::", names);
        }

        private static CppType ResolveBaseType(
            CppType type,
            ICppContainer context,
            Dictionary<string, CppClass> classIndex,
            Dictionary<string, List<CppClass>> shortIndex)
        {
            if (type is CppUnexposedType unexposed)
            {
                var resolved = ResolveClass(unexposed.Name, context, classIndex, shortIndex);
                return resolved ?? type;
            }

            if (type is CppQualifiedType qualified)
            {
                var resolvedElement = ResolveBaseType(qualified.ElementType, context, classIndex, shortIndex);
                if (!ReferenceEquals(resolvedElement, qualified.ElementType))
                {
                    return new CppQualifiedType(qualified.Qualifier, resolvedElement);
                }
            }

            return type;
        }

        private static CppClass ResolveClass(
            string name,
            ICppContainer context,
            Dictionary<string, CppClass> classIndex,
            Dictionary<string, List<CppClass>> shortIndex)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var baseName = StripTemplateArguments(name).Trim();
            if (string.IsNullOrWhiteSpace(baseName)) return null;

            var normalized = NormalizeQualifiedName(baseName);
            var isAbsolute = baseName.StartsWith("::");
            if (normalized.Contains("::") && classIndex.TryGetValue(normalized, out var qualifiedHit))
            {
                return qualifiedHit;
            }
            if (isAbsolute)
            {
                return null;
            }

            var scopes = GetScopeNames(context);
            for (int i = scopes.Count; i >= 0; i--)
            {
                var prefix = i > 0 ? string.Join("::", scopes.GetRange(0, i)) : string.Empty;
                var candidate = string.IsNullOrEmpty(prefix) ? normalized : $"{prefix}::{normalized}";
                if (classIndex.TryGetValue(candidate, out var scopedHit))
                {
                    return scopedHit;
                }
            }

            if (shortIndex.TryGetValue(normalized, out var list) && list.Count == 1)
            {
                return list[0];
            }

            return null;
        }

        private static string StripTemplateArguments(string name)
        {
            var index = name.IndexOf('<');
            return index >= 0 ? name.Substring(0, index) : name;
        }

        private static string NormalizeQualifiedName(string name)
        {
            return name.StartsWith("::") ? name.Substring(2) : name;
        }

        private static List<string> GetScopeNames(ICppContainer context)
        {
            var names = new List<string>();
            var current = context as CppElement;
            while (current != null)
            {
                if (current is ICppMember member && !string.IsNullOrWhiteSpace(member.Name))
                {
                    names.Add(member.Name);
                }
                current = current.Parent as CppElement;
            }
            names.Reverse();
            return names;
        }
    }

    /// <summary>
    /// Internal parser implementation.
    /// </summary>
    internal class CppParserImpl
    {
        private readonly List<Token> _tokens;
        private readonly CppCompilation _compilation;
        private readonly CppParserOptions _options;
        private readonly string _filename;
        private int _position;
        private CppVisibility _currentVisibility = CppVisibility.Default;

        public CppParserImpl(List<Token> tokens, CppCompilation compilation, CppParserOptions options, string filename)
        {
            _tokens = tokens;
            _compilation = compilation;
            _options = options;
            _filename = filename;
            _position = 0;
        }

        public void Parse()
        {
            while (!IsAtEnd())
            {
                try
                {
                    ParseTopLevel(_compilation);
                }
                catch (Exception ex)
                {
                    _compilation.Diagnostics.Error($"Parse error: {ex.Message}", Current().Location);
                    // Skip to next semicolon or brace to recover
                    SkipToRecoveryPoint();
                }
            }
        }

        private void ParseTopLevel(ICppGlobalDeclarationContainer container)
        {
            var attributes = ParseAttributes();

            if (Check(TokenType.Namespace))
            {
                ParseNamespace(container, attributes);
            }
            else if (Check(TokenType.Class) || Check(TokenType.Struct) || Check(TokenType.Union))
            {
                ParseClassOrStruct(container, attributes);
            }
            else if (Check(TokenType.Enum))
            {
                ParseEnum(container, attributes);
            }
            else if (Check(TokenType.Template))
            {
                ParseTemplate(container, attributes);
            }
            else if (Check(TokenType.Typedef))
            {
                ParseTypedef(container, attributes);
            }
            else if (Check(TokenType.Using))
            {
                ParseUsing(container, attributes);
            }
            else if (Check(TokenType.Extern))
            {
                // Skip extern declarations
                Advance();
                if (Check(TokenType.StringLiteral)) Advance(); // extern "C"
                if (Check(TokenType.LeftBrace))
                {
                    SkipBlock();
                }
                else
                {
                    SkipToSemicolon();
                }
            }
            else if (IsAtEnd())
            {
                return;
            }
            else
            {
                // Try to parse as function or variable declaration
                ParseFunctionOrVariable(container, attributes);
            }
        }

        private List<CppAttribute> ParseAttributes()
        {
            var attributes = new List<CppAttribute>();

            while (Check(TokenType.DoubleLeftBracket))
            {
                Advance(); // [[

                while (!Check(TokenType.DoubleRightBracket) && !IsAtEnd())
                {
                    var attr = ParseSingleAttribute();
                    if (attr != null) attributes.Add(attr);

                    if (Check(TokenType.Comma))
                    {
                        Advance();
                    }
                }

                Expect(TokenType.DoubleRightBracket); // ]]
            }

            return attributes;
        }

        private CppAttribute ParseSingleAttribute()
        {
            if (!Check(TokenType.Identifier)) return null;

            var name = Advance().Text;
            var attr = new CppAttribute(name);
            attr.Span = new CppSourceSpan(Previous().Location, Previous().Location);

            // Check for scope (e.g., gnu::unused)
            if (Check(TokenType.DoubleColon))
            {
                Advance();
                if (Check(TokenType.Identifier))
                {
                    attr.Scope = name;
                    attr.Name = Advance().Text;
                }
            }

            // Parse arguments
            if (Check(TokenType.LeftParen))
            {
                Advance();
                var args = ParseAttributeArguments();
                attr.Arguments = args;
            }

            return attr;
        }

        private string ParseAttributeArguments()
        {
            var sb = new System.Text.StringBuilder();
            int depth = 1;

            while (!IsAtEnd() && depth > 0)
            {
                if (Check(TokenType.LeftParen))
                {
                    depth++;
                    sb.Append(Advance().Text);
                }
                else if (Check(TokenType.RightParen))
                {
                    depth--;
                    if (depth > 0) sb.Append(Advance().Text);
                    else Advance();
                }
                else
                {
                    sb.Append(Advance().Text);
                }
            }

            return sb.ToString().Trim();
        }

        private void ParseNamespace(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            Expect(TokenType.Namespace);

            // Optional attributes after namespace keyword
            attributes.AddRange(ParseAttributes());

            // Handle nested namespace syntax: namespace A::B::C
            var names = new List<string>();
            if (Check(TokenType.Identifier))
            {
                names.Add(Advance().Text);
                while (Check(TokenType.DoubleColon))
                {
                    Advance();
                    if (Check(TokenType.Identifier))
                    {
                        names.Add(Advance().Text);
                    }
                }
            }

            // Handle namespace alias: namespace X = Y::Z;
            if (Check(TokenType.Equals))
            {
                Advance();
                ParseTypeName();
                if (Check(TokenType.Semicolon)) Advance();
                return;
            }

            Expect(TokenType.LeftBrace);

            // Create nested namespaces
            ICppGlobalDeclarationContainer current = container;
            foreach (var name in names)
            {
                var existingNs = current.Namespaces.FirstOrDefault(n => n.Name == name);
                if (existingNs != null)
                {
                    if (attributes.Count > 0)
                    {
                        foreach (var attr in attributes)
                        {
                            if (!existingNs.Attributes.Any(a => a.Name == attr.Name && a.Arguments == attr.Arguments && a.Scope == attr.Scope))
                            {
                                existingNs.Attributes.Add(attr);
                            }
                        }
                    }
                    current = existingNs;
                }
                else
                {
                    var ns = new CppNamespace(name);
                    ns.Span = new CppSourceSpan(Current().Location, Current().Location);
                    foreach (var attr in attributes)
                    {
                        ns.Attributes.Add(attr);
                    }
                    current.Namespaces.Add(ns);
                    current = ns;
                }
                attributes = new List<CppAttribute>(); // Only apply to outermost
            }

            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                ParseTopLevel(current);
            }

            Expect(TokenType.RightBrace);
        }

        private void ParseClassOrStruct(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            var classKind = Check(TokenType.Class) ? CppClassKind.Class :
                            Check(TokenType.Struct) ? CppClassKind.Struct : CppClassKind.Union;
            Advance();

            // Optional attributes after class keyword
            attributes.AddRange(ParseAttributes());

            string name = "";
            if (Check(TokenType.Identifier))
            {
                name = Advance().Text;
            }

            // Optional class specifiers (e.g., "final")
            while (Check(TokenType.Identifier) && (Current().Text == "final" || Current().Text == "sealed"))
            {
                Advance();
            }

            var cppClass = new CppClass(name);
            cppClass.ClassKind = classKind;
            cppClass.Span = new CppSourceSpan(Previous().Location, Previous().Location);
            cppClass.IsDefinition = false;

            foreach (var attr in attributes)
            {
                cppClass.Attributes.Add(attr);
            }

            // Base types
            if (Check(TokenType.Colon))
            {
                Advance();
                ParseBaseTypes(cppClass);
            }

            // Forward declaration or definition
            if (Check(TokenType.Semicolon))
            {
                Advance();
                container.Classes.Add(cppClass);
                return;
            }

            if (!Check(TokenType.LeftBrace))
            {
                // Could be a variable declaration using the class type
                SkipToSemicolon();
                container.Classes.Add(cppClass);
                return;
            }

            cppClass.IsDefinition = true;
            Expect(TokenType.LeftBrace);

            // Parse class body
            _currentVisibility = classKind == CppClassKind.Class ? CppVisibility.Private : CppVisibility.Public;
            ParseClassBody(cppClass);

            Expect(TokenType.RightBrace);

            // Optional trailing semicolon or variable name
            if (Check(TokenType.Identifier))
            {
                Advance(); // variable name
            }
            if (Check(TokenType.Semicolon))
            {
                Advance();
            }

            container.Classes.Add(cppClass);
        }

        private void ParseBaseTypes(CppClass cppClass)
        {
            do
            {
                var visibility = CppVisibility.Default;
                bool isVirtual = false;

                if (Check(TokenType.Virtual))
                {
                    isVirtual = true;
                    Advance();
                }

                if (Check(TokenType.Public) || Check(TokenType.Private) || Check(TokenType.Protected))
                {
                    visibility = Check(TokenType.Public) ? CppVisibility.Public :
                                Check(TokenType.Protected) ? CppVisibility.Protected : CppVisibility.Private;
                    Advance();
                }

                if (Check(TokenType.Virtual))
                {
                    isVirtual = true;
                    Advance();
                }

                var baseType = ParseType();
                if (baseType != null)
                {
                    var bt = new CppBaseType(baseType);
                    bt.Visibility = visibility;
                    bt.IsVirtual = isVirtual;
                    cppClass.BaseTypes.Add(bt);
                }

            } while (Check(TokenType.Comma) && Advance() != null);
        }

        private void ParseClassBody(CppClass cppClass)
        {
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                // Visibility specifier
                if (Check(TokenType.Public) || Check(TokenType.Private) || Check(TokenType.Protected))
                {
                    _currentVisibility = Check(TokenType.Public) ? CppVisibility.Public :
                                        Check(TokenType.Protected) ? CppVisibility.Protected : CppVisibility.Private;
                    Advance();
                    Expect(TokenType.Colon);
                    continue;
                }

                var attributes = ParseAttributes();

                if (Check(TokenType.Class) || Check(TokenType.Struct) || Check(TokenType.Union))
                {
                    ParseNestedClass(cppClass, attributes);
                }
                else if (Check(TokenType.Enum))
                {
                    ParseNestedEnum(cppClass, attributes);
                }
                else if (Check(TokenType.Typedef))
                {
                    ParseTypedefInClass(cppClass, attributes);
                }
                else if (Check(TokenType.Using))
                {
                    SkipToSemicolon();
                }
                else if (Check(TokenType.Template))
                {
                    ParseTemplateInClass(cppClass, attributes);
                }
                else if (Check(TokenType.Friend))
                {
                    Advance();
                    SkipToSemicolon();
                }
                else
                {
                    ParseMemberDeclaration(cppClass, attributes);
                }
            }
        }

        private void ParseMemberDeclaration(CppClass cppClass, List<CppAttribute> attributes)
        {
            bool isStatic = false;
            bool isVirtual = false;
            bool isExplicit = false;
            bool isInline = false;

            // Parse modifiers
            while (true)
            {
                if (Check(TokenType.Static)) { isStatic = true; Advance(); }
                else if (Check(TokenType.Virtual)) { isVirtual = true; Advance(); }
                else if (Check(TokenType.Explicit)) { isExplicit = true; Advance(); }
                else if (Check(TokenType.Inline)) { isInline = true; Advance(); }
                else if (Check(TokenType.Mutable)) { Advance(); }
                else if (Check(TokenType.Constexpr) || Check(TokenType.Consteval) || Check(TokenType.Constinit)) { Advance(); }
                else break;
            }

            attributes.AddRange(ParseAttributes());

            // Check for destructor
            if (Check(TokenType.Tilde))
            {
                Advance();
                if (Check(TokenType.Identifier))
                {
                    var destructorName = "~" + Advance().Text;
                    var destructor = new CppFunction(destructorName);
                    destructor.Visibility = _currentVisibility;
                    destructor.Flags = CppFunctionFlags.Destructor | CppFunctionFlags.Method;
                    if (isVirtual) destructor.Flags |= CppFunctionFlags.Virtual;
                    destructor.Span = new CppSourceSpan(Previous().Location, Previous().Location);
                    foreach (var attr in attributes) destructor.Attributes.Add(attr);

                    Expect(TokenType.LeftParen);
                    Expect(TokenType.RightParen);

                    ParseFunctionSuffix(destructor);
                    cppClass.Functions.Add(destructor);
                }
                return;
            }

            // Check for constructor
            if (Check(TokenType.Identifier) && Current().Text == cppClass.Name && PeekNext().Type == TokenType.LeftParen)
            {
                var constructor = ParseConstructor(cppClass.Name, attributes);
                if (constructor != null)
                {
                    constructor.Visibility = _currentVisibility;
                    if (isExplicit) constructor.Flags |= CppFunctionFlags.Constructor;
                    cppClass.Constructors.Add(constructor);
                }
                return;
            }

            // Parse type
            var type = ParseType();
            if (type == null)
            {
                SkipToRecoveryPoint();
                return;
            }

            // Check for function or field
            if (!Check(TokenType.Identifier) && !Check(TokenType.Operator))
            {
                SkipToSemicolon();
                return;
            }

            string memberName;
            if (Check(TokenType.Operator))
            {
                memberName = ParseOperatorName();
            }
            else
            {
                memberName = Advance().Text;
            }

            // Function
            if (Check(TokenType.LeftParen))
            {
                var func = ParseFunctionDeclaration(memberName, type, attributes, isStatic, isVirtual, isInline);
                if (func != null)
                {
                    func.Visibility = _currentVisibility;
                    func.Flags |= CppFunctionFlags.Method;
                    cppClass.Functions.Add(func);
                }
            }
            // Field
            else
            {
                var field = new CppField(type, memberName);
                field.Visibility = _currentVisibility;
                field.Span = new CppSourceSpan(Previous().Location, Previous().Location);
                if (isStatic) field.StorageQualifier = CppStorageQualifier.Static;
                field.Attributes = attributes;

                // Handle array suffix
                while (Check(TokenType.LeftBracket))
                {
                    Advance();
                    SkipToClosingBracket(TokenType.LeftBracket, TokenType.RightBracket);
                }

                // Handle initializer
                if (Check(TokenType.Equals))
                {
                    Advance();
                    SkipToSemicolonOrComma();
                }

                cppClass.Fields.Add(field);

                // Handle multiple fields
                while (Check(TokenType.Comma))
                {
                    Advance();
                    // Parse additional declarators
                    if (Check(TokenType.Identifier))
                    {
                        var additionalField = new CppField(type, Advance().Text);
                        additionalField.Visibility = _currentVisibility;
                        cppClass.Fields.Add(additionalField);
                    }

                    if (Check(TokenType.Equals))
                    {
                        Advance();
                        SkipToSemicolonOrComma();
                    }
                }

                if (Check(TokenType.Semicolon)) Advance();
            }
        }

        private CppFunction ParseConstructor(string name, List<CppAttribute> attributes)
        {
            Advance(); // constructor name
            var constructor = new CppFunction(name);
            constructor.IsConstructor = true;
            constructor.Flags = CppFunctionFlags.Constructor | CppFunctionFlags.Method;
            constructor.Span = new CppSourceSpan(Previous().Location, Previous().Location);
            foreach (var attr in attributes) constructor.Attributes.Add(attr);

            Expect(TokenType.LeftParen);
            ParseParameterList(constructor);
            Expect(TokenType.RightParen);

            ParseFunctionSuffix(constructor);
            return constructor;
        }

        private string ParseOperatorName()
        {
            Advance(); // 'operator'
            var sb = new System.Text.StringBuilder("operator");

            // Collect operator tokens
            while (!Check(TokenType.LeftParen) && !IsAtEnd())
            {
                sb.Append(Advance().Text);
            }

            return sb.ToString();
        }

        private CppFunction ParseFunctionDeclaration(string name, CppType returnType, List<CppAttribute> attributes,
            bool isStatic, bool isVirtual, bool isInline)
        {
            var func = new CppFunction(name);
            func.ReturnType = returnType;
            func.Span = new CppSourceSpan(Previous().Location, Previous().Location);
            if (isStatic) func.StorageQualifier = CppStorageQualifier.Static;
            if (isVirtual) func.Flags |= CppFunctionFlags.Virtual;
            if (isInline) func.Flags |= CppFunctionFlags.Inline;
            foreach (var attr in attributes) func.Attributes.Add(attr);

            Expect(TokenType.LeftParen);
            ParseParameterList(func);
            Expect(TokenType.RightParen);

            ParseFunctionSuffix(func);
            return func;
        }

        private void ParseFunctionSuffix(CppFunction func)
        {
            // const, noexcept, override, final, = 0, = default, = delete
            while (true)
            {
                if (Check(TokenType.Const))
                {
                    func.Flags |= CppFunctionFlags.Const;
                    Advance();
                }
                else if (Check(TokenType.Identifier) && (Current().Text == "noexcept" ||
                         Current().Text == "override" || Current().Text == "final"))
                {
                    Advance();
                    if (Check(TokenType.LeftParen))
                    {
                        Advance();
                        SkipToClosingBracket(TokenType.LeftParen, TokenType.RightParen);
                    }
                }
                else if (Check(TokenType.Ampersand) || Check(TokenType.DoubleAmpersand))
                {
                    Advance(); // ref-qualifier
                }
                else if (Check(TokenType.Arrow))
                {
                    Advance();
                    ParseType(); // trailing return type
                }
                else
                {
                    break;
                }
            }

            if (Check(TokenType.Equals))
            {
                Advance();
                if (Check(TokenType.IntegerLiteral) && Current().Text == "0")
                {
                    func.Flags |= CppFunctionFlags.Pure;
                    Advance();
                }
                else if (Check(TokenType.Identifier))
                {
                    var text = Current().Text;
                    if (text == "default") func.Flags |= CppFunctionFlags.Defaulted;
                    Advance();
                }
            }

            // Skip function body or trailing semicolon
            if (Check(TokenType.LeftBrace))
            {
                SkipBlock();
            }
            else if (Check(TokenType.Colon))
            {
                // Initializer list
                Advance();
                while (!Check(TokenType.LeftBrace) && !IsAtEnd())
                {
                    Advance();
                }
                if (Check(TokenType.LeftBrace)) SkipBlock();
            }
            else if (Check(TokenType.Semicolon))
            {
                Advance();
            }
        }

        private void ParseParameterList(CppFunction func)
        {
            if (Check(TokenType.RightParen)) return;
            if (Check(TokenType.Void) && PeekNext().Type == TokenType.RightParen)
            {
                Advance();
                return;
            }

            do
            {
                if (Check(TokenType.Ellipsis))
                {
                    Advance();
                    break;
                }

                var paramAttrs = ParseAttributes();
                var paramType = ParseType();
                if (paramType == null) break;

                string paramName = "";
                if (Check(TokenType.Identifier))
                {
                    paramName = Advance().Text;
                }

                // Handle array suffix on parameter
                while (Check(TokenType.LeftBracket))
                {
                    Advance();
                    SkipToClosingBracket(TokenType.LeftBracket, TokenType.RightBracket);
                }

                var param = new CppParameter(paramType, paramName);
                param.Span = new CppSourceSpan(Current().Location, Current().Location);
                func.Parameters.Add(param);

                // Default value
                if (Check(TokenType.Equals))
                {
                    Advance();
                    SkipToCommaOrCloseParen();
                }

            } while (Check(TokenType.Comma) && Advance() != null);
        }

        private void ParseEnum(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            Expect(TokenType.Enum);

            bool isScoped = false;
            if (Check(TokenType.Class) || Check(TokenType.Struct))
            {
                isScoped = true;
                Advance();
            }

            attributes.AddRange(ParseAttributes());

            string name = "";
            if (Check(TokenType.Identifier))
            {
                name = Advance().Text;
            }

            var cppEnum = new CppEnum(name);
            cppEnum.IsScoped = isScoped;
            cppEnum.Span = new CppSourceSpan(Previous().Location, Previous().Location);
            foreach (var attr in attributes) cppEnum.Attributes.Add(attr);

            // Underlying type
            if (Check(TokenType.Colon))
            {
                Advance();
                cppEnum.IntegerType = ParseType();
            }

            if (!Check(TokenType.LeftBrace))
            {
                if (Check(TokenType.Semicolon)) Advance();
                container.Enums.Add(cppEnum);
                return;
            }

            Expect(TokenType.LeftBrace);

            long nextValue = 0;
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                var itemAttrs = ParseAttributes();

                if (!Check(TokenType.Identifier)) break;

                var itemName = Advance().Text;
                long itemValue = nextValue;

                if (Check(TokenType.Equals))
                {
                    Advance();
                    // Try to parse value
                    if (Check(TokenType.IntegerLiteral))
                    {
                        var valueText = Advance().Text;
                        if (long.TryParse(valueText, out long parsed))
                        {
                            itemValue = parsed;
                        }
                        else if (valueText.StartsWith("0x") || valueText.StartsWith("0X"))
                        {
                            if (long.TryParse(valueText.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out parsed))
                            {
                                itemValue = parsed;
                            }
                        }
                        if (!Check(TokenType.Comma) && !Check(TokenType.RightBrace))
                        {
                            SkipToCommaOrCloseBrace();
                        }
                    }
                    else
                    {
                        SkipToCommaOrCloseBrace();
                    }
                }

                var enumItem = new CppEnumItem(itemName, itemValue);
                enumItem.Span = new CppSourceSpan(Previous().Location, Previous().Location);
                cppEnum.Items.Add(enumItem);

                nextValue = itemValue + 1;

                if (Check(TokenType.Comma))
                {
                    Advance();
                }
            }

            Expect(TokenType.RightBrace);
            if (Check(TokenType.Semicolon)) Advance();

            container.Enums.Add(cppEnum);
        }

        private void ParseNestedClass(CppClass parent, List<CppAttribute> attributes)
        {
            var savedVisibility = _currentVisibility;
            var classKind = Check(TokenType.Class) ? CppClassKind.Class :
                            Check(TokenType.Struct) ? CppClassKind.Struct : CppClassKind.Union;
            Advance();

            string name = "";
            if (Check(TokenType.Identifier))
            {
                name = Advance().Text;
            }

            var nested = new CppClass(name);
            nested.ClassKind = classKind;
            nested.Visibility = _currentVisibility;
            nested.Span = new CppSourceSpan(Previous().Location, Previous().Location);
            foreach (var attr in attributes) nested.Attributes.Add(attr);

            if (Check(TokenType.Colon))
            {
                Advance();
                ParseBaseTypes(nested);
            }

            if (Check(TokenType.LeftBrace))
            {
                nested.IsDefinition = true;
                Advance();
                _currentVisibility = classKind == CppClassKind.Class ? CppVisibility.Private : CppVisibility.Public;
                ParseClassBody(nested);
                Expect(TokenType.RightBrace);
            }

            if (Check(TokenType.Semicolon)) Advance();

            parent.Classes.Add(nested);
            _currentVisibility = savedVisibility;
        }

        private void ParseNestedEnum(CppClass parent, List<CppAttribute> attributes)
        {
            var dummyContainer = new CppGlobalDeclarationContainer();
            ParseEnum(dummyContainer, attributes);
            foreach (var e in dummyContainer.Enums)
            {
                e.Visibility = _currentVisibility;
                parent.Enums.Add(e);
            }
        }

        private void ParseTypedefInClass(CppClass parent, List<CppAttribute> attributes)
        {
            var dummyContainer = new CppGlobalDeclarationContainer();
            ParseTypedef(dummyContainer, attributes);
            foreach (var td in dummyContainer.Typedefs)
            {
                parent.Typedefs.Add(td);
            }
        }

        private void ParseTemplateInClass(CppClass parent, List<CppAttribute> attributes)
        {
            Advance(); // template
            if (Check(TokenType.LessThan))
            {
                Advance();
                SkipToClosingAngleBracket();
            }

            var newAttrs = ParseAttributes();
            attributes.AddRange(newAttrs);

            if (Check(TokenType.Class) || Check(TokenType.Struct))
            {
                ParseNestedClass(parent, attributes);
            }
            else
            {
                // Template function
                ParseMemberDeclaration(parent, attributes);
            }
        }

        private void ParseTemplate(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            Advance(); // template
            if (Check(TokenType.LessThan))
            {
                Advance();
                SkipToClosingAngleBracket();
            }

            var newAttrs = ParseAttributes();
            attributes.AddRange(newAttrs);

            if (Check(TokenType.Class) || Check(TokenType.Struct) || Check(TokenType.Union))
            {
                ParseClassOrStruct(container, attributes);
            }
            else
            {
                // Template function
                ParseFunctionOrVariable(container, attributes);
            }
        }

        private void ParseTypedef(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            Expect(TokenType.Typedef);

            var type = ParseType();
            if (type == null)
            {
                SkipToSemicolon();
                return;
            }

            if (!Check(TokenType.Identifier))
            {
                SkipToSemicolon();
                return;
            }

            var name = Advance().Text;
            var typedef = new CppTypedef(name, type);
            typedef.Span = new CppSourceSpan(Previous().Location, Previous().Location);

            if (Check(TokenType.Semicolon)) Advance();

            container.Typedefs.Add(typedef);
        }

        private void ParseUsing(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            Expect(TokenType.Using);

            if (Check(TokenType.Namespace))
            {
                // using namespace X;
                SkipToSemicolon();
                return;
            }

            if (Check(TokenType.Identifier))
            {
                var name = Advance().Text;
                if (Check(TokenType.Equals))
                {
                    Advance();
                    var type = ParseType();
                    if (type != null)
                    {
                        var typedef = new CppTypedef(name, type);
                        typedef.Span = new CppSourceSpan(Previous().Location, Previous().Location);
                        container.Typedefs.Add(typedef);
                    }
                }
            }

            if (Check(TokenType.Semicolon)) Advance();
        }

        private void ParseFunctionOrVariable(ICppGlobalDeclarationContainer container, List<CppAttribute> attributes)
        {
            bool isStatic = false;
            bool isInline = false;
            bool isExtern = false;

            while (true)
            {
                if (Check(TokenType.Static)) { isStatic = true; Advance(); }
                else if (Check(TokenType.Inline)) { isInline = true; Advance(); }
                else if (Check(TokenType.Extern)) { isExtern = true; Advance(); }
                else if (Check(TokenType.Constexpr) || Check(TokenType.Consteval) || Check(TokenType.Constinit)) { Advance(); }
                else break;
            }

            attributes.AddRange(ParseAttributes());

            var type = ParseType();
            if (type == null)
            {
                SkipToRecoveryPoint();
                return;
            }

            // Handle qualified member definitions parsed as a type name, e.g., Text::Text(...)
            if (Check(TokenType.LeftParen) && type is CppUnexposedType ut && ut.Name.Contains("::"))
            {
                var func = ParseFunctionDeclaration(ut.Name, type, attributes, isStatic, false, isInline);
                if (func != null)
                {
                    if (isExtern) func.StorageQualifier = CppStorageQualifier.Extern;
                    container.Functions.Add(func);
                }
                return;
            }

            // Handle out-of-class member definitions like: inline Text::Text(...)
            if (Check(TokenType.DoubleColon))
            {
                var qualifiedName = new System.Text.StringBuilder(type.GetDisplayName());
                while (Check(TokenType.DoubleColon))
                {
                    Advance();
                    if (Check(TokenType.Identifier))
                    {
                        qualifiedName.Append("::");
                        qualifiedName.Append(Advance().Text);
                    }
                    else
                    {
                        break;
                    }
                }

                if (Check(TokenType.LeftParen))
                {
                    var func = ParseFunctionDeclaration(qualifiedName.ToString(), type, attributes, isStatic, false, isInline);
                    if (func != null)
                    {
                        if (isExtern) func.StorageQualifier = CppStorageQualifier.Extern;
                        container.Functions.Add(func);
                    }
                    return;
                }
            }

            if (!Check(TokenType.Identifier) && !Check(TokenType.Operator))
            {
                SkipToSemicolon();
                return;
            }

            string name;
            if (Check(TokenType.Operator))
            {
                name = ParseOperatorName();
            }
            else
            {
                name = Advance().Text;
            }

            if (Check(TokenType.LeftParen))
            {
                var func = ParseFunctionDeclaration(name, type, attributes, isStatic, false, isInline);
                if (func != null)
                {
                    if (isExtern) func.StorageQualifier = CppStorageQualifier.Extern;
                    container.Functions.Add(func);
                }
            }
            else
            {
                var field = new CppField(type, name);
                field.Span = new CppSourceSpan(Previous().Location, Previous().Location);
                if (isStatic) field.StorageQualifier = CppStorageQualifier.Static;
                if (isExtern) field.StorageQualifier = CppStorageQualifier.Extern;
                field.Attributes = attributes;

                // Handle array suffix
                while (Check(TokenType.LeftBracket))
                {
                    Advance();
                    SkipToClosingBracket(TokenType.LeftBracket, TokenType.RightBracket);
                }

                // Handle initializer
                if (Check(TokenType.Equals))
                {
                    Advance();
                    SkipToSemicolonOrComma();
                }

                container.Fields.Add(field);
                if (Check(TokenType.Semicolon)) Advance();
            }
        }

        // Type parsing continues in next message due to length...
        private CppType ParseType()
        {
            CppTypeQualifier? preQualifier = null;

            // Leading const/volatile
            if (Check(TokenType.Const))
            {
                preQualifier = CppTypeQualifier.Const;
                Advance();
            }
            else if (Check(TokenType.Volatile))
            {
                preQualifier = CppTypeQualifier.Volatile;
                Advance();
            }

            CppType baseType = null;

            // Primitive types
            if (Check(TokenType.Void)) { baseType = CppPrimitiveType.Void; Advance(); }
            else if (Check(TokenType.Bool)) { baseType = CppPrimitiveType.Bool; Advance(); }
            else if (Check(TokenType.Char)) { baseType = CppPrimitiveType.Char; Advance(); }
            else if (Check(TokenType.WChar)) { baseType = CppPrimitiveType.WChar; Advance(); }
            else if (Check(TokenType.Float)) { baseType = CppPrimitiveType.Float; Advance(); }
            else if (Check(TokenType.Double)) { baseType = CppPrimitiveType.Double; Advance(); }
            else if (Check(TokenType.Auto)) { baseType = new CppUnexposedType("auto"); Advance(); }
            else if (Check(TokenType.Signed) || Check(TokenType.Unsigned))
            {
                baseType = ParseIntegerType();
            }
            else if (Check(TokenType.Short) || Check(TokenType.Int) || Check(TokenType.Long))
            {
                baseType = ParseIntegerType();
            }
            else if (Check(TokenType.Typename))
            {
                Advance();
                baseType = ParseTypeName();
            }
            else if (Check(TokenType.Identifier) || Check(TokenType.DoubleColon))
            {
                baseType = ParseTypeName();
            }
            else if (Check(TokenType.Class) || Check(TokenType.Struct) || Check(TokenType.Enum))
            {
                Advance();
                if (Check(TokenType.Identifier))
                {
                    baseType = new CppUnexposedType(Advance().Text);
                }
                else
                {
                    return null;
                }
            }

            if (baseType == null) return null;

            // Apply pre-qualifier
            if (preQualifier.HasValue)
            {
                baseType = new CppQualifiedType(preQualifier.Value, baseType);
            }

            // Post-qualifiers, pointers, references
            while (true)
            {
                if (Check(TokenType.Const))
                {
                    Advance();
                    baseType = new CppQualifiedType(CppTypeQualifier.Const, baseType);
                }
                else if (Check(TokenType.Volatile))
                {
                    Advance();
                    baseType = new CppQualifiedType(CppTypeQualifier.Volatile, baseType);
                }
                else if (Check(TokenType.Star))
                {
                    Advance();
                    baseType = new CppPointerType(baseType);
                }
                else if (Check(TokenType.Ampersand))
                {
                    Advance();
                    baseType = new CppReferenceType(baseType);
                }
                else if (Check(TokenType.DoubleAmpersand))
                {
                    Advance();
                    // Rvalue reference - represent as reference for simplicity
                    baseType = new CppReferenceType(baseType);
                }
                else
                {
                    break;
                }
            }

            return baseType;
        }

        private CppType ParseIntegerType()
        {
            bool isSigned = true;
            bool hasShort = false;
            bool hasLong = false;
            int longCount = 0;
            bool hasInt = false;

            while (true)
            {
                if (Check(TokenType.Signed)) { isSigned = true; Advance(); }
                else if (Check(TokenType.Unsigned)) { isSigned = false; Advance(); }
                else if (Check(TokenType.Short)) { hasShort = true; Advance(); }
                else if (Check(TokenType.Long)) { hasLong = true; longCount++; Advance(); }
                else if (Check(TokenType.Int)) { hasInt = true; Advance(); }
                else if (Check(TokenType.Char)) { Advance(); return isSigned ? CppPrimitiveType.Char : CppPrimitiveType.UnsignedChar; }
                else break;
            }

            if (hasShort)
            {
                return isSigned ? CppPrimitiveType.Short : CppPrimitiveType.UnsignedShort;
            }
            if (hasLong && longCount >= 2)
            {
                return isSigned ? CppPrimitiveType.LongLong : CppPrimitiveType.UnsignedLongLong;
            }
            if (hasLong)
            {
                // 'long' alone - treat as int on most platforms
                return isSigned ? CppPrimitiveType.Int : CppPrimitiveType.UnsignedInt;
            }

            return isSigned ? CppPrimitiveType.Int : CppPrimitiveType.UnsignedInt;
        }

        private CppType ParseTypeName()
        {
            var sb = new System.Text.StringBuilder();

            // Optional leading ::
            if (Check(TokenType.DoubleColon))
            {
                sb.Append("::");
                Advance();
            }

            while (Check(TokenType.Identifier))
            {
                sb.Append(Advance().Text);

                // Template arguments
                if (Check(TokenType.LessThan))
                {
                    sb.Append(ParseTemplateArgs());
                }

                if (Check(TokenType.DoubleColon))
                {
                    sb.Append("::");
                    Advance();
                }
                else
                {
                    break;
                }
            }

            if (sb.Length == 0) return null;

            var name = sb.ToString();

            // Check for known class types that we've already parsed
            // For now, return as unexposed type
            return new CppUnexposedType(name);
        }

        private string ParseTemplateArgs()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('<');
            Advance(); // <

            int depth = 1;
            while (!IsAtEnd() && depth > 0)
            {
                if (Check(TokenType.LessThan))
                {
                    depth++;
                    sb.Append(Advance().Text);
                }
                else if (Check(TokenType.GreaterThan))
                {
                    depth--;
                    if (depth > 0) sb.Append(Advance().Text);
                    else Advance();
                }
                else if (Check(TokenType.GreaterEqual))
                {
                    // Handle >> being lexed as >=
                    // This shouldn't happen with our lexer, but handle it
                    depth--;
                    if (depth > 0) sb.Append(">");
                    Advance();
                }
                else
                {
                    sb.Append(Advance().Text);
                }
            }
            sb.Append('>');

            return sb.ToString();
        }

        // Helper methods
        private bool Check(TokenType type) => !IsAtEnd() && Current().Type == type;

        private bool IsAtEnd() => _position >= _tokens.Count || Current().Type == TokenType.EndOfFile;

        private Token Current() => _tokens[_position];

        private Token Previous() => _position > 0 ? _tokens[_position - 1] : _tokens[0];

        private Token PeekNext() => _position + 1 < _tokens.Count ? _tokens[_position + 1] : _tokens[_tokens.Count - 1];

        private Token Advance()
        {
            if (!IsAtEnd()) _position++;
            return Previous();
        }

        private void Expect(TokenType type)
        {
            if (Check(type))
            {
                Advance();
            }
            else
            {
                _compilation.Diagnostics.Warning($"Expected {type} but got {Current().Type}", Current().Location);
            }
        }

        private void SkipToSemicolon()
        {
            while (!IsAtEnd() && !Check(TokenType.Semicolon))
            {
                if (Check(TokenType.LeftBrace)) SkipBlock();
                else Advance();
            }
            if (Check(TokenType.Semicolon)) Advance();
        }

        private void SkipToSemicolonOrComma()
        {
            int braceDepth = 0;
            int parenDepth = 0;
            while (!IsAtEnd())
            {
                if (Check(TokenType.LeftBrace)) braceDepth++;
                if (Check(TokenType.RightBrace)) braceDepth--;
                if (Check(TokenType.LeftParen)) parenDepth++;
                if (Check(TokenType.RightParen)) parenDepth--;

                if (braceDepth == 0 && parenDepth == 0)
                {
                    if (Check(TokenType.Semicolon) || Check(TokenType.Comma)) break;
                }
                Advance();
            }
        }

        private void SkipToCommaOrCloseParen()
        {
            int depth = 0;
            while (!IsAtEnd())
            {
                if (Check(TokenType.LeftParen) || Check(TokenType.LessThan) || Check(TokenType.LeftBrace)) depth++;
                if (Check(TokenType.RightParen) || Check(TokenType.RightBrace))
                {
                    if (depth == 0) break;
                    depth--;
                }
                if (Check(TokenType.GreaterThan) && depth > 0) depth--;
                if (Check(TokenType.Comma) && depth == 0) break;
                Advance();
            }
        }

        private void SkipToCommaOrCloseBrace()
        {
            int depth = 0;
            while (!IsAtEnd())
            {
                if (Check(TokenType.LeftBrace) || Check(TokenType.LeftParen)) depth++;
                if (Check(TokenType.RightBrace))
                {
                    if (depth == 0) break;
                    depth--;
                }
                if (Check(TokenType.RightParen) && depth > 0) depth--;
                if (Check(TokenType.Comma) && depth == 0) break;
                Advance();
            }
        }

        private void SkipBlock()
        {
            Expect(TokenType.LeftBrace);
            int depth = 1;
            while (!IsAtEnd() && depth > 0)
            {
                if (Check(TokenType.LeftBrace)) depth++;
                if (Check(TokenType.RightBrace)) depth--;
                Advance();
            }
        }

        private void SkipToClosingBracket(TokenType open, TokenType close)
        {
            int depth = 1;
            while (!IsAtEnd() && depth > 0)
            {
                if (Check(open)) depth++;
                if (Check(close)) depth--;
                Advance();
            }
        }

        private void SkipToClosingAngleBracket()
        {
            int depth = 1;
            while (!IsAtEnd() && depth > 0)
            {
                if (Check(TokenType.LessThan)) depth++;
                if (Check(TokenType.GreaterThan)) depth--;
                Advance();
            }
        }

        private void SkipToRecoveryPoint()
        {
            while (!IsAtEnd())
            {
                if (Check(TokenType.Semicolon) || Check(TokenType.RightBrace))
                {
                    Advance();
                    break;
                }
                if (Check(TokenType.LeftBrace))
                {
                    SkipBlock();
                    break;
                }
                Advance();
            }
        }
    }
}
