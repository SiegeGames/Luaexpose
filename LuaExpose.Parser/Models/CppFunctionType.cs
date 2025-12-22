// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ function type (e.g. for function pointers).
    /// </summary>
    public sealed class CppFunctionType : CppTypeDeclaration
    {
        public CppFunctionType(CppType returnType) : base(CppTypeKind.Function)
        {
            ReturnType = returnType ?? CppPrimitiveType.Void;
            Parameters = new CppContainerList<CppParameter>(this);
        }

        public CppType ReturnType { get; set; }

        public CppContainerList<CppParameter> Parameters { get; }

        public CppCallingConvention CallingConvention { get; set; }

        public override int SizeOf { get; set; }

        public override CppType GetCanonicalType() => this;

        public override IEnumerable<ICppDeclaration> Children() => Parameters;

        public override string ToString()
        {
            var builder = new StringBuilder();
            builder.Append(ReturnType.GetDisplayName());
            builder.Append(" (*)(");
            for (var i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                if (i > 0) builder.Append(", ");
                builder.Append(param);
            }
            builder.Append(")");
            return builder.ToString();
        }
    }
}
