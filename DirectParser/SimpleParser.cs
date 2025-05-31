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
            @"(?:class|struct)\s+(?:(?<export>\w+_API)\s+)?(?<name>\w+)(?:\s*:\s*(?<bases>[\w\s,:<>]+?))?\s*(?={)",
            RegexOptions.Compiled | RegexOptions.Multiline);
        
        private static readonly Regex FunctionRegex = new Regex(
            @"(?<attrs>(?:\[\[.*?\]\]\s*)*)(?<modifiers>(?:(?:static|virtual|const|inline|explicit|friend)\s+)*)" +
            @"(?<template>template\s*<.*?>\s*)?(?<return>[\w\s\*&:<>,]+?)\s+" +
            @"(?<name>\w+)\s*\((?<params>.*?)\)\s*(?<const>const)?\s*(?<override>override)?",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex EnumRegex = new Regex(
            @"enum\s+(?:class\s+)?(?<name>\w+)(?:\s*:\s*(?<type>\w+))?\s*{(?<body>.*?)}",
            RegexOptions.Compiled | RegexOptions.Singleline);
        
        private static readonly Regex FieldRegex = new Regex(
            @"(?<attrs>(?:\[\[.*?\]\]\s*)*)(?<modifiers>(?:(?:static|const|mutable)\s+)*)" +
            @"(?<type>[\w\s\*&:<>,]+?)\s+(?<name>\w+)(?:\s*=\s*(?<init>.*?))?;",
            RegexOptions.Compiled | RegexOptions.Singleline);

        public ParsedFile ParseFile(string filePath)
        {
            var content = File.ReadAllText(filePath);
            var result = new ParsedFile { FilePath = filePath };
            
            // Remove comments to avoid false matches
            content = RemoveComments(content);
            
            // Parse namespaces and their contents
            ParseNamespaces(content, result);
            
            // Parse global scope items
            ParseScope(content, result.GlobalScope, null);
            
            return result;
        }

        private string RemoveComments(string content)
        {
            // Remove single-line comments
            content = Regex.Replace(content, @"//.*$", "", RegexOptions.Multiline);
            
            // Remove multi-line comments
            content = Regex.Replace(content, @"/\*.*?\*/", "", RegexOptions.Singleline);
            
            return content;
        }

        private void ParseNamespaces(string content, ParsedFile file)
        {
            var namespaceRegex = new Regex(
                @"namespace\s+(?<name>\w+)\s*{(?<body>(?:[^{}]|{(?:[^{}]|{[^{}]*})*})*)}",
                RegexOptions.Singleline);
            
            var matches = namespaceRegex.Matches(content);
            foreach (Match match in matches)
            {
                var ns = new ParsedNamespace
                {
                    Name = match.Groups["name"].Value,
                    StartOffset = match.Index,
                    EndOffset = match.Index + match.Length
                };
                
                // Check for namespace attributes
                var attrMatch = AttributeRegex.Match(content.Substring(Math.Max(0, match.Index - 100), 100));
                if (attrMatch.Success)
                {
                    ns.Attributes.Add(ParseAttribute(attrMatch));
                }
                
                // Parse namespace body
                ParseScope(match.Groups["body"].Value, ns, ns.Name);
                
                file.Namespaces.Add(ns);
            }
        }

        private void ParseScope(string content, ParsedScope scope, string namespaceName)
        {
            // Find all classes/structs
            ParseClasses(content, scope, namespaceName);
            
            // Find all global/namespace functions
            ParseFunctions(content, scope, namespaceName, null);
            
            // Find all enums
            ParseEnums(content, scope, namespaceName);
            
            // Find all global variables
            ParseFields(content, scope, namespaceName, null);
        }

        private void ParseClasses(string content, ParsedScope scope, string namespaceName)
        {
            var matches = ClassRegex.Matches(content);
            foreach (Match match in matches)
            {
                // Extract class body
                var bodyStart = content.IndexOf('{', match.Index + match.Length);
                if (bodyStart == -1) continue;
                
                var bodyEnd = FindMatchingBrace(content, bodyStart);
                if (bodyEnd == -1) continue;
                
                var classContent = content.Substring(match.Index, bodyEnd - match.Index + 1);
                var bodyContent = content.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
                
                // Check for attributes before class
                var attrMatches = AttributeRegex.Matches(content.Substring(Math.Max(0, match.Index - 200), 200));
                var attributes = new List<ParsedAttribute>();
                foreach (Match attrMatch in attrMatches)
                {
                    attributes.Add(ParseAttribute(attrMatch));
                }
                
                if (attributes.Count == 0) continue; // Skip classes without LUA attributes
                
                var parsedClass = new ParsedClass
                {
                    Name = match.Groups["name"].Value,
                    Namespace = namespaceName,
                    Attributes = attributes,
                    StartOffset = match.Index,
                    EndOffset = bodyEnd + 1
                };
                
                // Parse base classes
                if (match.Groups["bases"].Success)
                {
                    parsedClass.BaseClasses = ParseBaseClasses(match.Groups["bases"].Value);
                }
                
                // Parse class members
                ParseClassMembers(bodyContent, parsedClass);
                
                scope.Classes.Add(parsedClass);
            }
        }

        private void ParseClassMembers(string content, ParsedClass parsedClass)
        {
            // Track access level
            var accessLevel = "private"; // Default for class
            var accessRegex = new Regex(@"^\s*(public|protected|private)\s*:", RegexOptions.Multiline);
            
            var accessMatches = accessRegex.Matches(content).Cast<Match>().OrderBy(m => m.Index).ToList();
            var sections = new List<(string access, int start, int end)>();
            
            for (int i = 0; i < accessMatches.Count; i++)
            {
                var start = accessMatches[i].Index + accessMatches[i].Length;
                var end = i < accessMatches.Count - 1 ? accessMatches[i + 1].Index : content.Length;
                sections.Add((accessMatches[i].Groups[1].Value, start, end));
            }
            
            // Add initial section if no access specifier at start
            if (accessMatches.Count == 0 || accessMatches[0].Index > 0)
            {
                sections.Insert(0, (accessLevel, 0, accessMatches.Count > 0 ? accessMatches[0].Index : content.Length));
            }
            
            // Parse each section
            foreach (var (access, start, end) in sections)
            {
                var sectionContent = content.Substring(start, end - start);
                
                // Parse functions in this section
                ParseFunctions(sectionContent, null, parsedClass.Namespace, parsedClass);
                
                // Parse fields in this section
                ParseFields(sectionContent, null, parsedClass.Namespace, parsedClass);
            }
        }

        private void ParseFunctions(string content, ParsedScope scope, string namespaceName, ParsedClass containingClass)
        {
            var matches = FunctionRegex.Matches(content);
            foreach (Match match in matches)
            {
                var attrString = match.Groups["attrs"].Value;
                if (string.IsNullOrWhiteSpace(attrString)) continue;
                
                var attrMatches = AttributeRegex.Matches(attrString);
                if (!attrMatches.Cast<Match>().Any()) continue;
                
                var function = new ParsedFunction
                {
                    Name = match.Groups["name"].Value,
                    ReturnType = match.Groups["return"].Value.Trim(),
                    Parameters = ParseParameters(match.Groups["params"].Value),
                    IsStatic = match.Groups["modifiers"].Value.Contains("static"),
                    IsVirtual = match.Groups["modifiers"].Value.Contains("virtual"),
                    IsConst = match.Groups["const"].Success,
                    IsOverride = match.Groups["override"].Success,
                    Namespace = namespaceName,
                    ContainingClass = containingClass
                };
                
                foreach (Match attrMatch in attrMatches)
                {
                    function.Attributes.Add(ParseAttribute(attrMatch));
                }
                
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

        private void ParseEnums(string content, ParsedScope scope, string namespaceName)
        {
            var matches = EnumRegex.Matches(content);
            foreach (Match match in matches)
            {
                // Check for attributes
                var attrMatches = AttributeRegex.Matches(content.Substring(Math.Max(0, match.Index - 100), 100));
                if (!attrMatches.Cast<Match>().Any()) continue;
                
                var parsedEnum = new ParsedEnum
                {
                    Name = match.Groups["name"].Value,
                    UnderlyingType = match.Groups["type"].Success ? match.Groups["type"].Value : "int",
                    Namespace = namespaceName
                };
                
                foreach (Match attrMatch in attrMatches)
                {
                    parsedEnum.Attributes.Add(ParseAttribute(attrMatch));
                }
                
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

        private void ParseFields(string content, ParsedScope scope, string namespaceName, ParsedClass containingClass)
        {
            var matches = FieldRegex.Matches(content);
            foreach (Match match in matches)
            {
                var attrString = match.Groups["attrs"].Value;
                if (string.IsNullOrWhiteSpace(attrString)) continue;
                
                var attrMatches = AttributeRegex.Matches(attrString);
                if (!attrMatches.Cast<Match>().Any()) continue;
                
                var field = new ParsedField
                {
                    Name = match.Groups["name"].Value,
                    Type = match.Groups["type"].Value.Trim(),
                    IsStatic = match.Groups["modifiers"].Value.Contains("static"),
                    IsConst = match.Groups["modifiers"].Value.Contains("const"),
                    Namespace = namespaceName,
                    ContainingClass = containingClass
                };
                
                foreach (Match attrMatch in attrMatches)
                {
                    field.Attributes.Add(ParseAttribute(attrMatch));
                }
                
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

        private ParsedAttribute ParseAttribute(Match match)
        {
            var attr = new ParsedAttribute
            {
                Name = "LUA_" + match.Groups[1].Value
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

        private int FindMatchingBrace(string content, int openBraceIndex)
        {
            var depth = 1;
            var i = openBraceIndex + 1;
            
            while (i < content.Length && depth > 0)
            {
                if (content[i] == '{') depth++;
                else if (content[i] == '}') depth--;
                i++;
            }
            
            return depth == 0 ? i - 1 : -1;
        }
    }
}