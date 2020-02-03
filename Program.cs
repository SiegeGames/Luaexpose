using System;
using System.IO;
using CppAst;
using System.Linq;
using System.Collections.Generic;
using ConcurrentCollections;
using Scriban;
using System.Collections.Concurrent;
using Scriban.Runtime;
using System.Globalization;
using System.Text;

namespace LuaExpose
{
    public static class StringExtensions
    {
        public static string FirstCharToUpper(this string input)
        {
            switch (input)
            {
                case null: throw new ArgumentNullException(nameof(input));
                case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
                default: return input.First().ToString().ToUpper() + input.Substring(1);
            }
        }

        public static string FirstCharToLower(this string input)
        {
            switch (input)
            {
                case null: throw new ArgumentNullException(nameof(input));
                case "": throw new ArgumentException($"{nameof(input)} cannot be empty", nameof(input));
                default: return input.First().ToString().ToLower() + input.Substring(1);
            }
        }
    }


    public static class CppExtenstions
    {
        public static string GetName(this ICppDeclaration input)
        {
            return (input as ICppMember).Name;
        }

        public static bool IsConstructor(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_CTOR");
        }
        public static bool IsNormalFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC");
        }
        public static bool IsOverloadFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC_OVERLOAD");
        }
        public static bool IsTemplateFunc(this CppFunction input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_FUNC_TEMPLATE");
        }

        public static bool IsEnum(this CppEnum input)
        {
            return input.Attributes.Any(x => x.Name == "LUA_USERTYPE_ENUM");
        }
        public static bool IsClass(this CppClass input, LuaUserType a)
        {
            return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE") && x.Span.Start.File == a.OriginLocation);
        }
        public static bool IsClassTwo(this CppClass input)
        {
            return input.Attributes.Any(x => (x.Name == "LUA_USERTYPE" || x.Name == "LUA_USERTYPE_NO_CTOR" || x.Name == "LUA_USERTYPE_TEMPLATE"));
        }

        public static List<CppFunction> GetNormalFunctions(this LuaUserType input)
        {
            return (input.OriginalElement as ICppDeclarationContainer).Functions.Where(x => x.IsNormalFunc()).ToList();
        }

        public static List<CppEnum> GetEnums(this LuaUserType input)
        {
            if (input.OriginalElement is CppEnum)
                return new List<CppEnum> { input.OriginalElement as CppEnum };

            return (input.OriginalElement as ICppDeclarationContainer).Enums.Where(x => x.IsEnum()).ToList();
        }

        public static List<CppClass> GetClasses(this LuaUserType input)
        {
            // There are cases where if you have an enum class they get pulled into a class
            // and enum which we only want them to be an enum 
            if (input.OriginalElement is CppEnum)
                return new List<CppClass>();

            if (input.OriginalElement is CppNamespace)
                return (input.OriginalElement as ICppDeclarationContainer).Classes.Where(x => x.IsClass(input)).ToList();

            return new List<CppClass> { input.OriginalElement as CppClass };
        }
    }

    public class ScribanHelperFunctions : ScriptObject
    {
        public static string FormatNamespaceSetup(object luaUserType)
        {
            var lu = luaUserType as LuaUserType;
            return $"auto {lu.TypeNameLower} = state[\"{lu.TypeNameLower}\"].get_or_create<sol::table>();";
        }

        public static string FormatNamespaceFunctionSetup(object luaUserType, object cppFunction)
        {
            var lu = luaUserType as LuaUserType;
            var cpp = cppFunction as CppFunction;

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
                    currentOutput.Append($"state.set_function(\"{fullyQualifiedFunctionName}\",");
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
                currentOutput.Append($");\n        ");

                while (deleteList.Any())
                {
                    parentContainer.Functions.Remove(deleteList.Dequeue());
                }
            }

            return currentOutput.ToString();
        }
        public static string FormatUserType(object luaUserType, object cppClass)
        {
            var lu = luaUserType as LuaUserType;
            var cpp = cppClass as CppClass;

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
                // if we are a templated user type then we need to add each
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
                currentOutput.Append($"state.new_usertype<{typeList[i]}>(\"{typeList[i]}\",\n            ");
                var constructors = cpp.Constructors.Where(x => x.IsConstructor());

                // so if we don't have a constructor then we should make it happen
                if (noConstructor.Any() || (!hasCS.Any() || !constructors.Any()))
                {
                    currentOutput.Append($"sol::no_constructor,\n            ");
                }

                // we have a constructor set for functions
                if (hasCS.Any() && constructors.Any())
                {
                    // we have a constructor 
                    currentOutput.Append($"sol::constructors<");


                    for (int j = 0; j < constructors.Count(); j++)
                    {
                        var of = constructors.ElementAt(j);
                        var paramList = string.Join(',', of.Parameters.Select(x => x.Type.GetDisplayName()));
                        currentOutput.Append($"{typeList[i]}({paramList})");

                        if (j != constructors.Count() - 1)
                            currentOutput.Append(",");
                    }

                    currentOutput.Append($">(),\n            ");

                }

                var shouldAddBaseData = cpp.BaseTypes.Any(x => (x.Type as CppClass).IsClassTwo());

                // add some base types
                if (shouldAddBaseData)
                {
                    var baseTypes = string.Join(", ", cpp.BaseTypes);
                    currentOutput.Append($"sol::base_classes, sol::bases<{baseTypes}>(),\n            ");
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

                var functionsWithSameName = functionList.GroupBy(x => x.Name).Where(group => group.Count() > 1).SelectMany(z => z).ToList();
                for (int w = 0; w < functionList.Count(); w++)
                {
                    var ff = functionList[w];
                    if (functionsWithSameName.Any(x => x.Name == ff.Name))
                    {
                        var yy = functionsWithSameName.Where(z => z.Name == ff.Name).ToList();
                        currentOutput.Append($"\"{ff.Name}\", sol::overload(\n            ");
                        for (int a = 0; a < yy.Count(); a++)
                        {                            
                            currentOutput.Append($"   sol::resolve<{yy[a].ReturnType.GetDisplayName()}(");
                            var paramList = string.Join(',', yy[a].Parameters.Select(x => x.Type.GetDisplayName()));

                            currentOutput.Append($"{paramList})>(&{fullyQualifiedFunctionName}{yy[a].Name})");

                            if (a != yy.Count() - 1)
                            {
                                w++;
                                currentOutput.Append(",\n            ");
                            }
                            else
                                currentOutput.Append("\n            ),\n            ");
                        }                        
                    }
                    else
                    {
                        currentOutput.Append($"\"{ff.Name}\",&{fullyQualifiedFunctionName}{ff.Name}");
                        if (w != functionList.Count() - 1)
                        {
                            currentOutput.Append(",\n            ");
                        }
                        else
                            currentOutput.Append("\n        ");

                    }
                }


                // now we need to walk through field types ? 


                currentOutput.Append($");\n        ");
            }

            return currentOutput.ToString();
        }
    }

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

    class Program
    {
        static List<string> globalHeaders = new List<string>
        {
            @"#include ""base/Types.h""",
            @"#include ""scripting/usertypes/LuaUsertypes.h""",
            @"#include ""scripting/LuaCustomizations.h""",
        };

        static Dictionary<string, LuaUserTypeFile> files = new Dictionary<string, LuaUserTypeFile>();
        static LuaUserTypeFile GetPathFile(string path)
        {
            LuaUserTypeFile file = null;
            if (!files.TryGetValue(path, out file))
                file = AddPathFile(path);

            return file;
        }

        static LuaUserTypeFile AddPathFile(string path)
        {
            if (!files.ContainsKey(path))
            {
                files[path] = new LuaUserTypeFile();
                files[path].path = path;
            }

            return files[path];
        }

        static void ParseClasses(CppClass inElement)
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

        static void ParseEnums(CppEnum inElement)
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

        static void ParseNamespace(CppNamespace inElement)
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

        private static void WriteAllFiles(string v)
        {
            foreach (var f in files)
            {
                var newFileName = $"LuaUsertypes{Path.GetFileNameWithoutExtension(f.Key).FirstCharToUpper()}.cpp";
                var newPath = Path.Combine(v, newFileName);

                using (System.IO.StreamWriter file =
                    new System.IO.StreamWriter(newPath))
                {

                    var content = GetContentFromLuaUserFile(f.Value, Path.GetFileNameWithoutExtension(f.Key));
                    file.Write(content);
                }
            }
        }

        private static string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName)
        {
            ConcurrentQueue<string> includeFiles = new ConcurrentQueue<string>(globalHeaders);

            value.userTypes.AsParallel().ForAll(x =>
            {
                var p = Path.GetFullPath(x.Value.OriginLocation);
                int s = p.LastIndexOf("siege");
                var y = p.Substring(s);

                includeFiles.Enqueue($@"#include ""{y.Remove(0, 6).Replace('\\', '/')}""");
            });
            var isUINamespaceRequired = false;
            if (includeFiles.Any(x => x.Contains("ui")))
            {
                includeFiles.Enqueue(@"#include ""ui/Forward.h""");
                isUINamespaceRequired = true;
            }

            var userTypes = value.userTypes.Values.ToList();

            var template = Template.Parse(File.ReadAllText("luascript_template.txt"));

            var scriptObject1 = new ScriptObject();
            scriptObject1.Import(typeof(ScribanHelperFunctions));
            scriptObject1.Import(new { Includes = includeFiles.Distinct(), Ltype = fileName.FirstCharToUpper(), Ns = fileName.ToLower(), Ui = isUINamespaceRequired, Types = userTypes });

            var context = new TemplateContext();
            context.PushGlobal(scriptObject1);

            return template.Render(context);
        }

        static void Main(string[] args)
        {
            var f = Directory.EnumerateFiles(@"D:\dig\src\siege\", "*.h", SearchOption.AllDirectories);
            CppParserOptions p = new CppParserOptions();
            p.ParseComments = false;
            //p.ConfigureForWindowsMsvc(CppTargetCpu.X86_64, CppVisualStudioVersion.VS2019);
            //p.Defines.Add("_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH");            
            p.ParseSystemIncludes = false;
            p.AdditionalArguments.Add("-std=c++17");
            p.AdditionalArguments.Add("-xc++");
            p.AdditionalArguments.Add("-Wno-pragma-once-outside-header");
            p.AdditionalArguments.Add("-Wno-unknown-attributes");
            p.IncludeFolders.Add(@"D:\dig\src\siege\");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\SDL2\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\parallel_hashmap\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\bgfx\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\luajit\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\sol\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\box2d\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\fmod\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\steam\include");
            p.SystemIncludeFolders.Add(@"D:\dig\libs\bgfx\include\compat\msvc");
            p.SystemIncludeFolders.Add(@"D:\dig\src\siege\external");
            
            // we could likely make this only parse files that we care about ?
            // we will have an ini.file that lives in each game that has a list of info 
            var compilation = CppParser.ParseFiles(f.ToList(), p);
            
            var siegeNamespace = compilation.Namespaces.Where(x => x.Name.Contains("siege")).FirstOrDefault();


            // USERTYPE WORK FLOW
            // The path of how this should work is as follows: 
            // Parse the root namespace and have the concept of a LuaUserTypeFile 
            // this represents each of the userTypeFiles that we have today.
            ParseNamespace(siegeNamespace);

            WriteAllFiles(@"D:\testcpp");

            Console.ReadKey();
 
        }

    }
}
