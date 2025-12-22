// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// A C++ value (constant).
    /// </summary>
    public class CppValue
    {
        public CppValue(object value)
        {
            Value = value;
        }

        public object Value { get; }

        public override string ToString() => Value?.ToString() ?? "null";
    }
}
