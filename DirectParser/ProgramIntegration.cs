using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommandLine;
using CppAst;

namespace LuaExpose.DirectParser
{
    /// <summary>
    /// Integration module to use DirectParser with the existing LuaExpose program
    /// </summary>
    public static class ProgramIntegration
    {
        /// <summary>
        /// Parse files using DirectParser instead of CppAST
        /// </summary>
        public static CppCompilation ParseWithDirectParser(List<string> files, Program.Options options)
        {
            Console.WriteLine($"[DirectParser] Parsing {files.Count} files...");
            
            var parser = new SimpleParser();
            var parsedFiles = new List<ParsedFile>();
            var errors = new List<string>();
            
            // Parse each file
            foreach (var file in files)
            {
                try
                {
                    Console.WriteLine($"[DirectParser] Parsing: {file}");
                    var result = parser.ParseFile(file);
                    parsedFiles.Add(result);
                }
                catch (Exception ex)
                {
                    errors.Add($"Error parsing {file}: {ex.Message}");
                    Console.WriteLine($"[DirectParser] ERROR: {ex.Message}");
                }
            }
            
            // Report results
            Console.WriteLine($"[DirectParser] Parsed {parsedFiles.Count} files successfully");
            if (errors.Count > 0)
            {
                Console.WriteLine($"[DirectParser] {errors.Count} files failed to parse:");
                foreach (var error in errors)
                {
                    Console.WriteLine($"  - {error}");
                }
            }
            
            // Convert to CppCompilation for compatibility
            var compilation = CppAstAdapter.ConvertToCompilation(parsedFiles);
            
            // Add diagnostics
            foreach (var error in errors)
            {
                compilation.Diagnostics.Error(error);
            }
            
            return compilation;
        }

        /// <summary>
        /// Get include paths for DirectParser (simplified version)
        /// </summary>
        public static List<string> GetDirectParserIncludes(Program.Options options)
        {
            var includes = new List<string>
            {
                options.RootSource,
                options.InputDirectory,
                options.libs
            };
            
            // Add common library paths
            var libPath = options.libs;
            includes.Add(Path.Combine(libPath, "sol", "include"));
            includes.Add(Path.Combine(libPath, "luajit", "include"));
            
            if (options.IsGame)
            {
                includes.Add(Path.Combine(libPath, "SDL3", "include"));
                includes.Add(Path.Combine(libPath, "box2d", "include"));
                includes.Add(Path.Combine(libPath, "fmod", "include"));
            }
            
            return includes.Distinct().ToList();
        }
    }
}