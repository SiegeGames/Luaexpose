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
          if (input == null)
            return false;

          return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE") && x.Span.Start.File == a.OriginLocation);
        }
        public static bool IsClassTwo(this CppClass input)
        {
            if (input == null)
              return false;
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

            return (input.OriginalElement as ICppDeclarationContainer).Enums.Where(x => x.IsEnum() && x.SourceFile == input.OriginLocation).ToList();
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
                case CppTypeKind.Primitive:
                case CppTypeKind.Pointer:
                case CppTypeKind.Reference:
                case CppTypeKind.Array:
                case CppTypeKind.Function:
                case CppTypeKind.Enum:
                case CppTypeKind.TemplateParameterType:
                case CppTypeKind.Unexposed:
                case CppTypeKind.StructOrClass:
                default:
                    return input.TypeKind;
            }

        }
        
        public static CppType GetFinalType(this CppType input)
        {
            switch (input.TypeKind)
            {
                case CppTypeKind.Qualified:
                    return (input as CppTypeWithElementType).ElementType.GetFinalType();
                case CppTypeKind.Typedef:
                    return (input as CppTypedef).ElementType.GetFinalType();
                default:
                    return input;
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
            if (x.Contains("Vec2<T, U>"))
            {
                return x.Replace("Vec2<T, U>", templatedClassName);
            }
            if (x.Contains("Rect<T, U>"))
            {
                return x.Replace("Rect<T, U>", templatedClassName);
            }
            if (x.Contains("shared_ptr"))
            {
                // Handle different ways shared_ptr can be represented
                switch (input.TypeKind)
                {
                    case CppTypeKind.StructOrClass:
                        var inputClass = input as CppClass;
                        if (inputClass != null && inputClass.TemplateParameters.Count > 0)
                        {
                            return $"std::shared_ptr<{inputClass.TemplateParameters[0].ConvertToSiegeType(templatedClassName)}>";
                        }
                        break;
                        
                    case CppTypeKind.Typedef:
                        var typedef = input as CppTypedef;
                        if (typedef?.ElementType?.TypeKind == CppTypeKind.StructOrClass)
                        {
                            var templatedClass = typedef.ElementType as CppClass;
                            if (templatedClass != null && templatedClass.TemplateParameters.Count > 0)
                            {
                                return $"std::shared_ptr<{templatedClass.TemplateParameters[0].ConvertToSiegeType(templatedClassName)}>";
                            }
                        }
                        break;
                        
                    case CppTypeKind.Unexposed:
                        var unexposed = input as CppUnexposedType;
                        if (unexposed != null && unexposed.TemplateParameters.Count > 0)
                        {
                            return $"std::shared_ptr<{unexposed.TemplateParameters[0].ConvertToSiegeType(templatedClassName)}>";
                        }
                        break;
                        
                    case CppTypeKind.Qualified:
                    case CppTypeKind.Pointer:
                    case CppTypeKind.Reference:
                        // For qualified/pointer/reference types, check the element type
                        var withElement = input as CppTypeWithElementType;
                        if (withElement?.ElementType != null)
                        {
                            return withElement.ElementType.ConvertToSiegeType(templatedClassName);
                        }
                        break;
                }
                
                // If we couldn't parse the template parameters, return the display name
                return x;
            }

            return input.GetDisplayName();
        }

        public static string GetTypeScriptName(this CppParameter input)
        {
            if ((input.Name == "" && input.Type.TypeKind == CppTypeKind.Unexposed && (input.Type as CppUnexposedType).Name == "T...") ||
                (input.Name == "args" && input.Type.TypeKind == CppTypeKind.StructOrClass && (input.Type as CppClass).Name == "variadic_args"))
            {
                return "...args";
            }
            else
            {
                return input.Name;
            }
        }

        private static readonly Dictionary<string, string> TypeMappings = new Dictionary<string, string>
        {
            { "String", "string" },
            { "Unicode", "string" },
            { "Path", "string" },
            { "CallbackHandle", "number" },
            { "basic_string", "string" },
            { "basic_object", "any" },
            { "basic_function", "any" },
            { "basic_protected_function", "any"},
            { "basic_table_core", "any" },
            { "state_view", "any" },
            { "variadic_args", "any" },
            { "T...", "any" },
            { "T", "any" },
        };

        private static readonly List<string> PreservedTypedefs = new List<string>
        {
            "EntityID",
            "ComponentID",
            "PrefabID",
            "TileID",
        };

        public enum TypeScriptSourceType {
            Parameter,
            Return,
            Field,
        }


        public static string ConvertToTypeScriptType(this CppType input, TypeScriptSourceType source, CppTypedef specialization = null)
        {
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

                    if (PreservedTypedefs.Contains(typedef.Name))
                    {
                        return typedef.Name;
                    }
                    else if (typedef.ElementType.TypeKind == CppTypeKind.Primitive 
                        || typedef.ElementType.TypeKind == CppTypeKind.Typedef)
                    {
                        return typedef.ElementType.ConvertToTypeScriptType(source, specialization);
                    }
                    else if (TypeMappings.ContainsKey(typedef.Name))
                    {
                        return TypeMappings[typedef.Name];
                    }
                    else if (typedef.ElementType.TypeKind == CppTypeKind.StructOrClass)
                    {
                        var templatedClass = typedef.ElementType as CppClass;
                        if (templatedClass != null && templatedClass.TemplateParameters.Count > 0)
                        {
                            if (templatedClass.Name == "vector" || templatedClass.Name == "array")
                            {
                                string templatedType = templatedClass.TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
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
                    }
                    return typedef.Name;
                case CppTypeKind.StructOrClass:
                    {
                        string name = (input as CppClass).Name;
                        if (TypeMappings.ContainsKey(name))
                        {
                            return TypeMappings[name];
                        }
                        else if (name == "Vec2" && (input as CppClass).TemplateParameters.Count > 0)
                        {
                            var templateParam = (input as CppClass).TemplateParameters[0].GetFinalType();
                            if (templateParam is CppPrimitiveType primitiveType)
                            {
                                if (primitiveType.Kind == CppPrimitiveKind.Int)
                                    return "PixelVector";
                                else if (primitiveType.Kind == CppPrimitiveKind.Float)
                                    return "Vector";
                            }
                            // Fall back to generic Vec2 if template parameter is not recognized
                            return "Vec2";
                        }
                        else if (name == "Rectangle" && (input as CppClass).TemplateParameters.Count > 0)
                        {
                            var templateParam = (input as CppClass).TemplateParameters[0].GetFinalType();
                            if (templateParam is CppPrimitiveType primitiveType)
                            {
                                if (primitiveType.Kind == CppPrimitiveKind.Int)
                                    return "PixelRect";
                                else if (primitiveType.Kind == CppPrimitiveKind.Float)
                                    return "Rect";
                            }
                            // Fall back to generic Rectangle if template parameter is not recognized
                            return "Rectangle";
                        }
                        else if (name == "HashedString" && source == TypeScriptSourceType.Parameter)
                        {
                            return "HashedString | string";
                        }

                        else if (name == "vector" || name == "array")
                        {
                            if ((input as CppClass).TemplateParameters.Count > 0)
                            {
                                string templatedType = (input as CppClass).TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
                                if (source == TypeScriptSourceType.Parameter)
                                    return $"ArrayList<{templatedType}> | Array<{templatedType}>";
                                else
                                    return $"ArrayList<{templatedType}>";
                            }
                            // Fall back to untyped array if no template parameters
                            return "any[]";
                        }
                        else if (name == "shared_ptr")
                        {
                            if ((input as CppClass).TemplateParameters.Count > 0)
                            {
                                return (input as CppClass).TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
                            }
                            // Fall back to any if no template parameters
                            return "any";
                        }
                        else if (name == "optional")
                        {
                            if ((input as CppClass).TemplateParameters.Count > 0)
                            {
                                return (input as CppClass).TemplateParameters[0].ConvertToTypeScriptType(source, specialization) + " | undefined";
                            }
                            // Fall back to any | undefined if no template parameters
                            return "any | undefined";
                        }
                        else
                        {
                            return name;
                        }
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
                            var unexposedType = input as CppUnexposedType;
                            if (unexposedType.TemplateParameters.Count > 0)
                            {
                                return unexposedType.TemplateParameters[0].ConvertToTypeScriptType(source, specialization) + " | undefined";
                            }
                            return "any | undefined";
                        }
                        else if (name.StartsWith("vector<") || name.StartsWith("array<"))
                        {
                            var unexposedType = input as CppUnexposedType;
                            if (unexposedType.TemplateParameters.Count > 0)
                            {
                                string templatedType = unexposedType.TemplateParameters[0].ConvertToTypeScriptType(source, specialization);
                                if (source == TypeScriptSourceType.Parameter)
                                    return $"ArrayList<{templatedType}> | Array<{templatedType}>";
                                else
                                    return $"ArrayList<{templatedType}>";
                            }
                            return "any[]";
                        }

                        int index = name.LastIndexOf("::");
                        if (index != -1)
                            name = name.Substring(index + 2);
                        name = name.Replace("&&", "");

                        if (TypeMappings.ContainsKey(name))
                        {
                            return TypeMappings[name];
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
                        else
                        {
                            return name;
                        }
                    }
                default:
                    return String.Empty;
            }
            return String.Empty;
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
            
            // Handle null type (can happen with template parameters)
            if (input.Type == null)
            {
                return input.Name ?? "";
            }
            
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
                        x.Append(rt.GetDisplayName());
                        break;
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
                        x.Append(rt.GetDisplayName());
                        break;
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
