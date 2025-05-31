using System;
using System.Collections.Generic;
using System.Linq;
using CppAst;

namespace LuaExpose.DirectParser
{
    /// <summary>
    /// Adapter that converts DirectParser elements to CppAst-compatible types
    /// This allows the existing code generation to work with the new parser
    /// </summary>
    public static class CppAstAdapter
    {
        private static readonly TypeParser TypeParser = new TypeParser();

        public static CppCompilation ConvertToCompilation(List<ParsedFile> parsedFiles)
        {
            var compilation = new CppCompilation();
            
            foreach (var file in parsedFiles)
            {
                // Process global scope
                ProcessScope(file.GlobalScope, compilation);
                
                // Process namespaces
                foreach (var ns in file.Namespaces)
                {
                    var cppNamespace = ConvertNamespace(ns);
                    compilation.Namespaces.Add(cppNamespace);
                }
            }
            
            return compilation;
        }

        private static void ProcessScope(ParsedScope scope, ICppGlobalDeclarationContainer container)
        {
            foreach (var cls in scope.Classes)
            {
                container.Classes.Add(ConvertClass(cls));
            }
            
            foreach (var func in scope.Functions)
            {
                container.Functions.Add(ConvertFunction(func));
            }
            
            foreach (var enm in scope.Enums)
            {
                container.Enums.Add(ConvertEnum(enm));
            }
            
            foreach (var field in scope.Fields)
            {
                // Global variables are not directly supported in CppAst the same way
                // We'll handle them through a workaround if needed
            }
        }

        private static CppNamespace ConvertNamespace(ParsedNamespace parsedNs)
        {
            var ns = new CppNamespace(parsedNs.Name);
            
            // Add attributes
            foreach (var attr in parsedNs.Attributes)
            {
                ns.Attributes.Add(ConvertAttribute(attr));
            }
            
            // Process namespace contents
            ProcessScope(parsedNs, ns);
            
            return ns;
        }

        private static CppClass ConvertClass(ParsedClass parsedClass)
        {
            var cls = new CppClass(parsedClass.Name)
            {
                ClassKind = CppClassKind.Class,
                SizeOf = 0 // We don't calculate size
            };
            
            // Add attributes
            foreach (var attr in parsedClass.Attributes)
            {
                cls.Attributes.Add(ConvertAttribute(attr));
            }
            
            // Add base classes
            foreach (var baseClass in parsedClass.BaseClasses)
            {
                cls.BaseTypes.Add(new CppBaseType(CreateTypeRef(baseClass)));
            }
            
            // Add methods
            foreach (var func in parsedClass.Functions)
            {
                var method = ConvertToMethod(func, cls);
                cls.Functions.Add(method);
            }
            
            // Add fields
            foreach (var field in parsedClass.Fields)
            {
                cls.Fields.Add(ConvertField(field));
            }
            
            return cls;
        }

        private static CppFunction ConvertFunction(ParsedFunction parsedFunc)
        {
            var func = new CppFunction(parsedFunc.Name)
            {
                ReturnType = CreateTypeRef(parsedFunc.ReturnType),
                StorageQualifier = parsedFunc.IsStatic ? CppStorageQualifier.Static : CppStorageQualifier.None
            };
            
            // Add attributes
            foreach (var attr in parsedFunc.Attributes)
            {
                func.Attributes.Add(ConvertAttribute(attr));
            }
            
            // Add parameters
            foreach (var param in parsedFunc.Parameters)
            {
                func.Parameters.Add(ConvertParameter(param));
            }
            
            // Handle template parameters
            if (parsedFunc.TemplateParameters.Count > 0)
            {
                // CppAst doesn't directly support template parameters on functions
                // We'll store them in a custom attribute
                func.Attributes.Add(new CppAttribute($"TEMPLATE_{string.Join("_", parsedFunc.TemplateParameters)}"));
            }
            
            return func;
        }

        private static CppFunction ConvertToMethod(ParsedFunction parsedFunc, CppClass parent)
        {
            var method = ConvertFunction(parsedFunc);
            
            // Set method-specific properties
            if (parsedFunc.IsVirtual)
            {
                method.Flags |= CppFunctionFlags.Virtual;
            }
            
            if (parsedFunc.IsConst)
            {
                method.Flags |= CppFunctionFlags.Const;
            }
            
            // Note: CppAst doesn't have an Override flag
            // Store as attribute instead
            if (parsedFunc.IsOverride)
            {
                method.Attributes.Add(new CppAttribute("override"));
            }
            
            // Parent is set automatically when added to class
            
            return method;
        }

        private static CppEnum ConvertEnum(ParsedEnum parsedEnum)
        {
            var enm = new CppEnum(parsedEnum.Name)
            {
                IntegerType = CreateTypeRef(parsedEnum.UnderlyingType)
            };
            
            // Add attributes
            foreach (var attr in parsedEnum.Attributes)
            {
                enm.Attributes.Add(ConvertAttribute(attr));
            }
            
            // Add values
            foreach (var value in parsedEnum.Values)
            {
                var enumItem = new CppEnumItem(value.Name, 0); // Default value
                
                // Parse value if provided
                if (!string.IsNullOrEmpty(value.Value))
                {
                    if (long.TryParse(value.Value, out var intValue))
                    {
                        enumItem.Value = intValue;
                    }
                    else
                    {
                        // For complex expressions, store as ValueExpression
                        var expr = new CppRawExpression(CppExpressionKind.Unexposed);
                        expr.Text = value.Value;
                        enumItem.ValueExpression = expr;
                    }
                }
                
                enm.Items.Add(enumItem);
            }
            
            return enm;
        }

        private static CppField ConvertField(ParsedField parsedField)
        {
            var field = new CppField(CreateTypeRef(parsedField.Type), parsedField.Name)
            {
                StorageQualifier = parsedField.IsStatic ? CppStorageQualifier.Static : CppStorageQualifier.None
            };
            
            // Add attributes
            foreach (var attr in parsedField.Attributes)
            {
                field.Attributes.Add(ConvertAttribute(attr));
            }
            
            return field;
        }

        private static CppParameter ConvertParameter(ParsedParameter parsedParam)
        {
            var param = new CppParameter(CreateTypeRef(parsedParam.Type), parsedParam.Name ?? "");
            
            // Store default value if present
            if (!string.IsNullOrEmpty(parsedParam.DefaultValue))
            {
                var expr = new CppRawExpression(CppExpressionKind.Unexposed);
                expr.Text = parsedParam.DefaultValue;
                param.InitExpression = expr;
            }
            
            return param;
        }

        private static CppAttribute ConvertAttribute(ParsedAttribute parsedAttr)
        {
            var attr = new CppAttribute(parsedAttr.Name);
            
            // Convert arguments
            if (parsedAttr.Arguments.Count > 0)
            {
                // For template-style arguments
                var templateArgs = parsedAttr.GetTemplateArguments();
                if (templateArgs.Count > 0)
                {
                    attr.Arguments = string.Join(", ", templateArgs);
                }
                else
                {
                    // For key=value arguments
                    var argStrings = new List<string>();
                    foreach (var kvp in parsedAttr.Arguments)
                    {
                        if (kvp.Value == "true")
                        {
                            argStrings.Add(kvp.Key);
                        }
                        else
                        {
                            argStrings.Add($"{kvp.Key}={kvp.Value}");
                        }
                    }
                    attr.Arguments = string.Join(", ", argStrings);
                }
            }
            
            return attr;
        }

        private static CppType CreateTypeRef(string typeString)
        {
            var parsed = TypeParser.ParseType(typeString);
            
            // Create appropriate CppType based on parsed information
            CppType baseType;
            
            // Check for primitive types
            if (IsPrimitiveType(parsed.Name))
            {
                baseType = GetPrimitiveType(parsed.Name);
            }
            else
            {
                // For complex types, create a class reference
                // Note: This is a simplification - proper type resolution would be needed
                baseType = new CppClass(parsed.Name);
            }
            
            // Apply modifiers
            CppType result = baseType;
            
            if (parsed.IsConst)
            {
                result = new CppQualifiedType(CppTypeQualifier.Const, result);
            }
            
            if (parsed.IsPointer)
            {
                for (int i = 0; i < parsed.PointerLevel; i++)
                {
                    result = new CppPointerType(result);
                }
            }
            
            if (parsed.IsReference || parsed.IsRValueReference)
            {
                // CppAst only supports lvalue references
                result = new CppReferenceType(result);
            }
            
            return result;
        }

        private static bool IsPrimitiveType(string typeName)
        {
            var primitives = new[] { "void", "bool", "char", "short", "int", "long", 
                                    "float", "double", "int8_t", "int16_t", "int32_t", 
                                    "int64_t", "uint8_t", "uint16_t", "uint32_t", "uint64_t" };
            return primitives.Contains(typeName);
        }

        private static CppPrimitiveType GetPrimitiveType(string typeName)
        {
            return typeName switch
            {
                "void" => CppPrimitiveType.Void,
                "bool" => CppPrimitiveType.Bool,
                "char" => CppPrimitiveType.Char,
                "short" => CppPrimitiveType.Short,
                "int" => CppPrimitiveType.Int,
                "long" => CppPrimitiveType.LongLong, // Note: CppAst doesn't have separate Long
                "float" => CppPrimitiveType.Float,
                "double" => CppPrimitiveType.Double,
                "int8_t" => CppPrimitiveType.Char,
                "int16_t" => CppPrimitiveType.Short,
                "int32_t" => CppPrimitiveType.Int,
                "int64_t" => CppPrimitiveType.LongLong,
                "uint8_t" => CppPrimitiveType.UnsignedChar,
                "uint16_t" => CppPrimitiveType.UnsignedShort,
                "uint32_t" => CppPrimitiveType.UnsignedInt,
                "uint64_t" => CppPrimitiveType.UnsignedLongLong,
                _ => CppPrimitiveType.Int
            };
        }
    }
}