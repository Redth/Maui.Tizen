// Metadata-only .NET public API surface dumper.
//
// This tool reads managed assemblies (including net9.0-tizen7.0 / net11.0 outputs) purely via
// System.Reflection.Metadata (ECMA-335 metadata reader). It never loads, JITs, or executes the
// analyzed assembly, and never touches TizenFX or any target-platform runtime, so it can run on
// any machine with the .NET SDK -- no Tizen workload, emulator, or device is required.
//
// Usage:
//   maui-tizen-apidump <assembly-path> [<assembly-path> ...] --out <output-directory>
//
// For each input assembly, writes <output-directory>/<AssemblyName>.json containing a
// deterministic (sorted) description of every publicly-visible type and member.
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Migration.ApiDump;

public static class Program
{
    public static int Main(string[] args)
    {
        var assemblyPaths = new List<string>();
        string? outDir = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--out" or "-o")
            {
                if (++i >= args.Length)
                {
                    Console.Error.WriteLine("Missing value for --out");
                    return 1;
                }
                outDir = args[i];
            }
            else
            {
                assemblyPaths.Add(args[i]);
            }
        }

        if (assemblyPaths.Count == 0 || outDir is null)
        {
            Console.Error.WriteLine("Usage: maui-tizen-apidump <assembly-path> [...] --out <output-directory>");
            return 1;
        }

        Directory.CreateDirectory(outDir);

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        var failures = 0;
        foreach (var path in assemblyPaths.OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                var dump = AssemblyApiDumper.Dump(path);
                var outPath = Path.Combine(outDir, dump.AssemblyName + ".json");
                File.WriteAllText(outPath, JsonSerializer.Serialize(dump, jsonOptions));
                Console.WriteLine($"Wrote {outPath} ({dump.Types.Count} public types)");
            }
            catch (Exception ex)
            {
                failures++;
                Console.Error.WriteLine($"Failed to dump '{path}': {ex.Message}");
            }
        }

        return failures == 0 ? 0 : 2;
    }
}

/// <summary>Deterministic public API surface for a single assembly.</summary>
public sealed class AssemblyApiDump
{
    public required int SchemaVersion { get; init; }
    public required string AssemblyName { get; init; }
    public required string AssemblyVersion { get; init; }
    public required string TargetFramework { get; init; }
    public required string SourcePath { get; init; }
    public required string Sha256 { get; init; }
    public required List<ApiType> Types { get; init; }
}

public sealed class ApiType
{
    public required string Namespace { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; } // class | struct | interface | enum | delegate
    public required string Accessibility { get; init; } // public | protected | protected internal
    public bool IsStatic { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsSealed { get; init; }
    public int Arity { get; init; }
    public string? BaseType { get; init; }
    public List<string> Interfaces { get; init; } = [];
    public string? UnderlyingType { get; init; } // enum only: its backing primitive type.
    public string? DelegateSignature { get; init; } // delegate only: its Invoke method's signature.
    public List<string>? DelegateParameters { get; init; }
    public List<string>? GenericConstraints { get; init; }
    public List<ApiMember> Members { get; init; } = [];
}

public sealed class ApiMember
{
    public required string Kind { get; init; } // method | constructor | property | field | event
    public required string Signature { get; init; }
    public required string Accessibility { get; init; }
    public bool IsStatic { get; init; }
    public bool IsAbstract { get; init; }
    public bool IsVirtual { get; init; }
    public bool IsFinal { get; init; }
    public bool IsExtensionMethod { get; init; }
    public bool IsLiteral { get; init; }
    public bool IsInitOnly { get; init; }
    public string? ConstantValue { get; init; }
    public List<string>? GenericConstraints { get; init; }
    public List<string>? Parameters { get; init; }
    public string? GetterAccessibility { get; init; }
    public string? SetterAccessibility { get; init; }
}

internal static class AssemblyApiDumper
{
    public static AssemblyApiDump Dump(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        using var stream = new MemoryStream(bytes, writable: false);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        var assemblyDef = reader.GetAssemblyDefinition();
        var assemblyName = reader.GetString(assemblyDef.Name);
        var assemblyVersion = assemblyDef.Version.ToString();
        var targetFramework = GetTargetFramework(reader) ?? "unknown";

        var provider = new SignatureStringProvider(reader);
        var types = new List<ApiType>();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!IsPubliclyVisible(reader, typeDef))
            {
                continue;
            }

            if (IsCompilerGenerated(reader, typeDef.GetCustomAttributes()))
            {
                continue;
            }

            types.Add(BuildType(reader, typeDef, provider));
        }

        types.Sort((a, b) =>
        {
            var cmp = string.CompareOrdinal(FullName(a), FullName(b));
            return cmp != 0 ? cmp : a.Arity.CompareTo(b.Arity);
        });

        return new AssemblyApiDump
        {
            SchemaVersion = 2,
            AssemblyName = assemblyName,
            AssemblyVersion = assemblyVersion,
            TargetFramework = targetFramework,
            SourcePath = Path.GetFileName(path),
            Sha256 = sha256,
            Types = types,
        };
    }

    private static string FullName(ApiType t) => t.Namespace.Length == 0 ? t.Name : $"{t.Namespace}.{t.Name}";

    private static string? GetTargetFramework(MetadataReader reader)
    {
        foreach (var attrHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attr = reader.GetCustomAttribute(attrHandle);
            if (!TryGetAttributeTypeName(reader, attr, out var ns, out var name))
            {
                continue;
            }

            if (ns == "System.Runtime.Versioning" && name == "TargetFrameworkAttribute")
            {
                var value = attr.DecodeValue(new StringOnlyCustomAttributeTypeProvider());
                if (value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is string s)
                {
                    return s;
                }
            }
        }

        return null;
    }

    private static bool TryGetAttributeTypeName(MetadataReader reader, CustomAttribute attr, out string ns, out string name)
    {
        ns = "";
        name = "";
        var ctorHandle = attr.Constructor;
        EntityHandle typeHandle;
        if (ctorHandle.Kind == HandleKind.MemberReference)
        {
            typeHandle = reader.GetMemberReference((MemberReferenceHandle)ctorHandle).Parent;
        }
        else if (ctorHandle.Kind == HandleKind.MethodDefinition)
        {
            typeHandle = reader.GetMethodDefinition((MethodDefinitionHandle)ctorHandle).GetDeclaringType();
        }
        else
        {
            return false;
        }

        if (typeHandle.Kind == HandleKind.TypeReference)
        {
            var typeRef = reader.GetTypeReference((TypeReferenceHandle)typeHandle);
            ns = reader.GetString(typeRef.Namespace);
            name = reader.GetString(typeRef.Name);
            return true;
        }

        if (typeHandle.Kind == HandleKind.TypeDefinition)
        {
            var typeDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle);
            ns = reader.GetString(typeDef.Namespace);
            name = reader.GetString(typeDef.Name);
            return true;
        }

        return false;
    }

    private static bool IsCompilerGenerated(MetadataReader reader, CustomAttributeHandleCollection attrs)
    {
        foreach (var handle in attrs)
        {
            var attr = reader.GetCustomAttribute(handle);
            if (TryGetAttributeTypeName(reader, attr, out var ns, out var name) &&
                ns == "System.Runtime.CompilerServices" && name == "CompilerGeneratedAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPubliclyVisible(MetadataReader reader, TypeDefinition typeDef)
    {
        var visibility = typeDef.Attributes & TypeAttributes.VisibilityMask;
        return visibility switch
        {
            TypeAttributes.Public => true,
            TypeAttributes.NestedPublic => IsDeclaringTypePubliclyVisible(reader, typeDef),
            TypeAttributes.NestedFamily => IsDeclaringTypePubliclyVisible(reader, typeDef),
            TypeAttributes.NestedFamORAssem => IsDeclaringTypePubliclyVisible(reader, typeDef),
            _ => false,
        };
    }

    private static bool IsDeclaringTypePubliclyVisible(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaring = typeDef.GetDeclaringType();
        if (declaring.IsNil)
        {
            return false;
        }

        return IsPubliclyVisible(reader, reader.GetTypeDefinition(declaring));
    }

    private static ApiType BuildType(MetadataReader reader, TypeDefinition typeDef, SignatureStringProvider provider)
    {
        var ns = provider.GetEffectiveNamespace(typeDef);
        var name = provider.GetQualifiedTypeName(typeDef);
        var isInterface = (typeDef.Attributes & TypeAttributes.Interface) != 0;
        var isValueType = !isInterface && IsValueType(reader, typeDef);
        var isEnum = isValueType && IsEnum(reader, typeDef);
        var isDelegate = !isInterface && !isValueType && IsDelegate(reader, typeDef);

        var kind = isInterface ? "interface" : isEnum ? "enum" : isDelegate ? "delegate" : isValueType ? "struct" : "class";
        var isAbstract = (typeDef.Attributes & TypeAttributes.Abstract) != 0;
        var isSealed = (typeDef.Attributes & TypeAttributes.Sealed) != 0;
        var isStatic = isAbstract && isSealed;

        var members = new List<ApiMember>();

        if (kind is "class" or "struct" or "interface")
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (!IsPubliclyVisibleMember(method.Attributes))
                {
                    continue;
                }

                if ((method.Attributes & MethodAttributes.SpecialName) != 0 &&
                    !method.Name.IsNil &&
                    (reader.GetString(method.Name).StartsWith("op_", StringComparison.Ordinal)))
                {
                    // Operator overloads: still public API, keep them.
                }
                else if ((method.Attributes & MethodAttributes.SpecialName) != 0)
                {
                    // Property/event accessors are emitted via the property/event below instead.
                    var methodName = reader.GetString(method.Name);
                    if (methodName.StartsWith("get_", StringComparison.Ordinal) ||
                        methodName.StartsWith("set_", StringComparison.Ordinal) ||
                        methodName.StartsWith("add_", StringComparison.Ordinal) ||
                        methodName.StartsWith("remove_", StringComparison.Ordinal) ||
                        methodName.StartsWith("raise_", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                members.Add(new ApiMember
                {
                    Kind = methodHandle == typeDef.GetMethods().FirstOrDefault() && reader.GetString(method.Name) == ".ctor" ? "constructor" : (reader.GetString(method.Name) is ".ctor" or ".cctor" ? "constructor" : "method"),
                    Signature = provider.GetMethodSignatureString(methodHandle, method),
                    Accessibility = Accessibility(method.Attributes),
                    IsStatic = (method.Attributes & MethodAttributes.Static) != 0,
                    IsAbstract = (method.Attributes & MethodAttributes.Abstract) != 0,
                    IsVirtual = (method.Attributes & MethodAttributes.Virtual) != 0,
                    IsFinal = (method.Attributes & MethodAttributes.Final) != 0,
                    IsExtensionMethod = HasAttribute(
                        reader,
                        method.GetCustomAttributes(),
                        "System.Runtime.CompilerServices",
                        "ExtensionAttribute"),
                    GenericConstraints = provider.GetGenericConstraints(method.GetGenericParameters()),
                    Parameters = GetParameterMetadata(reader, method),
                });
            }

            foreach (var propHandle in typeDef.GetProperties())
            {
                var prop = reader.GetPropertyDefinition(propHandle);
                var accessors = prop.GetAccessors();
                MethodDefinition? getter = null;
                MethodDefinition? setter = null;
                if (!accessors.Getter.IsNil)
                {
                    getter = reader.GetMethodDefinition(accessors.Getter);
                }
                if (!accessors.Setter.IsNil)
                {
                    setter = reader.GetMethodDefinition(accessors.Setter);
                }

                var visibleAccessors = new List<MethodDefinition>();
                if (getter is not null && IsPubliclyVisibleMember(getter.Value.Attributes))
                    visibleAccessors.Add(getter.Value);
                if (setter is not null && IsPubliclyVisibleMember(setter.Value.Attributes))
                    visibleAccessors.Add(setter.Value);
                if (visibleAccessors.Count == 0)
                {
                    continue;
                }
                var accessorForVisibility = visibleAccessors
                    .OrderByDescending(accessor => AccessibilityRank(accessor.Attributes))
                    .First();

                members.Add(new ApiMember
                {
                    Kind = "property",
                    Signature = provider.GetPropertySignatureString(propHandle, prop, accessors),
                    Accessibility = Accessibility(accessorForVisibility.Attributes),
                    IsStatic = (accessorForVisibility.Attributes & MethodAttributes.Static) != 0,
                    IsAbstract = (accessorForVisibility.Attributes & MethodAttributes.Abstract) != 0,
                    IsVirtual = (accessorForVisibility.Attributes & MethodAttributes.Virtual) != 0,
                    IsFinal = (accessorForVisibility.Attributes & MethodAttributes.Final) != 0,
                    GetterAccessibility = getter is null ? null : Accessibility(getter.Value.Attributes),
                    SetterAccessibility = setter is null ? null : Accessibility(setter.Value.Attributes),
                    Parameters = GetPropertyParameterMetadata(reader, getter, setter),
                });
            }

            foreach (var eventHandle in typeDef.GetEvents())
            {
                var evt = reader.GetEventDefinition(eventHandle);
                var accessors = evt.GetAccessors();
                if (accessors.Adder.IsNil)
                {
                    continue;
                }

                var adder = reader.GetMethodDefinition(accessors.Adder);
                if (!IsPubliclyVisibleMember(adder.Attributes))
                {
                    continue;
                }

                members.Add(new ApiMember
                {
                    Kind = "event",
                    Signature = provider.GetEventSignatureString(eventHandle, evt),
                    Accessibility = Accessibility(adder.Attributes),
                    IsStatic = (adder.Attributes & MethodAttributes.Static) != 0,
                    IsAbstract = (adder.Attributes & MethodAttributes.Abstract) != 0,
                    IsVirtual = (adder.Attributes & MethodAttributes.Virtual) != 0,
                    IsFinal = (adder.Attributes & MethodAttributes.Final) != 0,
                });
            }

            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if (!IsPubliclyVisibleMember(field.Attributes))
                {
                    continue;
                }

                if ((field.Attributes & FieldAttributes.SpecialName) != 0)
                {
                    continue; // backing fields / enum value__ etc.
                }

                members.Add(new ApiMember
                {
                    Kind = "field",
                    Signature = provider.GetFieldSignatureString(fieldHandle, field),
                    Accessibility = Accessibility(field.Attributes),
                    IsStatic = (field.Attributes & FieldAttributes.Static) != 0,
                    IsLiteral = (field.Attributes & FieldAttributes.Literal) != 0,
                    IsInitOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                    ConstantValue = GetEncodedConstant(reader, field.GetDefaultValue()),
                    IsAbstract = false,
                    IsVirtual = false,
                });
            }
        }
        else if (kind is "enum")
        {
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & FieldAttributes.SpecialName) != 0)
                {
                    continue; // value__
                }

                if (!IsPubliclyVisibleMember(field.Attributes))
                {
                    continue;
                }

                var fieldName = reader.GetString(field.Name);
                var constantValue = GetEnumConstantValueString(reader, field);

                members.Add(new ApiMember
                {
                    Kind = "field",
                    Signature = constantValue is null ? fieldName : $"{fieldName} = {constantValue}",
                    Accessibility = Accessibility(field.Attributes),
                    IsStatic = true,
                    IsLiteral = (field.Attributes & FieldAttributes.Literal) != 0,
                    IsInitOnly = (field.Attributes & FieldAttributes.InitOnly) != 0,
                    ConstantValue = GetEncodedConstant(reader, field.GetDefaultValue()),
                });
            }
        }

        members.Sort((a, b) => string.CompareOrdinal(a.Kind + ":" + a.Signature, b.Kind + ":" + b.Signature));

        string? baseTypeName = null;
        if (!typeDef.BaseType.IsNil && kind is "class" or "struct")
        {
            var baseName = provider.GetHandleTypeName(typeDef.BaseType);
            if (baseName is not (null or "System.Object" or "System.ValueType" or "System.Enum"))
            {
                baseTypeName = baseName;
            }
        }

        var interfaces = new List<string>();
        foreach (var implHandle in typeDef.GetInterfaceImplementations())
        {
            var impl = reader.GetInterfaceImplementation(implHandle);
            var ifaceName = provider.GetHandleTypeName(impl.Interface);
            if (ifaceName is not null)
            {
                interfaces.Add(ifaceName);
            }
        }
        interfaces.Sort(StringComparer.Ordinal);

        string? underlyingType = null;
        if (kind == "enum")
        {
            underlyingType = GetEnumUnderlyingType(reader, typeDef, provider);
        }

        string? delegateSignature = null;
        List<string>? delegateParameters = null;
        if (kind == "delegate")
        {
            delegateSignature = GetDelegateInvokeSignature(reader, typeDef, provider);
            delegateParameters = GetDelegateParameterMetadata(reader, typeDef);
        }

        return new ApiType
        {
            Namespace = ns,
            Name = name,
            Kind = kind,
            Accessibility = TypeAccessibility(typeDef.Attributes),
            IsStatic = isStatic,
            IsAbstract = isAbstract && !isStatic,
            IsSealed = isSealed && !isStatic,
            Arity = provider.GetGenericArity(typeDef),
            BaseType = baseTypeName,
            Interfaces = interfaces,
            UnderlyingType = underlyingType,
            DelegateSignature = delegateSignature,
            DelegateParameters = delegateParameters,
            GenericConstraints = provider.GetGenericConstraints(typeDef.GetGenericParameters()),
            Members = members,
        };
    }

    /// <summary>The primitive backing type of an enum, read from its "value__" special field.</summary>
    private static string? GetEnumUnderlyingType(MetadataReader reader, TypeDefinition typeDef, SignatureStringProvider provider)
    {
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.SpecialName) != 0 && reader.GetString(field.Name) == "value__")
            {
                return field.DecodeSignature(provider, null);
            }
        }
        return null;
    }

    /// <summary>Reads the numeric/constant value backing an enum member field, formatted as a
    /// plain literal (e.g. "2" or "-1"). Returns null if the field has no constant (unexpected for
    /// a public enum member, but handled defensively).</summary>
    private static string? GetEnumConstantValueString(MetadataReader reader, FieldDefinition field)
    {
        var constantHandle = field.GetDefaultValue();
        if (constantHandle.IsNil)
        {
            return null;
        }

        var constant = reader.GetConstant(constantHandle);
        var blobReader = reader.GetBlobReader(constant.Value);
        return constant.TypeCode switch
        {
            ConstantTypeCode.Byte => blobReader.ReadByte().ToString(),
            ConstantTypeCode.SByte => blobReader.ReadSByte().ToString(),
            ConstantTypeCode.Int16 => blobReader.ReadInt16().ToString(),
            ConstantTypeCode.UInt16 => blobReader.ReadUInt16().ToString(),
            ConstantTypeCode.Int32 => blobReader.ReadInt32().ToString(),
            ConstantTypeCode.UInt32 => blobReader.ReadUInt32().ToString(),
            ConstantTypeCode.Int64 => blobReader.ReadInt64().ToString(),
            ConstantTypeCode.UInt64 => blobReader.ReadUInt64().ToString(),
            _ => null,
        };
    }

    private static List<string>? GetParameterMetadata(MetadataReader reader, MethodDefinition method)
    {
        var parameters = method
            .GetParameters()
            .Select(handle => reader.GetParameter(handle))
            .Where(parameter => parameter.SequenceNumber > 0)
            .OrderBy(parameter => parameter.SequenceNumber)
            .Select(parameter =>
            {
                var defaultValue = parameter.GetDefaultValue();
                var encodedDefault = "";
                if (!defaultValue.IsNil)
                {
                    encodedDefault = GetEncodedConstant(reader, defaultValue) ?? "";
                }
                var attributes = parameter
                    .GetCustomAttributes()
                    .Select(handle => reader.GetCustomAttribute(handle))
                    .Select(attribute =>
                        TryGetAttributeTypeName(reader, attribute, out var ns, out var name)
                            ? (ns.Length == 0 ? name : $"{ns}.{name}")
                            : "?")
                    .OrderBy(value => value, StringComparer.Ordinal);
                return $"{parameter.SequenceNumber}:{reader.GetString(parameter.Name)}:" +
                    $"{parameter.Attributes}:{encodedDefault}:{string.Join("&", attributes)}";
            })
            .ToList();
        return parameters.Count == 0 ? null : parameters;
    }

    private static List<string>? GetPropertyParameterMetadata(
        MetadataReader reader,
        MethodDefinition? getter,
        MethodDefinition? setter)
    {
        if (getter is not null)
            return GetParameterMetadata(reader, getter.Value);
        if (setter is null)
            return null;
        var parameters = GetParameterMetadata(reader, setter.Value);
        return parameters is { Count: > 1 } ? parameters[..^1] : null;
    }

    private static List<string>? GetDelegateParameterMetadata(
        MetadataReader reader,
        TypeDefinition typeDef)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == "Invoke")
                return GetParameterMetadata(reader, method);
        }
        return null;
    }

    private static string? GetEncodedConstant(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
            return null;
        var constant = reader.GetConstant(handle);
        return $"{constant.TypeCode}:{Convert.ToHexString(reader.GetBlobBytes(constant.Value))}";
    }

    private static bool HasAttribute(
        MetadataReader reader,
        CustomAttributeHandleCollection handles,
        string expectedNamespace,
        string expectedName)
    {
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            if (TryGetAttributeTypeName(reader, attribute, out var ns, out var name) &&
                ns == expectedNamespace &&
                name == expectedName)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A delegate type's public API surface is entirely its Invoke method's signature (the
    /// compiler-generated BeginInvoke/EndInvoke/.ctor are not meaningful API surface on their
    /// own). Without this, every delegate type dumped identically as an empty member list,
    /// regardless of its actual parameter/return shape.
    /// </summary>
    private static string? GetDelegateInvokeSignature(MetadataReader reader, TypeDefinition typeDef, SignatureStringProvider provider)
    {
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) == "Invoke")
            {
                return provider.GetMethodSignatureString(methodHandle, method);
            }
        }
        return null;
    }

    private static bool IsValueType(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil)
        {
            return false;
        }

        var baseName = GetHandleFullName(reader, typeDef.BaseType);
        return baseName is "System.ValueType" or "System.Enum";
    }

    private static bool IsEnum(MetadataReader reader, TypeDefinition typeDef)
        => !typeDef.BaseType.IsNil && GetHandleFullName(reader, typeDef.BaseType) == "System.Enum";

    private static bool IsDelegate(MetadataReader reader, TypeDefinition typeDef)
        => !typeDef.BaseType.IsNil && GetHandleFullName(reader, typeDef.BaseType) is "System.MulticastDelegate" or "System.Delegate";

    private static string? GetHandleFullName(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
                var tr = reader.GetTypeReference((TypeReferenceHandle)handle);
                var ns = reader.GetString(tr.Namespace);
                var n = reader.GetString(tr.Name);
                return ns.Length == 0 ? n : $"{ns}.{n}";
            case HandleKind.TypeDefinition:
                var td = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                var ns2 = reader.GetString(td.Namespace);
                var n2 = reader.GetString(td.Name);
                return ns2.Length == 0 ? n2 : $"{ns2}.{n2}";
            default:
                return null;
        }
    }

    private static bool IsPubliclyVisibleMember(MethodAttributes attrs)
    {
        var access = attrs & MethodAttributes.MemberAccessMask;
        return access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }

    private static bool IsPubliclyVisibleMember(FieldAttributes attrs)
    {
        var access = attrs & FieldAttributes.FieldAccessMask;
        return access is FieldAttributes.Public or FieldAttributes.Family or FieldAttributes.FamORAssem;
    }

    private static string Accessibility(MethodAttributes attrs) => (attrs & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => "public",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Private => "private",
        _ => "private scope",
    };

    private static int AccessibilityRank(MethodAttributes attrs) => (attrs & MethodAttributes.MemberAccessMask) switch
    {
        MethodAttributes.Public => 5,
        MethodAttributes.FamORAssem => 4,
        MethodAttributes.Family => 3,
        MethodAttributes.FamANDAssem => 2,
        MethodAttributes.Assembly => 1,
        _ => 0,
    };

    private static string Accessibility(FieldAttributes attrs) => (attrs & FieldAttributes.FieldAccessMask) switch
    {
        FieldAttributes.Public => "public",
        FieldAttributes.Family => "protected",
        FieldAttributes.FamORAssem => "protected internal",
        _ => "unknown",
    };

    private static string TypeAccessibility(TypeAttributes attrs) => (attrs & TypeAttributes.VisibilityMask) switch
    {
        TypeAttributes.Public or TypeAttributes.NestedPublic => "public",
        TypeAttributes.NestedFamily => "protected",
        TypeAttributes.NestedFamORAssem => "protected internal",
        _ => "unknown",
    };
}

/// <summary>Minimal attribute-value provider that only needs to decode string fixed arguments.</summary>
internal sealed class StringOnlyCustomAttributeTypeProvider : ICustomAttributeTypeProvider<string>
{
    public string GetSystemType() => "System.Type";
    public string GetTypeFromSerializedName(string name) => name;
    public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;
    public bool IsSystemType(string type) => type == "System.Type";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var td = reader.GetTypeDefinition(handle);
        var ns = reader.GetString(td.Namespace);
        var n = reader.GetString(td.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var tr = reader.GetTypeReference(handle);
        var ns = reader.GetString(tr.Namespace);
        var n = reader.GetString(tr.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }
}

/// <summary>
/// Renders ECMA-335 signatures (methods, properties, fields, events, base types, interfaces) as
/// readable, deterministic C#-like strings using only System.Reflection.Metadata -- no
/// System.Reflection loading of the target assembly is ever performed.
/// </summary>
internal sealed class SignatureStringProvider : ISignatureTypeProvider<string, object?>
{
    private readonly MetadataReader _reader;

    public SignatureStringProvider(MetadataReader reader) => _reader = reader;

    public string GetTypeDisplayName(TypeDefinition typeDef)
    {
        var name = _reader.GetString(typeDef.Name);
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>
    /// Full display name for a (possibly nested) type: the chain of enclosing type names joined
    /// with '+' (the CLR's own nested-type separator, distinct from the '.' used for namespaces
    /// and from member access), e.g. "Button+VisualStateManagerOverride". Without this, two
    /// unrelated nested types that happen to share a simple name (common for enums/delegates
    /// nested inside different handler classes) would be indistinguishable in the dump.
    /// </summary>
    public string GetQualifiedTypeName(TypeDefinition typeDef)
    {
        var names = new List<string>();
        var current = typeDef;
        while (true)
        {
            names.Add(GetTypeDisplayName(current));
            var declaring = current.GetDeclaringType();
            if (declaring.IsNil)
            {
                break;
            }
            current = _reader.GetTypeDefinition(declaring);
        }
        names.Reverse();
        return string.Join("+", names);
    }

    /// <summary>
    /// Namespace of a (possibly nested) type. Nested TypeDefs always report an empty namespace of
    /// their own -- only the outermost enclosing type carries it -- so this walks up the
    /// declaring-type chain rather than reading typeDef.Namespace directly.
    /// </summary>
    public string GetEffectiveNamespace(TypeDefinition typeDef)
    {
        var current = typeDef;
        while (true)
        {
            var declaring = current.GetDeclaringType();
            if (declaring.IsNil)
            {
                return _reader.GetString(current.Namespace);
            }
            current = _reader.GetTypeDefinition(declaring);
        }
    }

    public int GetGenericArity(TypeDefinition typeDef)
    {
        var name = _reader.GetString(typeDef.Name);
        var tick = name.IndexOf('`');
        return tick < 0 ? 0 : int.Parse(name[(tick + 1)..]);
    }

    public List<string>? GetGenericConstraints(GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return null;

        var constraints = new List<string>();
        foreach (var handle in handles)
        {
            var parameter = _reader.GetGenericParameter(handle);
            var typeConstraints = parameter
                .GetConstraints()
                .Select(constraintHandle =>
                {
                    var constraint = _reader.GetGenericParameterConstraint(constraintHandle);
                    return GetHandleTypeName(constraint.Type) ?? "?";
                })
                .OrderBy(value => value, StringComparer.Ordinal);
            constraints.Add(
                $"T{parameter.Index}:{parameter.Attributes}:{string.Join("&", typeConstraints)}");
        }
        return constraints;
    }

    public string? GetHandleTypeName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => FormatTypeReference((TypeReferenceHandle)handle),
        HandleKind.TypeDefinition => FormatTypeDefinition((TypeDefinitionHandle)handle),
        HandleKind.TypeSpecification => _reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        _ => null,
    };

    private string FormatTypeReference(TypeReferenceHandle handle)
    {
        var tr = _reader.GetTypeReference(handle);
        var name = CleanGenericName(_reader.GetString(tr.Name));
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{FormatTypeReference((TypeReferenceHandle)tr.ResolutionScope)}+{name}";
        var ns = _reader.GetString(tr.Namespace);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private string FormatTypeDefinition(TypeDefinitionHandle handle)
    {
        var td = _reader.GetTypeDefinition(handle);
        var ns = GetEffectiveNamespace(td);
        var name = GetQualifiedTypeName(td);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    private static string CleanGenericName(string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    public string GetMethodSignatureString(MethodDefinitionHandle handle, MethodDefinition method)
    {
        var name = _reader.GetString(method.Name);
        var decoded = method.DecodeSignature(this, null);
        var generic = decoded.GenericParameterCount > 0 ? $"<{string.Join(", ", Enumerable.Range(0, decoded.GenericParameterCount).Select(i => "T" + i))}>" : "";
        var parameters = string.Join(", ", decoded.ParameterTypes);
        var displayName = name is ".ctor" or ".cctor" ? "#ctor" : name;
        return $"{displayName}{generic}({parameters}) -> {decoded.ReturnType}";
    }

    public string GetPropertySignatureString(PropertyDefinitionHandle handle, PropertyDefinition prop, PropertyAccessors accessors)
    {
        var name = _reader.GetString(prop.Name);
        var decoded = prop.DecodeSignature(this, null);
        var parameters = decoded.ParameterTypes.Length > 0 ? $"[{string.Join(", ", decoded.ParameterTypes)}]" : "";
        var accessorKinds = new List<string>();
        if (!accessors.Getter.IsNil) accessorKinds.Add("get");
        if (!accessors.Setter.IsNil) accessorKinds.Add("set");
        return $"{name}{parameters} {{ {string.Join("; ", accessorKinds)}; }} : {decoded.ReturnType}";
    }

    public string GetEventSignatureString(EventDefinitionHandle handle, EventDefinition evt)
    {
        var name = _reader.GetString(evt.Name);
        var typeName = GetHandleTypeName(evt.Type) ?? "?";
        return $"{name} : {typeName}";
    }

    public string GetFieldSignatureString(FieldDefinitionHandle handle, FieldDefinition field)
    {
        var name = _reader.GetString(field.Name);
        var typeName = field.DecodeSignature(this, null);
        return $"{name} : {typeName}";
    }

    // ISignatureTypeProvider<string, object?> implementation -----------------------------------

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
    {
        PrimitiveTypeCode.Boolean => "bool",
        PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.Char => "char",
        PrimitiveTypeCode.Double => "double",
        PrimitiveTypeCode.Int16 => "short",
        PrimitiveTypeCode.Int32 => "int",
        PrimitiveTypeCode.Int64 => "long",
        PrimitiveTypeCode.IntPtr => "nint",
        PrimitiveTypeCode.Object => "object",
        PrimitiveTypeCode.SByte => "sbyte",
        PrimitiveTypeCode.Single => "float",
        PrimitiveTypeCode.String => "string",
        PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Void => "void",
        PrimitiveTypeCode.TypedReference => "TypedReference",
        _ => typeCode.ToString(),
    };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => FormatTypeDefinition(handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => FormatTypeReference(handle);

    public string GetSZArrayType(string elementType) => elementType + "[]";

    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', Math.Max(shape.Rank - 1, 0)) + "]";

    public string GetByReferenceType(string elementType) => elementType + "&";

    public string GetPointerType(string elementType) => elementType + "*";

    public string GetPinnedType(string elementType) => elementType;

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";

    public string GetGenericMethodParameter(object? genericContext, int index) => "T" + index;

    public string GetGenericTypeParameter(object? genericContext, int index) => "T" + index;

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => $"{unmodifiedType} {(isRequired ? "modreq" : "modopt")}({modifier})";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*<" + string.Join(", ", signature.ParameterTypes.Append(signature.ReturnType)) + ">";

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}
