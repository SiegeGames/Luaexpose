// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// A C++ qualified type (e.g `const int`)
    /// </summary>
    public sealed class CppQualifiedType : CppTypeWithElementType
    {
        public CppQualifiedType(CppTypeQualifier qualifier, CppType elementType) : base(CppTypeKind.Qualified, elementType)
        {
            Qualifier = qualifier;
            SizeOf = elementType.SizeOf;
        }

        public CppTypeQualifier Qualifier { get; }

        private bool Equals(CppQualifiedType other) => base.Equals(other) && Qualifier == other.Qualifier;

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppQualifiedType other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (base.GetHashCode() * 397) ^ (int)Qualifier; }
        }

        public override CppType GetCanonicalType()
        {
            var elementTypeCanonical = ElementType.GetCanonicalType();
            return ReferenceEquals(elementTypeCanonical, ElementType) ? this : new CppQualifiedType(Qualifier, elementTypeCanonical);
        }

        public override string ToString()
        {
            return $"{Qualifier.ToString().ToLowerInvariant()} {ElementType.GetDisplayName()}";
        }
    }
}
