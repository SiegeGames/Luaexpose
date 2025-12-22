using CppAst;
using Scriban;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LuaExpose
{
    public class TypeScriptCodeGenWriter : CodeGenWriter
    {
        private HashSet<string> _existingAliasNames;

        public class TypeScriptVariable
        {
            public string Name;
            public string Type;
            public bool IsStatic;
        }

        public class TypeScriptAlias
        {
            public string Name;
            public string Type;
        }

        public class TypeScriptFunction
        {
            public string Name;
            public string ReturnType;
            public List<TypeScriptVariable> Parameters = new List<TypeScriptVariable>();
            public List<string> Generics = new List<string>();
            public bool HasGenerics { get { return Generics.Count > 0; } }
            public bool IsStatic = false;
            public bool IsPrivate = false;
            public TypeScriptFunction(string Name, string ReturnType)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
            }
            public TypeScriptFunction(string Name, string ReturnType, CppFunction func, CppTypedef specialization)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
                this.Parameters = func.Parameters.Select(param => new TypeScriptVariable {
                    Name = param.GetTypeScriptName(),
                    Type = NormalizeParameterType(param.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Parameter, specialization))
                }).ToList();

                remapTypes(func);
            }
            public TypeScriptFunction(CppFunction func, string className, CppTypedef specialization)
            {
                Name = func.GetName();
                ReturnType = func.ReturnType.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Return, specialization);
                IsStatic = func.StorageQualifier == CppStorageQualifier.Static;
                if (IsStatic)
                {
                    Parameters.Add(new TypeScriptVariable { Name = "this", Type = "void" });
                }
                Parameters.AddRange(func.Parameters.Select(param => new TypeScriptVariable {
                    Name = param.GetTypeScriptName(),
                    Type = NormalizeParameterType(param.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Parameter, specialization))
                }).ToList());
                remapTypes(func);
            }

            private static string NormalizeParameterType(string type)
            {
                if (type == "FileSaveCompleteHandler")
                {
                    return "FileSaveCompleteHandler ";
                }

                return type;
            }

            private void remapTypes(CppFunction func)
            {
                Dictionary<string, string> typeRemapping = new Dictionary<string, string>();
                if ((func.IsExposedFunc() || func.IsConstructor()) && func.Attributes[0].Arguments != null)
                {
                    string pattern = @"(?<!\[),(?<!\[[^\]]*,)(?![^\]]*\],)";
                    foreach (string arg in Regex.Split(func.Attributes[0].Arguments, pattern))
                    {
                        string[] values = arg.Split("=");
                        if (values.Length == 2)
                        {
                            typeRemapping[values[0]] = values[1];
                        }
                    }
                }
                if (typeRemapping.Count > 0)
                {
                    if (func.IsNormalGenericFunc())
                    {
                        Generics = typeRemapping.Values.Distinct().ToList();
                    }
                    foreach (TypeScriptVariable parameter in Parameters)
                    {
                        if (typeRemapping.ContainsKey(parameter.Name))
                        {
                            parameter.Type = typeRemapping[parameter.Name];
                        }
                    }
                    if (typeRemapping.ContainsKey("return"))
                    {
                        ReturnType = typeRemapping["return"];
                    }
                }
                ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
            }
        }

        public class TypeScriptEnum
        {
            public string Name;
            public List<string> Items = new List<string>();

            public TypeScriptEnum(CppEnum parsedEnum)
            {
                Name = parsedEnum.Name;
                Items = parsedEnum.Items.Select(item => item.Name).ToList();
            }
        }

        public class TypeScriptClass
        {
            public string Name;
            public List<TypeScriptFunction> Constructors = new List<TypeScriptFunction>();
            public List<TypeScriptFunction> Functions = new List<TypeScriptFunction>();
            public List<TypeScriptVariable> Fields = new List<TypeScriptVariable>();
            public List<TypeScriptVariable> OptionalFields = new List<TypeScriptVariable>();
            public CppTypedef Specialization;
            public bool HasConstructors { get { return Constructors.Count > 0; } }

            public TypeScriptClass(CppClass cppClass, CppTypedef specialization = null)
            {
                Name = specialization != null ? specialization.Name : cppClass.Name;
                Specialization = specialization;
                addClass(cppClass, false);
            }

            public void addClass(CppClass cppClass, bool isBaseClass)
            {
                foreach (CppBaseType baseType in cppClass.BaseTypes)
                {
                    if (baseType.Type.TypeKind == CppTypeKind.StructOrClass || baseType.Type.TypeKind == CppTypeKind.Typedef)
                    {
                        addClass(baseType.Type as CppClass, true);
                    }
                }

                if (!cppClass.Attributes.Where(x => x.Name == "LUA_USERTYPE_NO_CTOR").Any())
                {
                    Constructors.AddRange(cppClass.Constructors
                        .Where(func => func.IsConstructor())
                        .Select(func => new TypeScriptFunction("new", Name, func, Specialization)));

                    if (!isBaseClass)
                    {
                        Constructors.AddRange(cppClass.Functions
                        .Where(func => func.IsConstructor() && func.StorageQualifier == CppStorageQualifier.Static)
                        .Select(func => new TypeScriptFunction("new", Name, func, Specialization)));
                    }
                    if (Constructors.Count == 0 && !isBaseClass)
                    {
                        Constructors.Add(new TypeScriptFunction("new", Name));
                    }
                } else if (!isBaseClass)
                {
                    var constructor = new TypeScriptFunction("new", Name);
                    constructor.IsPrivate = true;
                    Constructors.Add(constructor);
                }

                Fields.AddRange(cppClass.Fields
                    .Where(field => field.IsNormalVar() || field.IsReadOnly())
                    .Select(field => new TypeScriptVariable
                    {
                        Name = field.Name,
                        Type = field.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Field, Specialization),
                        IsStatic = field.StorageQualifier == CppStorageQualifier.Static
                    }).ToList());

                OptionalFields.AddRange(cppClass.Fields
                    .Where(field => field.IsOptionalVar())
                    .Select(field => new TypeScriptVariable
                    {
                        Name = field.Name,
                        Type = field.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Field, Specialization),
                        IsStatic = field.StorageQualifier == CppStorageQualifier.Static
                    }).ToList());

                Fields.AddRange(cppClass.Functions
                    .Where(func => func.IsPropertyGetter())
                    .Select(func => new TypeScriptVariable
                    {
                        Name = func.Name.StartsWith("get_") ? func.Name.Substring(4) : func.Name,
                        Type = func.ReturnType.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Return, Specialization),
                    }).ToList());

                Functions.AddRange(cppClass.Functions
                    .Where(func => func.Name != "isSerializable" || cppClass.Name == "LuaComponent")
                    .Where(func => (func.IsNormalFunc() || func.IsOverloadFunc() || func.IsTemplateFunc()) && (!isBaseClass || func.StorageQualifier != CppStorageQualifier.Static))
                    .Select(func => new TypeScriptFunction(func, Name, Specialization)));

                // Ideally we'd pull out the function attribute arguments that has all of the specializations we want to expose
                // Foreach one of these specializations we'd create a new TypeScriptFunction that'd get exposed
                //foreach (var function in cppClass.Functions)
                //{
                //    if (function.IsTemplateFunc() && (!isBaseClass || function.StorageQualifier != CppStorageQualifier.Static))
                //    {
                //        var attrParams = function.Attributes[0].Arguments.Split(',');
                //        foreach (var param in attrParams)
                //        {
                //            Functions.Add(new TypeScriptFunction(function, Name, param));
                //        }
                //    }
                //}
            }
        }

        public class TypeScriptNamespace
        {
            public string Name;
            public List<TypeScriptFunction> Functions = new List<TypeScriptFunction>();
            public List<TypeScriptVariable> Fields = new List<TypeScriptVariable>();
            public TypeScriptNamespace() { }
            public TypeScriptNamespace(LuaUserType userType)
            {
                Name = userType.TypeNameLower;
                if (Name == "var")
                    Name = "Var";

                Functions.AddRange((userType.OriginalElement as CppNamespace).Functions
                .Where(func => func.IsExposedFunc())
                .Select(func => new TypeScriptFunction
                (
                    func.GetName(),
                    func.ReturnType.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Return),
                    func,
                    null
                )));

                Functions.ForEach(func => func.Parameters.Insert(0, new TypeScriptVariable { Name = "this", Type = "void" }));

                Fields.AddRange((userType.OriginalElement as CppNamespace).Fields
                    .Where(field => field.IsNormalVar())
                    .Select(field => new TypeScriptVariable
                    {
                        Name = field.GetName(),
                        Type = field.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Field),
                        IsStatic = field.StorageQualifier == CppStorageQualifier.Static
                    })
                );
            }
        }

        public TypeScriptCodeGenWriter(CppCompilation compilation, string scrib, string rootNS) : base(compilation, scrib, rootNS)
        {
            userTypeFilePattern = "*.d.ts";
        }


        protected override string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName, bool isGame)
        {
            LinkedList<TypeScriptClass> classes = new LinkedList<TypeScriptClass>();
            LinkedList<TypeScriptEnum> enums = new LinkedList<TypeScriptEnum>();
            LinkedList<TypeScriptNamespace> namespaces = new LinkedList<TypeScriptNamespace>();
            TypeScriptNamespace globalNamespace = new TypeScriptNamespace();

            bool FindSpecializationInNamespace(CppClass cppClass, CppNamespace cppNamespace, string specialization)
            {
                foreach (var cppTypedef in cppNamespace.Typedefs)
                {
                    if (cppTypedef.Name == specialization)
                    {
                        classes.AddLast(new TypeScriptClass(cppClass, cppTypedef as CppTypedef));
                        return true;
                    }
                }
                foreach (var innerNamespace in cppNamespace.Namespaces)
                {
                    if (FindSpecializationInNamespace(cppClass, innerNamespace, specialization))
                    {
                        return true;
                    }
                }
                return false;
            }

            foreach (var lu in value.userTypes.Values)
            {
                if (lu.IsClass)
                {
                    foreach (var cppClass in lu.Classes)
                    {
                        var isTemplated = cppClass.Attributes.Where(x => x.Name == "LUA_USERTYPE_TEMPLATE");
                        if (isTemplated.Any())
                        {
                            foreach (string specialization in isTemplated.First().Arguments.Split(','))
                            {
                                FindSpecializationInNamespace(cppClass, rootNamespace, specialization);
                            }
                        }
                        else
                        {
                            classes.AddLast(new TypeScriptClass(cppClass));
                        }
                    }
                }
                else if (lu.IsNamespace)
                {
                    TypeScriptNamespace scriptNamespace = new TypeScriptNamespace(lu);
                    if (scriptNamespace.Name == generatedNamespace)
                    {
                        globalNamespace = scriptNamespace;
                    }
                    else
                    {
                        namespaces.AddLast(scriptNamespace);
                    }
                }
                foreach (var parsedEnum in lu.Enums)
                {
                    enums.AddLast(new TypeScriptEnum(parsedEnum));
                }
            }

            ApplyLegacyOrder(fileName, classes, enums, namespaces);

            var scribe = Template.Parse(File.ReadAllText(template));
            var aliases = new List<TypeScriptAlias>();
            if (string.Equals(fileName, "Types", StringComparison.OrdinalIgnoreCase))
            {
                var existingAliases = GetExistingAliasNames();
                foreach (var kvp in CppExtenstions.GetTypedefAliases().OrderBy(kvp => kvp.Key))
                {
                    if (existingAliases.Contains(kvp.Key))
                    {
                        continue;
                    }
                    aliases.Add(new TypeScriptAlias { Name = kvp.Key, Type = kvp.Value });
                }
            }

            return scribe.Render(new { Classes = classes, Enums = enums, Namespaces = namespaces, Globals = globalNamespace, Aliases = aliases });
        }

        private HashSet<string> GetExistingAliasNames()
        {
            if (_existingAliasNames != null)
            {
                return _existingAliasNames;
            }

            _existingAliasNames = new HashSet<string>(StringComparer.Ordinal);
            var definitionsPath = GetDefinitionsTypesPath();
            if (string.IsNullOrWhiteSpace(definitionsPath) || !File.Exists(definitionsPath))
            {
                return _existingAliasNames;
            }

            var content = File.ReadAllText(definitionsPath);
            foreach (Match match in Regex.Matches(content, @"^\s*type\s+(?<name>[A-Za-z_]\w*)\s*=", RegexOptions.Multiline))
            {
                var name = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _existingAliasNames.Add(name);
                }
            }

            return _existingAliasNames;
        }

        private string GetDefinitionsTypesPath()
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return null;
            }

            var templateDir = Path.GetDirectoryName(template);
            if (string.IsNullOrWhiteSpace(templateDir))
            {
                return null;
            }

            var rootDir = Directory.GetParent(templateDir)?.FullName;
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                return null;
            }

            return Path.Combine(rootDir, "src", "scripts", "definitions", "types.d.ts");
        }

        private void ApplyLegacyOrder(string fileName, LinkedList<TypeScriptClass> classes, LinkedList<TypeScriptEnum> enums, LinkedList<TypeScriptNamespace> namespaces)
        {
            var legacyPath = GetLegacyOrderPath(fileName);
            if (string.IsNullOrWhiteSpace(legacyPath) || !File.Exists(legacyPath))
            {
                return;
            }

            var content = File.ReadAllText(legacyPath);
            var classOrder = GetLegacyOrder(content, @"^\s*class\s+(?<name>[A-Za-z_]\w*)");
            var enumOrder = GetLegacyOrder(content, @"^\s*enum\s+(?<name>[A-Za-z_]\w*)");
            var namespaceOrder = GetLegacyOrder(content, @"^\s*namespace\s+(?<name>[A-Za-z_]\w*)");

            if (classOrder.Count > 0)
            {
                ReplaceList(classes, ReorderLinkedList(classes, classOrder, item => item.Name));
            }

            if (enumOrder.Count > 0)
            {
                ReplaceList(enums, ReorderLinkedList(enums, enumOrder, item => item.Name));
            }

            if (namespaceOrder.Count > 0)
            {
                ReplaceList(namespaces, ReorderLinkedList(namespaces, namespaceOrder, item => item.Name));
            }
        }

        private string GetLegacyOrderPath(string fileName)
        {
            var outputPath = Path.Combine(outputDirectory, GetUsertypeFileName(fileName));
            if (File.Exists(outputPath))
            {
                return outputPath;
            }

            if (!string.IsNullOrWhiteSpace(template))
            {
                var templateDir = Path.GetDirectoryName(template);
                if (!string.IsNullOrWhiteSpace(templateDir))
                {
                    var rootDir = Path.GetFullPath(Path.Combine(templateDir, ".."));
                    var candidate = Path.Combine(rootDir, "src", "scripts", generatedNamespace, GetUsertypeFileName(fileName));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private static List<string> GetLegacyOrder(string content, string pattern)
        {
            var matches = Regex.Matches(content, pattern, RegexOptions.Multiline);
            var order = new List<string>();
            foreach (Match match in matches)
            {
                var name = match.Groups["name"].Value;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    order.Add(name);
                }
            }
            return order;
        }

        private static LinkedList<T> ReorderLinkedList<T>(LinkedList<T> list, List<string> order, Func<T, string> selector)
        {
            var map = list.ToLookup(item => selector(item), StringComparer.Ordinal);
            var used = new HashSet<T>();
            var reordered = new LinkedList<T>();

            foreach (var name in order)
            {
                foreach (var item in map[name])
                {
                    if (used.Add(item))
                    {
                        reordered.AddLast(item);
                    }
                }
            }

            foreach (var item in list)
            {
                if (used.Add(item))
                {
                    reordered.AddLast(item);
                }
            }

            return reordered;
        }

        private static void ReplaceList<T>(LinkedList<T> target, LinkedList<T> replacement)
        {
            target.Clear();
            foreach (var item in replacement)
            {
                target.AddLast(item);
            }
        }

        protected override string GetUsertypeFileName(string usertypeFile)
        {
            return $"{Path.GetFileNameWithoutExtension(usertypeFile).FirstCharToUpper()}.d.ts";
        }

        protected override void WriteAllFiles(string v, bool isGame)
        {
            CppExtenstions.SetLegacyPreservedTypedefs(GetLegacyPreservedTypedefs());

            var files = userTypeFiles.Keys.ToList();
            files.Sort((left, right) =>
            {
                var leftName = GetUsertypeFileName(left);
                var rightName = GetUsertypeFileName(right);

                var leftIsTypes = string.Equals(leftName, "Types.d.ts", StringComparison.OrdinalIgnoreCase);
                var rightIsTypes = string.Equals(rightName, "Types.d.ts", StringComparison.OrdinalIgnoreCase);

                if (leftIsTypes && !rightIsTypes)
                {
                    return 1;
                }

                if (!leftIsTypes && rightIsTypes)
                {
                    return -1;
                }

                return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var file in files)
            {
                var newPath = Path.Combine(v, GetUsertypeFileName(file));
                if (!ForceWriteOutputs
                    && File.Exists(newPath)
                    && (new FileInfo(newPath)).LastWriteTime > (new FileInfo(userTypeFiles[file].path)).LastWriteTime)
                {
                    continue;
                }
                var content = GetContentFromLuaUserFile(userTypeFiles[file], Path.GetFileNameWithoutExtension(file), isGame);
                WriteFileContent(content, newPath);
            }
        }

        private HashSet<string> GetLegacyPreservedTypedefs()
        {
            var typedefNames = CppExtenstions.GetTypedefNames();
            if (typedefNames == null || typedefNames.Count == 0)
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var legacyDir = GetLegacyRootPath();
            if (string.IsNullOrWhiteSpace(legacyDir) || !Directory.Exists(legacyDir))
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }

            var typedefLookup = new HashSet<string>(typedefNames, StringComparer.Ordinal);
            var preserved = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in Directory.EnumerateFiles(legacyDir, "*.d.ts"))
            {
                var content = File.ReadAllText(file);
                foreach (Match match in Regex.Matches(content, @"\b[A-Za-z_]\w*\b"))
                {
                    var token = match.Value;
                    if (typedefLookup.Contains(token)
                        || token.EndsWith("ID", StringComparison.Ordinal)
                        || token.EndsWith("Handle", StringComparison.Ordinal))
                    {
                        preserved.Add(token);
                    }
                }
            }

            return preserved;
        }

        private string GetLegacyRootPath()
        {
            if (string.IsNullOrWhiteSpace(template))
            {
                return null;
            }

            var templateDir = Path.GetDirectoryName(template);
            if (string.IsNullOrWhiteSpace(templateDir))
            {
                return null;
            }

            var rootDir = Directory.GetParent(templateDir)?.FullName;
            if (string.IsNullOrWhiteSpace(rootDir))
            {
                return null;
            }

            return Path.Combine(rootDir, "src", "scripts", generatedNamespace);
        }


    }
}
