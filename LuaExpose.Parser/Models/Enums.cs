// LuaExpose.Parser - Custom C++ Parser without libclang dependency
// All enums consolidated for simplicity

using System;

namespace CppAst
{
    /// <summary>
    /// Kinds of a C++ type (e.g primitive, pointer...)
    /// </summary>
    public enum CppTypeKind
    {
        Primitive,
        Pointer,
        Reference,
        Array,
        Qualified,
        Function,
        Typedef,
        StructOrClass,
        Enum,
        TemplateParameterType,
        Unexposed,
    }

    /// <summary>
    /// C++ primitive kinds
    /// </summary>
    public enum CppPrimitiveKind
    {
        Void,
        Bool,
        WChar,
        Char,
        Short,
        Int,
        LongLong,
        UnsignedChar,
        UnsignedShort,
        UnsignedInt,
        UnsignedLongLong,
        Float,
        Double,
        LongDouble,
    }

    /// <summary>
    /// Gets the visibility of a C++ element.
    /// </summary>
    public enum CppVisibility
    {
        Default,
        Public,
        Protected,
        Private,
    }

    /// <summary>
    /// Defines the type of storage.
    /// </summary>
    public enum CppStorageQualifier
    {
        None,
        Extern,
        Static,
    }

    /// <summary>
    /// Flags attached to a <see cref="CppFunction"/>
    /// </summary>
    [Flags]
    public enum CppFunctionFlags
    {
        None = 0,
        Const = 1 << 0,
        Defaulted = 1 << 1,
        Pure = 1 << 2,
        Virtual = 1 << 3,
        Method = 1 << 4,
        Inline = 1 << 5,
        Constructor = 1 << 6,
        Destructor = 1 << 7,
    }

    /// <summary>
    /// Type of a <see cref="CppClass"/> (class, struct or union)
    /// </summary>
    public enum CppClassKind
    {
        Class,
        Struct,
        Union,
    }

    /// <summary>
    /// Qualifiers for a <see cref="CppQualifiedType"/>
    /// </summary>
    public enum CppTypeQualifier
    {
        Const,
        Volatile,
    }

    /// <summary>
    /// Defines the calling convention.
    /// </summary>
    public enum CppCallingConvention
    {
        Default,
        C,
        X86StdCall,
        X86FastCall,
        X86ThisCall,
        X86Pascal,
        X86RegCall,
        X86VectorCall,
        Win64,
        X8664SysV,
        AArch64VectorCall,
    }

    /// <summary>
    /// Linkage kind.
    /// </summary>
    public enum CppLinkageKind
    {
        Invalid,
        NoLinkage,
        Internal,
        UniqueExternal,
        External,
    }

    /// <summary>
    /// Log message type.
    /// </summary>
    public enum CppLogMessageType
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    /// <summary>
    /// Token kind.
    /// </summary>
    public enum CppTokenKind
    {
        Punctuation,
        Keyword,
        Identifier,
        Literal,
        Comment,
    }
}
