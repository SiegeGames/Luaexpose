// LuaExpose.Parser - Custom C++ Parser without libclang dependency

using System.Collections.Generic;
using System.Text;

namespace CppAst
{
    /// <summary>
    /// A C++ function/method declaration.
    /// </summary>
    public sealed class CppFunction : CppDeclaration, ICppMemberWithVisibility, ICppTemplateOwner, ICppContainer
    {
        public CppFunction(string name)
        {
            Name = name;
            Parameters = new CppContainerList<CppParameter>(this);
            TemplateParameters = new List<CppType>();
            Attributes = new CppContainerList<CppAttribute>(this);
        }

        public CppVisibility Visibility { get; set; }

        public CppCallingConvention CallingConvention { get; set; }

        public CppContainerList<CppAttribute> Attributes { get; }

        public CppStorageQualifier StorageQualifier { get; set; }

        public CppLinkageKind LinkageKind { get; set; }

        public CppType ReturnType { get; set; }

        public bool IsConstructor { get; set; }

        public string Name { get; set; }

        public CppContainerList<CppParameter> Parameters { get; }

        public CppFunctionFlags Flags { get; set; }

        public List<CppType> TemplateParameters { get; }

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

            if ((Flags & CppFunctionFlags.Virtual) != 0)
            {
                builder.Append("virtual ");
            }

            if (!IsConstructor)
            {
                if (ReturnType != null)
                {
                    builder.Append(ReturnType.GetDisplayName());
                    builder.Append(" ");
                }
                else
                {
                    builder.Append("void ");
                }
            }

            builder.Append(Name);
            builder.Append("(");
            for (var i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                if (i > 0) builder.Append(", ");
                builder.Append(param);
            }

            builder.Append(")");

            if ((Flags & CppFunctionFlags.Const) != 0)
            {
                builder.Append(" const");
            }

            if ((Flags & CppFunctionFlags.Pure) != 0)
            {
                builder.Append(" = 0");
            }
            return builder.ToString();
        }

        public IEnumerable<ICppDeclaration> Children() => Parameters;
    }
}
