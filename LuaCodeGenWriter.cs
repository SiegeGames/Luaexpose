using CppAst;
using Scriban;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LuaExpose
{
    public class LuaCodeGenWriter : CodeGenWriter
    {

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

        public LuaCodeGenWriter(CppCompilation compilation, string scrib) : base(compilation, scrib)
        {
            userTypeFilePattern = "*.cpp";
            preservedFiles.Add("LuaUsertypes.cpp");
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
            var functionsWithSameName = parentContainer.Functions.Where(x => x.GetName() == cpp.GetName() && x.IsExposedFunc());


            var overloadFunctions = parentContainer.Functions.Where(x => x.GetName() == cpp.GetName() && (x.IsOverloadFunc()));

            StringBuilder currentOutput = new StringBuilder();
            var fullyQualifiedFunctionName = $"{lu.TypeNameLower}::{cpp.Name}";
            if (lu.TypeNameLower == "siege")
            {
                fullyQualifiedFunctionName = $"{cpp.Name}";
            }

            //var forwardFunctions = parentContainer.Functions.Where(x => x.Name == cpp.Name && x.IsFowardFunc());
            //foreach (var m in forwardFunctions) {
            //    var funcStringBuilder = new StringBuilder();
            //    // LUA_FORWARD_FUNC(arg=int, arg=int, return=void)

            //    var cc = m.Attributes[0].Arguments.Split(',');
            //    var returnString = "void";

            //    var argBuilder = new StringBuilder();
            //    var callBuilder = new StringBuilder();
            //    int index = 0;
            //    foreach (var item in cc) {
            //        var zz = item.Split('=');
            //        if (zz[0] == "arg") {
            //            callBuilder.Append($"arg{index}, ");

            //            argBuilder.Append($",{zz[1]} arg{index++}");
            //        }
            //        else if (zz[0] == "return")
            //            returnString = zz[1];
            //    }

            //    funcStringBuilder.Append($"{lu.TypeNameLower}.set_function(\"{cpp.GetName()}\", []({lu.TypeNameLower}& o{argBuilder}){{ o.{m.GetName()}({callBuilder})}}");
            //    funcStringBuilder.Append("\n            ");

            //    currentOutput.Append(funcStringBuilder.ToString());
            //}

            // in the case of one we can just bind the function
            // without resolving the overload of the params 
            if (functionsWithSameName.Count() == 1 && overloadFunctions.Count() == 0)
            {
                // these are global functions and just get thrown into the state
                if (lu.TypeNameLower == "siege")
                {
                    currentOutput.Append($"state.set_function(\"{cpp.GetName()}\",&{fullyQualifiedFunctionName});\n        ");
                }
                else // namespaces functions
                {
                    // check and see if this should be a static function? 
                    if (cpp.Attributes[0].Arguments == "use_static") {
                        // in this case we have to do something like an overload. 
                        var paramList = string.Join(',', cpp.Parameters.Select(x => x.Type.ConvertToSiegeType()));
                        bool constReturn = false;
                        if (cpp.ReturnType is CppAst.CppQualifiedType qr)
                            constReturn = qr.Qualifier == CppTypeQualifier.Const;
                        if (cpp.ReturnType is CppAst.CppReferenceType rt && rt.ElementType is CppQualifiedType qt)
                            constReturn = qt.Qualifier == CppTypeQualifier.Const;


                        var methodConst = cpp.Flags.HasFlag(CppFunctionFlags.Const);

                        currentOutput.Append($"{lu.TypeNameLower}.set_function(\"{cpp.GetName()}\", static_cast<{cpp.ReturnType.GetDisplayName()} (*)({paramList})");
                        currentOutput.Append($" {(methodConst ? "const" : "")} > (&{fullyQualifiedFunctionName}));\n        ");
                    }
                    else {
                        currentOutput.Append($"{lu.TypeNameLower}.set_function(\"{cpp.GetName()}\", &{fullyQualifiedFunctionName});\n        ");
                    }

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
                    currentOutput.Append($"{lu.TypeNameLower}.set_function(\"{cpp.GetName()}\", &{fullyQualifiedFunctionName}");
                }

                currentOutput.Append(", sol::overload(\n            ");

                // We have more than one function with the same name but different params
                // so we need to mark these with the overloaded
                for (int i = 0; i < functionsWithSameName.Count(); i++)
                {
                    var of = functionsWithSameName.ElementAt(i);
                    currentOutput.Append($"sol::resolve<{cpp.ReturnType.GetDisplayName()}(");
                    var paramList = string.Join(',', of.Parameters.Select(x => x.Type.ConvertToSiegeType()));
                    currentOutput.Append($"{paramList})>(&{fullyQualifiedFunctionName})");

                    if (i != functionsWithSameName.Count() - 1)
                        currentOutput.Append(",\n            ");
                    else
                        currentOutput.Append("\n        ");
                }
                currentOutput.Append($"));\n        ");
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
                        if (cccc != null && !string.IsNullOrEmpty(paramList) && cccc.GetCanonicalType() is CppClass cxz)
                        {
                            var t_type = cxz.TemplateParameters.Count() > 0 ? cxz.TemplateParameters[0].GetDisplayName() : "";
                           // var t_type = cxz.GetDisplayName().Split('<', '>')[1].Split(',')[0];
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
                        funcs.AddRange(c.Functions.Where(z => (z.IsExposedFunc()) && z.Flags.HasFlag(CppFunctionFlags.Virtual)));

                        WalkFunctionTree(c, funcs);
                    }
                }

                // This will be a merged list of functions from the base and the current class
                List<CppFunction> functionList = new List<CppFunction>();
                functionList.AddRange(cpp.Functions.Where(z => z.IsExposedFunc()));

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

                var foundStaticFuncs = functionList.Where(xsd => xsd.IsNormalStaticFunc()).Select(x => x.Name).ToList();

                for (int w = 0; w < functionList.Count(); w++)
                {
                    var funcStringBuilder = new StringBuilder();
                    var ff = functionList[w];
                    var shouldAttemptStatic = ff.IsNormalStaticFunc() || foundStaticFuncs.Contains(ff.Name);

                    if (functionsWithSameName.Any(x => x.Name == ff.Name) && !shouldAttemptStatic)
                    {
                        var yy = functionsWithSameName.Where(z => z.Name == ff.Name).ToList();
                        funcStringBuilder.Append($"\"{ff.GetName()}\", sol::overload(\n            ");
                        for (int a = 0; a < yy.Count(); a++)
                        {
                            var methodConst = ff.Flags.HasFlag(CppFunctionFlags.Const);

                            var isClass = yy[a].ReturnType is CppClass;
                            if (isClass && (yy[a].ReturnType as CppClass).Name != "shared_ptr")
                            {
                                funcStringBuilder.Append($"   sol::resolve<sol::object(");
                            }
                            else
                            {
                                funcStringBuilder.Append($"   sol::resolve<{yy[a].ReturnType.ConvertToSiegeType()}(");
                            }
                            
                            var paramList = string.Join(',', yy[a].Parameters.Select(x => x.Type.ConvertToSiegeType()));

                            funcStringBuilder.Append($"{paramList}) {(methodConst ? "const" : "")} >(&{fullyQualifiedFunctionName}{yy[a].Name})");

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
                        // check and see if this should be a static function? 
                        if (ff.Attributes[0].Arguments == "use_static" || shouldAttemptStatic) {
                            // in this case we have to do something like an overload. 
                            var paramList = string.Join(',', ff.Parameters.Select(x => x.Type.ConvertToSiegeType()));
                            bool constReturn = false;
                            if (ff.ReturnType is CppAst.CppQualifiedType qr)
                                constReturn = qr.Qualifier == CppTypeQualifier.Const;
                            if (ff.ReturnType is CppAst.CppReferenceType rt && rt.ElementType is CppQualifiedType qt)
                                constReturn = qt.Qualifier == CppTypeQualifier.Const;


                            var methodConst = ff.Flags.HasFlag(CppFunctionFlags.Const);

                            funcStringBuilder.Append($"\"{ff.GetName()}\", static_cast<{ff.ReturnType.GetDisplayName()} ({fullyQualifiedFunctionName}*)({paramList})");
                            funcStringBuilder.Append($" {(methodConst ? "const" : "")} > (&{ fullyQualifiedFunctionName}{ ff.Name})");


                            funcStringBuilder.Append("\n            ");
                        }
                        else {
                            funcStringBuilder.Append($"\"{ff.GetName()}\", &{fullyQualifiedFunctionName}{ff.Name}");
                            funcStringBuilder.Append("\n            ");
                        }
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

                        funcStringBuilder.Append($"\"{item.GetName()}{thingsWeCareAbout[w].Name.Replace(baseType, "") }\", &{fullyQualifiedFunctionName}{item.Name}<{thingsWeCareAbout[w].Name}>");
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

                var metaFunctions = cpp.Functions.Where(z => z.IsMetaFunc());
                if (metaFunctions.Count() != 0) {
                    foreach (var m in metaFunctions) {
                        var funcStringBuilder = new StringBuilder();
                        funcStringBuilder.Append($"sol::{CppExtenstions.ConvertToMetaEnum(m.Attributes[0].Arguments)}, &{fullyQualifiedFunctionName}{m.Name}");
                        funcStringBuilder.Append("\n            ");

                        functionStrings.Add(funcStringBuilder.ToString());
                    }
                }

                var forwardFunctions = cpp.Functions.Where(z => z.IsFowardFunc());
                foreach (var m in forwardFunctions) {
                    var funcStringBuilder = new StringBuilder();
                    // LUA_FORWARD_FUNC(arg=int, arg=int, return=void, caller={args})
                    var cc = m.Attributes[0].Arguments.Split(',');
                    var returnString = "void";
                    var funcBindName = m.GetName();
                    var callerOverride = "";
                    var argBuilder = new StringBuilder();
                    var callBuilder = new StringBuilder();
                    int index = 0;
                    for (int z = 0; z < cc.Length; z++) {
                        var item = cc[z];

                        var zz = item.Split('=');
                        if (zz[0] == "arg") {
                            if (index != 0)
                                callBuilder.Append($",");
                            callBuilder.Append($"arg{index}");
                            argBuilder.Append($",{zz[1]} arg{index++}");
                        }
                        else if (zz[0] == "return")
                            returnString = zz[1];
                        else if (zz[0] == "name")
                            funcBindName = zz[1];
                        else if (zz[0] == "caller")
                            callerOverride = zz[1];
                    }

                    if (string.IsNullOrEmpty(callerOverride))
                        callerOverride = $"{callBuilder}";
                    else {
                        callerOverride = callerOverride.Replace("args", callBuilder.ToString());
                        var first = callerOverride.IndexOf('|');
                        if (first != -1)
                            callerOverride = callerOverride.Remove(first, 1).Insert(first, "(");
                        first = callerOverride.IndexOf('|');
                        if (first != -1)
                            callerOverride = callerOverride.Remove(first, 1).Insert(first, ")");
                    }

                    var boolShouldReturn = returnString != "void";
                    if (boolShouldReturn)
                        returnString = $"-> {returnString}";

                    funcStringBuilder.Append($"\"{funcBindName}\", []({typeList[i]}& o{argBuilder}) {(boolShouldReturn ? returnString : "")} {{ {(boolShouldReturn ? "return" : "")} o.{m.Name}({callerOverride}); }}");
                    funcStringBuilder.Append("\n            ");

                    functionStrings.Add(funcStringBuilder.ToString());
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

            if (e.Items.Count() > 30)
            {
                currentOutput.Append($"state.new_enum<{e.Name}>(\"{e.Name}\", {{\n           ");
                for (int i = 0; i < e.Items.Count(); i++)
                {
                    currentOutput.Append($"{{ \"{e.Items[i].Name}\", {e.Name}::{e.Items[i].Name} }}");
                    if (i != e.Items.Count() - 1)
                    {
                        currentOutput.Append(",\n           ");
                    }
                    else
                        currentOutput.Append("\n        });\n        ");
                }
            }
            else
            {
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
            }

            extraIncludes.Add(AddTypeHeader(e));

            return currentOutput.ToString();
        }

        protected override string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName, bool isGame)
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

                    // List of functions that have been exposed so we don't expose overloaded functions multiple times
                    List<string> exposedFunctionsNames = new List<string>();
                    foreach (var f in lu.NormalFunctions)
                    {
                        if (!exposedFunctionsNames.Contains(f.GetName()))
                        {
                            namespaces.AddLast(GenerateNamespaceFunction(lu, f));
                            exposedFunctionsNames.Add(f.GetName());
                        }
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

            if (includes.Any(x => x.Contains("base/Event.h")))
            {
                includes.Add(@"#include ""ui/View.h""");
            }

            var scribe = Template.Parse(File.ReadAllText(template));
            return scribe.Render(new { Includes = includes.Distinct(), Ltype = fileName.FirstCharToUpper(), Namespaces = namespaces, Classes = classes, Enums = enums, Usings = usings });
        }

        protected override string GetUsertypeFileName(string usertypeFile)
        {
            return $"LuaUsertypes{Path.GetFileNameWithoutExtension(usertypeFile).FirstCharToUpper()}.cpp";
        }

        protected override void WriteAllFiles(string v, bool isGame)
        {
            base.WriteAllFiles(v, isGame);
            WriteLuaUserTypeFile(v, userTypeFiles, isGame);
        }

        private void WriteLuaUserTypeFile(string v, Dictionary<string, LuaUserTypeFile> userTypeFiles, bool isGame)
        {
            var newFileName = $"LuaUsertypes.cpp";
            var newPath = Path.Combine(v, newFileName);
            var fileExists = File.Exists(newPath);

            var cppTemplate = @"#include REPLACEMEWITHHEADER

#include <sol/sol.hpp>

namespace siege {
    void lua_expose_REPLACEMESOMEMORE(sol::state& state) {
// BEGIN
REPLACEMEWITHTEXT
// END
     }
}
";
            var cppHeaderTemplate = @"#pragma once

namespace sol {
    class state;
}

namespace siege {

    void lua_expose_REPLACEMESOMEMORE(sol::state& state);
// BEGIN
REPLACEMEWITHTEXT
// END
}
";
            StringBuilder fileContent = new StringBuilder();
            string originalContent = "";
            var currentContent = "";
            // this is a case where we need to load the file and append potentially ? 
            if (fileExists)
            {
                originalContent = File.ReadAllText(newPath);
                currentContent = originalContent.GetTextBetween("// BEGIN", "// END").Trim();
                fileContent.Append(currentContent);
            }

            foreach (var item in userTypeFiles)
            {
                if (!currentContent.Contains(Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()))
                    fileContent.Append($"        lua_expose_usertypes_{Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()}(state);\n");
            }

            if (!isGame)
            {
                if (!currentContent.Contains("usertypes_Game"))
                    fileContent.Append($"        lua_expose_usertypes_Game(state);\n");
            }

            var content = cppTemplate.Replace("REPLACEMEWITHTEXT", fileContent.ToString());
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

            if (content != originalContent)
            {
                WriteFileContent(content, newPath);
            }

            newFileName = $"LuaUsertypes.h";
            newPath = Path.Combine(v, newFileName);

            fileContent.Clear();
            if (fileExists)
            {
                originalContent = File.ReadAllText(newPath);
                currentContent = originalContent.GetTextBetween("// BEGIN", "// END").Trim();
                fileContent.Append(currentContent);
            }

            foreach (var item in userTypeFiles)
            {
                if (!currentContent.Contains(Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()))
                    fileContent.Append($"    void lua_expose_usertypes_{Path.GetFileNameWithoutExtension(item.Key).FirstCharToUpper()}(sol::state& state);\n");
            }

            if (!isGame)
            {
                if (!currentContent.Contains("usertypes_Game"))
                    fileContent.Append($"    extern void lua_expose_usertypes_Game(sol::state& state);\n");
            }

            content = cppHeaderTemplate.Replace("REPLACEMEWITHTEXT", fileContent.ToString());
            if (isGame)
            {
                content = content.Replace("REPLACEMESOMEMORE", "usertypes_Game");
            }
            else
            {
                content = content.Replace("REPLACEMESOMEMORE", "usertypes");
            }

            if (content != originalContent)
            {
                WriteFileContent(content, newPath);
            }
        }
    }
}
