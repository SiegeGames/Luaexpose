// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ primitive type (e.g `int`, `void`, `bool`...)
    /// </summary>
    public sealed class CppPrimitiveType : CppType
    {
        public static readonly CppPrimitiveType Void = new CppPrimitiveType(CppPrimitiveKind.Void);
        public static readonly CppPrimitiveType Bool = new CppPrimitiveType(CppPrimitiveKind.Bool);
        public static readonly CppPrimitiveType WChar = new CppPrimitiveType(CppPrimitiveKind.WChar);
        public static readonly CppPrimitiveType Char = new CppPrimitiveType(CppPrimitiveKind.Char);
        public static readonly CppPrimitiveType Short = new CppPrimitiveType(CppPrimitiveKind.Short);
        public static readonly CppPrimitiveType Int = new CppPrimitiveType(CppPrimitiveKind.Int);
        public static readonly CppPrimitiveType LongLong = new CppPrimitiveType(CppPrimitiveKind.LongLong);
        public static readonly CppPrimitiveType UnsignedChar = new CppPrimitiveType(CppPrimitiveKind.UnsignedChar);
        public static readonly CppPrimitiveType UnsignedShort = new CppPrimitiveType(CppPrimitiveKind.UnsignedShort);
        public static readonly CppPrimitiveType UnsignedInt = new CppPrimitiveType(CppPrimitiveKind.UnsignedInt);
        public static readonly CppPrimitiveType UnsignedLongLong = new CppPrimitiveType(CppPrimitiveKind.UnsignedLongLong);
        public static readonly CppPrimitiveType Float = new CppPrimitiveType(CppPrimitiveKind.Float);
        public static readonly CppPrimitiveType Double = new CppPrimitiveType(CppPrimitiveKind.Double);
        public static readonly CppPrimitiveType LongDouble = new CppPrimitiveType(CppPrimitiveKind.LongDouble);

        private readonly int _sizeOf;

        public CppPrimitiveType(CppPrimitiveKind kind) : base(CppTypeKind.Primitive)
        {
            Kind = kind;
            _sizeOf = GetSizeForKind(kind);
        }

        public CppPrimitiveKind Kind { get; }

        private static int GetSizeForKind(CppPrimitiveKind kind)
        {
            return kind switch
            {
                CppPrimitiveKind.Void => 0,
                CppPrimitiveKind.Bool => 1,
                CppPrimitiveKind.WChar => 2,
                CppPrimitiveKind.Char => 1,
                CppPrimitiveKind.Short => 2,
                CppPrimitiveKind.Int => 4,
                CppPrimitiveKind.LongLong => 8,
                CppPrimitiveKind.UnsignedChar => 1,
                CppPrimitiveKind.UnsignedShort => 2,
                CppPrimitiveKind.UnsignedInt => 4,
                CppPrimitiveKind.UnsignedLongLong => 8,
                CppPrimitiveKind.Float => 4,
                CppPrimitiveKind.Double => 8,
                CppPrimitiveKind.LongDouble => 8,
                _ => 0,
            };
        }

        public override string ToString()
        {
            return Kind switch
            {
                CppPrimitiveKind.Void => "void",
                CppPrimitiveKind.WChar => "wchar",
                CppPrimitiveKind.Char => "char",
                CppPrimitiveKind.Short => "short",
                CppPrimitiveKind.Int => "int",
                CppPrimitiveKind.LongLong => "long long",
                CppPrimitiveKind.UnsignedChar => "unsigned char",
                CppPrimitiveKind.UnsignedShort => "unsigned short",
                CppPrimitiveKind.UnsignedInt => "unsigned int",
                CppPrimitiveKind.UnsignedLongLong => "unsigned long long",
                CppPrimitiveKind.Float => "float",
                CppPrimitiveKind.Double => "double",
                CppPrimitiveKind.LongDouble => "long double",
                CppPrimitiveKind.Bool => "bool",
                _ => throw new InvalidOperationException($"Unhandled PrimitiveKind: {Kind}"),
            };
        }

        private bool Equals(CppPrimitiveType other) => base.Equals(other) && Kind == other.Kind;

        public override int SizeOf
        {
            get => _sizeOf;
            set => throw new InvalidOperationException("Cannot set the SizeOf of a primitive type");
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppPrimitiveType other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (base.GetHashCode() * 397) ^ (int)Kind; }
        }

        public override CppType GetCanonicalType() => this;
    }
}
