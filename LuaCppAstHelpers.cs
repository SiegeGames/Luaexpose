using CppAst;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static LuaExpose.LuaCodeGenWriter;

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
        public static bool IsNormalFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC");
        }
        public static bool IsNormalStaticFunc(this CppFunction input)
        {
            if (input.IsNormalFunc() && input.Attributes[0].Arguments != null)
                return input.Attributes[0].Arguments.Contains("use_static");

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
        public static bool IsTemplateFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC_TEMPLATE");
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
            return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE"));
        }
        public static List<CppFunction> GetNormalFunctions(this LuaUserType input)
        {
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsNormalFunc() || x.IsOverloadFunc()).ToList();
        }
        public static List<CppFunction> GetMetaFunctions(this LuaUserType input)
        {
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsMetaFunc()).ToList();
        }
        public static List<CppEnum> GetEnums(this LuaUserType input)
        {
            if (input.OriginalElement is CppEnum)
                return new List<CppEnum> { input.OriginalElement as CppEnum };

            return (input.OriginalElement as ICppDeclarationContainer).Enums.Where(x => x.IsEnum()).ToList();
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

        public static string ConvertToSiegeType(this CppType input)
        {
            var x = input.GetDisplayName();
            if (x.Contains("basic_function")) {
                return x.Replace("basic_function", "sol::function");
            }
            if (x.Contains("variadic_args")) {
                return x.Replace("variadic_args", "sol::variadic_args");
            }
            if (x.Contains("basic_string")) {
                return x.Replace("basic_string", "String");
            }

            return input.GetDisplayName();
        }

        public static string ConvertToMetaEnum(string value)
        {
            switch (value) {
                case "index":
                return "meta_function::index";
                break;
                default:
                    return "";
                break;
            }
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
