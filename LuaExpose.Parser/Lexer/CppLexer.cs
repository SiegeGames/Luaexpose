// LuaExpose.Parser - Custom C++ Lexer

using System;
using System.Collections.Generic;
using System.Text;
using CppAst;

namespace LuaExpose.Parser.Lexer
{
    /// <summary>
    /// C++ source code lexer.
    /// </summary>
    public class CppLexer
    {
        private readonly string _source;
        private readonly string _filename;
        private int _position;
        private int _line;
        private int _column;
        private readonly List<Token> _tokens;

        private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
        {
            { "class", TokenType.Class },
            { "struct", TokenType.Struct },
            { "union", TokenType.Union },
            { "enum", TokenType.Enum },
            { "namespace", TokenType.Namespace },
            { "template", TokenType.Template },
            { "typedef", TokenType.Typedef },
            { "using", TokenType.Using },
            { "public", TokenType.Public },
            { "private", TokenType.Private },
            { "protected", TokenType.Protected },
            { "virtual", TokenType.Virtual },
            { "static", TokenType.Static },
            { "const", TokenType.Const },
            { "volatile", TokenType.Volatile },
            { "mutable", TokenType.Mutable },
            { "inline", TokenType.Inline },
            { "extern", TokenType.Extern },
            { "explicit", TokenType.Explicit },
            { "friend", TokenType.Friend },
            { "operator", TokenType.Operator },
            { "typename", TokenType.Typename },
            { "constexpr", TokenType.Constexpr },
            { "consteval", TokenType.Consteval },
            { "constinit", TokenType.Constinit },
            { "void", TokenType.Void },
            { "bool", TokenType.Bool },
            { "char", TokenType.Char },
            { "wchar_t", TokenType.WChar },
            { "short", TokenType.Short },
            { "int", TokenType.Int },
            { "long", TokenType.Long },
            { "float", TokenType.Float },
            { "double", TokenType.Double },
            { "signed", TokenType.Signed },
            { "unsigned", TokenType.Unsigned },
            { "auto", TokenType.Auto },
        };

        public CppLexer(string source, string filename)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _filename = filename ?? "unknown";
            _position = 0;
            _line = 1;
            _column = 1;
            _tokens = new List<Token>();
        }

        public List<Token> Tokenize()
        {
            _tokens.Clear();

            while (!IsAtEnd())
            {
                SkipWhitespace();
                if (IsAtEnd()) break;

                var token = ScanToken();
                if (token != null && token.Type != TokenType.Comment && token.Type != TokenType.Preprocessor)
                {
                    _tokens.Add(token);
                }
            }

            _tokens.Add(new Token(TokenType.EndOfFile, "", GetLocation()));
            return _tokens;
        }

        private Token ScanToken()
        {
            var startLocation = GetLocation();
            char c = Advance();

            // Check for preprocessor directives at start of line
            if (c == '#')
            {
                return ScanPreprocessor(startLocation);
            }

            // Two-character tokens first
            if (c == '[' && Peek() == '[')
            {
                Advance();
                return new Token(TokenType.DoubleLeftBracket, "[[", startLocation);
            }
            if (c == ']' && Peek() == ']')
            {
                Advance();
                return new Token(TokenType.DoubleRightBracket, "]]", startLocation);
            }
            if (c == ':' && Peek() == ':')
            {
                Advance();
                return new Token(TokenType.DoubleColon, "::", startLocation);
            }
            if (c == '-' && Peek() == '>')
            {
                Advance();
                return new Token(TokenType.Arrow, "->", startLocation);
            }
            if (c == '&' && Peek() == '&')
            {
                Advance();
                return new Token(TokenType.DoubleAmpersand, "&&", startLocation);
            }
            if (c == '|' && Peek() == '|')
            {
                Advance();
                return new Token(TokenType.DoublePipe, "||", startLocation);
            }
            if (c == '=' && Peek() == '=')
            {
                Advance();
                return new Token(TokenType.DoubleEquals, "==", startLocation);
            }
            if (c == '!' && Peek() == '=')
            {
                Advance();
                return new Token(TokenType.NotEquals, "!=", startLocation);
            }
            if (c == '<' && Peek() == '=')
            {
                Advance();
                return new Token(TokenType.LessEqual, "<=", startLocation);
            }
            if (c == '>' && Peek() == '=')
            {
                Advance();
                return new Token(TokenType.GreaterEqual, ">=", startLocation);
            }
            if (c == '.' && Peek() == '.' && PeekNext() == '.')
            {
                Advance();
                Advance();
                return new Token(TokenType.Ellipsis, "...", startLocation);
            }

            // Comments
            if (c == '/')
            {
                if (Peek() == '/')
                {
                    return ScanLineComment(startLocation);
                }
                if (Peek() == '*')
                {
                    return ScanBlockComment(startLocation);
                }
                return new Token(TokenType.Slash, "/", startLocation);
            }

            // Single character tokens
            switch (c)
            {
                case '{': return new Token(TokenType.LeftBrace, "{", startLocation);
                case '}': return new Token(TokenType.RightBrace, "}", startLocation);
                case '(': return new Token(TokenType.LeftParen, "(", startLocation);
                case ')': return new Token(TokenType.RightParen, ")", startLocation);
                case '[': return new Token(TokenType.LeftBracket, "[", startLocation);
                case ']': return new Token(TokenType.RightBracket, "]", startLocation);
                case ';': return new Token(TokenType.Semicolon, ";", startLocation);
                case ':': return new Token(TokenType.Colon, ":", startLocation);
                case ',': return new Token(TokenType.Comma, ",", startLocation);
                case '.': return new Token(TokenType.Dot, ".", startLocation);
                case '*': return new Token(TokenType.Star, "*", startLocation);
                case '&': return new Token(TokenType.Ampersand, "&", startLocation);
                case '<': return new Token(TokenType.LessThan, "<", startLocation);
                case '>': return new Token(TokenType.GreaterThan, ">", startLocation);
                case '=': return new Token(TokenType.Equals, "=", startLocation);
                case '+': return new Token(TokenType.Plus, "+", startLocation);
                case '-': return new Token(TokenType.Minus, "-", startLocation);
                case '%': return new Token(TokenType.Percent, "%", startLocation);
                case '|': return new Token(TokenType.Pipe, "|", startLocation);
                case '^': return new Token(TokenType.Caret, "^", startLocation);
                case '~': return new Token(TokenType.Tilde, "~", startLocation);
                case '!': return new Token(TokenType.Exclamation, "!", startLocation);
                case '?': return new Token(TokenType.Question, "?", startLocation);
            }

            // String literal
            if (c == '"')
            {
                return ScanStringLiteral(startLocation);
            }

            // Char literal
            if (c == '\'')
            {
                return ScanCharLiteral(startLocation);
            }

            // Number literal
            if (char.IsDigit(c))
            {
                return ScanNumber(c, startLocation);
            }

            // Identifier or keyword
            if (IsIdentifierStart(c))
            {
                return ScanIdentifier(c, startLocation);
            }

            return new Token(TokenType.Unknown, c.ToString(), startLocation);
        }

        private Token ScanIdentifier(char first, CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append(first);

            while (!IsAtEnd() && IsIdentifierPart(Peek()))
            {
                sb.Append(Advance());
            }

            var text = sb.ToString();
            var type = Keywords.TryGetValue(text, out var keywordType) ? keywordType : TokenType.Identifier;
            return new Token(type, text, startLocation);
        }

        private Token ScanNumber(char first, CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append(first);

            // Handle hex/octal/binary prefixes
            if (first == '0' && !IsAtEnd())
            {
                char next = Peek();
                if (next == 'x' || next == 'X')
                {
                    sb.Append(Advance());
                    while (!IsAtEnd() && IsHexDigit(Peek()))
                    {
                        sb.Append(Advance());
                    }
                    return new Token(TokenType.IntegerLiteral, sb.ToString(), startLocation);
                }
                if (next == 'b' || next == 'B')
                {
                    sb.Append(Advance());
                    while (!IsAtEnd() && (Peek() == '0' || Peek() == '1'))
                    {
                        sb.Append(Advance());
                    }
                    return new Token(TokenType.IntegerLiteral, sb.ToString(), startLocation);
                }
            }

            // Regular decimal/float
            bool isFloat = false;
            while (!IsAtEnd() && (char.IsDigit(Peek()) || Peek() == '\''))
            {
                if (Peek() != '\'') sb.Append(Advance());
                else Advance(); // Skip digit separator
            }

            // Check for decimal point
            if (!IsAtEnd() && Peek() == '.' && char.IsDigit(PeekNext()))
            {
                isFloat = true;
                sb.Append(Advance()); // .
                while (!IsAtEnd() && char.IsDigit(Peek()))
                {
                    sb.Append(Advance());
                }
            }

            // Check for exponent
            if (!IsAtEnd() && (Peek() == 'e' || Peek() == 'E'))
            {
                isFloat = true;
                sb.Append(Advance());
                if (!IsAtEnd() && (Peek() == '+' || Peek() == '-'))
                {
                    sb.Append(Advance());
                }
                while (!IsAtEnd() && char.IsDigit(Peek()))
                {
                    sb.Append(Advance());
                }
            }

            // Suffix (u, l, ll, f, etc.)
            while (!IsAtEnd() && IsIdentifierPart(Peek()))
            {
                char suffix = Peek();
                if (suffix == 'f' || suffix == 'F') isFloat = true;
                sb.Append(Advance());
            }

            return new Token(isFloat ? TokenType.FloatLiteral : TokenType.IntegerLiteral, sb.ToString(), startLocation);
        }

        private Token ScanStringLiteral(CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append('"');

            while (!IsAtEnd() && Peek() != '"')
            {
                if (Peek() == '\\')
                {
                    sb.Append(Advance()); // backslash
                    if (!IsAtEnd()) sb.Append(Advance()); // escaped char
                }
                else if (Peek() == '\n')
                {
                    break; // Unterminated string
                }
                else
                {
                    sb.Append(Advance());
                }
            }

            if (!IsAtEnd()) sb.Append(Advance()); // closing "
            return new Token(TokenType.StringLiteral, sb.ToString(), startLocation);
        }

        private Token ScanCharLiteral(CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append('\'');

            while (!IsAtEnd() && Peek() != '\'')
            {
                if (Peek() == '\\')
                {
                    sb.Append(Advance());
                    if (!IsAtEnd()) sb.Append(Advance());
                }
                else
                {
                    sb.Append(Advance());
                }
            }

            if (!IsAtEnd()) sb.Append(Advance()); // closing '
            return new Token(TokenType.CharLiteral, sb.ToString(), startLocation);
        }

        private Token ScanLineComment(CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append('/');
            sb.Append(Advance()); // second /

            while (!IsAtEnd() && Peek() != '\n')
            {
                sb.Append(Advance());
            }

            return new Token(TokenType.Comment, sb.ToString(), startLocation);
        }

        private Token ScanBlockComment(CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append('/');
            sb.Append(Advance()); // *

            while (!IsAtEnd())
            {
                if (Peek() == '*' && PeekNext() == '/')
                {
                    sb.Append(Advance()); // *
                    sb.Append(Advance()); // /
                    break;
                }
                sb.Append(Advance());
            }

            return new Token(TokenType.Comment, sb.ToString(), startLocation);
        }

        private Token ScanPreprocessor(CppSourceLocation startLocation)
        {
            var sb = new StringBuilder();
            sb.Append('#');

            // Read until end of line (handling line continuations)
            while (!IsAtEnd())
            {
                if (Peek() == '\n')
                {
                    int lastIndex = sb.Length - 1;
                    if (lastIndex >= 0 && sb[lastIndex] == '\r')
                    {
                        lastIndex--;
                    }

                    if (lastIndex >= 0 && sb[lastIndex] == '\\')
                    {
                        sb.Remove(lastIndex, sb.Length - lastIndex);
                        Advance(); // consume newline
                        continue;
                    }
                    break;
                }
                sb.Append(Advance());
            }

            return new Token(TokenType.Preprocessor, sb.ToString(), startLocation);
        }

        private void SkipWhitespace()
        {
            while (!IsAtEnd() && char.IsWhiteSpace(Peek()))
            {
                Advance();
            }
        }

        private bool IsAtEnd() => _position >= _source.Length;

        private char Peek() => IsAtEnd() ? '\0' : _source[_position];

        private char PeekNext() => _position + 1 >= _source.Length ? '\0' : _source[_position + 1];

        private char Advance()
        {
            char c = _source[_position++];
            if (c == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            return c;
        }

        private CppSourceLocation GetLocation() => new CppSourceLocation(_filename, _position, _line, _column);

        private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

        private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

        private static bool IsHexDigit(char c) => char.IsDigit(c) || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
