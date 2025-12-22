// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// The result of a compilation for a set of C++ files.
    /// </summary>
    public class CppCompilation : CppGlobalDeclarationContainer
    {
        public CppCompilation()
        {
            Diagnostics = new CppDiagnosticBag();
            System = new CppGlobalDeclarationContainer();
        }

        /// <summary>
        /// Gets the attached diagnostic messages.
        /// </summary>
        public CppDiagnosticBag Diagnostics { get; }

        /// <summary>
        /// Gets the final input header text used by this compilation.
        /// </summary>
        public string InputText { get; set; }

        /// <summary>
        /// Gets a boolean indicating whether this instance has errors.
        /// </summary>
        public bool HasErrors => Diagnostics.HasErrors;

        /// <summary>
        /// Gets all the declarations that are coming from system include folders.
        /// </summary>
        public CppGlobalDeclarationContainer System { get; }
    }
}
