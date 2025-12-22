// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// A C++ array type (e.g `int[5]`)
    /// </summary>
    public sealed class CppArrayType : CppTypeWithElementType
    {
        public CppArrayType(CppType elementType, int size) : base(CppTypeKind.Array, elementType)
        {
            Size = size;
        }

        public int Size { get; set; }

        public override string ToString()
        {
            return Size > 0 ? $"{ElementType.GetDisplayName()}[{Size}]" : $"{ElementType.GetDisplayName()}[]";
        }

        public override CppType GetCanonicalType()
        {
            var elementTypeCanonical = ElementType.GetCanonicalType();
            return ReferenceEquals(elementTypeCanonical, ElementType) ? this : new CppArrayType(elementTypeCanonical, Size);
        }
    }
}
