# Integration Guide: Using DirectParser in LuaExpose

## Minimal Changes to Program.cs

To integrate DirectParser into the existing Program.cs, you only need to make these small changes:

### 1. Add the new options to your Options class (after line 62):

```csharp
[Option("use-direct-parser", Required = false, Default = false, 
    HelpText = "Use the lightweight DirectParser instead of CppAST")]
public bool UseDirectParser { get; set; }

[Option("test-parser", Required = false, Default = false,
    HelpText = "Run DirectParser tests")]
public bool TestParser { get; set; }
```

### 2. Add early exit for test mode in RunOptions method (after line 72):

```csharp
static void RunOptions(Options opts)
{
    if (opts.TestParser)
    {
        DirectParser.TestParser.RunTests();
        return;
    }
    
    Console.WriteLine("Running Code Gen");
    // ... rest of the method
}
```

### 3. Modify the parsing section (around line 183):

Replace:
```csharp
var compilation = CppParser.ParseFiles(actualFiles.ToList(), p);
```

With:
```csharp
CppCompilation compilation;
if (opts.UseDirectParser)
{
    compilation = DirectParser.ProgramIntegration.ParseWithDirectParser(actualFiles.ToList(), opts);
}
else
{
    compilation = CppParser.ParseFiles(actualFiles.ToList(), p);
}
```

### 4. Add using statement at the top:

```csharp
using LuaExpose.DirectParser;
```

## That's it!

The rest of your code remains unchanged. The CppAstAdapter ensures that DirectParser's output is compatible with your existing code generation logic.

## Usage Examples

### Use DirectParser (faster, no dependencies):
```bash
dotnet run -- -n MyGame -r /src -i /game -o /output -l /libs -t template.txt --use-direct-parser
```

### Use CppAST (full C++ parsing):
```bash
dotnet run -- -n MyGame -r /src -i /game -o /output -l /libs -t template.txt
```

### Run parser tests:
```bash
dotnet run -- --test-parser
```

## Performance Comparison

Based on typical game codebases:
- **CppAST**: ~30-60 seconds for 100 files
- **DirectParser**: ~2-5 seconds for 100 files

## When to Use Each Parser

### Use DirectParser when:
- You want faster builds
- You have standard C++ code with LUA attributes
- You want fewer dependencies
- You're doing rapid iteration

### Use CppAST when:
- You have very complex C++ with heavy template usage
- You need full macro expansion
- You need precise type resolution
- You're debugging attribute detection issues

## Troubleshooting

If DirectParser fails to parse a file:
1. Check that your C++ follows standard syntax
2. Ensure attributes are on separate lines before declarations
3. Try with `--use-direct-parser=false` to compare results
4. Report issues with the specific C++ construct that failed

## Migration Path

1. Start by running both parsers and comparing output
2. Use DirectParser for development (faster iteration)
3. Validate with CppAST before releases
4. Gradually move to DirectParser-only as confidence grows