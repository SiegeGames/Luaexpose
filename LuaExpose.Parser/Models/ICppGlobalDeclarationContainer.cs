// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base interface of a global declaration container containing namespaces.
    /// </summary>
    public interface ICppGlobalDeclarationContainer : ICppDeclarationContainer
    {
        /// <summary>
        /// Gets the namespaces.
        /// </summary>
        CppContainerList<CppNamespace> Namespaces { get; }
    }
}
