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
        // Keep track of all classes for type resolution
        private static Dictionary<string, CppClass> _classLookup = new Dictionary<string, CppClass>();

        public static CppCompilation ConvertToCompilation(List<ParsedFile> parsedFiles)
        {
            var compilation = new CppCompilation();
            var namespaceMap = new Dictionary<string, CppNamespace>();
            _classLookup.Clear();
            _typedefLookup.Clear();
            
            // First pass: Create all classes (without members) to enable cross-references
            foreach (var file in parsedFiles)
            {
                PreprocessClasses(file);
            }
            
            // Second pass: Process files normally
            foreach (var file in parsedFiles)
            {
                // Process namespaces - merge across files
                foreach (var parsedNs in file.Namespaces)
                {
                    CppNamespace cppNamespace;
                    
                    if (namespaceMap.ContainsKey(parsedNs.Name))
                    {
                        // Merge into existing namespace
                        cppNamespace = namespaceMap[parsedNs.Name];
                        System.Console.WriteLine($"[DirectParser] Merging namespace '{parsedNs.Name}' from {file.FilePath}");
                        System.Console.WriteLine($"  - Has {parsedNs.NestedNamespaces?.Count ?? 0} nested namespaces to merge");
                        
                        // Merge attributes (avoid duplicates)
                        if (parsedNs.Attributes != null)
                        {
                            foreach (var attr in parsedNs.Attributes)
                            {
                                var convertedAttr = ConvertAttribute(attr);
                                if (!cppNamespace.Attributes.Any(a => a.Name == convertedAttr.Name))
                                {
                                    cppNamespace.Attributes.Add(convertedAttr);
                                }
                            }
                        }
                        
                        // Process and merge contents
                        ProcessScope(parsedNs, cppNamespace);
                        
                        // Process nested namespaces
                        if (parsedNs.NestedNamespaces != null)
                        {
                            foreach (var nestedNs in parsedNs.NestedNamespaces)
                            {
                                var convertedNested = ConvertNamespace(nestedNs);
                                cppNamespace.Namespaces.Add(convertedNested);
                            }
                        }

                    }
                    else
                    {
                        // Create new namespace
                        cppNamespace = ConvertNamespace(parsedNs);
                        namespaceMap[parsedNs.Name] = cppNamespace;
                    }
                }
                
                // Process global scope
                ProcessScope(file.GlobalScope, compilation);
            }
            
            // Add all merged namespaces to compilation
            foreach (var ns in namespaceMap.Values)
            {
                compilation.Namespaces.Add(ns);
                
                //PrintNamespaceHierarchy(ns, 0);
            }
            
            return compilation;
        }
        
        private static void PreprocessClasses(ParsedFile file)
        {
            // Preprocess classes from global scope
            if (file.GlobalScope?.Classes != null)
            {
                foreach (var cls in file.GlobalScope.Classes)
                {
                    CreateClassStub(cls);
                }
            }
            
            // Preprocess classes from namespaces
            foreach (var ns in file.Namespaces)
            {
                PreprocessNamespaceClasses(ns);
            }
        }
        
        private static void PreprocessNamespaceClasses(ParsedNamespace ns)
        {
            if (ns.Classes != null)
            {
                foreach (var cls in ns.Classes)
                {
                    CreateClassStub(cls);
                }
            }
            
            // Recursively process nested namespaces
            if (ns.NestedNamespaces != null)
            {
                foreach (var nested in ns.NestedNamespaces)
                {
                    PreprocessNamespaceClasses(nested);
                }
            }
        }
        
        private static void CreateClassStub(ParsedClass parsedClass)
        {
            var fullName = string.IsNullOrEmpty(parsedClass.Namespace) ? parsedClass.Name : $"{parsedClass.Namespace}::{parsedClass.Name}";
            if (!_classLookup.ContainsKey(fullName))
            {
                var cls = new CppClass(parsedClass.Name)
                {
                    ClassKind = CppClassKind.Class,
                    SizeOf = 0
                };
                
                // Set source location - critical for CodeGenWriter
                if (!string.IsNullOrEmpty(parsedClass.FilePath))
                {
                    cls.Span = new CppSourceSpan(
                        new CppSourceLocation(parsedClass.FilePath, parsedClass.StartOffset, 0, 0),
                        new CppSourceLocation(parsedClass.FilePath, parsedClass.EndOffset, 0, 0)
                    );
                }
                
                // Add attributes so IsClassTwo() works
                if (parsedClass.Attributes != null)
                {
                    foreach (var attr in parsedClass.Attributes)
                    {
                        cls.Attributes.Add(ConvertAttribute(attr));
                    }
                }
                
                _classLookup[parsedClass.Name] = cls;
                _classLookup[fullName] = cls;
            }
        }
        
        private static void PrintNamespaceHierarchy(CppNamespace ns, int indent)
        {
            var indentStr = new string(' ', indent * 2);
            System.Console.WriteLine($"{indentStr}[DirectParser] Namespace '{ns.Name}':");
            System.Console.WriteLine($"{indentStr}  - {ns.Classes.Count} classes");
            System.Console.WriteLine($"{indentStr}  - {ns.Functions.Count} functions");
            System.Console.WriteLine($"{indentStr}  - {ns.Enums.Count} enums");
            System.Console.WriteLine($"{indentStr}  - {ns.Attributes.Count} attributes");
            foreach (var attr in ns.Attributes)
            {
                System.Console.WriteLine($"{indentStr}    * {attr.Name}");
            }
            System.Console.WriteLine($"{indentStr}  - {ns.Namespaces.Count} nested namespaces");
            
            // Print nested namespaces
            foreach (var nested in ns.Namespaces)
            {
                PrintNamespaceHierarchy(nested, indent + 1);
            }
        }

        private static Dictionary<string, ParsedTypedef> _typedefLookup = new Dictionary<string, ParsedTypedef>();
        
        private static void ProcessScope(ParsedScope scope, ICppGlobalDeclarationContainer container)
        {
            // First, collect all typedefs for lookup
            if (scope.Typedefs != null)
            {
                foreach (var typedef in scope.Typedefs)
                {
                    var fullName = string.IsNullOrEmpty(typedef.Namespace) ? typedef.Name : $"{typedef.Namespace}::{typedef.Name}";
                    _typedefLookup[typedef.Name] = typedef;
                    _typedefLookup[fullName] = typedef;
                    container.Typedefs.Add(ConvertTypedef(typedef));
                }
            }
            
            if (scope.Classes != null)
            {
                var additionalClasses = new List<CppClass>();
                
                foreach (var cls in scope.Classes)
                {
                    var convertedClass = ConvertClass(cls);
                    // Only add if not already in container
                    if (!container.Classes.Contains(convertedClass))
                    {
                        container.Classes.Add(convertedClass);
                    }
                    
                    // Check if this class has LUA_USERTYPE_TEMPLATE attribute
                    var templateAttr = cls.Attributes?.FirstOrDefault(a => a.Name == "LUA_USERTYPE_TEMPLATE");
                    if (templateAttr != null)
                    {
                        // Create specialized versions for each typedef mentioned
                        var templateArgs = templateAttr.GetTemplateArguments();
                        foreach (var typedefName in templateArgs)
                        {
                            if (_typedefLookup.TryGetValue(typedefName, out var typedef))
                            {
                                var specializedClass = CreateSpecializedTemplateClass(cls, typedef, typedefName);
                                if (specializedClass != null && !container.Classes.Any(c => c.Name == specializedClass.Name))
                                {
                                    additionalClasses.Add(specializedClass);
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[CppAstAdapter] Warning: Could not find typedef '{typedefName}' for template specialization");
                            }
                        }
                    }
                }
                
                // Add the specialized classes
                foreach (var specializedClass in additionalClasses)
                {
                    container.Classes.Add(specializedClass);
                }
            }
            
            if (scope.Functions != null)
            {
                foreach (var func in scope.Functions)
                {
                  container.Functions.Add(ConvertFunction(func));
                }
            }
            
            if (scope.Enums != null)
            {
                foreach (var enm in scope.Enums)
                {
                    container.Enums.Add(ConvertEnum(enm));
                }
            }
            
            if (scope.Fields != null)
            {
                foreach (var field in scope.Fields)
                {
                    // Global variables are not directly supported in CppAst the same way
                    // We'll handle them through a workaround if needed
                }
            }
        }

        private static CppNamespace ConvertNamespace(ParsedNamespace parsedNs)
        {
            var ns = new CppNamespace(parsedNs.Name);
            
            // Set source location - critical for CodeGenWriter
            if (!string.IsNullOrEmpty(parsedNs.FilePath))
            {
                ns.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedNs.FilePath, parsedNs.StartOffset, 0, 0),
                    new CppSourceLocation(parsedNs.FilePath, parsedNs.EndOffset, 0, 0)
                );
            }
            
            // Add attributes
            if (parsedNs.Attributes != null)
            {
                foreach (var attr in parsedNs.Attributes)
                {
                    ns.Attributes.Add(ConvertAttribute(attr));
                }
            }
            
            // Process namespace contents
            ProcessScope(parsedNs, ns);
            
            // Process nested namespaces
            if (parsedNs.NestedNamespaces != null)
            {
                foreach (var nestedNs in parsedNs.NestedNamespaces)
                {
                    var convertedNested = ConvertNamespace(nestedNs);
                    ns.Namespaces.Add(convertedNested);
                }
            }
            
            return ns;
        }

        private static CppClass ConvertClass(ParsedClass parsedClass)
        {
            // Check if we already converted this class
            var fullName = string.IsNullOrEmpty(parsedClass.Namespace) ? parsedClass.Name : $"{parsedClass.Namespace}::{parsedClass.Name}";
            if (_classLookup.ContainsKey(fullName))
            {
                var existingClass = _classLookup[fullName];
                
                // Process base class from LUA_USERTYPE_NO_CTOR attribute if not already done
                var existingNoctorAttr = parsedClass.Attributes?.FirstOrDefault(a => a.Name == "LUA_USERTYPE_NO_CTOR");
                if (existingNoctorAttr != null && existingNoctorAttr.Arguments != null && existingNoctorAttr.Arguments.ContainsKey("arg0"))
                {
                    var baseClassName = existingNoctorAttr.Arguments["arg0"];
                    Console.WriteLine($"[CppAstAdapter] Existing class {parsedClass.Name} has base class from attribute: {baseClassName}");
                    // Only add if not already in base classes
                    if (!existingClass.BaseTypes.Any(bt => bt.Type?.GetDisplayName() == baseClassName))
                    {
                        // Use the looked-up class if available
                        CppType baseType;
                        if (_classLookup.ContainsKey(baseClassName))
                        {
                            baseType = _classLookup[baseClassName];
                            Console.WriteLine($"[CppAstAdapter] Using looked-up class for {baseClassName}");
                        }
                        else
                        {
                            baseType = CreateTypeRef(baseClassName);
                            Console.WriteLine($"[CppAstAdapter] Creating new type ref for {baseClassName}");
                        }
                        existingClass.BaseTypes.Add(new CppBaseType(baseType));
                        Console.WriteLine($"[CppAstAdapter] Added base type {baseClassName} to existing {parsedClass.Name}, now has {existingClass.BaseTypes.Count} base types");
                    }
                }
                
                // If it's a stub (no members), populate it
                if (existingClass.Functions.Count == 0 && existingClass.Fields.Count == 0 && 
                    (parsedClass.Functions?.Count > 0 || parsedClass.Fields?.Count > 0))
                {
                    // Continue to populate the existing class
                    PopulateClassMembers(existingClass, parsedClass);
                }
                return existingClass;
            }
            
            var cls = new CppClass(parsedClass.Name)
            {
                ClassKind = CppClassKind.Class,
                SizeOf = 0 // We don't calculate size
            };
            
            // Add to lookup immediately to handle circular references
            _classLookup[parsedClass.Name] = cls;
            _classLookup[fullName] = cls;
            
            // Set source location
            if (!string.IsNullOrEmpty(parsedClass.FilePath))
            {
                cls.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedClass.FilePath, parsedClass.StartOffset, 0, 0),
                    new CppSourceLocation(parsedClass.FilePath, parsedClass.EndOffset, 0, 0)
                );
            }
            
            // Add attributes
            if (parsedClass.Attributes != null)
            {
                foreach (var attr in parsedClass.Attributes)
                {
                    cls.Attributes.Add(ConvertAttribute(attr));
                }
            }
            
            // Add base classes
            if (parsedClass.BaseClasses != null)
            {
                foreach (var baseClass in parsedClass.BaseClasses)
                {
                    cls.BaseTypes.Add(new CppBaseType(CreateTypeRef(baseClass)));
                }
            }
            
            // Check for base class in LUA_USERTYPE_NO_CTOR attribute
            var noctorAttr = parsedClass.Attributes?.FirstOrDefault(a => a.Name == "LUA_USERTYPE_NO_CTOR");
            if (noctorAttr != null && noctorAttr.Arguments != null && noctorAttr.Arguments.ContainsKey("arg0"))
            {
                var baseClassName = noctorAttr.Arguments["arg0"];
                Console.WriteLine($"[CppAstAdapter] Class {parsedClass.Name} has base class from attribute: {baseClassName}");
                // Only add if not already in base classes
                if (!cls.BaseTypes.Any(bt => bt.Type?.GetDisplayName() == baseClassName))
                {
                    // Use the looked-up class if available
                    CppType baseType;
                    if (_classLookup.ContainsKey(baseClassName))
                    {
                        baseType = _classLookup[baseClassName];
                        Console.WriteLine($"[CppAstAdapter] Using looked-up class for {baseClassName}");
                    }
                    else
                    {
                        baseType = CreateTypeRef(baseClassName);
                        Console.WriteLine($"[CppAstAdapter] Creating new type ref for {baseClassName}");
                    }
                    cls.BaseTypes.Add(new CppBaseType(baseType));
                    Console.WriteLine($"[CppAstAdapter] Added base type {baseClassName} to {parsedClass.Name}, now has {cls.BaseTypes.Count} base types");
                }
            }
            
            // Populate members
            PopulateClassMembers(cls, parsedClass);
            
            return cls;
        }

        private static void PopulateClassMembers(CppClass cls, ParsedClass parsedClass)
        {
            // Add methods
            if (parsedClass.Functions != null)
            {
                System.Console.WriteLine($"[DirectParser] Adding {parsedClass.Functions.Count} functions to class {parsedClass.Name}");
                foreach (var func in parsedClass.Functions)
                {
                    var isConstructor = func.Attributes.Any(x => x.Name == "LUA_CTOR");
                    var method = ConvertToMethod(func, cls);
                    
                    // Check if this is a true constructor or a static factory
                    // True constructors: have LUA_CTOR, are not static, and have the same name as the class
                    // Static factories: have LUA_CTOR but are static methods
                    if (isConstructor && !func.IsStatic && func.Name == parsedClass.Name)
                    {
                        cls.Constructors.Add(method);
                        System.Console.WriteLine($"  - Added constructor: {func.Name}");
                    }
                    else
                    {
                        cls.Functions.Add(method);
                        System.Console.WriteLine($"  - Added function: {func.Name} (static: {func.IsStatic}, ctor attr: {isConstructor})");
                    }
                }
            }
            
            // Add fields
            if (parsedClass.Fields != null)
            {
                System.Console.WriteLine($"[DirectParser] Adding {parsedClass.Fields.Count} fields to class {parsedClass.Name}");
                foreach (var field in parsedClass.Fields)
                {
                    cls.Fields.Add(ConvertField(field));
                    System.Console.WriteLine($"  - Added field: {field.Name} of type {field.Type}");
                }
            }
        }

        private static CppFunction ConvertFunction(ParsedFunction parsedFunc)
        {
            var func = new CppFunction(parsedFunc.Name)
            {
                ReturnType = CreateTypeRef(parsedFunc.ReturnType),
                StorageQualifier = parsedFunc.IsStatic ? CppStorageQualifier.Static : CppStorageQualifier.None
            };
            
            // Set source location
            if (!string.IsNullOrEmpty(parsedFunc.FilePath))
            {
                func.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedFunc.FilePath, parsedFunc.StartOffset, 0, 0),
                    new CppSourceLocation(parsedFunc.FilePath, parsedFunc.EndOffset, 0, 0)
                );
            }
            
            // Add attributes
            if (parsedFunc.Attributes != null)
            {
                foreach (var attr in parsedFunc.Attributes)
                {
                    func.Attributes.Add(ConvertAttribute(attr));
                }
            }
            
            // Add parameters
            if (parsedFunc.Parameters != null)
            {
                foreach (var param in parsedFunc.Parameters)
                {
                    func.Parameters.Add(ConvertParameter(param));
                }
            }
            
            // Handle template parameters
            if (parsedFunc.TemplateParameters != null && parsedFunc.TemplateParameters.Count > 0)
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
            
            // Set source location
            if (!string.IsNullOrEmpty(parsedEnum.FilePath))
            {
                enm.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedEnum.FilePath, parsedEnum.StartOffset, 0, 0),
                    new CppSourceLocation(parsedEnum.FilePath, parsedEnum.EndOffset, 0, 0)
                );
            }
            
            // Add attributes
            if (parsedEnum.Attributes != null)
            {
                foreach (var attr in parsedEnum.Attributes)
                {
                    enm.Attributes.Add(ConvertAttribute(attr));
                }
            }
            
            // Add values
            if (parsedEnum.Values != null)
            {
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
            }
            
            return enm;
        }

        private static CppClass CreateSpecializedTemplateClass(ParsedClass templateClass, ParsedTypedef typedef, string typedefName)
        {
            try
            {
                Console.WriteLine($"[CppAstAdapter] Creating specialized template class for {typedefName} from template {templateClass?.Name ?? "null"}");
                
                if (templateClass == null)
                {
                    Console.WriteLine($"[CppAstAdapter] Error: templateClass is null");
                    return null;
                }
                
                if (typedef == null)
                {
                    Console.WriteLine($"[CppAstAdapter] Error: typedef is null");
                    return null;
                }
                
                // Parse the typedef to extract template arguments
                // e.g., "Range<int32_t>" -> extract "int32_t"
                var typedefType = typedef.Type;
                var templateArgStart = typedefType.IndexOf('<');
                var templateArgEnd = typedefType.LastIndexOf('>');
                
                if (templateArgStart < 0 || templateArgEnd <= templateArgStart)
                {
                    Console.WriteLine($"[CppAstAdapter] Warning: Could not parse template arguments from typedef {typedefName} = {typedefType}");
                    return null;
                }
                
                var templateArg = typedefType.Substring(templateArgStart + 1, templateArgEnd - templateArgStart - 1).Trim();
                
                // Normalize common type aliases
                if (templateArg == "int32_t") templateArg = "int";
                else if (templateArg == "uint32_t") templateArg = "unsigned int";
                else if (templateArg == "int64_t") templateArg = "long long";
                else if (templateArg == "uint64_t") templateArg = "unsigned long long";
                
                Console.WriteLine($"[CppAstAdapter] Extracted template argument: {templateArg}");
            
            // Create a specialized class with the typedef name
            var specializedClass = new CppClass(typedefName)
            {
                ClassKind = CppClassKind.Class,
                SizeOf = 0
            };
            
            // Copy source location from template
            specializedClass.Span = templateClass.FilePath != null ? new CppSourceSpan(
                new CppSourceLocation(templateClass.FilePath, templateClass.StartOffset, 0, 0),
                new CppSourceLocation(templateClass.FilePath, templateClass.EndOffset, 0, 0)
            ) : new CppSourceSpan();
            
            // Copy attributes (but not LUA_USERTYPE_TEMPLATE)
            if (templateClass.Attributes != null)
            {
                foreach (var attr in templateClass.Attributes)
                {
                    if (attr.Name != "LUA_USERTYPE_TEMPLATE")
                    {
                        specializedClass.Attributes.Add(ConvertAttribute(attr));
                    }
                }
            }

        // Add LUA_USERTYPE attribute to make it a proper usertype
        var userTypeAttr = new CppAttribute("LUA_USERTYPE");
        userTypeAttr.Span = specializedClass.Span;
            specializedClass.Attributes.Add(userTypeAttr);
            
            // Copy and specialize constructors
            if (templateClass.Functions != null)
            {
                foreach (var ctor in templateClass.Functions.Where(f => f.Attributes != null && f.Attributes.Any(a => a.Name == "LUA_CTOR")))
            {
                var specializedCtor = new CppFunction(typedefName)
                {
                    Flags = CppFunctionFlags.None
                };
                
                if (ctor.IsStatic) specializedCtor.StorageQualifier = CppStorageQualifier.Static;
                
                // Copy attributes
                foreach (var attr in ctor.Attributes)
                {
                    specializedCtor.Attributes.Add(ConvertAttribute(attr));
                }
                
                // Convert parameters with template substitution
                foreach (var param in ctor.Parameters)
                {
                    var paramType = SubstituteTemplateParameter(param.Type, "T", templateArg);
                    specializedCtor.Parameters.Add(new CppParameter(CreateTypeRef(paramType), param.Name ?? ""));
                }
                
                specializedClass.Constructors.Add(specializedCtor);
                }
            }
            
            // Copy and specialize other functions
            if (templateClass.Functions != null)
            {
                foreach (var func in templateClass.Functions.Where(f => f.Attributes != null && f.Attributes.Any(a => a.Name == "LUA_FUNC")))
            {
                var specializedFunc = new CppFunction(func.Name)
                {
                    ReturnType = CreateTypeRef(SubstituteTemplateParameter(func.ReturnType, "T", templateArg)),
                    Flags = CppFunctionFlags.None
                };
                
                if (func.IsConst) specializedFunc.Flags |= CppFunctionFlags.Const;
                if (func.IsVirtual) specializedFunc.Flags |= CppFunctionFlags.Virtual;
                if (func.IsStatic) specializedFunc.StorageQualifier = CppStorageQualifier.Static;
                
                // Copy attributes
                foreach (var attr in func.Attributes)
                {
                    specializedFunc.Attributes.Add(ConvertAttribute(attr));
                }
                
                // Convert parameters with template substitution
                foreach (var param in func.Parameters)
                {
                    var paramType = SubstituteTemplateParameter(param.Type, "T", templateArg);
                    specializedFunc.Parameters.Add(new CppParameter(CreateTypeRef(paramType), param.Name ?? ""));
                }
                
                specializedClass.Functions.Add(specializedFunc);
                }
            }
            
            // Copy and specialize fields
            if (templateClass.Fields != null)
            {
                foreach (var field in templateClass.Fields.Where(f => f.Attributes != null && f.Attributes.Any(a => a.Name == "LUA_VAR" || a.Name == "LUA_VAR_READONLY")))
            {
                var fieldType = SubstituteTemplateParameter(field.Type, "T", templateArg);
                var specializedField = new CppField(CreateTypeRef(fieldType), field.Name)
                {
                    StorageQualifier = field.IsStatic ? CppStorageQualifier.Static : CppStorageQualifier.None
                };

                if (field.Attributes != null)
                {
                  specializedField.Attributes = new List<CppAttribute>(field.Attributes.Count);
            }
                
                // Copy attributes
                foreach (var attr in field.Attributes)
                {
                    specializedField.Attributes.Add(ConvertAttribute(attr));
                }
                
                specializedClass.Fields.Add(specializedField);
                }
            }
            
            var funcCount = specializedClass.Functions?.Count ?? 0;
            var fieldCount = specializedClass.Fields?.Count ?? 0;
            Console.WriteLine($"[CppAstAdapter] Created specialized class {typedefName} with {funcCount} functions and {fieldCount} fields");
            
            return specializedClass;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CppAstAdapter] Exception in CreateSpecializedTemplateClass: {ex.Message}");
                Console.WriteLine($"[CppAstAdapter] Stack trace: {ex.StackTrace}");
                return null;
            }
        }
        
        private static string SubstituteTemplateParameter(string type, string templateParam, string replacement)
        {
            if (string.IsNullOrEmpty(type))
                return type;
                
            // Handle exact match (just "T")
            if (type == templateParam)
                return replacement;
                
            // Handle "const T&", "T&", "const T", etc.
            var pattern = $@"\b{templateParam}\b";
            return System.Text.RegularExpressions.Regex.Replace(type, pattern, replacement);
        }

        private static CppTypedef ConvertTypedef(ParsedTypedef parsedTypedef)
        {
            var typedef = new CppTypedef(parsedTypedef.Name, CreateTypeRef(parsedTypedef.Type));
            
            // Set source location
            if (!string.IsNullOrEmpty(parsedTypedef.FilePath))
            {
                typedef.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedTypedef.FilePath, parsedTypedef.StartOffset, 0, 0),
                    new CppSourceLocation(parsedTypedef.FilePath, parsedTypedef.EndOffset, 0, 0)
                );
            }
            
            return typedef;
        }

        private static CppField ConvertField(ParsedField parsedField)
        {
            var field = new CppField(CreateTypeRef(parsedField.Type), parsedField.Name)
            {
                StorageQualifier = parsedField.IsStatic ? CppStorageQualifier.Static : CppStorageQualifier.None,
                Attributes = new List<CppAttribute>()
            };
            
            // Set source location
            if (!string.IsNullOrEmpty(parsedField.FilePath))
            {
                field.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedField.FilePath, parsedField.StartOffset, 0, 0),
                    new CppSourceLocation(parsedField.FilePath, parsedField.EndOffset, 0, 0)
                );
            }
            
            // Add attributes
            if (parsedField.Attributes != null)
            {
                foreach (var attr in parsedField.Attributes)
                {
                    field.Attributes.Add(ConvertAttribute(attr));
                }
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
            
            // Set source location if available
            if (!string.IsNullOrEmpty(parsedAttr.FilePath))
            {
                attr.Span = new CppSourceSpan(
                    new CppSourceLocation(parsedAttr.FilePath, parsedAttr.StartOffset, 0, 0),
                    new CppSourceLocation(parsedAttr.FilePath, parsedAttr.EndOffset, 0, 0)
                );
                
                // Debug output for critical attributes
                if (parsedAttr.Name == "LUA_USERTYPE_NAMESPACE" || parsedAttr.Name.Contains("LUA_USERTYPE"))
                {
                    System.Console.WriteLine($"[DirectParser] Set attribute '{parsedAttr.Name}' span to file: {parsedAttr.FilePath}");
                }
            }
            else
            {
                if (parsedAttr.Name == "LUA_USERTYPE_NAMESPACE" || parsedAttr.Name.Contains("LUA_USERTYPE"))
                {
                    System.Console.WriteLine($"[DirectParser] WARNING: Attribute '{parsedAttr.Name}' has no file path!");
                }
            }
            
            // Convert arguments
            if (parsedAttr.Arguments != null && parsedAttr.Arguments.Count > 0)
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

        private static int _typeRecursionDepth = 0;
        private const int MAX_TYPE_RECURSION_DEPTH = 50;
        
        private static CppType CreateTypeRef(string typeString)
        {
            // Guard against infinite recursion
            _typeRecursionDepth++;
            try
            {
                if (_typeRecursionDepth > MAX_TYPE_RECURSION_DEPTH)
                {
                    Console.WriteLine($"[CppAstAdapter] WARNING: Maximum type recursion depth reached for type '{typeString}', returning as unexposed type");
                    return new CppUnexposedType(typeString);
                }
                
                var parser = new TypeParser();
                var parsed = parser.ParseType(typeString);
                
                // Create appropriate CppType based on parsed information
                CppType baseType;
            
            // Check for primitive types
            if (IsPrimitiveType(parsed.Name))
            {
                baseType = GetPrimitiveType(parsed.Name);
            }
            else
            {
                // For complex types, try to find the class in our lookup
                if (_classLookup.ContainsKey(parsed.Name))
                {
                    baseType = _classLookup[parsed.Name];
                }
                else
                {
                    // Handle common template types that need special treatment
                    var className = parsed.Name;
                    
                    // Undo the namespace normalization for std types
                    if (className.StartsWith("std."))
                    {
                        className = className.Replace("std.", "std::");
                    }
                    
                    // Normalize vector/array types
                    if (className == "std::vector")
                    {
                        className = "vector";
                    }
                    else if (className == "std::array")
                    {
                        className = "array";
                    }
                    
                    // For CppUnexposedType, we might need to handle template types differently
                    if (parsed.TemplateArguments.Count > 0 && 
                        (className == "vector" || className == "array" || className == "optional" || 
                         className == "shared_ptr" || className == "Vec2" || className == "Rectangle"))
                    {
                        // Create an unexposed type with template parameters
                        var unexposedType = new CppUnexposedType(className)
                        {
                           
                        };
                        
                        // Add template parameters
                        foreach (var templateArg in parsed.TemplateArguments)
                        {
                            var templateType = CreateTypeRef(templateArg);
                            unexposedType.TemplateParameters.Add(templateType);
                        }
                        
                        baseType = unexposedType;
                    }
                    else
                    {
                        // Fallback: create a class reference
                        var classType = new CppClass(className);
                        
                        // If it has template arguments, add them as template parameters
                        if (parsed.TemplateArguments.Count > 0)
                        {
                            foreach (var templateArg in parsed.TemplateArguments)
                            {
                                // Recursively parse template arguments
                                var templateType = CreateTypeRef(templateArg);
                                classType.TemplateParameters.Add(templateType);
                            }
                        }
                        
                        baseType = classType;
                    }
                }
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
            finally
            {
                _typeRecursionDepth--;
            }
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