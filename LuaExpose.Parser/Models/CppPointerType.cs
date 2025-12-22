// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ pointer type (e.g `int*`)
    /// </summary>
    public sealed class CppPointerType : CppTypeWithElementType
    {
        public CppPointerType(CppType elementType) : base(CppTypeKind.Pointer, elementType)
        {
            SizeOf = IntPtr.Size;
        }

        public override string ToString()
        {
            return $"{ElementType.GetDisplayName()}*";
        }

        public override CppType GetCanonicalType()
        {
            var elementTypeCanonical = ElementType.GetCanonicalType();
            if (ReferenceEquals(elementTypeCanonical, ElementType)) return this;
            return new CppPointerType(elementTypeCanonical);
        }
    }
}
