// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ standard or scoped enum.
    /// </summary>
    public sealed class CppEnum : CppTypeDeclaration, ICppMemberWithVisibility
    {
        public CppEnum(string name) : base(CppTypeKind.Enum)
        {
            Name = name;
            Items = new CppContainerList<CppEnumItem>(this);
            Attributes = new CppContainerList<CppAttribute>(this);
        }

        public CppVisibility Visibility { get; set; }

        public string Name { get; set; }

        public bool IsScoped { get; set; }

        public CppType IntegerType { get; set; }

        public CppContainerList<CppEnumItem> Items { get; }

        public bool IsAnonymous { get; set; }

        public CppContainerList<CppAttribute> Attributes { get; }

        private bool Equals(CppEnum other) => base.Equals(other) && Equals(Parent, other.Parent) && Equals(Name, other.Name);

        public override int SizeOf
        {
            get => IntegerType?.SizeOf ?? 0;
            set => throw new InvalidOperationException("Cannot set the SizeOf an enum");
        }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppEnum other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (Parent != null ? Parent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override CppType GetCanonicalType() => IntegerType;

        public override IEnumerable<ICppDeclaration> Children() => Items;

        public override string ToString()
        {
            var builder = new StringBuilder();
            if (Visibility != CppVisibility.Default)
            {
                builder.Append(Visibility.ToString().ToLowerInvariant());
                builder.Append(" ");
            }

            builder.Append("enum ");
            if (IsScoped)
            {
                builder.Append("class ");
            }

            builder.Append(Name);

            if (IntegerType != null && !(IntegerType is CppPrimitiveType primitive && primitive.Kind == CppPrimitiveKind.Int))
            {
                builder.Append(": ");
                builder.Append(IntegerType.GetDisplayName());
            }

            builder.Append(" {...}");
            return builder.ToString();
        }
    }
}
