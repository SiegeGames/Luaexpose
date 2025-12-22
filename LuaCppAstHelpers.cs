using CppAst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static LuaExpose.CodeGenWriter;

namespace LuaExpose
{
    public static class CppExtenstions
    {
        private static HashSet<string> _primitiveAliases = new HashSet<string>(StringComparer.Ordinal);
        private static HashSet<string> _enumNames = new HashSet<string>(StringComparer.Ordinal);
        private static Dictionary<string, CppTypedef> _typedefsByName = new Dictionary<string, CppTypedef>(StringComparer.Ordinal);
        private static HashSet<string> _preservedTypedefs = new HashSet<string>(StringComparer.Ordinal);
        private static HashSet<string> _templateSpecializationNames = new HashSet<string>(StringComparer.Ordinal);
        private static Dictionary<string, string> _typedefAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private static HashSet<string> _legacyPreservedTypedefs = new HashSet<string>(StringComparer.Ordinal);
        private static HashSet<string> _classNames = new HashSet<string>(StringComparer.Ordinal);

        public static void InitializePrimitiveAliases(CppCompilation compilation)
        {
            if (compilation == null)
            {
                _primitiveAliases = new HashSet<string>(StringComparer.Ordinal);
                _typedefsByName = new Dictionary<string, CppTypedef>(StringComparer.Ordinal);
                return;
            }

            var typedefs = new List<CppTypedef>();
            CollectTypedefs(compilation, typedefs);

            var typedefMap = typedefs
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            _typedefsByName = typedefMap.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.FirstOrDefault(),
                StringComparer.Ordinal);

            var resolved = new Dictionary<string, bool>(StringComparer.Ordinal);
            var aliases = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in typedefMap.Keys)
            {
                if (ResolveAliasToPrimitive(name, typedefMap, resolved, new HashSet<string>(StringComparer.Ordinal)))
                {
                    aliases.Add(name);
                }
            }

            _primitiveAliases = aliases;
        }

        public static void InitializeEnumNames(CppCompilation compilation)
        {
            if (compilation == null)
            {
                _enumNames = new HashSet<string>(StringComparer.Ordinal);
                return;
            }

            var enums = new List<CppEnum>();
            CollectEnums(compilation, enums);

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cppEnum in enums)
            {
                if (!string.IsNullOrWhiteSpace(cppEnum?.Name))
                {
                    names.Add(cppEnum.Name);
                }
            }

            _enumNames = names;
        }

        public static void InitializePreservedTypedefs(CppCompilation compilation)
        {
            _preservedTypedefs = new HashSet<string>(StringComparer.Ordinal);
            _templateSpecializationNames = new HashSet<string>(StringComparer.Ordinal);
            _typedefAliases = new Dictionary<string, string>(StringComparer.Ordinal);
            _legacyPreservedTypedefs = new HashSet<string>(StringComparer.Ordinal);
            _classNames = new HashSet<string>(StringComparer.Ordinal);

            if (compilation == null)
            {
                return;
            }

            var classes = new List<CppClass>();
            CollectClasses(compilation, classes);

            foreach (var cppClass in classes)
            {
                if (!string.IsNullOrWhiteSpace(cppClass?.Name))
                {
                    _classNames.Add(cppClass.Name);
                }

                if (cppClass?.Attributes == null)
                {
                    continue;
                }

                foreach (var attr in cppClass.Attributes.Where(x => x.Name == "LUA_USERTYPE_TEMPLATE"))
                {
                    if (string.IsNullOrWhiteSpace(attr.Arguments))
                    {
                        continue;
                    }

                    foreach (var specialization in attr.Arguments.Split(','))
                    {
                        var name = specialization.Trim();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            continue;
                        }

                        _templateSpecializationNames.Add(name);
                        _preservedTypedefs.Add(name);
                    }
                }
            }
        }

        public static IReadOnlyDictionary<string, string> GetTypedefAliases()
        {
            return _typedefAliases;
        }

        public static IReadOnlyCollection<string> GetTypedefNames()
        {
            return _typedefsByName.Keys;
        }

        public static void SetLegacyPreservedTypedefs(IEnumerable<string> names)
        {
            _legacyPreservedTypedefs = names != null
                ? new HashSet<string>(names, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        }

        public static string GetName(this ICppDeclaration input)
        {
            return (input as ICppMember).Name;
        }
        public static bool IsConstructor(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_CTOR");
        }
        public static string GetName(this CppFunction input)
        {
            if (input.IsNormalFunc() && input.Attributes[0].Arguments != null)
            {
                foreach(string arg in input.Attributes[0].Arguments.Split(","))
                {
                    if (arg.Contains("="))
                    {
                        string[] argPair = arg.Split("=");
                        if (argPair.Length == 2 && argPair[0] == "$name")
                        {
                            return argPair[1].Trim();
                        }
                    }
                }
            }
            if (input.IsMetaFunc())
            {
                return ConvertToMetaEnum(input.Attributes[0].Arguments);
            }
            return input.Name;
        }
        public static bool IsNormalFunc(this CppFunction input)
        {
            return input.Attributes.Count > 0 && input.Attributes[0].Name == "LUA_FUNC";
        }
        public static bool IsNormalStaticFunc(this CppFunction input)
        {
            if (input.IsExposedFunc() && input.Attributes[0].Arguments != null)
                return input.Attributes[0].Arguments.Contains("use_static");

            return false;
        }
        public static bool IsNormalGenericFunc(this CppFunction input)
        {
            if (input.IsExposedFunc() && input.Attributes[0].Arguments != null)
                return input.Attributes[0].Arguments.Contains("use_generic");

            return false;
        }
        public static bool IsMetaFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_META_FUNC");
        }
        public static bool IsFowardFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FORWARD_FUNC");
        }
        public static bool IsOverloadFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC_OVERLOAD");
        }
        public static bool IsExposedFunc(this CppFunction input)
        {
            return input.IsNormalFunc() || input.IsOverloadFunc() || input.IsMetaFunc();
        }
        public static bool IsTemplateFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC_TEMPLATE");
        }
        public static bool IsPropertyGetter(this CppFunction input)
        {
            return input.Attributes.Count > 0 && input.Attributes[0].Name == "LUA_PROPERTY_GET";
        }
        public static bool IsPropertySetter(this CppFunction input)
        {
            return input.Attributes.Count > 0 && input.Attributes[0].Name == "LUA_PROPERTY_SET";
        }
        public static bool IsEnum(this CppEnum input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_USERTYPE_ENUM");
        }
        public static bool IsClass(this CppClass input, LuaUserType a)
        {
            return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE") && x.Span.Start.File == a.OriginLocation);
        }
        public static bool IsClassTwo(this CppClass input)
        {
            if (input == null || input.Attributes == null) return false;
            return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE"));
        }
        public static List<CppFunction> GetNormalFunctions(this LuaUserType input)
        {
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsExposedFunc()).ToList();
        }
        public static List<CppFunction> GetMetaFunctions(this LuaUserType input)
        {
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsMetaFunc()).ToList();
        }
        public static List<CppEnum> GetEnums(this LuaUserType input)
        {
            if (input.OriginalElement is CppEnum)
                return new List<CppEnum> { input.OriginalElement as CppEnum };

            var enums = (input.OriginalElement as ICppDeclarationContainer).Enums
                .Where(x => x.IsEnum() && x.SourceFile == input.OriginLocation)
                .ToList();

            // Ensure deterministic enum ordering (prefer source order).
            enums.Sort((a, b) =>
            {
                var fileCmp = string.Compare(a.Span.Start.File, b.Span.Start.File, StringComparison.OrdinalIgnoreCase);
                if (fileCmp != 0) return fileCmp;

                var lineCmp = a.Span.Start.Line.CompareTo(b.Span.Start.Line);
                if (lineCmp != 0) return lineCmp;

                var colCmp = a.Span.Start.Column.CompareTo(b.Span.Start.Column);
                if (colCmp != 0) return colCmp;

                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            return enums;
        }
        public static List<CppClass> GetClasses(this LuaUserType input)
        {
            // There are cases where if you have an enum class they get pulled into a class
            // and enum which we only want them to be an enum 
            if (input.OriginalElement is CppEnum)
                return new List<CppClass>();

            if (input.OriginalElement is CppNamespace)
                return (input.OriginalElement as ICppDeclarationContainer).Classes.Where(x => x.IsClass(input)).ToList();

            return new List<CppClass> { input.OriginalElement as CppClass };
        }
        public static bool IsReadOnly(this CppField input)
        {
            if (input.Attributes == null) return false;

            return input.Attributes.Any(x => x.Name == "LUA_VAR_READONLY");
        }
        public static bool IsNormalVar(this CppField input)
        {
            if (input.Attributes == null) return false;

            return input.Attributes.Any(x => x.Name == "LUA_VAR");
        }
        public static bool IsOptionalVar(this CppField input)
        {
            if (input.Attributes == null) return false;

            return input.Attributes.Any(x => x.Name == "LUA_VAR_OPTIONAL");
        }

        public static CppTypeKind GetFinalTypeKind(this CppType input)
        {
            switch (input.TypeKind)
            {
                case CppTypeKind.Qualified:
                    return (input as CppTypeWithElementType).ElementType.GetFinalTypeKind();
                case CppTypeKind.Typedef:
                    return (input as CppTypedef).ElementType.GetFinalTypeKind();
                case CppTypeKind.Unexposed:
                    if (input is CppUnexposedType ut && IsPrimitiveAlias(ut.Name))
                    {
                        return CppTypeKind.Primitive;
                    }
                    return input.TypeKind;
                case CppTypeKind.Primitive:
                case CppTypeKind.Pointer:
                case CppTypeKind.Reference:
                case CppTypeKind.Array:
                case CppTypeKind.Function:
                case CppTypeKind.Enum:
                case CppTypeKind.TemplateParameterType:
                case CppTypeKind.StructOrClass:
                default:
                    return input.TypeKind;
            }

        }

        private static bool IsPrimitiveAlias(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_primitiveAliases.Contains(name))
            {
                return true;
            }

            switch (name)
            {
                case "int8":
                case "uint8":
                case "int16":
                case "uint16":
                case "int32":
                case "uint32":
                case "int64":
                case "uint64":
                case "intp":
                case "uintp":
                case "int8_t":
                case "uint8_t":
                case "int16_t":
                case "uint16_t":
                case "int32_t":
                case "uint32_t":
                case "int64_t":
                case "uint64_t":
                case "size_t":
                case "uintptr_t":
                case "intptr_t":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ResolveAliasToPrimitive(string name, Dictionary<string, List<CppTypedef>> typedefMap, Dictionary<string, bool> resolved, HashSet<string> visiting)
        {
            if (resolved.TryGetValue(name, out var cached))
            {
                return cached;
            }

            if (!typedefMap.TryGetValue(name, out var typedefs))
            {
                resolved[name] = false;
                return false;
            }

            if (!visiting.Add(name))
            {
                resolved[name] = false;
                return false;
            }

            foreach (var td in typedefs)
            {
                if (IsPrimitiveType(td.ElementType, typedefMap, resolved, visiting))
                {
                    resolved[name] = true;
                    visiting.Remove(name);
                    return true;
                }
            }

            visiting.Remove(name);
            resolved[name] = false;
            return false;
        }

        private static bool IsPrimitiveType(CppType type, Dictionary<string, List<CppTypedef>> typedefMap, Dictionary<string, bool> resolved, HashSet<string> visiting)
        {
            if (type == null)
            {
                return false;
            }

            switch (type.TypeKind)
            {
                case CppTypeKind.Primitive:
                    return true;
                case CppTypeKind.Qualified:
                    return IsPrimitiveType((type as CppTypeWithElementType).ElementType, typedefMap, resolved, visiting);
                case CppTypeKind.Typedef:
                    return ResolveAliasToPrimitive((type as CppTypedef).Name, typedefMap, resolved, visiting);
                case CppTypeKind.Unexposed:
                    {
                        var ut = type as CppUnexposedType;
                        if (ut == null) return false;
                        if (IsPrimitiveAlias(ut.Name)) return true;
                        return ResolveAliasToPrimitive(ut.Name, typedefMap, resolved, visiting);
                    }
                default:
                    return false;
            }
        }

        private static void CollectTypedefs(ICppDeclarationContainer container, List<CppTypedef> typedefs)
        {
            if (container == null)
            {
                return;
            }

            foreach (var td in container.Typedefs)
            {
                typedefs.Add(td);
            }

            foreach (var cls in container.Classes)
            {
                CollectTypedefs(cls, typedefs);
            }

            if (container is ICppGlobalDeclarationContainer global)
            {
                foreach (var ns in global.Namespaces)
                {
                    CollectTypedefs(ns, typedefs);
                }
            }
        }

        private static void CollectEnums(ICppDeclarationContainer container, List<CppEnum> enums)
        {
            if (container == null)
            {
                return;
            }

            foreach (var en in container.Enums)
            {
                enums.Add(en);
            }

            foreach (var cls in container.Classes)
            {
                CollectEnums(cls, enums);
            }

            if (container is ICppGlobalDeclarationContainer global)
            {
                foreach (var ns in global.Namespaces)
                {
                    CollectEnums(ns, enums);
                }
            }
        }

        private static void CollectClasses(ICppDeclarationContainer container, List<CppClass> classes)
        {
            if (container == null)
            {
                return;
            }

            foreach (var cls in container.Classes)
            {
                classes.Add(cls);
                CollectClasses(cls, classes);
            }

            if (container is ICppGlobalDeclarationContainer global)
            {
                foreach (var ns in global.Namespaces)
                {
                    CollectClasses(ns, classes);
                }
            }
        }

        public static string ConvertToSiegeType(this CppType input, string templatedClassName = "")
        {
            var x = input.GetDisplayName();
            if (x.Contains("const const"))
            {
                x = x.Replace("const const", "const");
            }
            if (x.Contains("basic_function")) {
                return x.Replace("basic_function", "sol::function");
            }
            if (x.Contains("basic_protected_function"))
            {
                return x.Replace("basic_protected_function", "sol::protected_function");
            }
            if (x.Contains("variadic_args")) {
                return x.Replace("variadic_args", "sol::variadic_args");
            }
            if (x.Contains("basic_table_core"))
            {
                return x.Replace("basic_table_core", "sol::table");
            }
            if (x.Contains("basic_object"))
            {
                return x.Replace("basic_object", "sol::object");
            }
            if (x.Contains("basic_string")) {
                return x.Replace("basic_string", "String");
            }
            if (x.Contains("std::string"))
            {
                return x.Replace("std::string", "String");
            }
            if (x.Contains("Vec2<T, U>"))
            {
                return x.Replace("Vec2<T, U>", templatedClassName);
            }
            if (x.Contains("Vec2<T,U>"))
            {
                return x.Replace("Vec2<T,U>", templatedClassName);
            }
            if (x.Contains("Rect<T, U>"))
            {
                return x.Replace("Rect<T, U>", templatedClassName);
            }
            if (x.Contains("Rect<T,U>"))
            {
                return x.Replace("Rect<T,U>", templatedClassName);
            }
            if (x.Contains("shared_ptr") && input.TypeKind == CppTypeKind.StructOrClass)
            {
                CppClass inputClass = input as CppClass;
                return $"std::shared_ptr<{inputClass.TemplateParameters[0].ConvertToSiegeType(templatedClassName)}>";
            }

            return input.GetDisplayName();
        }

        public static string GetTypeScriptName(this CppParameter input)
        {
            if (input.Name == "")
            {
                if (input.Type.TypeKind == CppTypeKind.Unexposed)
                {
                    var name = (input.Type as CppUnexposedType).Name;
                    if (name == "T..." || name == "T")
                    {
                        return "...args";
                    }
                }
                else if (input.Type.TypeKind == CppTypeKind.TemplateParameterType)
                {
                    var name = (input.Type as CppTemplateParameterType).Name;
                    if (name == "T")
                    {
                        return "...args";
                    }
                }
            }

            if (input.Name == "args")
            {
                if (input.Type.TypeKind == CppTypeKind.StructOrClass && (input.Type as CppClass).Name == "variadic_args")
                {
                    return "...args";
                }

                if (input.Type.TypeKind == CppTypeKind.Unexposed)
                {
                    var name = (input.Type as CppUnexposedType).Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        if (name.StartsWith("const ", StringComparison.Ordinal))
                        {
                            name = name.Substring("const ".Length).Trim();
                        }

                        var lastScope = name.LastIndexOf("::", StringComparison.Ordinal);
                        if (lastScope >= 0)
                        {
                            name = name.Substring(lastScope + 2);
                        }

                        if (name == "variadic_args")
                        {
                            return "...args";
                        }
                    }
                }
            }
            else
            {
                return input.Name;
            }

            return input.Name;
        }

        private static readonly Dictionary<string, string> TypeMappings = new Dictionary<string, string>
        {
            { "String", "string" },
            { "Unicode", "string" },
            { "Path", "string" },
            { "table", "any" },
            { "object", "any" },
            { "CallbackHandle", "number" },
            { "basic_string", "string" },
            { "basic_object", "any" },
            { "basic_function", "any" },
            { "basic_protected_function", "any"},
            { "function", "any" },
            { "protected_function", "any" },
            { "basic_table_core", "any" },
            { "state_view", "any" },
            { "variadic_args", "any" },
            { "T...", "any" },
            { "T", "any" },
            { "ErrorCode", "number" },
            { "Connection", "number" },
        };

        private static bool IsPreservedTypedef(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            return _preservedTypedefs.Contains(name) || _legacyPreservedTypedefs.Contains(name);
        }

        private static bool TryPreserveTypedef(CppTypedef typedef)
        {
            if (typedef == null || string.IsNullOrWhiteSpace(typedef.Name))
            {
                return false;
            }

            if (_preservedTypedefs.Contains(typedef.Name))
            {
                return true;
            }

            if (_templateSpecializationNames.Contains(typedef.Name))
            {
                _preservedTypedefs.Add(typedef.Name);
                return true;
            }

            if (_legacyPreservedTypedefs.Count > 0 && !_legacyPreservedTypedefs.Contains(typedef.Name))
            {
                return false;
            }

            if (TypeMappings.ContainsKey(typedef.Name))
            {
                return false;
            }

            if (TryMapPrimitiveName(typedef.Name, out _))
            {
                return false;
            }

            if (IsPointerLikeAlias(typedef.ElementType))
            {
                return false;
            }

            if (IsContainerAlias(typedef.ElementType))
            {
                return false;
            }

            _preservedTypedefs.Add(typedef.Name);
            RegisterTypedefAlias(typedef);
            return true;
        }

        private static void RegisterTypedefAlias(CppTypedef typedef)
        {
            if (typedef == null || string.IsNullOrWhiteSpace(typedef.Name))
            {
                return;
            }

            if (_templateSpecializationNames.Contains(typedef.Name))
            {
                return;
            }

            if (_typedefAliases.ContainsKey(typedef.Name))
            {
                return;
            }

            var aliasType = GetTypedefAliasType(typedef);
            if (string.IsNullOrWhiteSpace(aliasType))
            {
                return;
            }

            if (aliasType == typedef.Name)
            {
                return;
            }

            _typedefAliases[typedef.Name] = aliasType;
        }

        private static string GetTypedefAliasType(CppTypedef typedef)
        {
            if (typedef?.ElementType == null)
            {
                return null;
            }

            var wasPreserved = _preservedTypedefs.Remove(typedef.Name);
            var aliasType = typedef.ElementType.ConvertToTypeScriptType(TypeScriptSourceType.Field, typedef);
            if (wasPreserved)
            {
                _preservedTypedefs.Add(typedef.Name);
            }

            if (string.IsNullOrWhiteSpace(aliasType))
            {
                return null;
            }

            return aliasType;
        }

        private static bool IsPointerLikeAlias(CppType type)
        {
            if (type == null)
            {
                return false;
            }

            while (type.TypeKind == CppTypeKind.Qualified)
            {
                type = (type as CppTypeWithElementType)?.ElementType;
                if (type == null)
                {
                    return false;
                }
            }

            if (type.TypeKind == CppTypeKind.Pointer || type.TypeKind == CppTypeKind.Reference)
            {
                return true;
            }

            if (type.TypeKind == CppTypeKind.StructOrClass)
            {
                var name = (type as CppClass)?.Name;
                return name == "shared_ptr"
                    || name == "unique_ptr"
                    || name == "weak_ptr"
                    || name == "optional";
            }

            if (type.TypeKind == CppTypeKind.Unexposed)
            {
                var utName = (type as CppUnexposedType)?.Name;
                var normalized = NormalizeTemplateTypeName(utName);
                var ltIndex = normalized.IndexOf('<');
                var baseName = ltIndex >= 0 ? normalized.Substring(0, ltIndex) : normalized;

                return baseName == "shared_ptr"
                    || baseName == "unique_ptr"
                    || baseName == "weak_ptr"
                    || baseName == "optional";
            }

            return false;
        }

        private static bool IsContainerAlias(CppType type)
        {
            if (type == null)
            {
                return false;
            }

            while (type.TypeKind == CppTypeKind.Qualified)
            {
                type = (type as CppTypeWithElementType)?.ElementType;
                if (type == null)
                {
                    return false;
                }
            }

            if (type.TypeKind == CppTypeKind.Typedef)
            {
                return IsContainerAlias((type as CppTypedef)?.ElementType);
            }

            if (type.TypeKind == CppTypeKind.StructOrClass)
            {
                var name = (type as CppClass)?.Name;
                return name == "vector" || name == "array";
            }

            if (type.TypeKind == CppTypeKind.Unexposed)
            {
                var utName = (type as CppUnexposedType)?.Name;
                var normalized = NormalizeTemplateTypeName(utName);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return false;
                }

                if (normalized.StartsWith("vector<", StringComparison.Ordinal))
                {
                    return true;
                }

                if (normalized.StartsWith("array<", StringComparison.Ordinal))
                {
                    return true;
                }

                return normalized == "vector" || normalized == "array";
            }

            return false;
        }

        public enum TypeScriptSourceType {
            Parameter,
            Return,
            Field,
        }


        public static string ConvertToTypeScriptType(this CppType input, TypeScriptSourceType source, CppTypedef specialization = null)
        {
            if (source == TypeScriptSourceType.Parameter && IsHashedStringType(input))
            {
                return "HashedString | string";
            }

            switch (input.TypeKind)
            {
                case CppTypeKind.Primitive:
                    var primitive = input as CppPrimitiveType;
                    switch (primitive.Kind)
                    {
                        case CppPrimitiveKind.Void:
                            return String.Empty;
                        case CppPrimitiveKind.Bool:
                            return "boolean";
                        case CppPrimitiveKind.WChar:
                        case CppPrimitiveKind.Char:
                            return "string";
                        case CppPrimitiveKind.Short:
                        case CppPrimitiveKind.Int:
                        case CppPrimitiveKind.LongLong:
                        case CppPrimitiveKind.UnsignedChar:
                        case CppPrimitiveKind.UnsignedShort:
                        case CppPrimitiveKind.UnsignedInt:
                        case CppPrimitiveKind.UnsignedLongLong:
                        case CppPrimitiveKind.Float:
                        case CppPrimitiveKind.Double:
                        case CppPrimitiveKind.LongDouble:
                        default:
                            return "number";
                    }
                case CppTypeKind.Pointer:
                case CppTypeKind.Reference:
                case CppTypeKind.Qualified:
                    return (input as CppTypeWithElementType).ElementType.ConvertToTypeScriptType(source, specialization);
                case CppTypeKind.Array:
                    break;
                case CppTypeKind.Function:
                    break;
                case CppTypeKind.Typedef:
                    var typedef = input as CppTypedef;

                    if (source == TypeScriptSourceType.Parameter && IsHashedStringType(typedef.ElementType))
                    {
                        return "HashedString | string";
                    }

                    if (IsEventAlias(typedef))
                    {
                        return typedef.Name;
                    }

                    if (IsFunctionAlias(typedef))
                    {
                        return typedef.Name;
                    }

                    if (IsEnumType(typedef.ElementType))
                    {
                        return $"{GetEnumDisplayName(typedef.ElementType)} | number";
                    }

                    if (TypeMappings.ContainsKey(typedef.Name))
                    {
                        return TypeMappings[typedef.Name];
                    }

                    if (TryMapPrimitiveName(typedef.Name, out var mappedTypedef))
                    {
                        return mappedTypedef;
                    }

                    if (TryPreserveTypedef(typedef))
                    {
                        return typedef.Name;
                    }
                    else if (typedef.ElementType.TypeKind == CppTypeKind.Primitive 
                        || typedef.ElementType.TypeKind == CppTypeKind.Typedef)
                    {
                        return typedef.ElementType.ConvertToTypeScriptType(source, specialization);
                    }
                    else if (typedef.ElementType is CppUnexposedType utPrimitive && IsPrimitiveAlias(utPrimitive.Name))
                    {
                        return "number";
                    }
                    else if (typedef.ElementType.TypeKind == CppTypeKind.StructOrClass && (typedef.ElementType as CppClass).TemplateParameters.Count > 0)
                    {
                        var templatedClass = typedef.ElementType as CppClass;
                        if (templatedClass.Name == "vector" || templatedClass.Name == "array")
                        {
                            string templatedType = ConvertContainerElementType(templatedClass.TemplateParameters[0], source, specialization);
                            if (source == TypeScriptSourceType.Parameter)
                                return $"ArrayList<{templatedType}> | Array<{templatedType}>";
                            else
                                return $"ArrayList<{templatedType}>";
                        }
                        else if (templatedClass.Name == "shared_ptr")
                        {
                            return templatedClass.TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
                        }
                    }
                    return typedef.Name;
                case CppTypeKind.StructOrClass:
                    {
                        string name = (input as CppClass).Name;
                        if (ShouldUseSpecializationName(name, (input as CppClass).TemplateParameters, specialization))
                        {
                            return specialization.Name;
                        }
                        if (TypeMappings.ContainsKey(name))
                        {
                            return TypeMappings[name];
                        }
                        else if (name == "Vec2" && ((input as CppClass).TemplateParameters[0] as CppPrimitiveType).Kind == CppPrimitiveKind.Int)
                        {
                            return "PixelVector";
                        }
                        else if (name == "Vec2" && ((input as CppClass).TemplateParameters[0] as CppPrimitiveType).Kind == CppPrimitiveKind.Float)
                        {
                            return "Vector";
                        }
                        else if (name == "Rectangle" && ((input as CppClass).TemplateParameters[0] as CppPrimitiveType).Kind == CppPrimitiveKind.Int)
                        {
                            return "PixelRect";
                        }
                        else if (name == "Rectangle" && ((input as CppClass).TemplateParameters[0] as CppPrimitiveType).Kind == CppPrimitiveKind.Float)
                        {
                            return "Rect";
                        }
                        else if (name == "Range")
                        {
                            var argType = (input as CppClass).TemplateParameters.Count > 0
                                ? (input as CppClass).TemplateParameters[0]
                                : null;

                            if (argType is CppPrimitiveType primitiveArg)
                            {
                                if (primitiveArg.Kind == CppPrimitiveKind.Float
                                    || primitiveArg.Kind == CppPrimitiveKind.Double
                                    || primitiveArg.Kind == CppPrimitiveKind.LongDouble)
                                {
                                    return "FloatRange";
                                }

                                if (primitiveArg.Kind == CppPrimitiveKind.Short
                                    || primitiveArg.Kind == CppPrimitiveKind.Int
                                    || primitiveArg.Kind == CppPrimitiveKind.LongLong
                                    || primitiveArg.Kind == CppPrimitiveKind.UnsignedChar
                                    || primitiveArg.Kind == CppPrimitiveKind.UnsignedShort
                                    || primitiveArg.Kind == CppPrimitiveKind.UnsignedInt
                                    || primitiveArg.Kind == CppPrimitiveKind.UnsignedLongLong)
                                {
                                    return "IntRange";
                                }
                            }
                            else if (argType is CppTypedef typedefArg)
                            {
                                var typedefName = NormalizeTemplateArgument(typedefArg.Name);
                                if (IsFloatType(typedefName))
                                {
                                    return "FloatRange";
                                }
                                if (IsIntegerType(typedefName))
                                {
                                    return "IntRange";
                                }

                                if (typedefArg.ElementType is CppPrimitiveType typedefPrimitive)
                                {
                                    if (typedefPrimitive.Kind == CppPrimitiveKind.Float
                                        || typedefPrimitive.Kind == CppPrimitiveKind.Double
                                        || typedefPrimitive.Kind == CppPrimitiveKind.LongDouble)
                                    {
                                        return "FloatRange";
                                    }

                                    if (typedefPrimitive.Kind == CppPrimitiveKind.Short
                                        || typedefPrimitive.Kind == CppPrimitiveKind.Int
                                        || typedefPrimitive.Kind == CppPrimitiveKind.LongLong
                                        || typedefPrimitive.Kind == CppPrimitiveKind.UnsignedChar
                                        || typedefPrimitive.Kind == CppPrimitiveKind.UnsignedShort
                                        || typedefPrimitive.Kind == CppPrimitiveKind.UnsignedInt
                                        || typedefPrimitive.Kind == CppPrimitiveKind.UnsignedLongLong)
                                    {
                                        return "IntRange";
                                    }
                                }
                                else if (typedefArg.ElementType is CppUnexposedType typedefUnexposed)
                                {
                                    var unexposedName = NormalizeTemplateArgument(typedefUnexposed.Name);
                                    if (IsFloatType(unexposedName))
                                    {
                                        return "FloatRange";
                                    }
                                    if (IsIntegerType(unexposedName))
                                    {
                                        return "IntRange";
                                    }
                                }
                            }
                            else if (argType is CppUnexposedType unexposedArg)
                            {
                                var unexposedName = NormalizeTemplateArgument(unexposedArg.Name);
                                if (IsFloatType(unexposedName))
                                {
                                    return "FloatRange";
                                }
                                if (IsIntegerType(unexposedName))
                                {
                                    return "IntRange";
                                }
                            }
                        }
                        else if (name == "vector" || name == "array")
                        {
                            string templatedType = ConvertContainerElementType((input as CppClass).TemplateParameters[0], source, specialization);
                            if (source == TypeScriptSourceType.Parameter)
                                return $"ArrayList<{templatedType}> | Array<{templatedType}>";
                            else
                                return $"ArrayList<{templatedType}>";
                        }
                        else if (name == "shared_ptr")
                        {
                            return (input as CppClass).TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
                        }
                        else if (name == "optional")
                        {
                            return (input as CppClass).TemplateParameters[0].ConvertToTypeScriptType(source, specialization) + " | undefined";
                        }
                        return name;
                    }
                case CppTypeKind.Enum:
                    return (input as CppEnum).Name + " | number";
                case CppTypeKind.TemplateParameterType:
                    return (input as CppTemplateParameterType).Name;
                case CppTypeKind.Unexposed:
                    {
                        string name = (input as CppUnexposedType).Name;
                        if (name.StartsWith("const "))
                        {
                            name = name.Substring("const ".Length);
                        }
                        else if (name.StartsWith("optional"))
                        {
                            name = ((input as CppUnexposedType).TemplateParameters[0] as CppUnexposedType).Name;
                        }
                        else if (NormalizeTemplateTypeName(name).StartsWith("vector<") || NormalizeTemplateTypeName(name).StartsWith("array<"))
                        {
                            string templatedType = (input as CppUnexposedType).TemplateParameters.Count > 0
                                ? ConvertContainerElementType((input as CppUnexposedType).TemplateParameters[0], source, specialization)
                                : ConvertContainerTemplateArgument(ExtractFirstTemplateArgument(name), source, specialization);
                            if (source == TypeScriptSourceType.Parameter)
                                return $"ArrayList<{templatedType}> | Array<{templatedType}>";
                            else
                                return $"ArrayList<{templatedType}>";
                        }

                        var normalizedName = NormalizeTemplateTypeName(name);
                        var ltIndex = normalizedName.IndexOf('<');
                        var baseName = ltIndex >= 0 ? normalizedName.Substring(0, ltIndex) : normalizedName;
                        var templateArgs = ltIndex >= 0 ? ExtractTemplateArguments(normalizedName) : null;

                        if (ShouldUseSpecializationName(baseName, templateArgs, specialization))
                        {
                            return specialization.Name;
                        }

                        if (baseName == "shared_ptr" || baseName == "unique_ptr")
                        {
                            if (templateArgs != null && templateArgs.Count > 0)
                            {
                                return ConvertTemplateArgumentType(templateArgs[0], source, specialization);
                            }
                        }

                        if (baseName == "optional")
                        {
                            if (templateArgs != null && templateArgs.Count > 0)
                            {
                                var innerType = ConvertTemplateArgumentType(templateArgs[0], source, specialization);
                                if (innerType == "Rect" || innerType == "PixelRect" || innerType == "TileRect")
                                {
                                    return innerType;
                                }
                                return innerType + " | undefined";
                            }
                        }

                        if (baseName == "Vec2" || baseName == "Rectangle")
                        {
                            var arg = templateArgs != null && templateArgs.Count > 0
                                ? NormalizeTemplateArgument(templateArgs[0])
                                : NormalizeTemplateArgument(ExtractFirstTemplateArgument(normalizedName));
                            var unitArg = templateArgs != null && templateArgs.Count > 1
                                ? NormalizeTemplateArgument(templateArgs[1])
                                : string.Empty;

                            var specializationArgs = GetSpecializationTemplateArgs(specialization);
                            if (IsTemplateParameterIdentifier(arg) && specializationArgs != null && specializationArgs.Count > 0)
                            {
                                arg = NormalizeTemplateArgument(specializationArgs[0]);
                            }

                            if (IsTemplateParameterIdentifier(unitArg) && specializationArgs != null && specializationArgs.Count > 1)
                            {
                                unitArg = NormalizeTemplateArgument(specializationArgs[1]);
                            }

                            if (IsFloatType(arg))
                            {
                                return baseName == "Vec2" ? "Vector" : "Rect";
                            }

                            if (IsIntegerType(arg))
                            {
                                if (unitArg == "TILE")
                                {
                                    return baseName == "Vec2" ? "TileVector" : "TileRect";
                                }
                                if (unitArg == "PIXEL")
                                {
                                    return baseName == "Vec2" ? "PixelVector" : "PixelRect";
                                }
                                return baseName == "Vec2" ? "IntVector" : "PixelRect";
                            }
                        }

                        if (baseName == "Range")
                        {
                            var arg = templateArgs != null && templateArgs.Count > 0
                                ? NormalizeTemplateArgument(templateArgs[0])
                                : NormalizeTemplateArgument(ExtractFirstTemplateArgument(normalizedName));

                            if (IsFloatType(arg))
                            {
                                return "FloatRange";
                            }

                            if (IsIntegerType(arg))
                            {
                                return "IntRange";
                            }
                        }

                        if (ltIndex < 0)
                        {
                            int index = name.LastIndexOf("::");
                            if (index != -1)
                                name = name.Substring(index + 2);
                        }
                        else
                        {
                            name = baseName;
                        }
                        name = name.Replace("&&", "");

                        if (!IsPreservedTypedef(name) && name.EndsWith("Ptr", StringComparison.Ordinal))
                        {
                            var basePtr = name.Substring(0, name.Length - 3);
                            if (_classNames.Contains(basePtr))
                            {
                                return basePtr;
                            }
                        }

                        if (!IsPreservedTypedef(name)
                            && (name.EndsWith("ID", StringComparison.Ordinal)
                                || (name.EndsWith("Handle", StringComparison.Ordinal) && !_classNames.Contains(name))))
                        {
                            return "number";
                        }

                        if (IsKnownEnumName(name))
                        {
                            return name + " | number";
                        }

                        if (IsPreservedTypedef(name))
                        {
                            return name;
                        }

                        if (TryMapPrimitiveName(name, out var mappedPrimitive))
                        {
                            return mappedPrimitive;
                        }

                        if (TypeMappings.ContainsKey(name))
                        {
                            return TypeMappings[name];
                        }
                        else if (TryResolveTypedef(name, out var resolvedTypedef))
                        {
                            if (IsEventAlias(resolvedTypedef))
                            {
                                return resolvedTypedef.Name;
                            }
                            if (IsFunctionAlias(resolvedTypedef))
                            {
                                return resolvedTypedef.Name;
                            }
                            if (TryPreserveTypedef(resolvedTypedef))
                            {
                                return resolvedTypedef.Name;
                            }
                            if (ReferenceEquals(resolvedTypedef.ElementType, input))
                            {
                                return name;
                            }
                            return resolvedTypedef.ElementType.ConvertToTypeScriptType(source, resolvedTypedef);
                        }
                        else if (IsPrimitiveAlias(name))
                        {
                            return "number";
                        }
                        else if (specialization != null && specialization.ElementType.TypeKind == CppTypeKind.StructOrClass)
                        {
                            var specializationClass = (specialization.ElementType as CppClass);
                            Dictionary<string, string> templateParameters = specializationClass.SpecializedTemplate.TemplateParameters
                                .Select(parameter => parameter.ConvertToTypeScriptType(source, specialization))
                                .Zip(specializationClass.TemplateParameters.Select(parameter => parameter.ConvertToTypeScriptType(source, specialization)))
                                .ToDictionary(x => x.First, x => x.Second);

                            if (name.StartsWith(specializationClass.Name))
                            {
                                return specialization.Name;
                            }
                            else if (specializationClass.Name == "Rectangle" && name.Contains("Vec2"))
                            {
                                return specialization.Name.Replace("Rect", "Vector");
                            }
                            return templateParameters[name];
                        }
                        return name;
                    }
                default:
                    return String.Empty;
            }
            return String.Empty;
        }

        private static bool IsHashedStringType(CppType input)
        {
            if (input == null)
            {
                return false;
            }

            while (input is CppTypeWithElementType elementType
                && (input.TypeKind == CppTypeKind.Pointer
                    || input.TypeKind == CppTypeKind.Reference
                    || input.TypeKind == CppTypeKind.Qualified))
            {
                input = elementType.ElementType;
            }

            switch (input.TypeKind)
            {
                case CppTypeKind.Typedef:
                    {
                        var typedef = input as CppTypedef;
                        if (typedef.Name == "HashedString")
                        {
                            return true;
                        }
                        return IsHashedStringType(typedef.ElementType);
                    }
                case CppTypeKind.StructOrClass:
                    return (input as CppClass).Name == "HashedString";
                case CppTypeKind.Unexposed:
                    {
                        string name = (input as CppUnexposedType).Name;
                        if (name.StartsWith("const ", StringComparison.Ordinal))
                        {
                            name = name.Substring("const ".Length).Trim();
                        }

                        int lastScope = name.LastIndexOf("::", StringComparison.Ordinal);
                        if (lastScope >= 0)
                        {
                            name = name.Substring(lastScope + 2);
                        }

                        name = name.Replace("&&", string.Empty)
                                   .Replace("&", string.Empty)
                                   .Replace("*", string.Empty)
                                   .Trim();
                        return name == "HashedString";
                    }
                default:
                    return false;
            }
        }

        private static bool IsEnumType(CppType input)
        {
            if (input == null)
            {
                return false;
            }

            if (input.TypeKind == CppTypeKind.Enum)
            {
                return true;
            }

            if (input is CppTypeWithElementType elementType
                && (input.TypeKind == CppTypeKind.Pointer
                    || input.TypeKind == CppTypeKind.Reference
                    || input.TypeKind == CppTypeKind.Qualified))
            {
                return IsEnumType(elementType.ElementType);
            }

            if (input.TypeKind == CppTypeKind.Unexposed)
            {
                return IsKnownEnumName((input as CppUnexposedType).Name);
            }

            if (input.TypeKind == CppTypeKind.Typedef)
            {
                return IsEnumType((input as CppTypedef).ElementType);
            }

            return false;
        }

        private static string GetEnumDisplayName(CppType input)
        {
            if (input == null)
            {
                return string.Empty;
            }

            if (input.TypeKind == CppTypeKind.Enum)
            {
                return (input as CppEnum).Name;
            }

            if (input is CppTypeWithElementType elementType
                && (input.TypeKind == CppTypeKind.Pointer
                    || input.TypeKind == CppTypeKind.Reference
                    || input.TypeKind == CppTypeKind.Qualified))
            {
                return GetEnumDisplayName(elementType.ElementType);
            }

            if (input.TypeKind == CppTypeKind.Unexposed)
            {
                var name = (input as CppUnexposedType).Name;
                if (name.StartsWith("const ", StringComparison.Ordinal))
                {
                    name = name.Substring("const ".Length).Trim();
                }

                var lastScope = name.LastIndexOf("::", StringComparison.Ordinal);
                if (lastScope >= 0)
                {
                    name = name.Substring(lastScope + 2);
                }

                name = name.Replace("&&", string.Empty)
                           .Replace("&", string.Empty)
                           .Replace("*", string.Empty)
                           .Trim();
                return name;
            }

            if (input.TypeKind == CppTypeKind.Typedef)
            {
                return GetEnumDisplayName((input as CppTypedef).ElementType);
            }

            return string.Empty;
        }

        private static bool IsKnownEnumName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var trimmed = name.Trim();
            if (_enumNames.Contains(trimmed))
            {
                return true;
            }

            var lastScope = trimmed.LastIndexOf("::", StringComparison.Ordinal);
            if (lastScope >= 0)
            {
                return _enumNames.Contains(trimmed.Substring(lastScope + 2));
            }

            return false;
        }

        private static bool IsEventAlias(CppTypedef typedef)
        {
            if (typedef == null || typedef.ElementType == null)
            {
                return false;
            }

            return IsEventType(typedef.ElementType);
        }

        private static bool IsEventType(CppType type)
        {
            if (type == null)
            {
                return false;
            }

            while (type is CppTypeWithElementType elementType
                && (type.TypeKind == CppTypeKind.Qualified
                    || type.TypeKind == CppTypeKind.Pointer
                    || type.TypeKind == CppTypeKind.Reference))
            {
                type = elementType.ElementType;
            }

            switch (type.TypeKind)
            {
                case CppTypeKind.StructOrClass:
                    return (type as CppClass)?.Name == "Event";
                case CppTypeKind.Unexposed:
                    {
                        var ut = type as CppUnexposedType;
                        if (ut == null) return false;
                        var normalized = NormalizeTemplateTypeName(ut.Name);
                        if (normalized.StartsWith("Event<", StringComparison.Ordinal))
                        {
                            return true;
                        }

                        return normalized == "Event";
                    }
                case CppTypeKind.Typedef:
                    return IsEventAlias(type as CppTypedef);
                default:
                    return false;
            }
        }

        private static bool IsFunctionAlias(CppTypedef typedef)
        {
            if (typedef == null || typedef.ElementType == null)
            {
                return false;
            }

            return IsFunctionType(typedef.ElementType);
        }

        private static bool IsFunctionType(CppType type)
        {
            if (type == null)
            {
                return false;
            }

            while (type is CppTypeWithElementType elementType
                && (type.TypeKind == CppTypeKind.Qualified
                    || type.TypeKind == CppTypeKind.Pointer
                    || type.TypeKind == CppTypeKind.Reference))
            {
                type = elementType.ElementType;
            }

            switch (type.TypeKind)
            {
                case CppTypeKind.Unexposed:
                    {
                        var ut = type as CppUnexposedType;
                        if (ut == null) return false;
                        var normalized = NormalizeTemplateTypeName(ut.Name);
                        var ltIndex = normalized.IndexOf('<');
                        var baseName = ltIndex >= 0 ? normalized.Substring(0, ltIndex) : normalized;

                        return baseName == "function"
                            || baseName == "basic_function"
                            || baseName == "protected_function"
                            || baseName == "basic_protected_function";
                    }
                case CppTypeKind.StructOrClass:
                    {
                        var cls = type as CppClass;
                        return cls != null && (cls.Name == "function" || cls.Name == "basic_function" || cls.Name == "protected_function" || cls.Name == "basic_protected_function");
                    }
                case CppTypeKind.Typedef:
                    return IsFunctionAlias(type as CppTypedef);
                default:
                    return false;
            }
        }

        private static bool TryResolveTypedef(string name, out CppTypedef typedef)
        {
            typedef = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_typedefsByName.TryGetValue(name, out typedef) && typedef != null)
            {
                return true;
            }

            var lastScope = name.LastIndexOf("::", StringComparison.Ordinal);
            if (lastScope >= 0)
            {
                var shortName = name.Substring(lastScope + 2);
                if (_typedefsByName.TryGetValue(shortName, out typedef) && typedef != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeTemplateTypeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var lt = name.IndexOf('<');
            if (lt < 0)
            {
                return name;
            }

            var lastScope = name.LastIndexOf("::", lt, StringComparison.Ordinal);
            if (lastScope >= 0)
            {
                return name.Substring(lastScope + 2);
            }

            return name;
        }

        private static bool ShouldUseSpecializationName(string baseName, IReadOnlyList<CppType> templateParameters, CppTypedef specialization)
        {
            var specializationBaseName = GetSpecializationBaseName(specialization);
            if (string.IsNullOrWhiteSpace(specializationBaseName) || string.IsNullOrWhiteSpace(baseName))
            {
                return false;
            }

            if (!string.Equals(baseName, specializationBaseName, StringComparison.Ordinal))
            {
                return false;
            }

            if (templateParameters == null || templateParameters.Count == 0)
            {
                return true;
            }

            foreach (var param in templateParameters)
            {
                if (!(param is CppTemplateParameterType))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ShouldUseSpecializationName(string baseName, IReadOnlyList<string> templateArgs, CppTypedef specialization)
        {
            var specializationBaseName = GetSpecializationBaseName(specialization);
            if (string.IsNullOrWhiteSpace(specializationBaseName) || string.IsNullOrWhiteSpace(baseName))
            {
                return false;
            }

            if (!string.Equals(baseName, specializationBaseName, StringComparison.Ordinal))
            {
                return false;
            }

            if (templateArgs == null || templateArgs.Count == 0)
            {
                return true;
            }

            foreach (var arg in templateArgs)
            {
                var normalized = NormalizeTemplateArgument(arg);
                if (!IsTemplateParameterIdentifier(normalized))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsTemplateParameterIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (value.EndsWith("...", StringComparison.Ordinal))
            {
                value = value.Substring(0, value.Length - 3);
            }

            if (value.Length == 1 && char.IsUpper(value[0]))
            {
                return true;
            }

            if (value.Length == 2 && char.IsUpper(value[0]) && char.IsDigit(value[1]))
            {
                return true;
            }

            return false;
        }

        private static string GetSpecializationBaseName(CppTypedef specialization)
        {
            if (specialization == null)
            {
                return null;
            }

            if (specialization.ElementType is CppClass specializationClass)
            {
                return specializationClass.Name;
            }

            if (specialization.ElementType is CppUnexposedType unexposed)
            {
                var normalized = NormalizeTemplateTypeName(unexposed.Name);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    return null;
                }

                var ltIndex = normalized.IndexOf('<');
                return ltIndex >= 0 ? normalized.Substring(0, ltIndex) : normalized;
            }

            return null;
        }

        private static List<string> GetSpecializationTemplateArgs(CppTypedef specialization)
        {
            if (specialization == null)
            {
                return null;
            }

            if (specialization.ElementType is CppClass specializationClass && specializationClass.TemplateParameters.Count > 0)
            {
                return specializationClass.TemplateParameters
                    .Select(param => ConvertTemplateParamToName(param))
                    .Where(param => !string.IsNullOrWhiteSpace(param))
                    .ToList();
            }

            if (specialization.ElementType is CppUnexposedType unexposed)
            {
                var normalized = NormalizeTemplateTypeName(unexposed.Name);
                var ltIndex = normalized.IndexOf('<');
                if (ltIndex >= 0)
                {
                    return ExtractTemplateArguments(normalized);
                }
            }

            return null;
        }

        private static string ConvertTemplateParamToName(CppType param)
        {
            if (param == null)
            {
                return null;
            }

            switch (param.TypeKind)
            {
                case CppTypeKind.Primitive:
                    return ConvertPrimitiveKindToName((param as CppPrimitiveType)?.Kind ?? CppPrimitiveKind.Void);
                case CppTypeKind.Typedef:
                    return NormalizeTemplateArgument((param as CppTypedef)?.Name);
                case CppTypeKind.Unexposed:
                    return NormalizeTemplateArgument((param as CppUnexposedType)?.Name);
                case CppTypeKind.TemplateParameterType:
                    return (param as CppTemplateParameterType)?.Name;
                case CppTypeKind.Qualified:
                    return ConvertTemplateParamToName((param as CppTypeWithElementType)?.ElementType);
                default:
                    return NormalizeTemplateArgument(param.GetDisplayName());
            }
        }

        private static string ConvertPrimitiveKindToName(CppPrimitiveKind kind)
        {
            switch (kind)
            {
                case CppPrimitiveKind.Float:
                    return "float";
                case CppPrimitiveKind.Double:
                    return "double";
                case CppPrimitiveKind.LongDouble:
                    return "long double";
                case CppPrimitiveKind.Short:
                    return "short";
                case CppPrimitiveKind.Int:
                    return "int";
                case CppPrimitiveKind.LongLong:
                    return "long long";
                case CppPrimitiveKind.UnsignedChar:
                    return "uint8_t";
                case CppPrimitiveKind.UnsignedShort:
                    return "uint16_t";
                case CppPrimitiveKind.UnsignedInt:
                    return "uint32_t";
                case CppPrimitiveKind.UnsignedLongLong:
                    return "uint64_t";
                case CppPrimitiveKind.Char:
                case CppPrimitiveKind.WChar:
                    return "char";
                case CppPrimitiveKind.Bool:
                    return "bool";
                default:
                    return "int";
            }
        }

        private static string ConvertUnexposedTemplateArgument(string name, TypeScriptSourceType source, CppTypedef specialization)
        {
            var arg = ExtractFirstTemplateArgument(name);
            if (string.IsNullOrWhiteSpace(arg))
            {
                return "any";
            }

            return ConvertTemplateArgumentType(arg, source, specialization);
        }

        private static string ExtractFirstTemplateArgument(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var lt = name.IndexOf('<');
            var gt = name.LastIndexOf('>');
            if (lt < 0 || gt <= lt)
            {
                return string.Empty;
            }

            var args = name.Substring(lt + 1, gt - lt - 1);
            int depth = 0;
            for (int i = 0; i < args.Length; i++)
            {
                var ch = args[i];
                if (ch == '<') depth++;
                else if (ch == '>') depth--;
                else if (ch == ',' && depth == 0)
                {
                    return args.Substring(0, i).Trim();
                }
            }

            return args.Trim();
        }

        private static List<string> ExtractTemplateArguments(string name)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return results;
            }

            var lt = name.IndexOf('<');
            var gt = name.LastIndexOf('>');
            if (lt < 0 || gt <= lt)
            {
                return results;
            }

            var args = name.Substring(lt + 1, gt - lt - 1);
            int depth = 0;
            int start = 0;
            for (int i = 0; i < args.Length; i++)
            {
                var ch = args[i];
                if (ch == '<') depth++;
                else if (ch == '>') depth--;
                else if (ch == ',' && depth == 0)
                {
                    results.Add(args.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }

            if (start < args.Length)
            {
                results.Add(args.Substring(start).Trim());
            }

            return results;
        }

        private static string StripTemplateParamPrefix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("class ", StringComparison.Ordinal))
            {
                return trimmed.Substring(6).Trim();
            }
            if (trimmed.StartsWith("struct ", StringComparison.Ordinal))
            {
                return trimmed.Substring(7).Trim();
            }
            if (trimmed.StartsWith("typename ", StringComparison.Ordinal))
            {
                return trimmed.Substring(9).Trim();
            }

            return trimmed;
        }

        private static string NormalizeTemplateArgument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var trimmed = StripTemplateParamPrefix(value);
            if (trimmed.StartsWith("const ", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring("const ".Length).Trim();
            }

            trimmed = trimmed.Trim('&', '*', ' ');

            var lastScope = trimmed.LastIndexOf("::", StringComparison.Ordinal);
            if (lastScope >= 0)
            {
                trimmed = trimmed.Substring(lastScope + 2);
            }

            return trimmed;
        }

        private static string ConvertTemplateArgumentType(string value, TypeScriptSourceType source, CppTypedef specialization)
        {
            var normalized = NormalizeTemplateArgument(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "any";
            }

            if (TryMapPrimitiveName(normalized, out var primitiveMapped))
            {
                return primitiveMapped;
            }

            if ((normalized.EndsWith("ID", StringComparison.Ordinal)
                || (normalized.EndsWith("Handle", StringComparison.Ordinal) && !_classNames.Contains(normalized)))
                && !IsPreservedTypedef(normalized))
            {
                return "number";
            }

            if (normalized == "TileVector")
            {
                return "PixelVector";
            }

            if (normalized == "TileRect")
            {
                return "PixelRect";
            }

            if (normalized == "EntityID")
            {
                return "number";
            }

            if (TypeMappings.ContainsKey(normalized))
            {
                return TypeMappings[normalized];
            }

            if (IsPreservedTypedef(normalized))
            {
                return normalized;
            }

            if (IsKnownEnumName(normalized))
            {
                return normalized + " | number";
            }

            if (TryResolveTypedef(normalized, out var resolvedTypedef))
            {
                if (!ReferenceEquals(resolvedTypedef.ElementType, null))
                {
                    if (IsEventAlias(resolvedTypedef))
                    {
                        return resolvedTypedef.Name;
                    }
                    if (IsFunctionAlias(resolvedTypedef))
                    {
                        return resolvedTypedef.Name;
                    }
                    return resolvedTypedef.ElementType.ConvertToTypeScriptType(source, resolvedTypedef);
                }
            }

            if (IsPrimitiveAlias(normalized))
            {
                return "number";
            }

            var tmp = new CppUnexposedType(normalized);
            return tmp.ConvertToTypeScriptType(source, specialization);
        }

        private static string ConvertContainerElementType(CppType elementType, TypeScriptSourceType source, CppTypedef specialization)
        {
            if (elementType == null)
            {
                return "any";
            }

            var normalized = GetNormalizedTypeName(elementType);
            if (ShouldMapIdToNumberForContainer(normalized))
            {
                return "number";
            }

            return elementType.ConvertToTypeScriptType(source, specialization);
        }

        private static string ConvertContainerTemplateArgument(string argument, TypeScriptSourceType source, CppTypedef specialization)
        {
            var normalized = NormalizeTemplateArgument(argument);
            if (ShouldMapIdToNumberForContainer(normalized))
            {
                return "number";
            }

            return ConvertTemplateArgumentType(argument, source, specialization);
        }

        private static bool ShouldMapIdToNumberForContainer(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (_classNames.Contains(name))
            {
                return false;
            }

            return name.EndsWith("ID", StringComparison.Ordinal)
                || name.EndsWith("Handle", StringComparison.Ordinal);
        }

        private static string GetNormalizedTypeName(CppType type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            while (type is CppTypeWithElementType elementType
                && (type.TypeKind == CppTypeKind.Pointer
                    || type.TypeKind == CppTypeKind.Reference
                    || type.TypeKind == CppTypeKind.Qualified))
            {
                type = elementType.ElementType;
            }

            string name = null;
            if (type is CppTypedef td)
            {
                name = td.Name;
            }
            else if (type is CppClass cls)
            {
                name = cls.Name;
            }
            else if (type is CppEnum en)
            {
                name = en.Name;
            }
            else if (type is CppUnexposedType ut)
            {
                name = ut.Name;
            }
            else if (type is CppTemplateParameterType tp)
            {
                name = tp.Name;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return string.Empty;
            }

            var normalized = NormalizeTemplateTypeName(name);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            var ltIndex = normalized.IndexOf('<');
            if (ltIndex >= 0)
            {
                normalized = normalized.Substring(0, ltIndex);
            }

            return normalized.Trim();
        }

        private static bool TryMapPrimitiveName(string value, out string mapped)
        {
            mapped = null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value)
            {
                case "bool":
                    mapped = "boolean";
                    return true;
                case "char":
                case "wchar":
                case "wchar_t":
                    mapped = "string";
                    return true;
                case "void":
                    mapped = "any";
                    return true;
            }

            if (IsFloatType(value) || IsIntegerType(value) || value == "size_t" || value == "time_t" || value == "uintmax_t")
            {
                mapped = "number";
                return true;
            }

            return false;
        }

        private static bool IsFloatType(string value)
        {
            return value == "float" || value == "double" || value == "long double";
        }

        private static bool IsIntegerType(string value)
        {
            switch (value)
            {
                case "int":
                case "int8":
                case "int16":
                case "int32":
                case "int64":
                case "int8_t":
                case "int16_t":
                case "int32_t":
                case "int64_t":
                case "uint8":
                case "uint16":
                case "uint32":
                case "uint64":
                case "uint8_t":
                case "uint16_t":
                case "uint32_t":
                case "uint64_t":
                case "short":
                case "unsigned short":
                case "long":
                case "long long":
                case "unsigned":
                case "unsigned int":
                case "unsigned long":
                case "unsigned long long":
                case "size_t":
                case "ssize_t":
                case "ptrdiff_t":
                case "intptr_t":
                case "uintptr_t":
                case "intp":
                case "uintp":
                    return true;
                default:
                    return false;
            }
        }

        public static string ConvertToMetaEnum(string value)
        {
            return "sol::meta_function::" + value;
        }

        public static bool DumpErrorsIfAny(this CppCompilation compilation)
        {
            if (!compilation.Diagnostics.HasErrors)
                return false;

            var color = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;

            foreach (var dgn in compilation.Diagnostics.Messages)
                if (dgn.Type == CppLogMessageType.Error)
                    Console.WriteLine($"{dgn.Text} at {dgn.Location}");

            Console.ForegroundColor = color;

            return true;
        }

        public static string GetRealParamValue(this CppParameter input)
        {
            StringBuilder x = new StringBuilder();
            if (input.Type is CppReferenceType rf)
            {
                var rt = rf.ElementType;
                while (rt != null)
                {
                    if (rt is CppTypeWithElementType ct)
                    {
                        rt = ct.ElementType;
                    }
                    else
                    {
                        if (rt.TypeKind == CppTypeKind.Unexposed)
                        {
                            x.Append(rt.GetDisplayName());
                            break;
                        }
                    }
                }

                x.Append("&");
            }
            else if (input.Type is CppQualifiedType zf)
            {
                var rt = zf.ElementType;
                while (rt != null)
                {
                    if (rt is CppTypeWithElementType ct)
                    {
                        rt = ct.ElementType;
                    }
                    else
                    {
                        if (rt.TypeKind == CppTypeKind.Unexposed)
                        {
                            x.Append(rt.GetDisplayName());
                            break;
                        }
                    }
                }
            }
            else
            {
                x.Append(input.Type.GetDisplayName());
            }

            //    input.
            return x.ToString();
        }
    }
}
