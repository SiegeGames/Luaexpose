// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections;
using System.Collections.Generic;

namespace CppAst
{
    /// <summary>
    /// A generic list of <see cref="CppElement"/> held by a <see cref="ICppContainer"/>
    /// </summary>
    public class CppContainerList<TElement> : IList<TElement> where TElement : CppElement
    {
        private readonly List<TElement> _elements;

        public CppContainerList(ICppContainer container)
        {
            Container = container ?? throw new ArgumentNullException(nameof(container));
            _elements = new List<TElement>();
        }

        public ICppContainer Container { get; }

        public IEnumerator<TElement> GetEnumerator() => _elements.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_elements).GetEnumerator();

        public void Add(TElement item)
        {
            item.Parent = Container;
            _elements.Add(item);
        }

        public void AddRange(IEnumerable<TElement> collection)
        {
            if (collection != null)
            {
                foreach (var element in collection)
                {
                    Add(element);
                }
            }
        }

        public void Clear()
        {
            foreach (var element in _elements)
            {
                element.Parent = null;
            }
            _elements.Clear();
        }

        public bool Contains(TElement item) => _elements.Contains(item);

        public void CopyTo(TElement[] array, int arrayIndex) => _elements.CopyTo(array, arrayIndex);

        public bool Remove(TElement item)
        {
            if (_elements.Remove(item))
            {
                item.Parent = null;
                return true;
            }
            return false;
        }

        public int Count => _elements.Count;

        public bool IsReadOnly => false;

        public int IndexOf(TElement item) => _elements.IndexOf(item);

        public void Insert(int index, TElement item)
        {
            item.Parent = Container;
            _elements.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            var element = _elements[index];
            element.Parent = null;
            _elements.RemoveAt(index);
        }

        public TElement this[int index]
        {
            get => _elements[index];
            set => _elements[index] = value;
        }
    }
}
