// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ typedef (e.g `typedef int XXX`)
    /// </summary>
    public sealed class CppTypedef : CppTypeDeclaration, ICppMemberWithVisibility
    {
        public CppTypedef(string name, CppType type) : base(CppTypeKind.Typedef)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ElementType = type;
        }

        public CppType ElementType { get; }

        public CppVisibility Visibility { get; set; }

        public string Name { get; set; }

        private bool Equals(CppTypedef other) => base.Equals(other) && string.Equals(Name, other.Name);

        public override int SizeOf
        {
            get => ElementType?.SizeOf ?? 0;
            set => throw new InvalidOperationException("Cannot set the SizeOf a TypeDef");
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppTypedef other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (base.GetHashCode() * 397) ^ Name.GetHashCode(); }
        }

        public override CppType GetCanonicalType() => ElementType?.GetCanonicalType();

        public override string ToString() => $"typedef {ElementType?.GetDisplayName()} {Name}";
    }
}
