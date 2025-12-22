// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// Base class for a type using an element type.
    /// </summary>
    public abstract class CppTypeWithElementType : CppType
    {
        protected CppTypeWithElementType(CppTypeKind typeKind, CppType elementType) : base(typeKind)
        {
            ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
        }

        public CppType ElementType { get; }

        protected bool Equals(CppTypeWithElementType other)
        {
            return base.Equals(other) && ElementType.Equals(other.ElementType);
        }

        public override int SizeOf { get; set; }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((CppTypeWithElementType)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (base.GetHashCode() * 397) ^ ElementType.GetHashCode();
            }
        }
    }
}
