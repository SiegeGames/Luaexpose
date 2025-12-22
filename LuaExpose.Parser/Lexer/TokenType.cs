// LuaExpose.Parser - Custom C++ Lexer

namespace LuaExpose.Parser.Lexer
{
    /// <summary>
    /// Token types for C++ lexer.
    /// </summary>
    public enum TokenType
    {
        // End of file
        EndOfFile,

        // Unknown/error token
        Unknown,

        // Identifiers and literals
        Identifier,
        IntegerLiteral,
        FloatLiteral,
        StringLiteral,
        CharLiteral,

        // Keywords
        Class,
        Struct,
        Union,
        Enum,
        Namespace,
        Template,
        Typedef,
        Using,
        Public,
        Private,
        Protected,
        Virtual,
        Static,
        Const,
        Volatile,
        Mutable,
        Inline,
        Extern,
        Explicit,
        Friend,
        Operator,
        Typename,
        Constexpr,
        Consteval,
        Constinit,

        // Type keywords
        Void,
        Bool,
        Char,
        WChar,
        Short,
        Int,
        Long,
        Float,
        Double,
        Signed,
        Unsigned,
        Auto,

        // Punctuation
        LeftBrace,          // {
        RightBrace,         // }
        LeftParen,          // (
        RightParen,         // )
        LeftBracket,        // [
        RightBracket,       // ]
        Semicolon,          // ;
        Colon,              // :
        DoubleColon,        // ::
        Comma,              // ,
        Dot,                // .
        Arrow,              // ->
        Star,               // *
        Ampersand,          // &
        DoubleAmpersand,    // &&
        LessThan,           // <
        GreaterThan,        // >
        LessEqual,          // <=
        GreaterEqual,       // >=
        Equals,             // =
        DoubleEquals,       // ==
        NotEquals,          // !=
        Plus,               // +
        Minus,              // -
        Slash,              // /
        Percent,            // %
        Pipe,               // |
        DoublePipe,         // ||
        Caret,              // ^
        Tilde,              // ~
        Exclamation,        // !
        Question,           // ?
        Ellipsis,           // ...

        // Attributes
        DoubleLeftBracket,  // [[
        DoubleRightBracket, // ]]

        // Preprocessor (we skip these but need to recognize them)
        Preprocessor,

        // Comment
        Comment,
    }
}
