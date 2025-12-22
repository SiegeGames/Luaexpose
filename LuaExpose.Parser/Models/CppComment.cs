// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// A C++ comment.
    /// </summary>
    public class CppComment : CppElement
    {
        public CppComment()
        {
            Children = new List<CppComment>();
        }

        /// <summary>
        /// Gets or sets the kind of this comment.
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// Gets or sets the text of this comment.
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets the children comments.
        /// </summary>
        public List<CppComment> Children { get; }

        public override string ToString()
        {
            return Text ?? string.Empty;
        }
    }
}
