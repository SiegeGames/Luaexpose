// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ reference type (e.g `int&amp;`)
    /// </summary>
    public sealed class CppReferenceType : CppTypeWithElementType
    {
        public CppReferenceType(CppType elementType) : base(CppTypeKind.Reference, elementType)
        {
        }

        public override int SizeOf
        {
            get => ElementType.SizeOf;
            set => throw new InvalidOperationException("Cannot override the SizeOf of a Reference type");
        }

        public override string ToString()
        {
            return $"{ElementType.GetDisplayName()}&";
        }

        public override CppType GetCanonicalType()
        {
            var elementTypeCanonical = ElementType.GetCanonicalType();
            return ReferenceEquals(elementTypeCanonical, ElementType) ? this : new CppReferenceType(elementTypeCanonical);
        }
    }
}
