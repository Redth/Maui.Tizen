using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Guards the compiled output of the ref-pack lane, which is where the Wave B sources are actually
/// type-checked against real TizenFX.
/// </summary>
/// <remarks>
/// <para>
/// These assert on EMITTED metadata rather than on source text. A source-level check can be
/// satisfied by a file that is simply never compiled; only the produced metadata proves that no
/// colliding type reaches consumers.
/// </para>
/// <para>
/// Metadata is read with <see cref="MetadataReader"/> rather than <c>Assembly.Load</c> on purpose:
/// loading would require resolving Tizen.NUI, which cannot be loaded on a host TFM. Reading
/// metadata needs no references at all.
/// </para>
/// </remarks>
public class EmittedTypeTests
{

	static (IReadOnlyList<string> Defined, IReadOnlyList<string> Referenced) CoreMetadata { get; } =
		ReadMetadata(RefPackAssembly.Path);

	static (IReadOnlyList<string> Defined, IReadOnlyList<string> Referenced) ControlsMetadata { get; } =
		ReadMetadata(ControlsRefPackAssembly.Path);

	static (IReadOnlyList<string>, IReadOnlyList<string>) ReadMetadata(string path)
	{
		using var stream = File.OpenRead(path);
		using var pe = new PEReader(stream);

		var reader = pe.GetMetadataReader();

		var defined = new List<string>();
		foreach (var handle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(handle);
			var ns = reader.GetString(type.Namespace);
			var name = reader.GetString(type.Name);
			defined.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
		}

		var referenced = new List<string>();
		foreach (var handle in reader.TypeReferences)
		{
			var type = reader.GetTypeReference(handle);
			var ns = reader.GetString(type.Namespace);
			var name = reader.GetString(type.Name);
			referenced.Add(string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}");
		}

		return (defined, referenced);
	}

	/// <summary>
	/// <c>Microsoft.Maui.Platform.WrapperView</c> also exists in the neutral Microsoft.Maui.Core
	/// assembly. Emitting it here would give two loaded assemblies the same full type name, which is
	/// a CS0433 ambiguity for anyone referencing both. The wrapper compiles as
	/// <c>Microsoft.Maui.Platforms.Tizen.TizenWrapperView</c> instead.
	/// </summary>
	[Fact]
	public void DoesNotEmitCollidingWrapperView()
	{
		Assert.DoesNotContain("Microsoft.Maui.Platform.WrapperView", CoreMetadata.Defined);
		Assert.Contains("Microsoft.Maui.Platforms.Tizen.TizenWrapperView", CoreMetadata.Defined);
	}

	/// <summary>
	/// <c>IPlatformViewHandler</c> only exists inside MAUI's own Tizen build. This backend must
	/// neither declare it nor bind to it; the core slice supplies <c>ITizenPlatformViewHandler</c>.
	/// </summary>
	[Fact]
	public void NeitherDeclaresNorReferencesPlatformViewHandler()
	{
		string[] banned =
		{
			"Microsoft.Maui.IPlatformViewHandler",
			"Microsoft.Maui.Handlers.IPlatformViewHandler",
		};

		foreach (var name in banned)
		{
			Assert.DoesNotContain(name, CoreMetadata.Defined.Concat(ControlsMetadata.Defined));
			Assert.DoesNotContain(name, CoreMetadata.Referenced.Concat(ControlsMetadata.Referenced));
		}
		Assert.Contains(
			"Microsoft.Maui.Platforms.Tizen.ITizenPlatformViewHandler",
			CoreMetadata.Referenced.Concat(CoreMetadata.Defined));
	}

	/// <summary>
	/// No emitted type may sit in <c>Microsoft.Maui.Platform</c>: that namespace belongs to the
	/// neutral assembly. Everything compiled here belongs under <c>Microsoft.Maui.Platforms.Tizen</c>.
	/// </summary>
	[Fact]
	public void EmitsNothingIntoTheNeutralPlatformNamespace()
	{
		var offenders = CoreMetadata.Defined
			.Concat(ControlsMetadata.Defined)
			.Where(n => n.StartsWith("Microsoft.Maui.Platform.", StringComparison.Ordinal))
			.ToList();

		Assert.Empty(offenders);
	}

	/// <summary>
	/// The Wave B handlers must actually be in the compiled output. Without this the checks above
	/// would pass trivially if the sources were dropped from the compile lane.
	/// </summary>
	[Fact]
	public void EmitsTheWaveBHandlers()
	{
		string[] coreExpected =
		{
			"TizenScrollViewHandler", "TizenBorderHandler", "TizenImageHandler", "TizenImageButtonHandler",
			"TizenGraphicsViewHandler", "TizenShapeViewHandler", "TizenRefreshViewHandler",
			"TizenSwipeViewHandler", "TizenSwipeItemViewHandler", "TizenSwipeItemMenuItemHandler",
			"TizenIndicatorViewHandler",
		};
		string[] controlsExpected =
		{
			"TizenBoxViewHandler", "TizenLineHandler", "TizenPathHandler",
			"TizenPolygonHandler", "TizenPolylineHandler", "TizenRectangleHandler", "TizenRoundRectangleHandler",
		};

		var coreEmitted = CoreMetadata.Defined
			.Select(n => n.Contains('.', StringComparison.Ordinal) ? n[(n.LastIndexOf('.') + 1)..] : n)
			.ToHashSet(StringComparer.Ordinal);
		var controlsEmitted = ControlsMetadata.Defined
			.Select(n => n.Contains('.', StringComparison.Ordinal) ? n[(n.LastIndexOf('.') + 1)..] : n)
			.ToHashSet(StringComparer.Ordinal);

		foreach (var name in coreExpected)
			Assert.Contains(name, coreEmitted);

		foreach (var name in controlsExpected)
			Assert.Contains(name, controlsEmitted);
	}

	/// <summary>
	/// The migrated Wave B platform views must be emitted under Tizen-owned names.
	/// </summary>
	[Fact]
	public void EmitsTheWaveBPlatformViewsUnderTizenNames()
	{
		string[] expected =
		{
			"Microsoft.Maui.Platforms.Tizen.TizenWrapperView",
			"Microsoft.Maui.Platforms.Tizen.TizenScrollViewGroup",
			"Microsoft.Maui.Platforms.Tizen.TizenShapeView",
			"Microsoft.Maui.Platforms.Tizen.TizenSwipeViewGroup",
			"Microsoft.Maui.Platforms.Tizen.TizenPageControl",
			"Microsoft.Maui.Platforms.Tizen.TizenRefreshLayout",
			"Microsoft.Maui.Platforms.Tizen.TizenImageButtonView",
			"Microsoft.Maui.Platforms.Tizen.TizenTouchGraphicsView",
		};

		foreach (var name in expected)
		{
			Assert.Contains(name, CoreMetadata.Defined);
		}
	}

	[Fact]
	public void GroupedSourceCrossesTheCompiledItemAdaptorBoundaryAsIList()
	{
		using var stream = File.OpenRead(ControlsRefPackAssembly.Path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var source = FindType(reader, "Microsoft.Maui.Platforms.Tizen.Platform.TizenGroupItemSource");
		var observableSource = FindType(reader, "Microsoft.Maui.Platforms.Tizen.Platform.TizenObservableItemSource");
		var adaptor = FindType(reader, "Microsoft.Maui.Platforms.Tizen.Platform.TizenGroupItemTemplateAdaptor");

		var interfaces = source.GetInterfaceImplementations()
			.Select(handle => reader.GetInterfaceImplementation(handle).Interface)
			.Select(handle => TypeName(reader, handle))
			.ToHashSet(StringComparer.Ordinal);

		Assert.Contains("System.Collections.IList", interfaces);
		Assert.Contains("System.Collections.Specialized.INotifyCollectionChanged", interfaces);
		var observableInterfaces = observableSource.GetInterfaceImplementations()
			.Select(handle => reader.GetInterfaceImplementation(handle).Interface)
			.Select(handle => TypeName(reader, handle))
			.ToHashSet(StringComparer.Ordinal);
		Assert.Contains("System.Collections.IList", observableInterfaces);
		Assert.Contains("System.Collections.Specialized.INotifyCollectionChanged", observableInterfaces);
		Assert.Equal("Tizen.UIExtensions.NUI.ItemAdaptor", TypeName(reader, adaptor.BaseType));
	}

	[Fact]
	public void ItemsControlPublishesTheNativeMeasurableContract()
	{
		using var stream = File.OpenRead(ControlsRefPackAssembly.Path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var control = FindType(
			reader,
			"Microsoft.Maui.Platforms.Tizen.Platform.TizenItemsViewControl`1");
		var interfaces = control.GetInterfaceImplementations()
			.Select(handle => reader.GetInterfaceImplementation(handle).Interface)
			.Select(handle => TypeName(reader, handle))
			.ToHashSet(StringComparer.Ordinal);

		Assert.Contains("Tizen.UIExtensions.Common.IMeasurable", interfaces);
	}

	[Fact]
	public void PinnedItemAdaptorRetainsIListAndSubscribesToItsNotifications()
	{
		var versions = XDocument.Load(RepoPaths.Combine("Directory.Packages.props"));
		var version = versions.Descendants("PackageVersion")
			.Single(element => (string?)element.Attribute("Include") == "Tizen.UIExtensions.NUI")
			.Attribute("Version")!.Value;
		var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
			?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
		var assemblyPath = Directory.EnumerateFiles(
				Path.Combine(packages, "tizen.uiextensions.nui", version, "lib"),
				"Tizen.UIExtensions.NUI.dll",
				SearchOption.AllDirectories)
			.First();

		using var stream = File.OpenRead(assemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();
		var adaptor = FindType(reader, "Tizen.UIExtensions.NUI.ItemAdaptor");
		var setItemsSource = adaptor.GetMethods()
			.Select(reader.GetMethodDefinition)
			.Single(method => reader.GetString(method.Name) == "SetItemsSource");
		var il = pe.GetMethodBody(setItemsSource.RelativeVirtualAddress).GetILBytes();

		Assert.NotNull(il);
		Assert.True(ReferencesType(il!, reader, 0x75, "System.Collections.IList"));
		Assert.True(ReferencesType(il, reader, 0x75, "System.Collections.Specialized.INotifyCollectionChanged"));
		Assert.True(ReferencesMember(il, reader, 0x6f, "add_CollectionChanged"));
	}

	static TypeDefinition FindType(MetadataReader reader, string fullName)
	{
		foreach (var handle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(handle);
			var name = $"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";
			if (name == fullName)
				return type;
		}

		throw new InvalidOperationException($"Type '{fullName}' was not emitted.");
	}

	static string TypeName(MetadataReader reader, EntityHandle handle) =>
		handle.Kind switch
		{
			HandleKind.TypeDefinition => Name(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
			HandleKind.TypeReference => Name(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
			_ => handle.Kind.ToString(),
		};

	static string Name(MetadataReader reader, TypeDefinition type) =>
		$"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";

	static string Name(MetadataReader reader, TypeReference type) =>
		$"{reader.GetString(type.Namespace)}.{reader.GetString(type.Name)}";

	static bool ReferencesType(
		byte[] il,
		MetadataReader reader,
		byte opcode,
		string expected)
	{
		foreach (var handle in TokensFollowing(il, opcode))
		{
			if (handle.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference
				&& TypeName(reader, handle) == expected)
				return true;
		}

		return false;
	}

	static bool ReferencesMember(byte[] il, MetadataReader reader, byte opcode, string expected)
	{
		foreach (var handle in TokensFollowing(il, opcode))
		{
			if (handle.Kind == HandleKind.MemberReference
				&& reader.GetString(reader.GetMemberReference((MemberReferenceHandle)handle).Name) == expected)
				return true;
		}

		return false;
	}

	static IEnumerable<EntityHandle> TokensFollowing(byte[] il, byte opcode)
	{
		for (var index = 0; index + sizeof(int) < il.Length; index++)
		{
			if (il[index] != opcode)
				continue;

			var token = BitConverter.ToInt32(il, index + 1);
			EntityHandle handle;
			try
			{
				handle = MetadataTokens.EntityHandle(token);
			}
			catch (ArgumentException)
			{
				continue;
			}

			yield return handle;
		}
	}

	static IReadOnlyList<string> ReadMethodNames(string assemblyPath, string declaringTypeFullName)
	{
		using var stream = File.OpenRead(assemblyPath);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();

		foreach (var handle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(handle);
			var ns = reader.GetString(type.Namespace);
			var name = reader.GetString(type.Name);
			var full = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";

			if (!string.Equals(full, declaringTypeFullName, StringComparison.Ordinal))
				continue;

			return type.GetMethods()
				.Select(m => reader.GetString(reader.GetMethodDefinition(m).Name))
				.ToList();
		}

		return Array.Empty<string>();
	}

	/// <summary>
	/// The composition root must actually register Wave B's handlers, image sources and fonts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>ConfigurePlatformContent</c> is a partial method. If its implementing half is ever dropped
	/// from eng/Maui.Tizen.Core.Sources.props, the compiler ERASES both the method and the call -
	/// silently, with no error anywhere - and the whole backend composes to nothing: MAUI's neutral
	/// handlers and image sources still resolve, so the app runs and simply renders nothing Tizen.
	/// </para>
	/// <para>
	/// A partial method with no implementation leaves no metadata, so its presence here is proof
	/// that the implementing half was compiled in.
	/// </para>
	/// </remarks>
	[Fact]
	public void CompositionRootImplementsThePlatformContentHook()
	{
		var methods = ReadMethodNames(
			RefPackAssembly.Path,
			"Microsoft.Maui.Platforms.Tizen.Hosting.TizenMauiAppBuilderExtensions");

		Assert.Contains("ConfigurePlatformContent", methods);
	}

	/// <summary>
	/// The registration entry points the hook calls must be emitted too.
	/// </summary>
	[Fact]
	public void EmitsTheWaveBRegistrationEntryPoints()
	{
		string[] coreExpected =
		{
			"Microsoft.Maui.Platforms.Tizen.Hosting.TizenContentHandlerCollectionExtensions",
			"Microsoft.Maui.Platforms.Tizen.Hosting.TizenFontServiceCollectionExtensions",
			"Microsoft.Maui.Platforms.Tizen.TizenEmbeddedFontLoader",
			"Microsoft.Maui.Platforms.Tizen.TizenPlatformFontDirectoryProvider",
		};
		string[] controlsExpected =
		{
			"Microsoft.Maui.Platforms.Tizen.Hosting.TizenShapeHandlerCollectionExtensions",
			"Microsoft.Maui.Platforms.Tizen.Hosting.TizenControlsMauiAppBuilderExtensions",
		};

		foreach (var name in coreExpected)
			Assert.Contains(name, CoreMetadata.Defined);

		foreach (var name in controlsExpected)
			Assert.Contains(name, ControlsMetadata.Defined);
	}
}
