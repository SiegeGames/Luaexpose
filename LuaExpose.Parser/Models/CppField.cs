// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System;
using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ field (of a struct/class) or global variable.
    /// </summary>
    public sealed class CppField : CppDeclaration, ICppMemberWithVisibility
    {
        public CppField(CppType type, string name)
        {
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Name = name;
        }

        public CppVisibility Visibility { get; set; }

        public CppStorageQualifier StorageQualifier { get; set; }

        public List<CppAttribute> Attributes { get; set; }

        public CppType Type { get; set; }

        public string Name { get; set; }

        public bool IsAnonymous { get; set; }

        public CppValue InitValue { get; set; }

        public CppExpression InitExpression { get; set; }

        public bool IsBitField { get; set; }

        public int BitFieldWidth { get; set; }

        public override string ToString()
        {
            var builder = new StringBuilder();

            if (Visibility != CppVisibility.Default)
            {
                builder.Append(Visibility.ToString().ToLowerInvariant());
                builder.Append(" ");
            }

            if (StorageQualifier != CppStorageQualifier.None)
            {
                builder.Append(StorageQualifier.ToString().ToLowerInvariant());
                builder.Append(" ");
            }

            builder.Append(Type.GetDisplayName());
            builder.Append(" ");
            builder.Append(Name);

            if (InitExpression != null)
            {
                builder.Append(" = ");
                var initExpressionStr = InitExpression.ToString();
                if (string.IsNullOrEmpty(initExpressionStr))
                {
                    builder.Append(InitValue);
                }
                else
                {
                    builder.Append(initExpressionStr);
                }
            }
            else if (InitValue != null)
            {
                builder.Append(" = ");
                builder.Append(InitValue);
            }

            return builder.ToString();
        }
    }
}
