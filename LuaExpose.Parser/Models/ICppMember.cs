// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// A C++ declaration that has a name.
    /// </summary>
    public interface ICppMember : ICppElement
    {
        /// <summary>
        /// Name of this C++ declaration.
        /// </summary>
        string Name { get; set; }
    }
}
