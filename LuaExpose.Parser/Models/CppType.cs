// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base class for C++ types.
    /// </summary>
    public abstract class CppType : CppElement
    {
        protected CppType(CppTypeKind typeKind)
        {
            TypeKind = typeKind;
        }

        /// <summary>
        /// Gets the <see cref="CppTypeKind"/> of this instance.
        /// </summary>
        public CppTypeKind TypeKind { get; }

        public abstract int SizeOf { get; set; }

        protected bool Equals(CppType other)
        {
            return TypeKind == other.TypeKind;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is CppType type && Equals(type);
        }

        public override int GetHashCode()
        {
            return (int)TypeKind;
        }

        /// <summary>
        /// Gets the canonical type of this type instance.
        /// </summary>
        public abstract CppType GetCanonicalType();
    }
}
