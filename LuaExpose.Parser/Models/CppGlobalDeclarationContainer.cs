// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CppAst
{
    /// <summary>
    /// A base Cpp container for macros, classes, fields, functions, enums, typedefs.
    /// </summary>
    public class CppGlobalDeclarationContainer : CppElement, ICppGlobalDeclarationContainer
    {
        private readonly Dictionary<ICppContainer, Dictionary<string, CacheByName>> _multiCacheByName;

        public CppGlobalDeclarationContainer()
        {
            _multiCacheByName = new Dictionary<ICppContainer, Dictionary<string, CacheByName>>(ReferenceEqualityComparer<ICppContainer>.Default);
            Macros = new List<CppMacro>();
            Fields = new CppContainerList<CppField>(this);
            Functions = new CppContainerList<CppFunction>(this);
            Enums = new CppContainerList<CppEnum>(this);
            Classes = new CppContainerList<CppClass>(this);
            Typedefs = new CppContainerList<CppTypedef>(this);
            Namespaces = new CppContainerList<CppNamespace>(this);
            Attributes = new CppContainerList<CppAttribute>(this);
        }

        public List<CppMacro> Macros { get; }

        public CppContainerList<CppField> Fields { get; }

        public CppContainerList<CppFunction> Functions { get; }

        public CppContainerList<CppEnum> Enums { get; }

        public CppContainerList<CppClass> Classes { get; }

        public CppContainerList<CppTypedef> Typedefs { get; }

        public CppContainerList<CppNamespace> Namespaces { get; }

        public CppContainerList<CppAttribute> Attributes { get; }

        public virtual IEnumerable<ICppDeclaration> Children() => CppContainerHelper.Children(this);

        /// <summary>
        /// Find a CppElement by name declared directly by this container.
        /// </summary>
        public CppElement FindByName(string name)
        {
            return FindByName(this, name);
        }

        /// <summary>
        /// Find a CppElement by name declared within the specified container.
        /// </summary>
        public CppElement FindByName(ICppContainer container, string name)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var cacheByName = FindByNameInternal(container, name);
            return cacheByName.Element;
        }

        /// <summary>
        /// Find a CppElement by name and type declared directly by this container.
        /// </summary>
        public TCppElement FindByName<TCppElement>(string name) where TCppElement : CppElement
        {
            return FindByName<TCppElement>(this, name);
        }

        /// <summary>
        /// Find a CppElement by name and type declared within the specified container.
        /// </summary>
        public TCppElement FindByName<TCppElement>(ICppContainer container, string name) where TCppElement : CppElement
        {
            return (TCppElement)FindByName(container, name);
        }

        /// <summary>
        /// Find a list of CppElement matching name declared within the specified container.
        /// </summary>
        public IEnumerable<CppElement> FindListByName(ICppContainer container, string name)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (name == null) throw new ArgumentNullException(nameof(name));

            var cacheByName = FindByNameInternal(container, name);
            return cacheByName;
        }

        /// <summary>
        /// Clear the cache used by all FindByName functions.
        /// </summary>
        public void ClearCacheByName()
        {
            _multiCacheByName.Clear();
        }

        private CacheByName FindByNameInternal(ICppContainer container, string name)
        {
            if (!_multiCacheByName.TryGetValue(container, out var cacheByNames))
            {
                cacheByNames = new Dictionary<string, CacheByName>();
                _multiCacheByName.Add(container, cacheByNames);

                foreach (var element in container.Children())
                {
                    var cppElement = (CppElement)element;
                    if (element is ICppMember member && !string.IsNullOrEmpty(member.Name))
                    {
                        var elementName = member.Name;
                        if (!cacheByNames.TryGetValue(elementName, out var cacheByName))
                        {
                            cacheByName = new CacheByName();
                        }

                        if (cacheByName.Element == null)
                        {
                            cacheByName.Element = cppElement;
                        }
                        else
                        {
                            if (cacheByName.List == null)
                            {
                                cacheByName.List = new List<CppElement>();
                            }
                            cacheByName.List.Add(cppElement);
                        }

                        cacheByNames[elementName] = cacheByName;
                    }
                }
            }

            return cacheByNames.TryGetValue(name, out var cacheByNameFound) ? cacheByNameFound : new CacheByName();
        }

        private struct CacheByName : IEnumerable<CppElement>
        {
            public CppElement Element;
            public List<CppElement> List;

            public IEnumerator<CppElement> GetEnumerator()
            {
                if (Element != null) yield return Element;
                if (List != null)
                {
                    foreach (var cppElement in List)
                    {
                        yield return cppElement;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }

    internal class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    {
        public static readonly ReferenceEqualityComparer<T> Default = new ReferenceEqualityComparer<T>();

        private ReferenceEqualityComparer()
        {
        }

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}
