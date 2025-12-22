// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base class for all Cpp elements of the AST nodes.
    /// </summary>
    public abstract class CppElement : ICppElement
    {
        /// <summary>
        /// Gets or sets the source span of this element.
        /// </summary>
        public CppSourceSpan Span;

        /// <summary>
        /// Gets or sets the parent container of this element. Might be null.
        /// </summary>
        public ICppContainer Parent { get; internal set; }

        /// <summary>
        /// Gets the source file of this element.
        /// </summary>
        public string SourceFile => string.IsNullOrWhiteSpace(Span.Start.File) ? (Parent as CppElement)?.SourceFile : Span.Start.File;
    }
}
