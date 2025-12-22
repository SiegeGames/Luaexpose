// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// Base tag interface used to describe a container of <see cref="CppElement"/>
    /// </summary>
    public interface ICppContainer
    {
        /// <summary>
        /// Gets declarations from this container.
        /// </summary>
        /// <returns>A list of Cpp declarations</returns>
        IEnumerable<ICppDeclaration> Children();
    }
}
