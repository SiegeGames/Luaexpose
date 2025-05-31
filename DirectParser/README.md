# DirectParser - Lightweight C++ Parser for LuaExpose

## Overview

DirectParser is a lightweight, regex-based C++ parser designed specifically for LuaExpose. It replaces the heavy CppAST dependency with a simpler, faster solution focused on extracting Lua binding attributes and basic C++ structure.

## Key Benefits

1. **No External Dependencies**: No need for libclang or complex C++ parsing libraries
2. **Faster Parsing**: Focused only on what LuaExpose needs
3. **Easier to Debug**: Simple regex-based approach is easier to understand and modify
4. **Smaller Memory Footprint**: Doesn't build a full C++ AST
5. **Cross-Platform**: Pure C# implementation works everywhere

## Architecture

### Components

1. **SimpleParser.cs**: Main parser using regex patterns to extract:
   - Classes/structs with LUA attributes
   - Functions/methods with signatures
   - Enums and fields
   - Template declarations
   - Namespace structures

2. **ParsedElements.cs**: Lightweight AST representation:
   - Only stores information needed for code generation
   - Hierarchical structure (File → Namespace → Class → Members)
   - Attribute storage with argument parsing

3. **TypeParser.cs**: Handles C++ type parsing and conversion:
   - Parses complex C++ types (pointers, references, templates)
   - Converts to Sol3 types for binding generation
   - Converts to TypeScript types for .d.ts generation

4. **CppAstAdapter.cs**: Compatibility layer:
   - Converts DirectParser AST to CppAst format
   - Allows existing code generation to work unchanged

## Usage

### Command Line

```bash
# Use the new parser
LuaExpose --use-direct-parser -n MyNamespace -r /src -i /input -o /output -l /libs -t template.txt

# Run parser tests
LuaExpose --test-parser
```

### Programmatic Usage

```csharp
// Create parser
var parser = new SimpleParser();

// Parse a file
var result = parser.ParseFile("MyClass.h");

// Access parsed elements
foreach (var cls in result.GlobalScope.Classes)
{
    Console.WriteLine($"Class: {cls.Name}");
    foreach (var func in cls.Functions)
    {
        Console.WriteLine($"  Function: {func.Name}");
    }
}
```

## Supported C++ Constructs

### Attributes
- `[[LUA_USERTYPE]]` - Classes/structs
- `[[LUA_USERTYPE_TEMPLATE(T1, T2)]]` - Template classes
- `[[LUA_FUNC]]` - Functions
- `[[LUA_FUNC_TEMPLATE(T)]]` - Template functions
- `[[LUA_VAR]]` - Variables
- `[[LUA_USERTYPE_ENUM]]` - Enums
- And all other LUA_* attributes

### C++ Features
- Classes with inheritance
- Methods (static, const, virtual, override)
- Function overloading
- Templates (basic support)
- Namespaces
- Enums (including enum class)
- Fields and global variables
- Complex types (pointers, references, templates)

## Limitations

1. **No Macro Expansion**: Macros are not expanded (same as CppAST in practice)
2. **Limited Template Support**: Complex template metaprogramming may not parse correctly
3. **No Type Resolution**: Doesn't resolve typedefs or using declarations
4. **Basic Syntax Only**: Complex C++ syntax may require regex updates

## Integration with Existing Code

The parser is designed to be a drop-in replacement:

1. **Minimal Changes**: Only need to add `--use-direct-parser` flag
2. **Compatible Output**: CppAstAdapter ensures compatibility
3. **Fallback Option**: Can still use CppAST when needed

## Example

```cpp
// Input C++ file
[[LUA_USERTYPE]]
class Player : public GameObject {
public:
    [[LUA_CTOR]]
    Player(const std::string& name);
    
    [[LUA_FUNC]]
    void move(Vec2<float> position);
    
    [[LUA_VAR_READONLY]]
    int health;
};
```

The DirectParser will extract:
- Class name: `Player`
- Base class: `GameObject`
- Constructor with string parameter
- Method `move` with `Vec2<float>` parameter
- Read-only field `health` of type `int`

## Future Improvements

1. **Incremental Parsing**: Parse only changed files
2. **Better Error Messages**: More detailed parsing error information
3. **Performance Optimization**: Compiled regex patterns, parallel parsing
4. **Extended C++ Support**: More complex template scenarios
5. **Direct Code Generation**: Skip CppAst compatibility layer for better performance