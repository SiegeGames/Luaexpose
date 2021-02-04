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
    public class TealCodeGenWriter : CodeGenWriter
    {
        public class TealVariable
        {
            public string Name;
            public string Type;
            public bool IsStatic;
        }

        public class TealFunction
        {
            public string Name;
            public string ReturnType;
            public List<TealVariable> Parameters = new List<TealVariable>();
            public List<string> Generics = new List<string>();
            public bool HasGenerics { get { return Generics.Count > 0; } }
            public bool IsStatic = false;
            public TealFunction(string Name, string ReturnType)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
            }
            public TealFunction(string Name, string ReturnType, List<TealVariable> Parameters)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
                this.Parameters = Parameters;
            }
            public TealFunction(CppFunction func, string className, CppTypedef specialization)
            {
                Name = func.GetName();
                ReturnType = func.ReturnType.ConvertToTealType(specialization);
                IsStatic = func.StorageQualifier == CppStorageQualifier.Static;
                if (IsStatic)
                {
                    Parameters.Add(new TealVariable { Name = "this", Type = "void" });
                }
                Parameters.AddRange(func.Parameters.Select(param => new TealVariable {
                    Name = param.GetTealName(),
                    Type = param.Type.ConvertToTealType(specialization)
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
                    foreach (TealVariable parameter in Parameters)
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

        public class TealEnum
        {
            public string Name;
            public List<string> Items = new List<string>();

            public TealEnum(CppEnum parsedEnum)
            {
                Name = parsedEnum.Name;
                Items = parsedEnum.Items.Select(item => item.Name).ToList();
            }
        }

        public class TealClass
        {
            public string Name;
            public List<TealFunction> Constructors = new List<TealFunction>();
            public List<TealFunction> Functions = new List<TealFunction>();
            public List<TealVariable> Fields = new List<TealVariable>();
            public CppTypedef Specialization;
            public bool HasConstructors { get { return Constructors.Count > 0; } }

            public TealClass(CppClass cppClass, CppTypedef specialization = null)
            {
                Name = specialization != null ? specialization.Name : cppClass.Name;
                Specialization = specialization;
                addClass(cppClass);
            }

            public void addClass(CppClass cppClass)
            {
                foreach (CppBaseType baseType in cppClass.BaseTypes)
                {
                    if (baseType.Type.TypeKind == CppTypeKind.StructOrClass || baseType.Type.TypeKind == CppTypeKind.Typedef)
                    {
                        addClass(baseType.Type as CppClass);
                    }
                }

                if (!cppClass.Attributes.Where(x => x.Name == "LUA_USERTYPE_NO_CTOR").Any())
                {
                    Constructors.AddRange(cppClass.Constructors
                        .Where(func => func.IsConstructor())
                        .Select(func => new TealFunction
                        (
                            "new",
                            Name,
                            func.Parameters.Select(param => new TealVariable { Name = param.GetTealName(), Type = param.Type.ConvertToTealType(Specialization) }).ToList()
                        )));
                    if (Constructors.Count == 0)
                    {
                        Constructors.Add(new TealFunction("new", Name));
                    }
                }

                Fields.AddRange(cppClass.Fields
                    .Where(field => field.IsNormalVar() || field.IsReadOnly())
                    .Select(field => new TealVariable
                    {
                        Name = field.Name,
                        Type = field.Type.ConvertToTealType(Specialization),
                        IsStatic = field.StorageQualifier == CppStorageQualifier.Static
                    }).ToList());

                Functions.AddRange(cppClass.Functions
                    .Where(func => func.IsExposedFunc())
                    .Select(func => new TealFunction(func, Name, Specialization)));
            }
        }

        public class TealNamespace
        {
            public string Name;
            public List<TealFunction> Functions = new List<TealFunction>();
            public List<TealVariable> Fields = new List<TealVariable>();
            public TealNamespace() { }
            public TealNamespace(LuaUserType userType)
            {
                Name = userType.TypeNameLower;
                if (Name == "var")
                    Name = "Var";

                Functions.AddRange((userType.OriginalElement as CppNamespace).Functions
                .Where(func => func.IsExposedFunc())
                .Select(func => new TealFunction
                (
                    func.GetName(),
                    func.ReturnType.ConvertToTealType(),
                    func.Parameters.Select(param => new TealVariable { Name = param.GetTealName(), Type = param.Type.ConvertToTealType() }).ToList()
                )));

                Functions.ForEach(func => func.Parameters.Insert(0, new TealVariable { Name = "this", Type = "void" }));
            }
        }

        public TealCodeGenWriter(CppCompilation compilation, string scrib) : base(compilation, scrib)
        { }



        protected override string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName, bool isGame)
        {
            LinkedList<TealClass> classes = new LinkedList<TealClass>();
            LinkedList<TealEnum> enums = new LinkedList<TealEnum>();
            LinkedList<TealNamespace> namespaces = new LinkedList<TealNamespace>();
            TealNamespace globalNamespace = new TealNamespace();

            bool FindSpecializationInNamespace(CppClass cppClass, CppNamespace cppNamespace, string specialization)
            {
                foreach (var cppTypedef in cppNamespace.Typedefs)
                {
                    if (cppTypedef.Name == specialization)
                    {
                        classes.AddLast(new TealClass(cppClass, cppTypedef as CppTypedef));
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
                            classes.AddLast(new TealClass(cppClass));
                        }
                    }
                }
                else if (lu.IsNamespace)
                {
                    TealNamespace tealNamespace = new TealNamespace(lu);
                    if (tealNamespace.Name == "siege")
                    {
                        globalNamespace = tealNamespace;
                    }
                    else
                    {
                        namespaces.AddLast(tealNamespace);
                    }
                }
                foreach (var parsedEnum in lu.Enums)
                {
                    enums.AddLast(new TealEnum(parsedEnum));
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
