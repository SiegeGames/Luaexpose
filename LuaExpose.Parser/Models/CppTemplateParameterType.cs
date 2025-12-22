// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ template parameter type.
    /// </summary>
    public sealed class CppTemplateParameterType : CppType
    {
        public CppTemplateParameterType(string name) : base(CppTypeKind.TemplateParameterType)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }

        private bool Equals(CppTemplateParameterType other) => base.Equals(other) && Name.Equals(other.Name);

        public override int SizeOf
        {
            get => 0;
            set => throw new InvalidOperationException("This type does not support SizeOf");
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppTemplateParameterType other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return (base.GetHashCode() * 397) ^ Name.GetHashCode(); }
        }

        public override CppType GetCanonicalType() => this;

        public override string ToString() => Name;
    }
}
