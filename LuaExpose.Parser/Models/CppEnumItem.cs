// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;

namespace CppAst
{
    /// <summary>
    /// An enum item of <see cref="CppEnum"/>.
    /// </summary>
    public sealed class CppEnumItem : CppDeclaration, ICppMember
    {
        public CppEnumItem(string name, long value)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Value = value;
        }

        public string Name { get; set; }

        public long Value { get; set; }

        public CppExpression ValueExpression { get; set; }

        public override string ToString()
        {
            return $"{Name} = {ValueExpression ?? (object)Value}";
        }
    }
}
