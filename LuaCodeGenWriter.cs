﻿using CppAst;
using Scriban;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LuaExpose
{
    public class LuaCodeGenWriter
    {
        public class LuaUserType
        {
            public LuaUserType()
            {
            }

            private ICppDeclaration originalElement;
            private string originLocation;

            public ICppDeclaration OriginalElement { get => originalElement; set => originalElement = value; }
            public string OriginLocation { get => originLocation; set => originLocation = value; }
            public string TypeName { get => OriginalElement.GetName(); }
            public string TypeNameLower => TypeName.ToLower();

            public bool IsNamespace => originalElement is CppNamespace;
            public bool IsClass => originalElement is CppClass;

            public List<CppFunction> NormalFunctions => this.GetNormalFunctions();
            public List<CppEnum> Enums => this.GetEnums();

            public List<CppClass> Classes => this.GetClasses();
            public CppNamespace globalNameSpace;
        }

        public class LuaUserTypeFile
        {
            public LuaUserTypeFile()
            {
                userTypes = new Dictionary<string, LuaUserType>();
            }
            public Dictionary<string, LuaUserType> userTypes;
            public string path;
        }

        readonly List<string> globalHeaders = new List<string>
        {
            @"#include ""base/Types.h""",
            @"#include ""scripting/usertypes/LuaUsertypes.h""",
            @"#include ""scripting/LuaCustomizations.h""",
        };

        readonly List<string> globalGameHeaders = new List<string>
        {
            @"#include <base/Types.h>",
            @"#include <scripting/LuaCustomizations.h>",
            @"#include ""LuaUsertypes.h""",
        };

        string template;
        CppNamespace rootNamespace;
        CppCompilation compiledCode;
        Dictionary<string, LuaUserTypeFile> userTypeFiles;

        public LuaCodeGenWriter(CppCompilation compilation, string scrib)
        {
            rootNamespace = compilation.Namespaces.Where(x => x.Name.Contains("siege")).FirstOrDefault();
            compiledCode = compilation;
            userTypeFiles = new Dictionary<string, LuaUserTypeFile>();
            template = scrib;
        }

        LuaUserTypeFile GetPathFile(string path)
        {
            LuaUserTypeFile file = null;
            if (!userTypeFiles.TryGetValue(path, out file))
                file = AddPathFile(path);

            return file;
        }

        LuaUserTypeFile AddPathFile(string path)
        {
            if (!userTypeFiles.ContainsKey(path))
            {
                userTypeFiles[path] = new LuaUserTypeFile();
                userTypeFiles[path].path = path;
            }

            return userTypeFiles[path];
        }

        void ParseClasses(CppClass inElement)
        {
            if (string.IsNullOrEmpty(inElement.Span.End.File)) return;

            void AddFunction(string path)
            {
                var filePath = GetPathFile(path);
                var xx = new LuaUserType();
                xx.OriginalElement = inElement;
                xx.OriginLocation = path;
                filePath.userTypes[inElement.Name] = xx;
            }

            foreach (var a in inElement.Attributes)
            {
                if (a.Name.Contains("LUA_USERTYPE"))
                {
                    AddFunction(a.Span.End.File);
                }
            }
        }

        void ParseEnums(CppEnum inElement)
        {
            if (string.IsNullOrEmpty(inElement.Span.End.File)) return;

            void AddFunction(string path, CppAttribute a)
            {
                var filePath = GetPathFile(path);
                var name = (inElement?.Parent as ICppMember)?.Name;
                if (!filePath.userTypes.ContainsKey(name))
                {
                    // we are in the right file but this is likely caused by the scope not being
                    // where it should be so we need to find a new home for this, so we will use the
                    // scope of the parent for the attribute ? 
                    var newHome = new LuaUserType();
                    var parentName = (a?.Parent as ICppMember).Name;
                    newHome.OriginalElement = (a.Parent as ICppDeclaration);
                    newHome.OriginLocation = path;
                    filePath.userTypes[parentName] = newHome;
                    name = parentName;
                }
            }

            foreach (var a in inElement.Attributes)
            {
                if (a.Name == "LUA_USERTYPE_ENUM")
                {
                    AddFunction(a.Span.End.File, a);
                }
            }
        }

        void ParseNamespace(CppNamespace inElement)
        {
            // we have found a NameSpace that has been marked with an attribute
            if (inElement.Attributes.Count != 0)
            {
                foreach (var a in inElement.Attributes)
                {
                    if (a.Name == "LUA_USERTYPE_NAMESPACE")
                    {
                        var filePath = GetPathFile(a.Span.Start.File);

                        if (!filePath.userTypes.ContainsKey(inElement.Name))
                        {
                            var x = new LuaUserType();
                            x.OriginalElement = inElement;
                            x.OriginLocation = filePath.path;
                            filePath.userTypes[inElement.Name] = x;
                        }
                    }
                }
            }

            foreach (var ns in inElement.Namespaces)
            {
                ParseNamespace(ns);
            }

            foreach (var c in inElement.Classes)
            {
                ParseClasses(c);
            }

            foreach (var f in inElement.Enums)
            {
                ParseEnums(f);
            }
        }

        public string GenerateNamespaceSetup(LuaUserType lu)
        {            
            return $"auto {lu.TypeNameLower} = state[\"{lu.TypeNameLower}\"].get_or_create<sol::table>();";
        }

        public string GenerateNamespaceFunction(LuaUserType lu, CppFunction cpp)
        {
            // we likely removed this function from its parent so we don't need to process it
            if (cpp.Parent == null)
                return string.Empty;

            var parentContainer = (cpp.Parent as ICppDeclarationContainer);

            // these are functions with the same name that need to be overloaded, and then we will remove
            // them from the parent list so we don't iterate over them anymore in the future
            var functionsWithSameName = parentContainer.Functions.Where(x => x.Name == cpp.Name && (x.IsNormalFunc() || x.IsOverloadFunc()));

            StringBuilder currentOutput = new StringBuilder();
            var fullyQualifiedFunctionName = $"{lu.TypeNameLower}::{cpp.Name}";
            if (lu.TypeNameLower == "siege")
            {
                fullyQualifiedFunctionName = $"{cpp.Name}";
            }

            // in the case of one we can just bind the function
            // without resolving the overload of the params 
            if (functionsWithSameName.Count() == 1)
            {
                // these are global functions and just get thrown into the state
                if (lu.TypeNameLower == "siege")
                {
                    currentOutput.Append($"state.set_function(\"{cpp.Name}\",&{fullyQualifiedFunctionName});\n        ");
                }
                else // namespaces functions
                {
                    currentOutput.Append($"{lu.TypeNameLower}.set_function(\"{cpp.Name}\", &{fullyQualifiedFunctionName});\n        ");
                }
            }
            else
            {
                // these are global functions and just get thrown into the state
                if (lu.TypeNameLower == "siege")
                {
                    currentOutput.Append($"state.set_function(\"{fullyQualifiedFunctionName}\"");
                }
                else
                {
                    currentOutput.Append($"{lu.TypeNameLower}.set_function(\"{cpp.Name}\", &{fullyQualifiedFunctionName}");
                }

                currentOutput.Append(", sol::overload(\n            ");

                Queue<CppFunction> deleteList = new Queue<CppFunction>();

                // We have more than one function with the same name but different params
                // so we need to mark these with the overloaded
                for (int i = 0; i < functionsWithSameName.Count(); i++)
                {
                    var of = functionsWithSameName.ElementAt(i);

                    currentOutput.Append($"sol::resolve<{cpp.ReturnType.GetDisplayName()}(");
                    var paramList = string.Join(',', of.Parameters.Select(x => x.Type.GetDisplayName()));
                    currentOutput.Append($"{paramList})>(&{fullyQualifiedFunctionName})");

                    if (i != functionsWithSameName.Count() - 1)
                        currentOutput.Append(",\n            ");
                    else
                        currentOutput.Append("\n        ");

                    deleteList.Enqueue(of);
                }
                currentOutput.Append($"));\n        ");

                while (deleteList.Any())
                {
                    parentContainer.Functions.Remove(deleteList.Dequeue());
                }
            }

            return currentOutput.ToString();
        }

        public string AddTypeHeader(CppElement inClass)
        {
            string path = "";
            if (inClass.Span.Start.File == null)
            {
                if (inClass.Parent is CppNamespace ns)
                {
                    path = ns.Span.Start.File;
                }
                if (inClass.Parent is CppClass c)
                {
                    path = c.Span.Start.File;
                }
            }
            else
                path = inClass.Span.Start.File;
            var p = Path.GetFullPath(path);
            int s = p.LastIndexOf("siege");
            var y = p.Substring(s).Replace("World.h", "World.hpp"); // stupid hack

            return $@"#include ""{y.Remove(0, 6).Replace('\\', '/')}""";
        }

        public string GenerateUserType(LuaUserType lu, CppClass cpp, ref List<string> extraIncludes)
        {
            void WalkTypeTreeForBase(string baseType, ICppDeclarationContainer ns, ref List<CppClass> list)
            {                
                foreach (var c in ns.Classes)
                {
                    foreach (var bc in c.BaseTypes)
                    {
                        if (bc.Type.TypeKind == CppTypeKind.StructOrClass || bc.Type.TypeKind == CppTypeKind.Typedef)
                        {
                            var cx = bc.Type as CppClass;
                            if (cx.Name == baseType) list.Add(c);
                        }
                    }

                    WalkTypeTreeForBase(baseType, c, ref list);
                }

                if (ns is ICppGlobalDeclarationContainer nsa)
                {
                    foreach (var nn in nsa.Namespaces)
                    {
                        WalkTypeTreeForBase(baseType, nn, ref list);
                    }
                }
            }

            void WalkTypeTreeForType(string baseType, ICppDeclarationContainer ns, ref List<CppElement> list)
            {
                foreach (var c in ns.Classes)
                {
                    if (c.TypeKind == CppTypeKind.StructOrClass || c.TypeKind == CppTypeKind.Typedef)
                    {                        
                        if (c.Name == baseType) list.Add(c);
                    }

                    WalkTypeTreeForType(baseType, c, ref list);
                }

                foreach (var c in ns.Typedefs)
                {
                    if (c.Name == baseType) list.Add(c);                                        
                }

                if (ns is ICppGlobalDeclarationContainer nsa)
                {
                    foreach (var nn in nsa.Namespaces)
                    {
                        WalkTypeTreeForType(baseType, nn, ref list);
                    }
                }
            }

            var fullyQualifiedFunctionName = $"{lu.TypeName}::";
            if (lu.TypeNameLower == "siege")
            {
                fullyQualifiedFunctionName = $"";
            }

            var isTemplated = cpp.Attributes.Where(x => x.Name == "LUA_USERTYPE_TEMPLATE");
            var noConstructor = cpp.Attributes.Where(x => x.Name == "LUA_USERTYPE_NO_CTOR");
            var hasCS = cpp.Attributes.Where(x => x.Name == "LUA_USERTYPE");

            StringBuilder currentOutput = new StringBuilder();
            List<string> typeList = new List<string>();
            if (isTemplated.Any())
            {
                // if we are a template user type then we need to add each
                // type for processing
                typeList.AddRange(isTemplated.First().Arguments.Split(','));
            }
            else
            {
                // then we are just a single type and we add ourselves
                typeList.Add(cpp.Name);
            }

            for (int i = 0; i < typeList.Count; i++)
            {
                List<string> functionStrings = new List<string>();

                // this is the case where we are using templeted types
                if (typeList.Count > 1)
                {
                    List<CppElement> temp = new List<CppElement>();
                    WalkTypeTreeForType(typeList[i], rootNamespace, ref temp);
                    foreach (var item in temp)
                    {
                        extraIncludes.Add(AddTypeHeader(item));
                    }
                }

                currentOutput.Append($"state.new_usertype<{typeList[i]}>(\"{typeList[i]}\",\n            ");
                var constructors = cpp.Constructors.Where(x => x.IsConstructor());

                // so if we don't have a constructor then we should make it happen
                if (noConstructor.Any())
                {
                    functionStrings.Add($"sol::no_constructor\n            ");
                }

                // we have a constructor set for functions
                if (hasCS.Any() && constructors.Any())
                {
                    StringBuilder constructorsOutput = new StringBuilder();
                    // we have a constructor 
                    constructorsOutput.Append($"sol::constructors<");

                    for (int j = 0; j < constructors.Count(); j++)
                    {
                        var of = constructors.ElementAt(j);
                        var paramList = string.Join(',', of.Parameters.Select(x => x.Type.GetDisplayName()));
                        constructorsOutput.Append($"{typeList[i]}({paramList})");

                        if (j != constructors.Count() - 1)
                            constructorsOutput.Append(",");
                    }

                    constructorsOutput.Append($">()\n            ");
                    functionStrings.Add(constructorsOutput.ToString());

                }

                // These templated functions are stupid
                if (isTemplated.Any() && constructors.Any())
                {
                    StringBuilder constructorsOutput = new StringBuilder();
                    // we have a constructor 
                    constructorsOutput.Append($"sol::constructors<");

                    var cccc = compiledCode.FindByName<CppTypedef>(rootNamespace, typeList[i]);

                    for (int j = 0; j < constructors.Count(); j++)
                    {
                        var of = constructors.ElementAt(j);
                        var paramList = string.Join(',', of.Parameters.Select(x => x.GetRealParamValue()));

                        // we found a type and we need to make shit happen
                        if (cccc != null && cccc.GetCanonicalType() is CppUnexposedType cxz)
                        {
                            var t_type = cxz.GetDisplayName().Split('<', '>')[1].Split(',')[0];
                            paramList = paramList.Replace("T", t_type);
                        }

                        constructorsOutput.Append($"{typeList[i]}({paramList})");

                        if (j != constructors.Count() - 1)
                            constructorsOutput.Append(",");
                    }

                    constructorsOutput.Append($">()\n            ");
                    functionStrings.Add(constructorsOutput.ToString());

                }

                var shouldAddBaseData = cpp.BaseTypes.Any(x => (x.Type as CppClass).IsClassTwo());

                // add some base types
                if (shouldAddBaseData)
                {
                    List<String> baseTypesL = new List<string>();

                    void WalkBaseTypesTree(CppClass inCpp)
                    {
                        foreach (var cv in inCpp.BaseTypes)
                        {
                            var sdf = cv.Type as CppClass;
                            if (sdf.IsClassTwo())
                            {
                                baseTypesL.Add(sdf.Name);
                                WalkBaseTypesTree(sdf);
                            }                                
                        }                        
                    }

                    WalkBaseTypesTree(cpp);

                    var baseTypes = string.Join(", ", baseTypesL);
                    functionStrings.Add($"sol::base_classes, sol::bases<{baseTypes}>()\n            ");
                }

                void WalkFunctionTree(CppClass inClass, List<CppFunction> funcs)
                {
                    foreach (var bc in inClass.BaseTypes)
                    {
                        var c = bc.Type as CppClass;
                        funcs.AddRange(c.Functions.Where(z => (z.IsNormalFunc() || z.IsOverloadFunc()) && z.Flags.HasFlag(CppFunctionFlags.Virtual)));

                        WalkFunctionTree(c, funcs);
                    }
                }

                // This will be a merged list of functions from the base and the current class
                List<CppFunction> functionList = new List<CppFunction>();
                functionList.AddRange(cpp.Functions.Where(z => z.IsNormalFunc() || z.IsOverloadFunc()));

                if (shouldAddBaseData)
                {
                    WalkFunctionTree(cpp, functionList);
                }

                if (isTemplated.Any())
                {
                    fullyQualifiedFunctionName = $"{typeList[i]}::";
                }

                var overloadFunctions = functionList.Where(xsd => xsd.IsOverloadFunc());

                var functionsWithSameName = functionList.GroupBy(x => x.Name).Where(group => group.Count() > 1).SelectMany(z => z).ToList();

                functionsWithSameName.AddRange(overloadFunctions);
                for (int w = 0; w < functionList.Count(); w++)
                {
                    var funcStringBuilder = new StringBuilder();
                    var ff = functionList[w];
                    if (functionsWithSameName.Any(x => x.Name == ff.Name))
                    {
                        var yy = functionsWithSameName.Where(z => z.Name == ff.Name).ToList();
                        funcStringBuilder.Append($"\"{ff.Name}\", sol::overload(\n            ");
                        for (int a = 0; a < yy.Count(); a++)
                        {
                            var isClass = yy[a].ReturnType is CppClass;
                            if (isClass)
                            {
                                funcStringBuilder.Append($"   sol::resolve<sol::object(");
                            }
                            else
                            {
                                funcStringBuilder.Append($"   sol::resolve<{yy[a].ReturnType.ConvertToSiegeType()}(");
                            }
                            
                            var paramList = string.Join(',', yy[a].Parameters.Select(x => x.Type.ConvertToSiegeType()));

                            funcStringBuilder.Append($"{paramList})>(&{fullyQualifiedFunctionName}{yy[a].Name})");

                            if (a != yy.Count() - 1)
                            {
                                w++;
                                funcStringBuilder.Append(",\n            ");
                            }
                            else
                                funcStringBuilder.Append("\n            )\n            ");
                        }
                    }
                    else
                    {
                        funcStringBuilder.Append($"\"{ff.Name}\", &{fullyQualifiedFunctionName}{ff.Name}");
                        funcStringBuilder.Append("\n            ");
                    }

                    functionStrings.Add(funcStringBuilder.ToString());
                }

                var tempFuncs = cpp.Functions.Where(x => x.IsTemplateFunc());
                foreach (var item in tempFuncs)
                {
                    // so we need to parse the attributes of these functions
                    // we should add commas in here because we can! 
                    var attrParams = item.Attributes[0].Arguments.Split(',');

                    // this is the case where we have the ...
                    // which means we should find all the types on the left hand side
                    List<CppClass> thingsWeCareAbout = new List<CppClass>();
                    var baseType = attrParams[0];
                    if (attrParams.Length > 1)
                    {                        
                        WalkTypeTreeForBase(baseType, rootNamespace, ref thingsWeCareAbout);
                    }

                    for (int w = 0; w < thingsWeCareAbout.Count; w++)
                    {
                        var funcStringBuilder = new StringBuilder();

                        extraIncludes.Add(AddTypeHeader(thingsWeCareAbout[w]));

                        funcStringBuilder.Append($"\"{item.Name}{thingsWeCareAbout[w].Name.Replace(baseType, "") }\", &{fullyQualifiedFunctionName}{item.Name}<{thingsWeCareAbout[w].Name}>");
                        funcStringBuilder.Append("\n            ");

                        functionStrings.Add(funcStringBuilder.ToString());
                    }                    
                }


                void WalkFieldTree(CppClass inClass, List<CppField> funcs)
                {
                    foreach (var bc in inClass.BaseTypes)
                    {
                        var c = bc.Type as CppClass;
                        funcs.AddRange(c.Fields.Where(z => (z.IsNormalVar() || z.IsReadOnly())));
                        WalkFieldTree(c, funcs);
                    }
                }

                List<CppField> fieldList = new List<CppField>();
                fieldList.AddRange(cpp.Fields.Where(z => (z.IsNormalVar() || z.IsReadOnly())));
                // we need to walk the base data for any possible vars 
                if (shouldAddBaseData)
                {
                    WalkFieldTree(cpp, fieldList);
                }

                if (cpp.ClassKind == CppClassKind.Union)
                {
                    foreach (var uc in cpp.Classes)
                    {
                        fieldList.AddRange(uc.Fields.Where(z => (z.IsNormalVar() || z.IsReadOnly())));
                    }
                }

                foreach (var f in fieldList)
                {
                    var fieldStringBuilder = new StringBuilder();
                    if (f.IsReadOnly())
                    {
                        fieldStringBuilder.Append($"\"{f.Name}\", sol::readonly(&{fullyQualifiedFunctionName}{f.Name})");

                    }
                    else
                    {
                        fieldStringBuilder.Append($"\"{f.Name}\", &{fullyQualifiedFunctionName}{f.Name}");
                    }

                    fieldStringBuilder.Append("\n            ");
                    functionStrings.Add(fieldStringBuilder.ToString());

                }

                currentOutput.Append(string.Join(",", functionStrings));

                currentOutput.Append($");\n        ");
            }

            return currentOutput.ToString();
        }

        string GenerateEnum(LuaUserType lu, CppEnum e, ref List<string> extraIncludes)
        {
            StringBuilder currentOutput = new StringBuilder();

            currentOutput.Append($"state.new_enum(\"{e.Name}\",\n           ");
            for (int i = 0; i < e.Items.Count(); i++)
            {
                currentOutput.Append($"\"{e.Items[i].Name}\", {e.Name}::{e.Items[i].Name}");
                if (i != e.Items.Count() - 1)
                {
                    currentOutput.Append(",\n           ");
                }
                else
                    currentOutput.Append("\n        );\n        ");
            }

            extraIncludes.Add(AddTypeHeader(e));

            return currentOutput.ToString();
        }

        string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName, bool isGame)
        {
            ConcurrentQueue<string> includeFiles = new ConcurrentQueue<string>(isGame ? globalGameHeaders : globalHeaders);
            List<string> usings = new List<string>();

            LinkedList<string> namespaces = new LinkedList<string>();
            LinkedList<string> classes = new LinkedList<string>();
            LinkedList<string> enums = new LinkedList<string>();
            List<string> includes = new List<string>();

            value.userTypes.AsParallel().ForAll(x =>
            {
                var p = Path.GetFullPath(x.Value.OriginLocation);
                int s = p.LastIndexOf("siege");
                int offset = 6;
                if (isGame)
                {
                    s = p.LastIndexOf("src");
                    offset = 4;
                }

                var y = p.Substring(s).Replace("World.h", "World.hpp"); ;

                includeFiles.Enqueue($@"#include ""{y.Remove(0, offset).Replace('\\', '/')}""");
            });

            includes.AddRange(includeFiles);

            foreach (var lu in value.userTypes.Values)
            {
                if (lu.IsNamespace)
                {
                    if (fileName.ToLower() == "types")
                    {
                        usings.Add("using namespace siege;");
                    }
                    else
                    {
                        usings.Add($"using namespace {lu.OriginalElement.GetName().ToLower()};");
                        namespaces.AddLast(GenerateNamespaceSetup(lu));
                    }

                    foreach (var f in lu.NormalFunctions)
                    {
                        namespaces.AddLast(GenerateNamespaceFunction(lu, f));
                    }
                }

                if (lu.IsClass)
                {
                    foreach (var f in lu.Classes)
                    {
                        classes.AddLast(GenerateUserType(lu, f, ref includes));
                    }
                }

                foreach (var e in lu.Enums)
                {
                    enums.AddLast(GenerateEnum(lu, e, ref includes));
                }
            }

            if (includes.Any(x => x.Contains("ui")))
            {
                includes.Add(@"#include ""ui/Forward.h""");
                usings.Add("using namespace ui;");
            }

            if (includes.Any(x => x.Contains("base/Event.h")))
            {
                includes.Add(@"#include ""ui/View.h""");
            }

            var scribe = Template.Parse(File.ReadAllText(template));
            return scribe.Render(new { Includes = includes.Distinct(), Ltype = fileName.FirstCharToUpper(), Namespaces = namespaces, Classes = classes, Enums = enums, Usings = usings });
        }

        void WriteAllFiles(string v, bool isGame)
        {
            foreach (var f in userTypeFiles)
            {
                var newFileName = $"LuaUsertypes{Path.GetFileNameWithoutExtension(f.Key).FirstCharToUpper()}.cpp";
                var newPath = Path.Combine(v, newFileName);

                using (System.IO.StreamWriter file =
                    new System.IO.StreamWriter(newPath))
                {

                    var content = GetContentFromLuaUserFile(f.Value, Path.GetFileNameWithoutExtension(f.Key), isGame);
                    file.Write(content);
                }
            }

            WriteLuaUserTypeFile(v, userTypeFiles, isGame);
        }

        private void WriteLuaUserTypeFile(string v, Dictionary<string, LuaUserTypeFile> userTypeFiles, bool isGame)
        {
            var newFileName = $"LuaUsertypes.cpp";
            var newPath = Path.Combine(v, newFileName);

            using (System.IO.StreamWriter file =
                new System.IO.StreamWriter(newPath))
            {
                var cppTemplate = @"#include REPLACEMEWITHHEADER

#include <sol/sol.hpp>

namespace siege {
    void lua_expose_REPLACEMESOMEMORE(sol::state& state) {
REPLACEMEWITHTEXT
     }
}
";              StringBuilder things = new StringBuilder();
                foreach (var item in userTypeFiles)
                {
                    things.Append($"        lua_expose_usertypes_{Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()}(state);\n");
                }

                if (!isGame)
                {
                    things.Append($"        lua_expose_usertypes_Game(state);\n");
                }

                var content = cppTemplate.Replace("REPLACEMEWITHTEXT", things.ToString());
                if (isGame)
                {
                    content = content.Replace("REPLACEMEWITHHEADER", "\"LuaUsertypes.h\"");
                    content = content.Replace("REPLACEMESOMEMORE", "usertypes_Game");
                }
                else
                {
                    content = content.Replace("REPLACEMEWITHHEADER", "\"scripting/usertypes/LuaUsertypes.h\"");
                    content = content.Replace("REPLACEMESOMEMORE", "usertypes");
                }

                file.Write(content);
            }

            newFileName = $"LuaUsertypes.h";
            newPath = Path.Combine(v, newFileName);

            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(newPath))
            {
                var cppTemplate = @"#pragma once

namespace sol {
    class state;
}

namespace siege {

    void lua_expose_REPLACEMESOMEMORE(sol::state& state);
REPLACEMEWITHTEXT
}
"; 
                StringBuilder things = new StringBuilder();
                foreach (var item in userTypeFiles)
                {
                    things.Append($"    void lua_expose_usertypes_{Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()}(sol::state& state);\n");
                }

                if (!isGame)
                {
                    things.Append($"        extern void lua_expose_usertypes_Game(sol::state& state);\n");
                }

                var content = cppTemplate.Replace("REPLACEMEWITHTEXT", things.ToString());
                if (isGame)
                {
                    content = content.Replace("REPLACEMESOMEMORE", "usertypes_Game");
                }
                else
                {
                    content = content.Replace("REPLACEMESOMEMORE", "usertypes");
                }

                file.Write(content);
            }
        }

        public void Run(string outLocation, bool isGame)
        {
            Console.WriteLine($"Writing files to {outLocation}");

            Directory.CreateDirectory(outLocation);

            ParseNamespace(rootNamespace);
            WriteAllFiles(outLocation, isGame);
        }
    }
}
