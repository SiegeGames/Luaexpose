// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// Defines a C++ namespace.
    /// </summary>
    public class CppNamespace : CppDeclaration, ICppMember, ICppGlobalDeclarationContainer
    {
        public CppNamespace(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Fields = new CppContainerList<CppField>(this);
            Functions = new CppContainerList<CppFunction>(this);
            Enums = new CppContainerList<CppEnum>(this);
            Classes = new CppContainerList<CppClass>(this);
            Typedefs = new CppContainerList<CppTypedef>(this);
            Namespaces = new CppContainerList<CppNamespace>(this);
            Attributes = new CppContainerList<CppAttribute>(this);
        }

        public string Name { get; set; }

        public CppContainerList<CppField> Fields { get; }

        public CppContainerList<CppFunction> Functions { get; }

        public CppContainerList<CppEnum> Enums { get; }

        public CppContainerList<CppClass> Classes { get; }

        public CppContainerList<CppTypedef> Typedefs { get; }

        public CppContainerList<CppNamespace> Namespaces { get; }

        public CppContainerList<CppAttribute> Attributes { get; }

        protected bool Equals(CppNamespace other) => Equals(Parent, other.Parent) && Name.Equals(other.Name);

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((CppNamespace)obj);
        }

        public override int GetHashCode()
        {
            unchecked { return ((Parent != null ? Parent.GetHashCode() : 0) * 397) ^ (Name != null ? Name.GetHashCode() : 0); }
        }

        public override string ToString() => $"namespace {Name} {{...}}";

        public IEnumerable<ICppDeclaration> Children() => CppContainerHelper.Children(this);
    }
}
