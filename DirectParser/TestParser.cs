using System;
using System.IO;
using System.Linq;

namespace LuaExpose.DirectParser
{
    public class TestParser
    {
        public static void RunTests()
        {
            Console.WriteLine("=== DirectParser Test Suite ===\n");
            
            TestBasicClass();
            TestTemplateClass();
            TestFunctions();
            TestEnums();
            TestComplexTypes();
            
            Console.WriteLine("\n=== All tests completed ===");
        }

        private static void TestBasicClass()
        {
            Console.WriteLine("Test: Basic Class Parsing");
            
            var testCode = @"
[[LUA_USERTYPE]]
class TestClass : public BaseClass {
public:
    [[LUA_CTOR]]
    TestClass(int x, float y);
    
    [[LUA_FUNC]]
    void doSomething(const std::string& str);
    
    [[LUA_FUNC(use_static)]]
    static int getCount();
    
    [[LUA_VAR]]
    int value;
    
    [[LUA_VAR_READONLY]]
    float readOnlyValue;
    
private:
    [[LUA_FUNC]]
    void privateMethod();
};";

            var parser = new SimpleParser();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, testCode);
            
            try
            {
                var result = parser.ParseFile(tempFile);
                
                Console.WriteLine($"Classes found: {result.GlobalScope.Classes.Count}");
                var testClass = result.GlobalScope.Classes.FirstOrDefault();
                if (testClass != null)
                {
                    Console.WriteLine($"  Name: {testClass.Name}");
                    Console.WriteLine($"  Base classes: {string.Join(", ", testClass.BaseClasses)}");
                    Console.WriteLine($"  Functions: {testClass.Functions.Count}");
                    Console.WriteLine($"  Fields: {testClass.Fields.Count}");
                    
                    foreach (var func in testClass.Functions)
                    {
                        Console.WriteLine($"    Function: {func.Name} ({func.Attributes.Count} attributes)");
                        foreach (var attr in func.Attributes)
                        {
                            Console.WriteLine($"      Attribute: {attr.Name}");
                            if (attr.Arguments.Count > 0)
                            {
                                Console.WriteLine($"        Args: {string.Join(", ", attr.Arguments.Select(a => $"{a.Key}={a.Value}"))}");
                            }
                        }
                    }
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
            Console.WriteLine();
        }

        private static void TestTemplateClass()
        {
            Console.WriteLine("Test: Template Class Parsing");
            
            var testCode = @"
[[LUA_USERTYPE_TEMPLATE(T, U)]]
template<typename T, typename U>
class TemplateClass {
public:
    [[LUA_FUNC_TEMPLATE(float, double)]]
    T getValue();
    
    [[LUA_VAR]]
    U data;
};";

            var parser = new SimpleParser();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, testCode);
            
            try
            {
                var result = parser.ParseFile(tempFile);
                var templateClass = result.GlobalScope.Classes.FirstOrDefault();
                if (templateClass != null)
                {
                    Console.WriteLine($"  Name: {templateClass.Name}");
                    var templateAttr = templateClass.Attributes.FirstOrDefault(a => a.Name.Contains("TEMPLATE"));
                    if (templateAttr != null)
                    {
                        Console.WriteLine($"  Template args: {string.Join(", ", templateAttr.GetTemplateArguments())}");
                    }
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
            Console.WriteLine();
        }

        private static void TestFunctions()
        {
            Console.WriteLine("Test: Function Parsing");
            
            var testCode = @"
[[LUA_FUNC]]
void globalFunction(int x, float y = 3.14f);

[[LUA_FUNC_OVERLOAD]]
void overloadedFunc(int x);

[[LUA_FUNC_OVERLOAD]]
void overloadedFunc(float x);

namespace TestNamespace {
    [[LUA_FUNC]]
    std::vector<int> getVector();
}";

            var parser = new SimpleParser();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, testCode);
            
            try
            {
                var result = parser.ParseFile(tempFile);
                
                Console.WriteLine($"Global functions: {result.GlobalScope.Functions.Count}");
                foreach (var func in result.GlobalScope.Functions)
                {
                    Console.WriteLine($"  {func.GetSignature()}");
                }
                
                Console.WriteLine($"Namespaces: {result.Namespaces.Count}");
                foreach (var ns in result.Namespaces)
                {
                    Console.WriteLine($"  Namespace: {ns.Name}");
                    Console.WriteLine($"    Functions: {ns.Functions.Count}");
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
            Console.WriteLine();
        }

        private static void TestEnums()
        {
            Console.WriteLine("Test: Enum Parsing");
            
            var testCode = @"
[[LUA_USERTYPE_ENUM]]
enum class TestEnum : uint32_t {
    Value1,
    Value2 = 10,
    Value3 = Value2 + 5
};

[[LUA_USERTYPE_ENUM]]
enum SimpleEnum {
    A, B, C
};";

            var parser = new SimpleParser();
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, testCode);
            
            try
            {
                var result = parser.ParseFile(tempFile);
                
                Console.WriteLine($"Enums found: {result.GlobalScope.Enums.Count}");
                foreach (var enm in result.GlobalScope.Enums)
                {
                    Console.WriteLine($"  Enum: {enm.Name} : {enm.UnderlyingType}");
                    foreach (var val in enm.Values)
                    {
                        Console.WriteLine($"    {val.Name}{(val.Value != null ? " = " + val.Value : "")}");
                    }
                }
            }
            finally
            {
                File.Delete(tempFile);
            }
            Console.WriteLine();
        }

        private static void TestComplexTypes()
        {
            Console.WriteLine("Test: Complex Type Parsing");
            
            var typeParser = new TypeParser();
            
            var testTypes = new[]
            {
                "const int*",
                "std::vector<std::string>",
                "std::shared_ptr<MyClass>",
                "std::function<void(int, float)>",
                "const MyClass&",
                "MyClass&&",
                "std::optional<std::vector<int>>",
                "const char* const",
                "Vec2<float>"
            };
            
            foreach (var typeStr in testTypes)
            {
                var parsed = typeParser.ParseType(typeStr);
                Console.WriteLine($"  {typeStr} => {parsed}");
                Console.WriteLine($"    Sol: {typeParser.ConvertToSolType(parsed)}");
                Console.WriteLine($"    TS: {typeParser.ConvertToTypeScriptType(parsed)}");
            }
            Console.WriteLine();
        }
    }
}