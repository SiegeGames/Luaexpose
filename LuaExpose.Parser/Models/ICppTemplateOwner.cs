// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// Base interface of a type/method declared with template parameters.
    /// </summary>
    public interface ICppTemplateOwner
    {
        /// <summary>
        /// List of template parameters.
        /// </summary>
        List<CppType> TemplateParameters { get; }
    }
}
