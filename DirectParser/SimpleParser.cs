using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace LuaExpose.DirectParser
{
    /// <summary>
    /// Lightweight C++ parser focused on extracting LUA attributes and declarations
    /// </summary>
    public class SimpleParser
    {
        // Regex patterns for C++ constructs
        private static readonly Regex AttributeRegex = new Regex(
            @"\[\[\s*LUA_(\w+)(?:\((.*?)\))?\s*\]\]",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex ClassRegex = new Regex(
            @"(?<attrs>(?:\[\[.*?\]\]\s*)*)(?:class|struct)\s+(?:(?<export>\w+_API)\s+)?(?<attrs2>(?:\[\[.*?\]\]\s*)*)(?<name>\w+)(?:\s+final)?(?:\s*:\s*(?<bases>[\w\s,:<>]+?))?\s*(?={)",
            RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex FunctionRegex = new Regex(
        @"\[\[\s*LUA_(?:FUNC|CTOR|FUNC_TEMPLATE|META_FUNC)(?:\((?<attr_args>.*?)\))?\s*\]\]\s*" +
        @"(?<modifiers>(?:(?:static|virtual|const|inline|explicit|friend)\s+)*)" +
        @"(?<template>template\s*<.*?>\s*)?(?<return>[\w\s\*&:<>,]+?\s+)?" +  // Made optional with ?
        @"(?<name>\w+)\s*\((?<params>.*?)\)\s*(?<const>const)?\s*(?<override>override)?",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EnumRegex = new Regex(
            @"(?<attrs>(?:\[\[.*?\]\]\s*)*)enum\s+(?:class\s+)?(?<attrs2>(?:\[\[.*?\]\]\s*)*)(?<name>\w+)(?:\s*:\s*(?<type>\w+))?\s*{(?<body>.*?)}",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex FieldRegex = new Regex(
            @"\[\[\s*LUA_VAR(?:_READONLY)?(?:\(.*?\))?\s*\]\]\s*(?<modifiers>(?:(?:static|const|mutable)\s+)*)" +
            @"(?<type>[\w\s\*&:<>,]+?)\s+(?<name>\w+)(?:\s*=\s*(?<init>.*?))?;",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex TypedefRegex = new Regex(
            @"using\s+(?<name>\w+)\s*=\s*(?<type>[^;]+);",
            RegexOptions.Compiled | RegexOptions.Multiline);

        public ParsedFile ParseFile(string filePath)
        {
            try 
            {
                var content = File.ReadAllText(filePath);
                var result = new ParsedFile { FilePath = filePath };
                
                // Remove comments to avoid false matches
                content = RemoveComments(content);
                
                // Parse and expand macros
                content = ExpandMacros(content);
            
                // Parse namespaces and their contents
                var namespaceBodies = ParseNamespaces(content, result);
                
                // Parse global scope items (excluding namespace bodies)
                var globalContent = RemoveNamespaceBodies(content, namespaceBodies);
                ParseScope(globalContent, result.GlobalScope, null, filePath);
                
                return result;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new Exception($"Error parsing {filePath}: {ex.Message} at ({ex.TargetSite?.Name ?? "unknown"})", ex);
            }
        }

        private string RemoveComments(string content)
        {
            // Remove single-line comments
            content = Regex.Replace(content, @"//.*$", "", RegexOptions.Multiline);
            
            // Remove multi-line comments
            content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
            
            return content;
        }
        
        private string ExpandMacros(string content)
        {
            // Dictionary to store macro definitions
            var macros = new Dictionary<string, string>();
            
            // Find all macro definitions manually
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("#define"))
                {
                    var defineMatch = Regex.Match(line, @"#define\s+(\w+)\s*(.*)");
                    if (defineMatch.Success)
                    {
                        var macroName = defineMatch.Groups[1].Value;
                        var macroBody = new System.Text.StringBuilder();
                        var firstLine = defineMatch.Groups[2].Value;
                        
                        // Check if it's a multi-line macro
                        if (firstLine.TrimEnd().EndsWith("\\"))
                        {
                            // Remove trailing backslash
                            macroBody.AppendLine(firstLine.TrimEnd().TrimEnd('\\').TrimEnd());
                            
                            // Continue reading lines until we find one without trailing backslash
                            i++;
                            while (i < lines.Length)
                            {
                                var continuationLine = lines[i];
                                if (continuationLine.TrimEnd().EndsWith("\\"))
                                {
                                    macroBody.AppendLine(continuationLine.TrimEnd().TrimEnd('\\').TrimEnd());
                                    i++;
                                }
                                else
                                {
                                    macroBody.AppendLine(continuationLine);
                                    break;
                                }
                            }
                        }
                        else
                        {
                            macroBody.Append(firstLine);
                        }
                        
                        var body = macroBody.ToString().Trim();
                        macros[macroName] = body;
                        
                        if (macroName == "SETUP_COMPONENT")
                        {
                            Console.WriteLine($"[DirectParser] Found SETUP_COMPONENT macro with body:");
                            Console.WriteLine(body);
                            Console.WriteLine("[DirectParser] End of SETUP_COMPONENT macro");
                        }
                    }
                }
            }
            
            // Now expand macro invocations
            // Start with simple macros (no parameters)
            foreach (var macro in macros)
            {
                // Match macro invocations followed by semicolon (like SETUP_COMPONENT;)
                var invocationRegex = new Regex($@"\b{Regex.Escape(macro.Key)}\s*;", RegexOptions.Multiline);
                var replacementCount = 0;
                content = invocationRegex.Replace(content, match => 
                {
                    replacementCount++;
                    return macro.Value;
                });
                if (replacementCount > 0)
                {
                    Console.WriteLine($"[DirectParser] Replaced {replacementCount} instances of {macro.Key}");
                }
                
                // Also match without semicolon in case it's used in other contexts
                var invocationRegex2 = new Regex($@"\b{Regex.Escape(macro.Key)}\b(?!\s*\()", RegexOptions.Multiline);
                content = invocationRegex2.Replace(content, match =>
                {
                    // Don't replace if it's part of the #define itself
                    var lineStart = content.LastIndexOf('\n', match.Index) + 1;
                    var line = content.Substring(lineStart, match.Index - lineStart);
                    if (line.TrimStart().StartsWith("#define"))
                        return match.Value;
                    return macro.Value;
                });
            }
            
            return content;
        }

        private List<(int start, int end)> ParseNamespaces(string content, ParsedFile file)
        {
            return ParseNamespacesRecursive(content, file, null, null);
        }
        
        private const int MAX_NAMESPACE_DEPTH = 20;
        private int _currentNamespaceDepth = 0;
        
        private List<(int start, int end)> ParseNamespacesRecursive(string content, ParsedFile file, 
            ParsedNamespace parentNamespace, Dictionary<string, ParsedNamespace> globalNamespaceMap)
        {
            var namespaceBodies = new List<(int start, int end)>();
            
            // Guard against excessive nesting
            _currentNamespaceDepth++;
            try
            {
                if (_currentNamespaceDepth > MAX_NAMESPACE_DEPTH)
                {
                    Console.WriteLine($"[DirectParser] WARNING: Maximum namespace nesting depth ({MAX_NAMESPACE_DEPTH}) exceeded, stopping recursion");
                    return namespaceBodies;
                }
                
                // Use the global namespace map for top-level, or create one
                if (globalNamespaceMap == null)
                {
                    globalNamespaceMap = new Dictionary<string, ParsedNamespace>();
                }
            
            // Find namespace declarations
            var namespaceStartRegex = new Regex(
                @"namespace\s+(?<attrs>(?:\[\[.*?\]\]\s*)*)(?<name>\w+)\s*\{",
                RegexOptions.Singleline);
            
            var matches = namespaceStartRegex.Matches(content);
            if (parentNamespace == null)
            {
                System.Console.WriteLine($"[DirectParser] Found {matches.Count} namespace declaration(s) at level: {parentNamespace?.Name ?? "ROOT"}");
                foreach (Match m in matches)
                {
                    System.Console.WriteLine($"  - {m.Groups["name"].Value} at position {m.Index}");
                }
            }
            
            // Process matches, but skip those that are inside already-processed namespace bodies
            var processedRanges = new List<(int start, int end)>();
            
            foreach (Match match in matches)
            {
                // Skip if this match is inside an already-processed namespace
                if (processedRanges.Any(range => match.Index >= range.start && match.Index < range.end))
                {
                    System.Console.WriteLine($"[DirectParser] Skipping namespace '{match.Groups["name"].Value}' at {match.Index} - already inside another namespace");
                    continue;
                }
                
                var namespaceName = match.Groups["name"].Value;
                var fullName = parentNamespace != null ? $"{parentNamespace.Name}::{namespaceName}" : namespaceName;
                System.Console.WriteLine($"[DirectParser] Processing namespace '{namespaceName}' (parent: {parentNamespace?.Name ?? "none"})");
                ParsedNamespace ns;
                
                // For nested namespaces, always create new ones
                if (parentNamespace != null)
                {
                    ns = new ParsedNamespace
                    {
                        Name = namespaceName,
                        StartOffset = match.Index,
                        EndOffset = match.Index + match.Length,
                        FilePath = file.FilePath
                    };
                    // Add to parent namespace
                    parentNamespace.NestedNamespaces.Add(ns);
                    System.Console.WriteLine($"[DirectParser] Found nested namespace '{fullName}' and added to parent '{parentNamespace.Name}'");
                }
                else
                {
                    // Check if we already have this top-level namespace
                    if (globalNamespaceMap.ContainsKey(namespaceName))
                    {
                        ns = globalNamespaceMap[namespaceName];
                        System.Console.WriteLine($"[DirectParser] Merging additional content into existing namespace '{namespaceName}'");
                        
                    }
                    else
                    {
                        ns = new ParsedNamespace
                        {
                            Name = namespaceName,
                            StartOffset = match.Index,
                            EndOffset = match.Index + match.Length,
                            FilePath = file.FilePath
                        };
                        globalNamespaceMap[namespaceName] = ns;
                        
                    }
                }
                
                // Find the namespace body using brace matching
                var braceStart = content.IndexOf('{', match.Index + match.Length - 1);
                if (braceStart == -1) continue;
                
                var braceEnd = FindMatchingBrace(content, braceStart);
                if (braceEnd == -1 || braceEnd >= content.Length) continue;
                
                var bodyContentStart = braceStart + 1;
                var bodyContentLength = Math.Min(braceEnd - braceStart - 1, content.Length - bodyContentStart);
                if (bodyContentLength <= 0) continue;
                var bodyContent = content.Substring(bodyContentStart, bodyContentLength);
                
                // Check for namespace attributes
                var attrString = match.Groups["attrs"].Value;
                if (!string.IsNullOrWhiteSpace(attrString))
                {
                    var attrMatches = AttributeRegex.Matches(attrString);
                    foreach (Match attrMatch in attrMatches)
                    {
                        var attr = ParseAttribute(attrMatch, file.FilePath, match.Index);
                        ns.Attributes.Add(attr);
                        System.Console.WriteLine($"[DirectParser] Found namespace attribute {attr.Name} for namespace '{fullName}' in file: {attr.FilePath ?? "NULL"}");
                    }
                }
                
                // Update namespace span to include full body
                ns.StartOffset = match.Index;
                ns.EndOffset = braceEnd + 1;
                
                // First, parse nested namespaces
                var nestedBodies = ParseNamespacesRecursive(bodyContent, file, ns, globalNamespaceMap);
                
                // Add nested namespace bodies to our list so they're excluded from parent parsing
                foreach (var (nestedStart, nestedEnd) in nestedBodies)
                {
                    // Adjust offsets relative to parent
                    namespaceBodies.Add((bodyContentStart + nestedStart, bodyContentStart + nestedEnd));
                }
                
                // Remove nested namespace bodies before parsing the rest
                var cleanedBody = RemoveNamespaceBodies(bodyContent, nestedBodies);
                
                // Parse remaining content in this namespace
                ParseScope(cleanedBody, ns, fullName, file.FilePath);
                
                // Track namespace body region to exclude from parent scope parsing
                namespaceBodies.Add((match.Index, braceEnd + 1));
                
                // Also track for skipping in current level
                processedRanges.Add((match.Index, braceEnd + 1));
            }
            
            // Add namespaces to appropriate location
            if (parentNamespace != null)
            {
                // Add nested namespaces to parent - already handled above in the loop
            }
            else
            {
                // Add only truly top-level namespaces to file (only when NOT recursing)
                if (parentNamespace == null)
                {
                    foreach (var ns in globalNamespaceMap.Values)
                    {
                        if (!file.Namespaces.Any(n => n.Name == ns.Name))
                        {
                            file.Namespaces.Add(ns);
                            System.Console.WriteLine($"[DirectParser] Final namespace '{ns.Name}' contains:");
                            System.Console.WriteLine($"  - {ns.Classes.Count} classes");
                            System.Console.WriteLine($"  - {ns.Functions.Count} functions");
                            System.Console.WriteLine($"  - {ns.Enums.Count} enums");
                            System.Console.WriteLine($"  - {ns.NestedNamespaces.Count} nested namespaces");
                        }
                    }
                }
            }
            
            return namespaceBodies;
            }
            finally
            {
                _currentNamespaceDepth--;
            }
        }
        
        private string RemoveNamespaceBodies(string content, List<(int start, int end)> namespaceBodies)
        {
            // Sort namespace bodies by start position in reverse order
            var sortedBodies = namespaceBodies.OrderByDescending(x => x.start).ToList();
            
            // Remove each namespace body from the content
            foreach (var (start, end) in sortedBodies)
            {
                // Bounds check
                if (start >= content.Length) continue;
                var length = Math.Min(end - start, content.Length - start);
                if (length <= 0) continue;
                
                content = content.Remove(start, length);
            }
            
            return content;
        }

        private void ParseScope(string content, ParsedScope scope, string namespaceName, string filePath = null)
        {
            // Find all typedefs/using statements
            ParseTypedefs(content, scope, namespaceName, filePath);
            
            // Find all classes/structs
            ParseClasses(content, scope, namespaceName, filePath);
            
            // Find all global/namespace functions
            ParseFunctions(content, scope, namespaceName, null, filePath);
            
            // Find all enums
            ParseEnums(content, scope, namespaceName, filePath);
            
            // Find all global variables
            ParseFields(content, scope, namespaceName, null, filePath);
        }

        private void ParseTypedefs(string content, ParsedScope scope, string namespaceName, string filePath = null)
        {
            var matches = TypedefRegex.Matches(content);
            foreach (Match match in matches)
            {
                var typedef = new ParsedTypedef
                {
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value.Trim(),
                    Namespace = namespaceName,
                    FilePath = filePath,
                    StartOffset = match.Index,
                    EndOffset = match.Index + match.Length
                };
                
                scope.Typedefs.Add(typedef);
                
                System.Console.WriteLine($"[DirectParser] Found typedef: {typedef.Name} = {typedef.Type}");
            }
        }

        private void ParseClasses(string content, ParsedScope scope, string namespaceName, string filePath = null)
        {
            var matches = ClassRegex.Matches(content);
            var processedRanges = new List<(int start, int end)>();
            
            // Sort matches by position to process outer classes first
            var sortedMatches = matches.Cast<Match>().OrderBy(m => m.Index).ToList();
            
            foreach (Match match in sortedMatches)
            {
                // Skip if this match is inside an already-processed class
                if (processedRanges.Any(range => match.Index >= range.start && match.Index < range.end))
                {
                    System.Console.WriteLine($"[DirectParser] Skipping nested class/struct '{match.Groups["name"].Value}' at {match.Index}");
                    continue;
                }
                
                // Extract class body
                var bodyStart = content.IndexOf('{', match.Index + match.Length);
                if (bodyStart == -1) continue;
                
                var bodyEnd = FindMatchingBrace(content, bodyStart);
                if (bodyEnd == -1 || bodyEnd >= content.Length) continue;
                
                var classContentLength = Math.Min(bodyEnd - match.Index + 1, content.Length - match.Index);
                var classContent = content.Substring(match.Index, classContentLength);
                
                var bodyContentStart = bodyStart + 1;
                var bodyContentLength = Math.Min(bodyEnd - bodyStart - 1, content.Length - bodyContentStart);
                if (bodyContentLength <= 0) continue;
                var bodyContent = content.Substring(bodyContentStart, bodyContentLength);
                
                // Check for attributes before class and inline
                var attributes = new List<ParsedAttribute>();
                
                // Check attributes before the class/struct keyword
                var beforeAttrs = match.Groups["attrs"].Value;
                if (!string.IsNullOrWhiteSpace(beforeAttrs))
                {
                    var beforeAttrMatches = AttributeRegex.Matches(beforeAttrs);
                    foreach (Match attrMatch in beforeAttrMatches)
                    {
                        var attr = ParseAttribute(attrMatch, filePath, match.Index);
                        attributes.Add(attr);
                        System.Console.WriteLine($"[DirectParser] Found inline attribute {attr.Name} for class {match.Groups["name"].Value}");
                    }
                }
                
                // Check attributes after class/struct keyword (for export macros)
                var afterAttrs = match.Groups["attrs2"].Value;
                if (!string.IsNullOrWhiteSpace(afterAttrs))
                {
                    var afterAttrMatches = AttributeRegex.Matches(afterAttrs);
                    foreach (Match attrMatch in afterAttrMatches)
                    {
                        var attr = ParseAttribute(attrMatch, filePath, match.Index);
                        attributes.Add(attr);
                        System.Console.WriteLine($"[DirectParser] Found inline attribute {attr.Name} for class {match.Groups["name"].Value}");
                    }
                }
                
                // Also check for attributes in the 200 chars before (for separated attributes)
                if (match.Index > 0)
                {
                    var lookbackDistance = Math.Min(200, match.Index);
                    var startPos = match.Index - lookbackDistance;
                    var length = lookbackDistance;
                    
                    // Ensure we don't go past the end of content
                    if (startPos + length > content.Length)
                    {
                        length = content.Length - startPos;
                    }
                    
                    if (length > 0)
                    {
                        var beforeClassContent = content.Substring(startPos, length);
                        
                        // Check if we're inside another class/struct definition
                        // Look for any opening braces that haven't been closed
                        int braceDepth = 0;
                        for (int i = 0; i < beforeClassContent.Length; i++)
                        {
                            if (beforeClassContent[i] == '{') braceDepth++;
                            else if (beforeClassContent[i] == '}') braceDepth--;
                        }
                        
                        // Only look for attributes if we're not inside another class
                        if (braceDepth == 0)
                        {
                            var attrMatches = AttributeRegex.Matches(beforeClassContent);
                            foreach (Match attrMatch in attrMatches)
                            {
                                var attr = ParseAttribute(attrMatch, filePath, startPos);
                                // Check if we already have this attribute to avoid duplicates
                                if (!attributes.Any(a => a.Name == attr.Name && a.StartOffset == attr.StartOffset))
                                {
                                    attributes.Add(attr);
                                    System.Console.WriteLine($"[DirectParser] Found attribute {attr.Name} before class {match.Groups["name"].Value}");
                                }
                            }
                        }
                        else
                        {
                            System.Console.WriteLine($"[DirectParser] Skipping attribute lookback for class {match.Groups["name"].Value} - appears to be nested (brace depth: {braceDepth})");
                        }
                    }
                }
                
                if (attributes.Count == 0) 
                {
                    // Debug: Show why class was skipped
                    System.Console.WriteLine($"[DirectParser] Skipping class {match.Groups["name"].Value} in namespace {namespaceName ?? "global"} - no LUA attributes found");
                    continue;
                }
                
                var parsedClass = new ParsedClass
                {
                    Name = match.Groups["name"].Value,
                    Namespace = namespaceName,
                    Attributes = attributes,
                    FilePath = filePath,
                    StartOffset = match.Index,
                    EndOffset = bodyEnd + 1
                };
                
                // Debug attribute arguments
                foreach (var attr in attributes)
                {
                    if (attr.Name == "LUA_USERTYPE_NO_CTOR" && attr.Arguments != null)
                    {
                        System.Console.WriteLine($"[DirectParser] Class {parsedClass.Name} LUA_USERTYPE_NO_CTOR arguments: {string.Join(", ", attr.Arguments.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");
                    }
                }
                
                // Parse base classes
                if (match.Groups["bases"].Success)
                {
                    parsedClass.BaseClasses = ParseBaseClasses(match.Groups["bases"].Value);
                }
                
                // Parse class members
                ParseClassMembers(bodyContent, parsedClass, filePath);
                
                scope.Classes.Add(parsedClass);
                
                // Track this class's range to avoid parsing nested classes
                processedRanges.Add((match.Index, bodyEnd + 1));
            }
        }

        private void ParseClassMembers(string content, ParsedClass parsedClass, string filePath = null)
        {
            // Track access level
            var accessLevel = "private"; // Default for class
            var accessRegex = new Regex(@"^\s*(public|protected|private)\s*:", RegexOptions.Multiline);
            
            var accessMatches = accessRegex.Matches(content).Cast<Match>().OrderBy(m => m.Index).ToList();
            var sections = new List<(string access, int start, int end)>();
            
            for (int i = 0; i < accessMatches.Count; i++)
            {
                var start = accessMatches[i].Index + accessMatches[i].Length;
                // Ensure start doesn't exceed content length
                if (start > content.Length) continue;
                
                var end = i < accessMatches.Count - 1 ? accessMatches[i + 1].Index : content.Length;
                // Ensure end doesn't exceed content length
                end = Math.Min(end, content.Length);
                
                if (end > start)
                {
                    sections.Add((accessMatches[i].Groups[1].Value, start, end));
                }
            }
            
            // Add initial section if no access specifier at start
            if (accessMatches.Count == 0 || accessMatches[0].Index > 0)
            {
                sections.Insert(0, (accessLevel, 0, accessMatches.Count > 0 ? accessMatches[0].Index : content.Length));
            }
            
            // Parse each section
            foreach (var (access, start, end) in sections)
            {
                // Bounds check
                if (start >= content.Length) continue;
                var actualEnd = Math.Min(end, content.Length);
                var length = actualEnd - start;
                if (length <= 0) continue;
                
                var sectionContent = content.Substring(start, length);
                
                // Parse functions in this section
                ParseFunctions(sectionContent, null, parsedClass.Namespace, parsedClass, filePath);
                
                // Parse fields in this section
                ParseFields(sectionContent, null, parsedClass.Namespace, parsedClass, filePath);
                
                Console.WriteLine($"[DirectParser] Parsed class {parsedClass.Name} section ({access}): {parsedClass.Functions?.Count ?? 0} functions, {parsedClass.Fields?.Count ?? 0} fields");
            }
        }

        private void ParseFunctions(string content, ParsedScope scope, string namespaceName, ParsedClass containingClass, string filePath = null)
        {
            var matches = FunctionRegex.Matches(content);
            foreach (Match match in matches)
            {
                // Extract the LUA function attribute from the match
                var luaFuncAttrMatch = Regex.Match(match.Value, @"\[\[\s*LUA_(FUNC|CTOR|FUNC_TEMPLATE|META_FUNC)(?:\((?<args>.*?)\))?\s*\]\]");
                var attrName = "LUA_" + luaFuncAttrMatch.Groups[1].Value;
                
                var attr = new ParsedAttribute
                {
                    Name = attrName,
                    FilePath = filePath,
                    StartOffset = match.Index + luaFuncAttrMatch.Index,
                    EndOffset = match.Index + luaFuncAttrMatch.Index + luaFuncAttrMatch.Length
                };
                
                if (luaFuncAttrMatch.Groups["args"].Success)
                {
                    attr.Arguments = ParseAttributeArguments(luaFuncAttrMatch.Groups["args"].Value);
                }
                
                // For constructors, there's no return type
                var returnType = match.Groups["return"].Success ? match.Groups["return"].Value.Trim() : "";
                
                // Debug output for constructor parsing
                if (attrName == "LUA_CTOR")
                {
                    System.Console.WriteLine($"[DirectParser] Parsing constructor:");
                    System.Console.WriteLine($"  - Full match: {match.Value}");
                    System.Console.WriteLine($"  - Name: {match.Groups["name"].Value}");
                    System.Console.WriteLine($"  - Return type: '{returnType}'");
                    System.Console.WriteLine($"  - Modifiers: {match.Groups["modifiers"].Value}");
                    System.Console.WriteLine($"  - Parameters: {match.Groups["params"].Value}");
                }
                
                var function = new ParsedFunction
                {
                    Name = match.Groups["name"].Value,
                    ReturnType = returnType,
                    Parameters = ParseParameters(match.Groups["params"].Value),
                    IsStatic = match.Groups["modifiers"].Value.Contains("static"),
                    IsVirtual = match.Groups["modifiers"].Value.Contains("virtual"),
                    IsConst = match.Groups["const"].Success,
                    IsOverride = match.Groups["override"].Success,
                    Namespace = namespaceName,
                    ContainingClass = containingClass,
                    FilePath = filePath,
                    StartOffset = match.Index,
                    EndOffset = match.Index + match.Length,
                    Attributes = new List<ParsedAttribute> { attr }
                };
                
                if (match.Groups["template"].Success)
                {
                    function.TemplateParameters = ParseTemplateParameters(match.Groups["template"].Value);
                }
                
                if (containingClass != null)
                {
                    containingClass.Functions.Add(function);
                }
                else if (scope != null)
                {
                    scope.Functions.Add(function);
                }
            }
        }

        private void ParseEnums(string content, ParsedScope scope, string namespaceName, string filePath = null)
        {
            var matches = EnumRegex.Matches(content);
            foreach (Match match in matches)
            {
                // Check for attributes inline and before
                var attributes = new List<ParsedAttribute>();
                
                // Check inline attributes before enum keyword
                var beforeAttrs = match.Groups["attrs"].Value;
                if (!string.IsNullOrWhiteSpace(beforeAttrs))
                {
                    var beforeAttrMatches = AttributeRegex.Matches(beforeAttrs);
                    foreach (Match attrMatch in beforeAttrMatches)
                    {
                        var attr = ParseAttribute(attrMatch, filePath, match.Index);
                        attributes.Add(attr);
                    }
                }
                
                // Check inline attributes after enum keyword
                var afterAttrs = match.Groups["attrs2"].Value;
                if (!string.IsNullOrWhiteSpace(afterAttrs))
                {
                    var afterAttrMatches = AttributeRegex.Matches(afterAttrs);
                    foreach (Match attrMatch in afterAttrMatches)
                    {
                        var attr = ParseAttribute(attrMatch, filePath, match.Index);
                        attributes.Add(attr);
                    }
                }
                
                // Also check before enum declaration
                if (match.Index > 0)
                {
                    var lookbackDistance = Math.Min(100, match.Index);
                    var startPos = match.Index - lookbackDistance;
                    var length = lookbackDistance;
                    
                    // Ensure we don't go past the end of content
                    if (startPos + length > content.Length)
                    {
                        length = content.Length - startPos;
                    }
                    
                    if (length > 0)
                    {
                        var beforeEnumContent = content.Substring(startPos, length);
                        var attrMatches = AttributeRegex.Matches(beforeEnumContent);
                        foreach (Match attrMatch in attrMatches)
                        {
                            var attr = ParseAttribute(attrMatch, filePath, startPos);
                            if (!attributes.Any(a => a.Name == attr.Name && a.StartOffset == attr.StartOffset))
                            {
                                attributes.Add(attr);
                            }
                        }
                    }
                }
                
                if (attributes.Count == 0) continue;
                
                var parsedEnum = new ParsedEnum
                {
                    Name = match.Groups["name"].Value,
                    UnderlyingType = match.Groups["type"].Success ? match.Groups["type"].Value : "int",
                    Namespace = namespaceName,
                    FilePath = filePath,
                    StartOffset = match.Index,
                    EndOffset = match.Index + match.Length,
                    Attributes = attributes
                };
                
                // Parse enum values
                var body = match.Groups["body"].Value;
                var valueRegex = new Regex(@"(?<name>\w+)(?:\s*=\s*(?<value>[^,}]+))?");
                var valueMatches = valueRegex.Matches(body);
                
                foreach (Match valueMatch in valueMatches)
                {
                    parsedEnum.Values.Add(new ParsedEnumValue
                    {
                        Name = valueMatch.Groups["name"].Value,
                        Value = valueMatch.Groups["value"].Success ? valueMatch.Groups["value"].Value.Trim() : null
                    });
                }
                
                scope.Enums.Add(parsedEnum);
            }
        }

        private void ParseFields(string content, ParsedScope scope, string namespaceName, ParsedClass containingClass, string filePath = null)
        {
            var matches = FieldRegex.Matches(content);
            foreach (Match match in matches)
            {
                // Extract the LUA_VAR or LUA_VAR_READONLY attribute from the match
                var luaVarAttrMatch = Regex.Match(match.Value, @"\[\[\s*(LUA_VAR(?:_READONLY)?)(?:\((?<args>.*?)\))?\s*\]\]");
                var attr = new ParsedAttribute
                {
                    Name = luaVarAttrMatch.Groups[1].Value,
                    FilePath = filePath,
                    StartOffset = match.Index + luaVarAttrMatch.Index,
                    EndOffset = match.Index + luaVarAttrMatch.Index + luaVarAttrMatch.Length
                };
                
                if (luaVarAttrMatch.Groups["args"].Success)
                {
                    attr.Arguments = ParseAttributeArguments(luaVarAttrMatch.Groups["args"].Value);
                }
                
                var field = new ParsedField
                {
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value.Trim(),
                    IsStatic = match.Groups["modifiers"].Value.Contains("static"),
                    IsConst = match.Groups["modifiers"].Value.Contains("const"),
                    Namespace = namespaceName,
                    ContainingClass = containingClass,
                    FilePath = filePath,
                    StartOffset = match.Index,
                    EndOffset = match.Index + match.Length,
                    Attributes = new List<ParsedAttribute> { attr }
                };
                
                if (containingClass != null)
                {
                    containingClass.Fields.Add(field);
                }
                else if (scope != null)
                {
                    scope.Fields.Add(field);
                }
            }
        }

        private ParsedAttribute ParseAttribute(Match match, string filePath = null, int baseOffset = 0)
        {
            var attr = new ParsedAttribute
            {
                Name = "LUA_" + match.Groups[1].Value,
                FilePath = filePath,
                StartOffset = baseOffset + match.Index,
                EndOffset = baseOffset + match.Index + match.Length
            };
            
            if (match.Groups[2].Success)
            {
                attr.Arguments = ParseAttributeArguments(match.Groups[2].Value);
            }
            
            return attr;
        }

        private Dictionary<string, string> ParseAttributeArguments(string argString)
        {
            var args = new Dictionary<string, string>();
            
            // Handle template-style arguments (e.g., LUA_USERTYPE_TEMPLATE(T1, T2))
            if (!argString.Contains("="))
            {
                var parts = argString.Split(',').Select(s => s.Trim()).ToArray();
                for (int i = 0; i < parts.Length; i++)
                {
                    args[$"arg{i}"] = parts[i];
                }
                return args;
            }
            
            // Handle key=value arguments
            var argRegex = new Regex(@"(\$?\w+)\s*=\s*([^,]+)");
            var matches = argRegex.Matches(argString);
            foreach (Match match in matches)
            {
                args[match.Groups[1].Value] = match.Groups[2].Value.Trim();
            }
            
            // Handle standalone flags (e.g., use_static)
            var flagRegex = new Regex(@"\b(\w+)\b(?!\s*=)");
            var flagMatches = flagRegex.Matches(argString);
            foreach (Match match in flagMatches)
            {
                if (!args.ContainsKey(match.Groups[1].Value))
                {
                    args[match.Groups[1].Value] = "true";
                }
            }
            
            return args;
        }

        private List<string> ParseBaseClasses(string basesString)
        {
            var bases = new List<string>();
            var parts = SplitByComma(basesString);
            
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                // Remove access specifiers
                trimmed = Regex.Replace(trimmed, @"^\s*(public|protected|private)\s+", "");
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    bases.Add(trimmed);
                }
            }
            
            return bases;
        }

        private List<ParsedParameter> ParseParameters(string paramsString)
        {
            var parameters = new List<ParsedParameter>();
            if (string.IsNullOrWhiteSpace(paramsString)) return parameters;
            
            var parts = SplitByComma(paramsString);
            foreach (var part in parts)
            {
                var param = ParseParameter(part.Trim());
                if (param != null)
                {
                    parameters.Add(param);
                }
            }
            
            return parameters;
        }

        private ParsedParameter ParseParameter(string paramString)
        {
            if (string.IsNullOrWhiteSpace(paramString)) return null;
            
            // Handle ellipsis
            if (paramString == "...") 
            {
                return new ParsedParameter { Type = "...", Name = "" };
            }
            
            // Match parameter pattern: [const] type [&|*] [name] [= default]
            var paramRegex = new Regex(@"^(?<type>.*?)\s+(?<name>\w+)(?:\s*=\s*(?<default>.*))?$");
            var match = paramRegex.Match(paramString);
            
            if (match.Success)
            {
                return new ParsedParameter
                {
                    Type = match.Groups["type"].Value.Trim(),
                    Name = match.Groups["name"].Value,
                    DefaultValue = match.Groups["default"].Success ? match.Groups["default"].Value.Trim() : null
                };
            }
            
            // Fallback for type-only parameters
            return new ParsedParameter { Type = paramString, Name = "" };
        }

        private List<string> ParseTemplateParameters(string templateString)
        {
            var match = Regex.Match(templateString, @"template\s*<(.*)>");
            if (!match.Success) return new List<string>();
            
            return SplitByComma(match.Groups[1].Value)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        private List<string> SplitByComma(string input)
        {
            var parts = new List<string>();
            var current = "";
            var depth = 0;
            
            foreach (char c in input)
            {
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    parts.Add(current.Trim());
                    current = "";
                    continue;
                }
                current += c;
            }
            
            if (!string.IsNullOrWhiteSpace(current))
            {
                parts.Add(current.Trim());
            }
            
            return parts;
        }
        
        private int FindMatchingBrace(string content, int startBrace)
        {
            if (startBrace < 0 || startBrace >= content.Length)
            {
                Console.WriteLine($"[DirectParser] WARNING: Invalid startBrace position {startBrace} for content length {content.Length}");
                return -1;
            }
            
            int depth = 1;
            int maxIterations = content.Length - startBrace;
            int iterations = 0;
            
            for (int i = startBrace + 1; i < content.Length; i++)
            {
                iterations++;
                if (iterations > maxIterations)
                {
                    Console.WriteLine($"[DirectParser] WARNING: FindMatchingBrace exceeded max iterations, likely malformed code");
                    return -1;
                }
                
                if (content[i] == '{') depth++;
                else if (content[i] == '}') depth--;
                
                if (depth == 0) return i;
            }
            return -1; // No matching brace found
        }
    }
}