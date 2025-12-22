// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// A C++ macro definition.
    /// </summary>
    public class CppMacro : CppElement
    {
        public CppMacro(string name)
        {
            Name = name;
            Parameters = new List<string>();
            Tokens = new List<CppToken>();
        }

        public string Name { get; set; }

        public List<string> Parameters { get; }

        public List<CppToken> Tokens { get; }

        public string Value { get; set; }

        public override string ToString() => $"#define {Name} {Value}";
    }

    /// <summary>
    /// A C++ token.
    /// </summary>
    public class CppToken
    {
        public CppToken(CppTokenKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public CppTokenKind Kind { get; }

        public string Text { get; }

        public CppSourceSpan Span { get; set; }

        public override string ToString() => Text;
    }
}
