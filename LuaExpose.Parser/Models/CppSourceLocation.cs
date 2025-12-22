// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Defines a location within a source file.
    /// </summary>
    public struct CppSourceLocation
    {
        public CppSourceLocation(string file, int offset, int line, int column)
        {
            File = file;
            Offset = offset;
            Line = line;
            Column = column;
        }

        public readonly string File;
        public readonly int Offset;
        public readonly int Line;
        public readonly int Column;

        public override string ToString()
        {
            return $"{File}({Line}, {Column})";
        }
    }

    /// <summary>
    /// A range of source location.
    /// </summary>
    public struct CppSourceSpan
    {
        public CppSourceSpan(CppSourceLocation start, CppSourceLocation end)
        {
            Start = start;
            End = end;
        }

        public CppSourceLocation Start;
        public CppSourceLocation End;

        public override string ToString()
        {
            return $"{Start}";
        }
    }
}
