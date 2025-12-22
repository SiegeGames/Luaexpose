// LuaExpose.Parser - Custom C++ Lexer

using CppAst;

namespace LuaExpose.Parser.Lexer
{
    /// <summary>
    /// Represents a lexical token.
    /// </summary>
    public class Token
    {
        public Token(TokenType type, string text, CppSourceLocation location)
        {
            Type = type;
            Text = text;
            Location = location;
        }

        public TokenType Type { get; }
        public string Text { get; }
        public CppSourceLocation Location { get; }

        public override string ToString() => $"{Type}: '{Text}' at {Location}";
    }
}
