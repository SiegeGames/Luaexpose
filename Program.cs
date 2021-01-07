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

    class Program
    {
        public class Options
        {
            [Option('r', "root", Required = true, HelpText = "Root Source Dir")]
            public string RootSource { get; set; }

            [Option('s', "siege", Required = true, HelpText = "Siege Source Dir")]
            public string SiegeSource { get; set; }

            [Option('o', "output", Required = true, HelpText = "Siege Source Dir")]
            public string Output { get; set; }

            [Option('l', "lib", Required = true, HelpText = "Siege Source Dir")]
            public string libs { get; set; }

            [Option('t', "temp", Required = true, HelpText = "Lua Output Template")]
            public string Scrib { get; set; }

            [Option('g', "game", Required = false, HelpText = "Siege Source Dir")]
            public bool IsGame { get; set; }

            [Option('c', "cpp", Required = false, HelpText = "Siege Source Dir")]
            public string CppVersion { get; set; }

            [Option('O', "tealoutput", Required = true, HelpText = "Teal Declaration Output")]
            public string Declarations { get; set; }
            [Option('T', "tealtemp", Required = true, HelpText = "Teal Output Template")]
            public string TealScrib { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
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

            var f = Directory.EnumerateFiles(opts.RootSource, "*.h", SearchOption.AllDirectories);
            CppParserOptions p = new CppParserOptions();
            p.ParseComments = false;
            p.Defines.Add("_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH");
            p.Defines.Add("_HAS_DEPRECATED_RESULT_OF");
            p.ParseSystemIncludes = false;
            p.ParseAttributes = true;
            p.ParseAsCpp = true;

            p.AdditionalArguments.Add("-std=c++2a");
            
            p.AdditionalArguments.Add("-xc++");
            p.AdditionalArguments.Add("-Wno-pragma-once-outside-header");
            p.AdditionalArguments.Add("-Wno-unknown-attributes");
            p.IncludeFolders.Add(opts.RootSource);
            if (!opts.IsGame)
                p.IncludeFolders.Add(opts.SiegeSource);
            else
                p.SystemIncludeFolders.Add(opts.SiegeSource);
            p.SystemIncludeFolders.Add($"{opts.libs}/SDL2/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/parallel_hashmap/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/luajit/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/sol/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/box2d/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/fmod/include");
            p.SystemIncludeFolders.Add($"{opts.libs}/steam/include");

            if (OperatingSystem.IsMacOS())
            {
                p.TargetSystem = "darwin";
                p.TargetVendor = "apple";
                p.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/include");
                p.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/include/c++/v1");
                p.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Platforms/MacOSX.platform/Developer/SDKs/MacOSX.sdk/usr/include");
                p.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/clang/11.0.0/include");
                p.SystemIncludeFolders.Add("/Applications/Xcode.app/Contents/Developer/Toolchains/XcodeDefault.xctoolchain/usr/lib/clang/11.0.3/include");
                p.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include/compat/osx");
                p.SystemIncludeFolders.Add($"{opts.libs}/ghc/include");

                p.AdditionalArguments.Add("-stdlib=libc++");
            }
            
            if (OperatingSystem.IsLinux())
            { 
                p.TargetAbi = "gnu";
                p.TargetSystem = "linux";
                p.AdditionalArguments.Add("-stdlib=libc++");
                p.SystemIncludeFolders.Add($"/usr/include/c++/v1");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/8");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/9");
                p.SystemIncludeFolders.Add($"/usr/include/c++/7");
                p.SystemIncludeFolders.Add($"/usr/include/c++/9");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/8");
                p.SystemIncludeFolders.Add($"/usr/include/x86_64-linux-gnu/c++/9");
                p.SystemIncludeFolders.Add($"/usr/lib/clang/{opts.CppVersion}/include");
                p.SystemIncludeFolders.Add($"{opts.libs}/ghc/include");
            }

            if (OperatingSystem.IsWindows())
            {
                p.SystemIncludeFolders.Add($"{opts.libs}/bgfx/include/compat/msvc");
            }
            
            p.SystemIncludeFolders.Add($"{opts.SiegeSource}/external");


            //var actualFiles = f.Where(x =>
            //{
            //    DateTimeOffset dto = (new FileInfo(x)).LastWriteTime;
            //    return true;
            //});
            var actualFiles = f;
            if (actualFiles.Count() == 0)
            {
                Console.WriteLine("No new files to parse");
                currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                File.WriteAllText(@"last_run.txt", currentTime.ToString());
                return;
            }

            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            Console.WriteLine($"Using libClang to parse {actualFiles.Count()} Files");

            p.ParseMacros = true;
            var compilation = CppParser.ParseFiles(actualFiles.ToList(), p);

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
                var lua = new LuaCodeGenWriter(compilation, opts.Scrib);
                lua.Run(opts.Output, opts.IsGame);
                WriteWatch(localWatch, "LuaCodeGenWriter Runtime: ");
            }

            {
                Stopwatch localWatch = new Stopwatch();
                localWatch.Start();
                Console.WriteLine("Running TealCodeGenWriter");
                var lua = new TealCodeGenWriter(compilation, opts.TealScrib);
                lua.Run(opts.Declarations, opts.IsGame);
                WriteWatch(localWatch, "TealCodeGenWriter Runtime: ");
            }

            WriteWatch(stopWatch, "Total Runtime: ");

            currentTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            File.WriteAllText(last_run, currentTime.ToString());
        }

    }
}

