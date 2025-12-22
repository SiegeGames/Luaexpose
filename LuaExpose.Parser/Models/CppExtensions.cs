// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Extension methods.
    /// </summary>
    public static class CppExtensions
    {
        /// <summary>
        /// Gets a boolean indicating whether this token kind is an identifier or keyword.
        /// </summary>
        public static bool IsIdentifierOrKeyword(this CppTokenKind kind)
        {
            return kind == CppTokenKind.Identifier || kind == CppTokenKind.Keyword;
        }

        /// <summary>
        /// Gets the display name of the specified type.
        /// </summary>
        public static string GetDisplayName(this CppType type)
        {
            if (type is ICppMember member) return member.Name;
            return type.ToString();
        }

        /// <summary>
        /// Gets a boolean indicating whether the attribute is a dllexport or visibility("default").
        /// </summary>
        public static bool IsPublicExport(this CppAttribute attribute)
        {
            return attribute.Name == "dllexport" || (attribute.Name == "visibility" && attribute.Arguments == "\"default\"");
        }

        /// <summary>
        /// Gets a boolean indicating whether the class is a dllexport or visibility("default").
        /// </summary>
        public static bool IsPublicExport(this CppClass cppClass)
        {
            if (cppClass.Attributes != null)
            {
                foreach (var attr in cppClass.Attributes)
                {
                    if (attr.IsPublicExport()) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets a boolean indicating whether the function is a dllexport or visibility("default").
        /// </summary>
        public static bool IsPublicExport(this CppFunction function)
        {
            if (function.Attributes != null)
            {
                foreach (var attr in function.Attributes)
                {
                    if (attr.IsPublicExport()) return true;
                }
            }
            return function.LinkageKind == CppLinkageKind.External || function.LinkageKind == CppLinkageKind.UniqueExternal;
        }
    }
}
