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
            public TypeScriptFunction(string Name, string ReturnType, CppFunction func, CppTypedef specialization)
            {
                this.Name = Name;
                this.ReturnType = ReturnType.Length > 0 ? ReturnType : "void";
                this.Parameters = func.Parameters.Select(param => new TypeScriptVariable {
                    Name = param.GetTypeScriptName(),
                    Type = param.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Parameter, specialization)
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
                    Type = param.Type.ConvertToTypeScriptType(CppExtenstions.TypeScriptSourceType.Parameter, specialization)
                }).ToList());
                remapTypes(func);
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
        }


    }
}
