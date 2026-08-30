using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Maui.Tizen.TestUtils;

/// <summary>
/// Inspects the pinned Samsung Tizen reference pack without needing the Samsung workload.
/// </summary>
/// <remarks>
/// <para>
/// The reference pack is an ordinary NuGet package, so its contents can be read on any runner even
/// though nothing can be <em>built</em> against it. That is what lets the API15 rules be verified
/// facts rather than assertions copied out of a migration note: the ban on <c>Tizen.Maps</c> is
/// justified by that assembly being absent from the pack, and the ban on <c>Window.Instance</c> by
/// the member actually carrying <c>[Obsolete]</c>.
/// </para>
/// <para>
/// Metadata is read with the in-box <see cref="MetadataReader"/> rather than
/// <c>MetadataLoadContext</c>, which keeps the validation lane free of another package dependency
/// for what is ultimately a handful of metadata lookups.
/// </para>
/// </remarks>
public static class ReferencePackProbe
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>Downloads or locates the reference pack and returns the extraction directory.</summary>
    /// <returns><see langword="null"/> when the pack is not obtainable on this runner.</returns>
    public static async Task<string?> TryAcquireAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(packageId);
        ArgumentException.ThrowIfNullOrEmpty(version);

        var extracted = Path.Combine(
            Path.GetTempPath(),
            $"maui-tizen-refpack-{packageId.ToLowerInvariant()}-{version}");

        if (Directory.Exists(extracted) && Directory.EnumerateFiles(extracted, "*.dll", SearchOption.AllDirectories).Any())
            return extracted;

        var nupkg = await TryLocateNupkgAsync(packageId, version, cancellationToken).ConfigureAwait(false);
        if (nupkg is null)
            return null;

        try
        {
            Directory.CreateDirectory(extracted);
            ZipFile.ExtractToDirectory(nupkg, extracted, overwriteFiles: true);
            return extracted;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    static async Task<string?> TryLocateNupkgAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        var lowerId = packageId.ToLowerInvariant();
        var lowerVersion = version.ToLowerInvariant();

        // Cache first: a restore may already have produced it, and CI runners are often offline
        // for anything not already needed by a restore.
        var cached = Path.Combine(
            PackageDependencyProbe.GlobalPackagesFolder,
            lowerId,
            lowerVersion,
            $"{lowerId}.{lowerVersion}.nupkg");

        if (File.Exists(cached))
            return cached;

        var destination = Path.Combine(Path.GetTempPath(), $"{lowerId}.{lowerVersion}.nupkg");
        if (File.Exists(destination) && new FileInfo(destination).Length > 0)
            return destination;

        var url = $"https://api.nuget.org/v3-flatcontainer/{lowerId}/{lowerVersion}/{lowerId}.{lowerVersion}.nupkg";

        try
        {
            using var response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = File.Create(destination))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>Reference assembly file names in the pack.</summary>
    public static IReadOnlyList<string> EnumerateAssemblies(string extractedPackDirectory) =>
        [.. Directory
            .EnumerateFiles(extractedPackDirectory, "*.dll", SearchOption.AllDirectories)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Locates a reference assembly by file name.</summary>
    public static string? FindAssembly(string extractedPackDirectory, string fileName) =>
        Directory
            .EnumerateFiles(extractedPackDirectory, fileName, SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .FirstOrDefault();

    /// <summary>
    /// Reads the public property names of a type, and which of them are <c>[Obsolete]</c>.
    /// </summary>
    /// <returns><see langword="null"/> when the type is not present in the assembly.</returns>
    public static TypeMembers? ReadTypeMembers(string assemblyPath, string fullTypeName)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var peReader = new PEReader(stream);

        if (!peReader.HasMetadata)
            return null;

        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var ns = type.Namespace.IsNil ? string.Empty : reader.GetString(type.Namespace);
            var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

            if (!string.Equals(fullName, fullTypeName, StringComparison.Ordinal))
                continue;

            var properties = new List<string>();
            var methods = type.GetMethods()
                .Select(handle => reader.GetString(reader.GetMethodDefinition(handle).Name))
                .ToList();
            var obsolete = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var propertyName = reader.GetString(property.Name);
                properties.Add(propertyName);

                if (TryReadObsoleteMessage(reader, property.GetCustomAttributes(), out var message))
                    obsolete[propertyName] = message;
            }

            return new TypeMembers(fullName, properties, methods, obsolete);
        }

        return null;
    }

    static bool TryReadObsoleteMessage(MetadataReader reader, CustomAttributeHandleCollection attributes, out string message)
    {
        message = string.Empty;

        foreach (var handle in attributes)
        {
            var attribute = reader.GetCustomAttribute(handle);

            if (!IsObsolete(reader, attribute))
                continue;

            try
            {
                var value = attribute.DecodeValue(new StringOnlyAttributeTypeProvider());
                message = value.FixedArguments.Length > 0 && value.FixedArguments[0].Value is string s
                    ? s
                    : string.Empty;
            }
            catch (BadImageFormatException)
            {
                // The attribute is present; only its message could not be decoded.
            }

            return true;
        }

        return false;
    }

    static bool IsObsolete(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return false;

        var memberReference = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);

        if (memberReference.Parent.Kind != HandleKind.TypeReference)
            return false;

        var typeReference = reader.GetTypeReference((TypeReferenceHandle)memberReference.Parent);
        return reader.GetString(typeReference.Name) == "ObsoleteAttribute";
    }

    /// <summary>
    /// Minimal provider: only the <see cref="string"/> message argument of
    /// <see cref="ObsoleteAttribute"/> is read, so nothing else needs to resolve.
    /// </summary>
    sealed class StringOnlyAttributeTypeProvider : ICustomAttributeTypeProvider<object?>
    {
        public object? GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode;

        public object? GetSystemType() => null;

        public object? GetSZArrayType(object? elementType) => null;

        public object? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;

        public object? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => null;

        public object? GetTypeFromSerializedName(string name) => null;

        public PrimitiveTypeCode GetUnderlyingEnumType(object? type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(object? type) => false;
    }
}

/// <param name="ObsoleteMembers">Property name to the message on its <c>[Obsolete]</c> attribute.</param>
public sealed record TypeMembers(
    string FullName,
    IReadOnlyList<string> Properties,
    IReadOnlyList<string> Methods,
    IReadOnlyDictionary<string, string> ObsoleteMembers)
{
    public bool HasProperty(string name) => Properties.Contains(name, StringComparer.Ordinal);

    public bool HasMethod(string name) => Methods.Contains(name, StringComparer.Ordinal);

    public bool IsObsolete(string name) => ObsoleteMembers.ContainsKey(name);
}
