// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// A C++ function parameter.
    /// </summary>
    public sealed class CppParameter : CppDeclaration, ICppMember
    {
        public CppParameter(CppType type, string name)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Name = name ?? string.Empty;
        }

        public CppType Type { get; set; }

        public string Name { get; set; }

        public CppValue InitValue { get; set; }

        public CppExpression InitExpression { get; set; }

        private bool Equals(CppParameter other) => Equals(Type, other.Type) && Equals(Name, other.Name);

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppParameter other && Equals(other);

        public override int GetHashCode()
        {
            unchecked { return ((Type != null ? Type.GetHashCode() : 0) * 397) ^ (Name != null ? Name.GetHashCode() : 0); }
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Name))
            {
                return InitExpression != null ? $"{Type.GetDisplayName()} = {InitExpression}" : $"{Type.GetDisplayName()}";
            }
            return InitExpression != null ? $"{Type.GetDisplayName()} {Name} = {InitExpression}" : $"{Type.GetDisplayName()} {Name}";
        }
    }
}
