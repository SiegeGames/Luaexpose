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
    public abstract class CodeGenWriter
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

        protected string template;
        protected CppNamespace rootNamespace;
        protected CppCompilation compiledCode;
        protected Dictionary<string, LuaUserTypeFile> userTypeFiles;

        public CodeGenWriter(CppCompilation compilation, string scrib)
        {
            template = scrib;
            rootNamespace = compilation.Namespaces.Where(x => x.Name.Contains("siege")).FirstOrDefault();
            compiledCode = compilation;
            userTypeFiles = new Dictionary<string, LuaUserTypeFile>();
        }

        protected LuaUserTypeFile GetPathFile(string path)
        {
            LuaUserTypeFile file = null;
            if (!userTypeFiles.TryGetValue(path, out file))
                file = AddPathFile(path);

            return file;
        }

        protected LuaUserTypeFile AddPathFile(string path)
        {
            if (!userTypeFiles.ContainsKey(path))
            {
                userTypeFiles[path] = new LuaUserTypeFile();
                userTypeFiles[path].path = path;
            }

            return userTypeFiles[path];
        }

        protected void ParseClasses(CppClass inElement)
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

        protected void ParseEnums(CppEnum inElement)
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

        public void ParseNamespace(CppNamespace inElement)
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
        protected void WriteFileContent(string content, string newPath)
        {
            using (System.IO.StreamWriter file =
                new System.IO.StreamWriter(newPath))
            {
                file.Write(content);
            }
        }

        protected abstract string GetContentFromLuaUserFile(LuaUserTypeFile value, string fileName, bool isGame);

        protected abstract string GetUsertypeFileName(string usertypeFile);

        protected virtual void WriteAllFiles(string v, bool isGame)
        {
            foreach (var f in userTypeFiles)
            {
                var newPath = Path.Combine(v, GetUsertypeFileName(f.Key));
                if (File.Exists(newPath) && (new FileInfo(newPath)).LastWriteTime > (new FileInfo(f.Value.path)).LastWriteTime)
                {
                    continue;
                }
                var content = GetContentFromLuaUserFile(f.Value, Path.GetFileNameWithoutExtension(f.Key), isGame);
                WriteFileContent(content, newPath);
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
