// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base interface for all C++ declarations.
    /// </summary>
    public interface ICppDeclaration : ICppElement
    {
        /// <summary>
        /// Gets or sets the comment attached to this element. Might be null.
        /// </summary>
        CppComment Comment { get; set; }
    }
}
