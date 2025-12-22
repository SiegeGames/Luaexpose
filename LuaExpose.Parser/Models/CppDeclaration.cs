// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base class for any declaration that is not a type.
    /// </summary>
    public abstract class CppDeclaration : CppElement, ICppDeclaration
    {
        /// <summary>
        /// Gets or sets the comment attached to this element. Might be null.
        /// </summary>
        public CppComment Comment { get; set; }
    }
}
