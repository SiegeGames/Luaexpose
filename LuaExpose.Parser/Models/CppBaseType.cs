// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ base type used by <see cref="CppClass.BaseTypes"/>
    /// </summary>
    public sealed class CppBaseType : CppElement
    {
        public CppBaseType(CppType baseType)
        {
            Type = baseType ?? throw new ArgumentNullException(nameof(baseType));
        }

        public CppVisibility Visibility { get; set; }

        public bool IsVirtual { get; set; }

        public CppType Type { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();
            if (Visibility != CppVisibility.Default && Visibility != CppVisibility.Public)
            {
                builder.Append(Visibility.ToString().ToLowerInvariant()).Append(" ");
            }

            if (IsVirtual)
            {
                builder.Append("virtual ");
            }

            builder.Append(Type.GetDisplayName());
            return builder.ToString();
        }
    }
}
