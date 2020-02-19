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

            [Option('t', "temp", Required = true, HelpText = "Siege Source Dir")]
            public string Scrib { get; set; }

            [Option('g', "game", Required = false, HelpText = "Siege Source Dir")]
            public bool IsGame { get; set; }
        }

        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
            .WithParsed(RunOptions);
        }

        static void RunOptions(Options opts)
        {
            Console.WriteLine(opts.ToString());

            var f = Directory.EnumerateFiles(opts.RootSource, "*.h", SearchOption.AllDirectories);
            CppParserOptions p = new CppParserOptions();
            p.ParseComments = false;
            p.Defines.Add("_ALLOW_COMPILER_AND_STL_VERSION_MISMATCH");
            p.ParseSystemIncludes = false;
            p.AdditionalArguments.Add("-std=c++17");
            if (OperatingSystem.IsMacOS())
                p.AdditionalArguments.Add("-stdlib=libc++");
            p.AdditionalArguments.Add("-xc++");
            p.AdditionalArguments.Add("-Wno-pragma-once-outside-header");
            p.AdditionalArguments.Add("-Wno-unknown-attributes");
            p.IncludeFolders.Add(opts.RootSource);
            if (!opts.IsGame)
                p.IncludeFolders.Add(opts.SiegeSource);
            else
                p.SystemIncludeFolders.Add(opts.SiegeSource);
            p.SystemIncludeFolders.Add($"{opts.libs}\\SDL2\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\parallel_hashmap\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\bgfx\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\luajit\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\sol\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\box2d\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\fmod\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\steam\\include");
            p.SystemIncludeFolders.Add($"{opts.libs}\\bgfx\\include\\compat\\msvc");
            p.SystemIncludeFolders.Add($"{opts.SiegeSource}\\external");


            Stopwatch stopWatch = new Stopwatch();
            stopWatch.Start();

            Console.WriteLine("Using libClang to parse Files");

            var compilation = CppParser.ParseFiles(f.ToList(), p);

            if (compilation.DumpErrorsIfAny())
            {
                return;
            }

            Console.WriteLine("Running LuaCodeGenWriter");
            var lua = new LuaCodeGenWriter(compilation, opts.Scrib);
            lua.Run(opts.Output, opts.IsGame);

            stopWatch.Stop();
            TimeSpan ts = stopWatch.Elapsed;
            string elapsedTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);
            Console.WriteLine("RunTime " + elapsedTime);
        }

    }
}
