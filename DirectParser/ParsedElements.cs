using System.Collections.Generic;

namespace LuaExpose.DirectParser
{
    public class ParsedFile
    {
        public string FilePath { get; set; }
        public ParsedScope GlobalScope { get; set; } = new ParsedScope();
        public List<ParsedNamespace> Namespaces { get; set; } = new List<ParsedNamespace>();
    }

    public class ParsedScope
    {
        public List<ParsedClass> Classes { get; set; } = new List<ParsedClass>();
        public List<ParsedFunction> Functions { get; set; } = new List<ParsedFunction>();
        public List<ParsedEnum> Enums { get; set; } = new List<ParsedEnum>();
        public List<ParsedField> Fields { get; set; } = new List<ParsedField>();
    }

    public class ParsedNamespace : ParsedScope
    {
        public string Name { get; set; }
        public List<ParsedAttribute> Attributes { get; set; } = new List<ParsedAttribute>();
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }
    }

    public class ParsedClass
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public List<string> BaseClasses { get; set; } = new List<string>();
        public List<ParsedAttribute> Attributes { get; set; } = new List<ParsedAttribute>();
        public List<ParsedFunction> Functions { get; set; } = new List<ParsedFunction>();
        public List<ParsedField> Fields { get; set; } = new List<ParsedField>();
        public int StartOffset { get; set; }
        public int EndOffset { get; set; }

        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
    }

    public class ParsedFunction
    {
        public string Name { get; set; }
        public string ReturnType { get; set; }
        public List<ParsedParameter> Parameters { get; set; } = new List<ParsedParameter>();
        public List<ParsedAttribute> Attributes { get; set; } = new List<ParsedAttribute>();
        public List<string> TemplateParameters { get; set; } = new List<string>();
        public bool IsStatic { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsConst { get; set; }
        public bool IsOverride { get; set; }
        public string Namespace { get; set; }
        public ParsedClass ContainingClass { get; set; }

        public string FullName
        {
            get
            {
                if (ContainingClass != null)
                    return $"{ContainingClass.FullName}::{Name}";
                return string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
            }
        }

        public string GetSignature()
        {
            var paramStrings = new List<string>();
            foreach (var param in Parameters)
            {
                paramStrings.Add(param.Type);
            }
            return $"{ReturnType} {Name}({string.Join(", ", paramStrings)}){(IsConst ? " const" : "")}";
        }
    }

    public class ParsedParameter
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string DefaultValue { get; set; }
    }

    public class ParsedEnum
    {
        public string Name { get; set; }
        public string UnderlyingType { get; set; }
        public string Namespace { get; set; }
        public List<ParsedAttribute> Attributes { get; set; } = new List<ParsedAttribute>();
        public List<ParsedEnumValue> Values { get; set; } = new List<ParsedEnumValue>();

        public string FullName => string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
    }

    public class ParsedEnumValue
    {
        public string Name { get; set; }
        public string Value { get; set; }
    }

    public class ParsedField
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public List<ParsedAttribute> Attributes { get; set; } = new List<ParsedAttribute>();
        public bool IsStatic { get; set; }
        public bool IsConst { get; set; }
        public string Namespace { get; set; }
        public ParsedClass ContainingClass { get; set; }

        public string FullName
        {
            get
            {
                if (ContainingClass != null)
                    return $"{ContainingClass.FullName}::{Name}";
                return string.IsNullOrEmpty(Namespace) ? Name : $"{Namespace}::{Name}";
            }
        }
    }

    public class ParsedAttribute
    {
        public string Name { get; set; }
        public Dictionary<string, string> Arguments { get; set; } = new Dictionary<string, string>();

        public bool HasArgument(string key) => Arguments.ContainsKey(key);
        public string GetArgument(string key) => Arguments.TryGetValue(key, out var value) ? value : null;
        
        public List<string> GetTemplateArguments()
        {
            var args = new List<string>();
            int i = 0;
            while (Arguments.TryGetValue($"arg{i}", out var arg))
            {
                args.Add(arg);
                i++;
            }
            return args;
        }
    }
}