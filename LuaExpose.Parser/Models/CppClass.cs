// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ class, struct or union.
    /// </summary>
    public sealed class CppClass : CppTypeDeclaration, ICppMemberWithVisibility, ICppDeclarationContainer, ICppTemplateOwner
    {
        public CppClass(string name) : base(CppTypeKind.StructOrClass)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            BaseTypes = new List<CppBaseType>();
            Fields = new CppContainerList<CppField>(this);
            Constructors = new CppContainerList<CppFunction>(this);
            Functions = new CppContainerList<CppFunction>(this);
            Enums = new CppContainerList<CppEnum>(this);
            Classes = new CppContainerList<CppClass>(this);
            Typedefs = new CppContainerList<CppTypedef>(this);
            TemplateParameters = new List<CppType>();
            Attributes = new CppContainerList<CppAttribute>(this);
        }

        public CppClassKind ClassKind { get; set; }

        public string Name { get; set; }

        public CppVisibility Visibility { get; set; }

        public CppContainerList<CppAttribute> Attributes { get; }

        public bool IsDefinition { get; set; }

        public bool IsAnonymous { get; set; }

        public List<CppBaseType> BaseTypes { get; }

        public CppContainerList<CppField> Fields { get; }

        public CppContainerList<CppFunction> Constructors { get; set; }

        public CppContainerList<CppFunction> Functions { get; }

        public CppContainerList<CppEnum> Enums { get; }

        public CppContainerList<CppClass> Classes { get; }

        public CppContainerList<CppTypedef> Typedefs { get; }

        public List<CppType> TemplateParameters { get; }

        public CppClass SpecializedTemplate { get; set; }

        private bool Equals(CppClass other) => base.Equals(other) && Equals(Parent, other.Parent) && Name.Equals(other.Name);

        public override int SizeOf { get; set; }

        public override bool Equals(object obj) => ReferenceEquals(this, obj) || obj is CppClass other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (Parent != null ? Parent.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Name.GetHashCode();
                foreach (var templateParameter in TemplateParameters)
                {
                    hashCode = (hashCode * 397) ^ templateParameter.GetHashCode();
                }
                return hashCode;
            }
        }

        public override CppType GetCanonicalType() => this;

        public override string ToString()
        {
            var builder = new StringBuilder();
            switch (ClassKind)
            {
                case CppClassKind.Class:
                    builder.Append("class");
                    break;
                case CppClassKind.Struct:
                    builder.Append("struct");
                    break;
                case CppClassKind.Union:
                    builder.Append("union");
                    break;
            }

            if (!string.IsNullOrEmpty(Name))
            {
                builder.Append(" ");
                builder.Append(Name);
            }

            if (BaseTypes.Count > 0)
            {
                builder.Append(" : ");
                for (var i = 0; i < BaseTypes.Count; i++)
                {
                    var baseType = BaseTypes[i];
                    if (i > 0) builder.Append(", ");
                    builder.Append(baseType);
                }
            }

            builder.Append(" { ... }");
            return builder.ToString();
        }

        public override IEnumerable<ICppDeclaration> Children()
        {
            foreach (var item in CppContainerHelper.Children(this))
            {
                yield return item;
            }

            foreach (var item in Constructors)
            {
                yield return item;
            }
        }
    }
}
