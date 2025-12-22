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
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommandLine;
using LuaExpose.Parser;

namespace LuaExpose
{

    public static class OperatingSystem
    {
        public static bool IsWindows() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public static bool IsMacOS() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static bool IsLinux() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    }

    public class Program
    {
        public class Options
        {
            [Option('n', "namespace", Required = true, HelpText = "Root namespace")]
            public string RootNamespace { get; set; }
            [Option('r', "root", Required = true, HelpText = "Root Source Dir")]
            public string RootSource { get; set; }

            [Option('i', "input", Required = true, HelpText = "Directory containing source code to expose to Lua")]
            public string InputDirectory { get; set; }

            [Option('o', "output", Required = true, HelpText = "Directory where this tool should place its generated code")]
            public string Output { get; set; }

            [Option('l', "lib", Required = true, HelpText = "Directory containing external library code")]
            public string libs { get; set; }

            [Option('t', "temp", Required = true, HelpText = "Lua Output Template")]
            public string Scrib { get; set; }

            [Option('g', "game", Required = false, HelpText = "Specifies whether this generation is done in the context of a Siege game")]
            public bool IsGame { get; set; }

            [Option('c', "cpp", Required = false, HelpText = "C++ version to use in generating C++ code")]
            public string CppVersion { get; set; }

            [Option('O', "tsoutput", Required = false, HelpText = "TypeScript Declaration Output")]
            public string Declarations { get; set; }
            [Option('T', "tstemp", Required = false, HelpText = "TypeScript Output Template")]
            public string TypeScriptScrib { get; set; }

            [Option("preprocess", Required = false, HelpText = "Enable macro preprocessing (requires clang/gcc/msvc in PATH)")]
            public bool EnablePreprocessing { get; set; }
        }

        static void Main(string[] args)
        {
            CommandLine.Parser.Default.ParseArguments<Options>(args)
            .WithParsed(RunOptions);
        }

        static void RunOptions(Options opts)
        {
            Console.WriteLine("Running Code Gen");
            var currentTime = 0L;
            var last_run = @"last_run.txt";
            if (opts.IsGame)
                last_run = @"last_run_game.txt";

            try
            {
                string text = File.ReadAllText(last_run);
                currentTime = long.Parse(text);
            }
            catch (Exception)
            {
                Console.WriteLine("Initial run, parsing all code");
            }

            var actualFiles = Directory.EnumerateFiles(opts.RootSource, "*.h", SearchOption.AllDirectories);
            if (actualFiles.Count() == 0)
            {
                Console.WriteLine("No new files to parse");
                currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                File.WriteAllText(@"last_run.txt", currentTime.ToString());
                return;
            }

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            Console.WriteLine($"Parsing {actualFiles.Count()} Files");

            var parserOptions = new CppParserOptions
            {
                ParseComments = false,
                ParseSystemIncludes = false,
                ParseAttributes = true,
                ParseAsCpp = true,
                ParseMacros = true,
                EnablePreprocessing = opts.EnablePreprocessing,
                CppStandard = "c++20"
            };

            parserOptions.Defines.Add("_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH");
            parserOptions.Defines.Add("_HAS_DEPRECATED_RESULT_OF");

            parserOptions.IncludeFolders.Add(opts.RootSource);
            if (!opts.IsGame)
                parserOptions.IncludeFolders.Add(opts.InputDirectory);
            else
                parserOptions.SystemIncludeFolders.Add(opts.InputDirectory);

            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/SDL3/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/parallel_hashmap/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/luajit/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/sol/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/box2d/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/fmod/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/steam/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/fmt/include");
            parserOptions.SystemIncludeFolders.Add($"{opts.libs}/GameNetworkingSockets/include");

            if (OperatingSystem.IsMacOS())
            {
                parserOptions.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/include");
                parserOptions.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/include/c++/v1");
                parserOptions.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk/usr/include");
                parserOptions.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/clang/11.0.0/include");
                parserOptions.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/clang/11.0.3/include");
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include/compat/osx");
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/ghc/include");
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/invoke/include");
            }

            if (OperatingSystem.IsLinux())
            {
                parserOptions.SystemIncludeFolders.Add($"/usr/include/c++/v1");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/8");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/9");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/c++/7");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/c++/9");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/8");
                parserOptions.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/9");
                parserOptions.SystemIncludeFolders.Add($"/usr/lib/clang/{opts.CppVersion}/include");
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/ghc/include");
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/invoke/include");
            }

            if (OperatingSystem.IsWindows())
            {
                parserOptions.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include/compat/msvc");
            }

            parserOptions.SystemIncludeFolders.Add($"{opts.InputDirectory}/external");

            var compilation = CppParser.ParseFiles(actualFiles.ToList(), parserOptions);

            if (compilation.DumpErrorsIfAny())
            {
                return;
            }

            void WriteWatch(Stopwatch watch, string prefix)
            {
                watch.Stop();
                TimeSpan ts = watch.Elapsed;
                string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                    ts.Hours, ts.Minutes, ts.Seconds,
                    ts.Milliseconds / 10);
                Console.WriteLine(prefix + elapsedTime);
            }

            {
                Stopwatch localWatch = new Stopwatch();
                localWatch.Start();
                Console.WriteLine("Running LuaCodeGenWriter");
                var lua = new LuaCodeGenWriter(compilation, opts.Scrib, opts.RootNamespace);
                lua.Run(opts.Output, opts.IsGame);
                WriteWatch(localWatch, "LuaCodeGenWriter Runtime: ");
            }

            {
                Stopwatch localWatch = new Stopwatch();
                localWatch.Start();
                Console.WriteLine("Running TypeScriptCodeGenWriter");
                var lua = new TypeScriptCodeGenWriter(compilation, opts.TypeScriptScrib, opts.RootNamespace);
                lua.Run(opts.Declarations, opts.IsGame);
                WriteWatch(localWatch, "TypeScriptCodeGenWriter Runtime: ");
            }

            WriteWatch(stopWatch, "Total Runtime: ");

            currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            File.WriteAllText(last_run, currentTime.ToString());
        }

    }
}

