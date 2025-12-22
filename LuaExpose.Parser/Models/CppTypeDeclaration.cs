// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;
using System.Linq;

namespace CppAst
{
    /// <summary>
    /// Base class for a type declaration (CppClass, CppEnum, CppFunctionType or CppTypedef)
    /// </summary>
    public abstract class CppTypeDeclaration : CppType, ICppDeclaration, ICppContainer
    {
        protected CppTypeDeclaration(CppTypeKind typeKind) : base(typeKind)
        {
        }

        /// <inheritdoc />
        public CppComment Comment { get; set; }

        /// <inheritdoc />
        public virtual IEnumerable<ICppDeclaration> Children()
        {
            return Enumerable.Empty<ICppDeclaration>();
        }
    }
}
