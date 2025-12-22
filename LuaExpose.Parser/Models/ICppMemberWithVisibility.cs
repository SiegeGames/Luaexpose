// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Interface of a <see cref="ICppMember"/> with a <see cref="CppVisibility"/>.
    /// </summary>
    public interface ICppMemberWithVisibility : ICppMember
    {
        /// <summary>
        /// Gets or sets the visibility of this element.
        /// </summary>
        CppVisibility Visibility { get; set; }
    }
}
