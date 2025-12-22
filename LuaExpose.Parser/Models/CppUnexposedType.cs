// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// A type not fully/correctly exposed by the parser.
    /// Template parameter type instances are actually exposed with this type.
    /// </summary>
    public sealed class CppUnexposedType : CppType, ICppTemplateOwner
    {
        public CppUnexposedType(string name) : base(CppTypeKind.Unexposed)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            TemplateParameters = new List<CppType>();
        }

        public string Name { get; }

        public override int SizeOf { get; set; }

        public List<CppType> TemplateParameters { get; }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppUnexposedType other && Name.Equals(other.Name);

        public override int GetHashCode()
        {
            unchecked { return (base.GetHashCode() * 397) ^ Name.GetHashCode(); }
        }

        public override CppType GetCanonicalType() => this;

        public override string ToString() => Name;
    }
}
