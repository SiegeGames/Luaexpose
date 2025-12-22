// LuaExpose.Parser - Custom C++ Parser without libclang dependency

namespace CppAst
{
    /// <summary>
    /// Base interface of a <see cref="ICppContainer"/> containing fields, functions, enums, classes, typedefs.
    /// </summary>
    public interface ICppDeclarationContainer : ICppContainer
    {
        /// <summary>
        /// Gets the fields/variables.
        /// </summary>
        CppContainerList<CppField> Fields { get; }

        /// <summary>
        /// Gets the functions/methods.
        /// </summary>
        CppContainerList<CppFunction> Functions { get; }

        /// <summary>
        /// Gets the enums.
        /// </summary>
        CppContainerList<CppEnum> Enums { get; }

        /// <summary>
        /// Gets the classes, structs.
        /// </summary>
        CppContainerList<CppClass> Classes { get; }

        /// <summary>
        /// Gets the typedefs.
        /// </summary>
        CppContainerList<CppTypedef> Typedefs { get; }

        /// <summary>
        /// Gets the attributes.
        /// </summary>
        CppContainerList<CppAttribute> Attributes { get; }
    }
}
