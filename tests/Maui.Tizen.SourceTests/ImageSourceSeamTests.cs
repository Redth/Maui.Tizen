using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Proves, from compiled IL, that the image-source composition seam constructs Tizen-owned
/// implementations for all four image source types.
/// </summary>
/// <remarks>
/// <para>
/// This cannot be asserted by resolving services on the host. Wave A guards its registrations
/// behind <c>#if TIZEN</c>, so on a host TFM <c>AddTizenImageSources</c> registers nothing at all
/// and the file and stream sources resolve to MAUI's neutral services. The ref-pack lane is the
/// only workload-free place where the <c>TIZEN</c> branches actually exist, so the proof is taken
/// from its emitted IL rather than from a container.
/// </para>
/// <para>
/// Reading IL — rather than asserting the registration methods merely exist — is what makes this
/// meaningful. The failure being guarded against is silent: MAUI's neutral package already
/// registers a service for every image source type, so dropping a Tizen registration produces no
/// error, no log and no missing-service exception. It produces a blank image.
/// </para>
/// <para>
/// The assertion is deliberately written against the <em>union</em> of the registration methods
/// rather than against one named method, so it holds both now and after Wave B's URI and font
/// registrations are folded into Wave A's single <c>AddTizenImageSources</c> seam at the final
/// rebase. What must never change is the set of implementations the seam constructs.
/// </para>
/// </remarks>
public class ImageSourceSeamTests
{
	/// <summary>The registration methods that make up the image-source seam.</summary>
	/// <remarks>
	/// After the fold this is expected to be just <c>AddTizenImageSources</c>. Both are listed so
	/// the test does not have to change in the same commit as the fold — and so that deleting one
	/// without moving its registrations fails loudly.
	/// </remarks>
	static readonly string[] SeamMethods =
	{
		"AddTizenImageSources",
		"AddTizenUriAndFontImageSources",
	};

	/// <summary>Every Tizen image source service the seam must construct.</summary>
	static readonly string[] RequiredServices =
	{
		"TizenFileImageSourceService",
		"TizenStreamImageSourceService",
		"TizenUriImageSourceService",
		"TizenFontImageSourceService",
	};

	/// <summary>Types constructed by <c>newobj</c> anywhere in the seam's methods.</summary>
	static IReadOnlyCollection<string> ConstructedTypes()
	{
		using var stream = File.OpenRead(RefPackAssembly.Path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();

		var found = new HashSet<string>(StringComparer.Ordinal);
		var matched = new HashSet<string>(StringComparer.Ordinal);

		foreach (var handle in reader.MethodDefinitions)
		{
			var method = reader.GetMethodDefinition(handle);

			if (!SeamMethods.Contains(reader.GetString(method.Name), StringComparer.Ordinal))
				continue;

			matched.Add(reader.GetString(method.Name));

			// The registrations are static lambdas, so the newobj lives in the compiler-generated
			// closure method rather than in the registration method itself. Scanning the whole
			// declaring type - the extension class and its nested closures - catches both shapes.
			foreach (var name in ConstructedTypesIn(reader, pe, method.GetDeclaringType()))
				found.Add(name);
		}

		Assert.True(
			matched.Count > 0,
			"No image-source registration method was found in the ref-pack assembly. The seam has "
			+ "been renamed or dropped from eng/Maui.Tizen.Core.Sources.props.");

		return found;
	}

	static IEnumerable<string> ConstructedTypesIn(MetadataReader reader, PEReader pe, TypeDefinitionHandle typeHandle)
	{
		var type = reader.GetTypeDefinition(typeHandle);

		foreach (var handle in type.GetMethods())
		{
			var method = reader.GetMethodDefinition(handle);

			if (method.RelativeVirtualAddress == 0)
				continue;

			var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
			if (il is null)
				continue;

			for (var i = 0; i + 4 < il.Length; i++)
			{
				// newobj <token>
				if (il[i] != 0x73)
					continue;

				var token = BitConverter.ToInt32(il, i + 1);
				var name = DeclaringTypeName(reader, token);

				if (name is not null)
					yield return name;
			}
		}

		// Registration lambdas are emitted into a nested closure class.
		foreach (var nested in type.GetNestedTypes())
		{
			foreach (var name in ConstructedTypesIn(reader, pe, nested))
				yield return name;
		}
	}

	/// <summary>Resolves the type declaring the constructor a <c>newobj</c> token refers to.</summary>
	static string? DeclaringTypeName(MetadataReader reader, int token)
	{
		var kind = token >>> 24;
		var row = token & 0x00FFFFFF;

		if (row == 0)
			return null;

		try
		{
			return kind switch
			{
				// MethodDef: a constructor defined in this assembly.
				0x06 => reader.GetString(reader.GetTypeDefinition(
					reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row)).GetDeclaringType()).Name),

				// MemberRef: a constructor in a referenced assembly.
				0x0A => reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row)).Parent.Kind switch
				{
					HandleKind.TypeReference => reader.GetString(reader.GetTypeReference(
						(TypeReferenceHandle)reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row)).Parent).Name),
					_ => null,
				},

				_ => null,
			};
		}
		catch (BadImageFormatException)
		{
			return null;
		}
	}

	/// <summary>
	/// Every image source type must be served by a Tizen implementation.
	/// </summary>
	/// <remarks>
	/// Dropping one during the fold would not fail anything else: MAUI's neutral package still
	/// registers a service for that type, so it resolves, does nothing on Tizen, and renders blank.
	/// </remarks>
	[Theory]
	[InlineData("TizenFileImageSourceService")]
	[InlineData("TizenStreamImageSourceService")]
	[InlineData("TizenUriImageSourceService")]
	[InlineData("TizenFontImageSourceService")]
	public void TheSeamConstructsEveryTizenImageSourceService(string serviceType)
	{
		var constructed = ConstructedTypes();

		Assert.True(
			constructed.Contains(serviceType),
			$"The image-source seam never constructs {serviceType}. MAUI's neutral package still "
			+ $"registers a service for that image source type, so the omission is silent: the "
			+ $"source resolves, produces no image on Tizen, and the control renders blank. "
			+ $"Constructed: {string.Join(", ", constructed.Where(c => c.StartsWith("Tizen", StringComparison.Ordinal)).OrderBy(c => c, StringComparer.Ordinal))}.");
	}

	/// <summary>Guards the list above against silently covering nothing.</summary>
	[Fact]
	public void TheRequiredServiceListMatchesTheFourImageSourceTypes() =>
		Assert.Equal(4, RequiredServices.Distinct(StringComparer.Ordinal).Count());
}
