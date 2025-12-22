// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// Kind of expression.
    /// </summary>
    public enum CppExpressionKind
    {
        Unexposed,
        DeclRef,
        MemberRef,
        Call,
        ObjCMessage,
        Block,
        IntegerLiteral,
        FloatingLiteral,
        ImaginaryLiteral,
        StringLiteral,
        CharacterLiteral,
        Paren,
        UnaryOperator,
        ArraySubscript,
        BinaryOperator,
        CompoundAssign,
        Conditional,
        CStyleCast,
        CompoundLiteral,
        InitList,
        AddrLabel,
        Statement,
        GenericSelection,
    }

    /// <summary>
    /// A C++ expression.
    /// </summary>
    public class CppExpression : CppElement
    {
        public CppExpression(CppExpressionKind kind)
        {
            Kind = kind;
            Arguments = new List<CppExpression>();
        }

        public CppExpressionKind Kind { get; }

        public List<CppExpression> Arguments { get; }

        public override string ToString() => string.Empty;
    }

    /// <summary>
    /// A raw literal expression.
    /// </summary>
    public class CppRawExpression : CppExpression
    {
        public CppRawExpression(CppExpressionKind kind) : base(kind)
        {
        }

        public string Text { get; set; }

        public override string ToString() => Text ?? string.Empty;
    }

    /// <summary>
    /// A literal expression.
    /// </summary>
    public class CppLiteralExpression : CppExpression
    {
        public CppLiteralExpression(CppExpressionKind kind, string value) : base(kind)
        {
            Value = value;
        }

        public string Value { get; set; }

        public override string ToString() => Value ?? string.Empty;
    }
}
