// LuaExpose.Parser - C++ Preprocessor Integration
// Runs system preprocessors (clang -E, gcc -E, cl /E) to expand macros

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace LuaExpose.Parser.Preprocessor
{
    /// <summary>
    /// Result of a preprocessing operation.
    /// </summary>
    public class PreprocessorResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string ErrorOutput { get; set; }
        public string PreprocessorUsed { get; set; }
    }

    /// <summary>
    /// Options for the preprocessor.
    /// </summary>
    public class PreprocessorOptions
    {
        public List<string> Defines { get; } = new List<string>();
        public List<string> IncludeFolders { get; } = new List<string>();
        public List<string> SystemIncludeFolders { get; } = new List<string>();
        public string CppStandard { get; set; } = "c++20";
        public string PreferredPreprocessor { get; set; } // "clang", "gcc", "msvc", or null for auto-detect
        public int TimeoutMs { get; set; } = 30000;
    }

    /// <summary>
    /// Runs system C++ preprocessors to expand macros in source files.
    /// Uses a fallback chain: clang -> gcc -> MSVC
    /// </summary>
    public class CppPreprocessor
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Preprocess a single file.
        /// </summary>
        public static PreprocessorResult PreprocessFile(string filePath, PreprocessorOptions options = null)
        {
            options ??= new PreprocessorOptions();

            if (!File.Exists(filePath))
            {
                return new PreprocessorResult
                {
                    Success = false,
                    ErrorOutput = $"File not found: {filePath}"
                };
            }

            // Try preprocessors in order of preference
            var preprocessors = GetPreprocessorOrder(options.PreferredPreprocessor);

            foreach (var preprocessor in preprocessors)
            {
                var result = TryPreprocess(preprocessor, filePath, options);
                if (result.Success)
                {
                    return result;
                }
            }

            return new PreprocessorResult
            {
                Success = false,
                ErrorOutput = "No suitable preprocessor found. Install clang, gcc, or MSVC."
            };
        }

        /// <summary>
        /// Preprocess source code from a string (writes to temp file first).
        /// </summary>
        public static PreprocessorResult PreprocessString(string source, PreprocessorOptions options = null, string filename = "input.h")
        {
            options ??= new PreprocessorOptions();

            var tempDir = Path.Combine(Path.GetTempPath(), "LuaExpose_Preprocess");
            Directory.CreateDirectory(tempDir);

            var tempFile = Path.Combine(tempDir, filename);
            try
            {
                File.WriteAllText(tempFile, source);
                var result = PreprocessFile(tempFile, options);
                return result;
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        private static List<string> GetPreprocessorOrder(string preferred)
        {
            var order = new List<string>();

            if (!string.IsNullOrEmpty(preferred))
            {
                order.Add(preferred.ToLowerInvariant());
            }

            // Default order based on platform
            if (IsWindows)
            {
                if (!order.Contains("clang")) order.Add("clang");
                if (!order.Contains("msvc")) order.Add("msvc");
                if (!order.Contains("gcc")) order.Add("gcc");
            }
            else
            {
                if (!order.Contains("clang")) order.Add("clang");
                if (!order.Contains("gcc")) order.Add("gcc");
            }

            return order;
        }

        private static PreprocessorResult TryPreprocess(string preprocessor, string filePath, PreprocessorOptions options)
        {
            return preprocessor switch
            {
                "clang" => TryClang(filePath, options),
                "gcc" => TryGcc(filePath, options),
                "msvc" => TryMsvc(filePath, options),
                _ => new PreprocessorResult { Success = false, ErrorOutput = $"Unknown preprocessor: {preprocessor}" }
            };
        }

        private static PreprocessorResult TryClang(string filePath, PreprocessorOptions options)
        {
            var args = BuildClangArgs(filePath, options);
            return RunProcess("clang", args, options.TimeoutMs);
        }

        private static PreprocessorResult TryGcc(string filePath, PreprocessorOptions options)
        {
            var args = BuildGccArgs(filePath, options);
            return RunProcess("gcc", args, options.TimeoutMs);
        }

        private static PreprocessorResult TryMsvc(string filePath, PreprocessorOptions options)
        {
            // On Windows, try to find cl.exe in PATH or via vswhere
            var clPath = FindMsvcCompiler();
            if (clPath == null)
            {
                return new PreprocessorResult { Success = false, ErrorOutput = "MSVC cl.exe not found" };
            }

            var args = BuildMsvcArgs(filePath, options);
            return RunProcess(clPath, args, options.TimeoutMs);
        }

        private static string BuildClangArgs(string filePath, PreprocessorOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("-E -P "); // Preprocess only, no line markers
            sb.Append($"-x c++ -std={options.CppStandard} ");

            foreach (var define in options.Defines)
            {
                sb.Append($"-D{EscapeArg(define)} ");
            }

            foreach (var include in options.IncludeFolders)
            {
                sb.Append($"-I\"{include}\" ");
            }

            foreach (var sysInclude in options.SystemIncludeFolders)
            {
                sb.Append($"-isystem \"{sysInclude}\" ");
            }

            sb.Append($"\"{filePath}\"");
            return sb.ToString();
        }

        private static string BuildGccArgs(string filePath, PreprocessorOptions options)
        {
            // GCC uses same arguments as clang for preprocessing
            return BuildClangArgs(filePath, options);
        }

        private static string BuildMsvcArgs(string filePath, PreprocessorOptions options)
        {
            var sb = new StringBuilder();
            sb.Append("/E /TP "); // Preprocess to stdout, treat as C++

            // Map C++ standard to MSVC flag
            var stdFlag = options.CppStandard switch
            {
                "c++20" => "/std:c++20",
                "c++17" => "/std:c++17",
                "c++14" => "/std:c++14",
                _ => "/std:c++20"
            };
            sb.Append($"{stdFlag} ");

            foreach (var define in options.Defines)
            {
                sb.Append($"/D{EscapeArg(define)} ");
            }

            foreach (var include in options.IncludeFolders)
            {
                sb.Append($"/I\"{include}\" ");
            }

            foreach (var sysInclude in options.SystemIncludeFolders)
            {
                sb.Append($"/I\"{sysInclude}\" ");
            }

            sb.Append($"\"{filePath}\"");
            return sb.ToString();
        }

        private static PreprocessorResult RunProcess(string executable, string arguments, int timeoutMs)
        {
            var result = new PreprocessorResult { PreprocessorUsed = executable };

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };

                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                if (!process.WaitForExit(timeoutMs))
                {
                    process.Kill();
                    result.Success = false;
                    result.ErrorOutput = "Preprocessor timed out";
                    return result;
                }

                result.Output = outputBuilder.ToString();
                result.ErrorOutput = errorBuilder.ToString();
                result.Success = process.ExitCode == 0;

                // Filter out line markers if present (lines starting with #)
                if (result.Success)
                {
                    result.Output = FilterLineMarkers(result.Output);
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorOutput = ex.Message;
                return result;
            }
        }

        private static string FilterLineMarkers(string output)
        {
            // Remove preprocessor line markers like:
            // # 1 "filename.h"
            // # 1 "<built-in>" 1
            var sb = new StringBuilder();
            using var reader = new StringReader(output);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                // Skip lines that are just line markers
                if (!line.StartsWith("# ") && !line.StartsWith("#line "))
                {
                    sb.AppendLine(line);
                }
            }
            return sb.ToString();
        }

        private static string FindMsvcCompiler()
        {
            // First check if cl.exe is in PATH
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "cl.exe",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    var path = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (path.Length > 0 && File.Exists(path[0]))
                    {
                        return path[0];
                    }
                }
            }
            catch { }

            // Try vswhere to find Visual Studio
            try
            {
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var vswherePath = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe");

                if (File.Exists(vswherePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = vswherePath,
                        Arguments = "-latest -property installationPath",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    var vsPath = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(vsPath))
                    {
                        // Common paths for cl.exe in VS installation
                        var possiblePaths = new[]
                        {
                            Path.Combine(vsPath, "VC", "Tools", "MSVC"),
                        };

                        foreach (var basePath in possiblePaths)
                        {
                            if (Directory.Exists(basePath))
                            {
                                // Find latest version
                                var versions = Directory.GetDirectories(basePath);
                                foreach (var version in versions.OrderByDescending(v => v))
                                {
                                    var clPath = Path.Combine(version, "bin", "Hostx64", "x64", "cl.exe");
                                    if (File.Exists(clPath))
                                    {
                                        return clPath;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private static string EscapeArg(string arg)
        {
            // Escape special characters in defines
            if (arg.Contains(" ") || arg.Contains("\""))
            {
                return "\"" + arg.Replace("\"", "\\\"") + "\"";
            }
            return arg;
        }

        /// <summary>
        /// Check if a preprocessor is available on the system.
        /// </summary>
        public static bool IsPreprocessorAvailable(string preprocessor)
        {
            try
            {
                var executable = preprocessor.ToLowerInvariant() switch
                {
                    "clang" => "clang",
                    "gcc" => "gcc",
                    "msvc" => FindMsvcCompiler() ?? "cl",
                    _ => preprocessor
                };

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                process.WaitForExit(5000);
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get list of available preprocessors on this system.
        /// </summary>
        public static List<string> GetAvailablePreprocessors()
        {
            var available = new List<string>();

            if (IsPreprocessorAvailable("clang")) available.Add("clang");
            if (IsPreprocessorAvailable("gcc")) available.Add("gcc");
            if (IsWindows && IsPreprocessorAvailable("msvc")) available.Add("msvc");

            return available;
        }
    }
}
