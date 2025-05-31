using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LuaExpose.DirectParser
{
    /// <summary>
    /// Handles parsing and normalization of C++ types
    /// </summary>
    public class TypeParser
    {
        // Common type aliases
        private static readonly Dictionary<string, string> TypeAliases = new Dictionary<string, string>
        {
            { "std::string", "String" },
            { "std::basic_string<char>", "String" },
            { "std::vector", "vector" },
            { "std::shared_ptr", "shared_ptr" },
            { "std::unique_ptr", "unique_ptr" },
            { "std::optional", "optional" },
            { "std::function", "function" },
            { "unsigned int", "uint32_t" },
            { "unsigned long", "uint64_t" },
            { "unsigned short", "uint16_t" },
            { "unsigned char", "uint8_t" },
            { "signed char", "int8_t" },
            { "long long", "int64_t" },
            { "unsigned long long", "uint64_t" }
        };

        // Template specializations
        private static readonly Dictionary<string, string> TemplateSpecializations = new Dictionary<string, string>
        {
            { "Vec2<float>", "Vector" },
            { "Vec2<double>", "Vector" },
            { "Vec3<float>", "Vector3" },
            { "Vec3<double>", "Vector3" },
            { "Vec4<float>", "Vector4" },
            { "Vec4<double>", "Vector4" }
        };

        public ParsedType ParseType(string typeString)
        {
            if (string.IsNullOrWhiteSpace(typeString))
                return new ParsedType { Name = "void" };

            typeString = typeString.Trim();
            var result = new ParsedType();

            // Extract cv-qualifiers
            if (typeString.StartsWith("const "))
            {
                result.IsConst = true;
                typeString = typeString.Substring(6);
            }

            if (typeString.EndsWith(" const"))
            {
                result.IsTrailingConst = true;
                typeString = typeString.Substring(0, typeString.Length - 6);
            }

            if (typeString.StartsWith("volatile "))
            {
                result.IsVolatile = true;
                typeString = typeString.Substring(9);
            }

            // Extract reference/pointer
            if (typeString.EndsWith("&&"))
            {
                result.IsRValueReference = true;
                typeString = typeString.Substring(0, typeString.Length - 2).TrimEnd();
            }
            else if (typeString.EndsWith("&"))
            {
                result.IsReference = true;
                typeString = typeString.Substring(0, typeString.Length - 1).TrimEnd();
            }
            
            while (typeString.EndsWith("*"))
            {
                result.PointerLevel++;
                typeString = typeString.Substring(0, typeString.Length - 1).TrimEnd();
            }

            // Handle const after type name (e.g., "int const*")
            if (typeString.EndsWith(" const"))
            {
                result.IsConst = true;
                typeString = typeString.Substring(0, typeString.Length - 6);
            }

            // Parse template parameters
            var templateMatch = Regex.Match(typeString, @"^([\w:]+)<(.+)>$");
            if (templateMatch.Success)
            {
                result.Name = templateMatch.Groups[1].Value;
                result.TemplateArguments = ParseTemplateArguments(templateMatch.Groups[2].Value);
                
                // Check for template specializations
                var fullTemplate = $"{result.Name}<{string.Join(", ", result.TemplateArguments)}>";
                if (TemplateSpecializations.TryGetValue(fullTemplate, out var specialized))
                {
                    result.Name = specialized;
                    result.TemplateArguments.Clear();
                }
            }
            else
            {
                result.Name = typeString;
            }

            // Apply type aliases
            if (TypeAliases.TryGetValue(result.Name, out var alias))
            {
                result.Name = alias;
            }

            // Normalize namespace separators
            result.Name = result.Name.Replace("::", ".");

            return result;
        }

        private List<string> ParseTemplateArguments(string argsString)
        {
            var args = new List<string>();
            var current = "";
            var depth = 0;

            foreach (char c in argsString)
            {
                if (c == '<') depth++;
                else if (c == '>') depth--;
                else if (c == ',' && depth == 0)
                {
                    args.Add(current.Trim());
                    current = "";
                    continue;
                }
                current += c;
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                args.Add(current.Trim());
            }

            // Recursively parse template arguments
            return args.Select(arg => ParseType(arg).ToString()).ToList();
        }

        public string NormalizeType(string typeString)
        {
            var parsed = ParseType(typeString);
            return parsed.ToString();
        }

        public string ConvertToSolType(ParsedType type)
        {
            // Handle special Sol3 types
            var baseType = type.Name;
            
            // Handle containers
            if (baseType == "vector" && type.TemplateArguments.Count > 0)
            {
                return $"std::vector<{type.TemplateArguments[0]}>";
            }
            
            if (baseType == "shared_ptr" && type.TemplateArguments.Count > 0)
            {
                return $"std::shared_ptr<{type.TemplateArguments[0]}>";
            }
            
            if (baseType == "optional" && type.TemplateArguments.Count > 0)
            {
                return $"std::optional<{type.TemplateArguments[0]}>";
            }
            
            // Restore C++ style for Sol
            baseType = baseType.Replace(".", "::");
            
            // Build full type
            var result = baseType;
            if (type.TemplateArguments.Count > 0)
            {
                result = $"{baseType}<{string.Join(", ", type.TemplateArguments)}>";
            }
            
            if (type.IsConst && !type.IsPointer && !type.IsReference)
            {
                result = $"const {result}";
            }
            
            if (type.PointerLevel > 0)
            {
                result += new string('*', type.PointerLevel);
            }
            
            if (type.IsReference)
            {
                result += "&";
            }
            
            if (type.IsRValueReference)
            {
                result += "&&";
            }
            
            return result;
        }

        public string ConvertToTypeScriptType(ParsedType type)
        {
            var baseType = type.Name;
            
            // TypeScript type mappings
            var tsTypeMap = new Dictionary<string, string>
            {
                { "void", "void" },
                { "bool", "boolean" },
                { "int", "number" },
                { "float", "number" },
                { "double", "number" },
                { "int8_t", "number" },
                { "int16_t", "number" },
                { "int32_t", "number" },
                { "int64_t", "number" },
                { "uint8_t", "number" },
                { "uint16_t", "number" },
                { "uint32_t", "number" },
                { "uint64_t", "number" },
                { "size_t", "number" },
                { "String", "string" },
                { "char", "string" },
                { "Vector", "Vector" },
                { "Vector3", "Vector3" },
                { "Vector4", "Vector4" }
            };
            
            if (tsTypeMap.TryGetValue(baseType, out var tsType))
            {
                baseType = tsType;
            }
            
            // Handle containers
            if (baseType == "vector" && type.TemplateArguments.Count > 0)
            {
                var elementType = ConvertToTypeScriptType(ParseType(type.TemplateArguments[0]));
                return $"{elementType}[]";
            }
            
            if (baseType == "optional" && type.TemplateArguments.Count > 0)
            {
                var elementType = ConvertToTypeScriptType(ParseType(type.TemplateArguments[0]));
                return $"{elementType} | undefined";
            }
            
            if (baseType == "shared_ptr" && type.TemplateArguments.Count > 0)
            {
                return ConvertToTypeScriptType(ParseType(type.TemplateArguments[0]));
            }
            
            // Handle function types
            if (baseType == "function" && type.TemplateArguments.Count > 0)
            {
                // Parse function signature from template arg
                var funcSig = type.TemplateArguments[0];
                var match = Regex.Match(funcSig, @"^(.+)\((.*)\)$");
                if (match.Success)
                {
                    var retType = ConvertToTypeScriptType(ParseType(match.Groups[1].Value));
                    var paramTypes = match.Groups[2].Value.Split(',')
                        .Select(p => ConvertToTypeScriptType(ParseType(p.Trim())))
                        .ToList();
                    
                    if (paramTypes.Count == 0 || (paramTypes.Count == 1 && paramTypes[0] == "void"))
                    {
                        return $"() => {retType}";
                    }
                    
                    var paramList = string.Join(", ", paramTypes.Select((t, i) => $"arg{i}: {t}"));
                    return $"({paramList}) => {retType}";
                }
            }
            
            // For pointers in TypeScript, we typically just use the base type
            if (type.PointerLevel > 0)
            {
                return baseType;
            }
            
            return baseType;
        }
    }

    public class ParsedType
    {
        public string Name { get; set; }
        public List<string> TemplateArguments { get; set; } = new List<string>();
        public bool IsConst { get; set; }
        public bool IsTrailingConst { get; set; }
        public bool IsVolatile { get; set; }
        public bool IsReference { get; set; }
        public bool IsRValueReference { get; set; }
        public int PointerLevel { get; set; }
        public bool IsPointer => PointerLevel > 0;

        public override string ToString()
        {
            var result = Name;
            
            if (TemplateArguments.Count > 0)
            {
                result = $"{Name}<{string.Join(", ", TemplateArguments)}>";
            }
            
            if (IsConst)
            {
                result = $"const {result}";
            }
            
            if (PointerLevel > 0)
            {
                result += new string('*', PointerLevel);
            }
            
            if (IsReference)
            {
                result += "&";
            }
            
            if (IsRValueReference)
            {
                result += "&&";
            }
            
            return result;
        }
    }
}