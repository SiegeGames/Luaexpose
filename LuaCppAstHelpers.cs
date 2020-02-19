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
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsNormalFunc()).ToList();
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
            switch (input.GetDisplayName())
            {
                case "basic_function":
                    return "sol::function";
                case "variadic_args":
                    return "sol::variadic_args";
                case "basic_string":
                    return "String";
                default:
                    return input.GetDisplayName();
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
                if (rf.ElementType is CppQualifiedType ct)
                {
                    if (ct.Qualifier == CppTypeQualifier.Const)
                    {
                        x.Append("const ");
                    }
                }
                x.Append("&");
            }

            if (input.Type is CppQualifiedType xr)
            {

                    if (xr.Qualifier == CppTypeQualifier.Const)
                    {
                        x.Append("const ");
                    }

            }


            x.Append(input.Name);
            //    input.
            return x.ToString();
        }
    }
}
