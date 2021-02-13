using CppAst;
using Scriban;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaExpose
{
    public class TypeScriptCodeGenWriter : CodeGenWriter
    {
        public class TypeScriptVariable
        {
            public string Name;
            public string Type;
            public bool IsStatic;
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
            public TypeScriptFunction(string Name, string ReturnType, List<TypeScriptVariable> Parameters)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
                this.Parameters = Parameters;
            }
            public TypeScriptFunction(CppFunction func, string className, CppTypedef specialization)
            {
                Name = func.GetName();
                ReturnType = func.ReturnType.ConvertToTypeScriptType(specialization);
                IsStatic = func.StorageQualifier == CppStorageQualifier.Static;
                if (IsStatic)
                {
                    Parameters.Add(new TypeScriptVariable { Name = "this", Type = "void" });
                }
                Parameters.AddRange(func.Parameters.Select(param => new TypeScriptVariable {
                    Name = param.GetTypeScriptName(),
                    Type = param.Type.ConvertToTypeScriptType(specialization)
                }).ToList());

                Dictionary<string, string> typeRemapping = new Dictionary<string, string>();
                if (func.IsExposedFunc() && func.Attributes[0].Arguments != null)
                {
                    foreach (string arg in func.Attributes[0].Arguments.Split(","))
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
                        .Select(func => new TypeScriptFunction
                        (
                            "new",
                            Name,
                            func.Parameters.Select(param => new TypeScriptVariable { Name = param.GetTypeScriptName(), Type = param.Type.ConvertToTypeScriptType(Specialization) }).ToList()
                        )));

                    if (!isBaseClass)
                    {
                        Constructors.AddRange(cppClass.Functions
                        .Where(func => func.IsConstructor())
                        .Select(func => new TypeScriptFunction
                        (
                            "new",
                            Name,
                            func.Parameters.Select(param => new TypeScriptVariable { Name = param.GetTypeScriptName(), Type = param.Type.ConvertToTypeScriptType(Specialization) }).ToList()
                        )));
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
                        Type = field.Type.ConvertToTypeScriptType(Specialization),
                        IsStatic = field.StorageQualifier == CppStorageQualifier.Static
                    }).ToList());

                Functions.AddRange(cppClass.Functions
                    .Where(func => func.IsExposedFunc() && (!isBaseClass || func.StorageQualifier != CppStorageQualifier.Static))
                    .Select(func => new TypeScriptFunction(func, Name, Specialization)));
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
                    func.ReturnType.ConvertToTypeScriptType(),
                    func.Parameters.Select(param => new TypeScriptVariable { Name = param.GetTypeScriptName(), Type = param.Type.ConvertToTypeScriptType() }).ToList()
                )));

                Functions.ForEach(func => func.Parameters.Insert(0, new TypeScriptVariable { Name = "this", Type = "void" }));
            }
        }

        public TypeScriptCodeGenWriter(CppCompilation compilation, string scrib) : base(compilation, scrib)
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
                    if (scriptNamespace.Name == "siege")
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

            var scribe = Template.Parse(File.ReadAllText(template));
            return scribe.Render(new { Classes = classes, Enums = enums, Namespaces = namespaces, Globals = globalNamespace });
        }

        protected override string GetUsertypeFileName(string usertypeFile)
        {
            return $"{Path.GetFileNameWithoutExtension(usertypeFile).FirstCharToUpper()}.d.ts";
        }

        protected override void WriteAllFiles(string v, bool isGame)
        {
            base.WriteAllFiles(v, isGame);

            // TODO(jmcmorris): We need proper topological sorting of the usertype files so that dependencies are loaded first
    //        var newFileName = "..\\..\\tlconfig.lua";
    //        var newPath = Path.Combine(v, newFileName);
    //        string content = @"return {
    //include_dir = {
    //    'src\\teal\\',
    //},
    //preload_modules = {";

    //        content += string.Join(",", userTypeFiles.Select(usertypeFile => $"\n    '{Path.GetFileNameWithoutExtension(usertypeFile.Key).FirstCharToUpper()}'"));
    //        content += "\n    }\n}\n";
    //        WriteFileContent(content, newPath);
        }


    }
}
