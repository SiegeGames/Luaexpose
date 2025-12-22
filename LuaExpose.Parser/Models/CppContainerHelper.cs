// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// Helper for container children enumeration.
    /// </summary>
    public static class CppContainerHelper
    {
        public static IEnumerable<ICppDeclaration> Children(ICppDeclarationContainer container)
        {
            foreach (var item in container.Fields)
            {
                yield return item;
            }
            foreach (var item in container.Functions)
            {
                yield return item;
            }
            foreach (var item in container.Enums)
            {
                yield return item;
            }
            foreach (var item in container.Classes)
            {
                yield return item;
            }
            foreach (var item in container.Typedefs)
            {
                yield return item;
            }

            if (container is ICppGlobalDeclarationContainer globalContainer)
            {
                foreach (var item in globalContainer.Namespaces)
                {
                    yield return item;
                }
            }
        }
    }
}
