using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Hosting;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Covers the Controls-level shape handler composition.
/// </summary>
/// <remarks>
/// <para>
/// Registering handlers is only half the job; something has to <em>call</em> the registration. The
/// failure when nothing does is silent, which is what makes it worth this much test: MAUI Controls
/// already maps all eight shapes to its own neutral handlers, so every shape still resolves, still
/// lays out, and simply never draws on Tizen.
/// </para>
/// <para>
/// The Tizen shape handlers themselves cannot be resolved on a host TFM — they derive from
/// <c>TizenShapeViewHandler</c>, whose platform view is a NUI type, so the CLR cannot even load
/// them here. That part is honestly integration-only. What these tests do cover, executably, is
/// every step that can be checked without a device: that the risk is real (the defaults are
/// neutral), that a late registration actually wins for all eight types, that the registration
/// lists exactly the right eight, and that the production entry points really do call it.
/// </para>
/// </remarks>
public class ShapeCompositionTests
{
	sealed class HostApplication : Microsoft.Maui.Controls.Application
	{
		protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState) =>
			new(new ContentPage());
	}

	/// <summary>A host-loadable stand-in, used only to observe which registration wins.</summary>
	sealed class StubShapeHandler : Microsoft.Maui.Handlers.ViewHandler<IShapeView, object>
	{
		public StubShapeHandler()
			: base(Microsoft.Maui.Handlers.ViewHandler.ViewMapper)
		{
		}

		protected override object CreatePlatformView() => new();
	}

	/// <summary>The eight Controls shape types the Tizen backend must take over.</summary>
	public static TheoryData<Type> ShapeTypes =>
		new()
		{
			typeof(BoxView),
			typeof(Ellipse),
			typeof(Line),
			typeof(Microsoft.Maui.Controls.Shapes.Path),
			typeof(Polygon),
			typeof(Polyline),
			typeof(Rectangle),
			typeof(RoundRectangle),
		};

	/// <summary>The Tizen handler each shape must end up on.</summary>
	static readonly (string Shape, string Handler)[] ExpectedRegistrations =
	{
		("BoxView", "TizenBoxViewHandler"),
		("Ellipse", "TizenShapeViewHandler"),
		("Line", "TizenLineHandler"),
		("Path", "TizenPathHandler"),
		("Polygon", "TizenPolygonHandler"),
		("Polyline", "TizenPolylineHandler"),
		("Rectangle", "TizenRectangleHandler"),
		("RoundRectangle", "TizenRoundRectangleHandler"),
	};

	static IMauiHandlersFactory BuildHandlers(Action<IMauiHandlersCollection>? configure = null)
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();

		if (configure is not null)
			builder.ConfigureMauiHandlers(configure);

		return builder.Build().Services.GetRequiredService<IMauiHandlersFactory>();
	}

	/// <summary>
	/// Establishes the risk: without a Tizen registration every shape resolves to MAUI's neutral
	/// handler.
	/// </summary>
	/// <remarks>
	/// This is why an uncalled registration method is not a cosmetic problem. Nothing throws and
	/// nothing is missing — the shapes just quietly stop being Tizen's.
	/// </remarks>
	[Theory]
	[MemberData(nameof(ShapeTypes))]
	public void WithoutARegistrationAShapeResolvesToTheNeutralHandler(Type shapeType)
	{
		var handlerType = BuildHandlers().GetHandlerType(shapeType);

		Assert.NotNull(handlerType);
		Assert.StartsWith("Microsoft.Maui", handlerType!.Namespace, StringComparison.Ordinal);
		Assert.Contains(".Handlers", handlerType.Namespace, StringComparison.Ordinal);
	}

	/// <summary>
	/// A registration added after MAUI's defaults wins, for every one of the eight.
	/// </summary>
	/// <remarks>
	/// The mechanism <c>AddTizenShapeHandlers</c> relies on. Backend configuration necessarily runs
	/// after <c>UseMauiApp</c> has registered the Controls handlers, so if this were
	/// <c>TryAdd</c>-like the Tizen registrations would lose the race exactly as the font services
	/// once did. It is not, and this pins that per shape type rather than assuming it generalises.
	/// </remarks>
	[Theory]
	[MemberData(nameof(ShapeTypes))]
	public void ARegistrationAddedAfterTheDefaultsWins(Type shapeType)
	{
		var handlers = BuildHandlers(collection => collection.AddHandler(shapeType, typeof(StubShapeHandler)));

		Assert.Equal(typeof(StubShapeHandler), handlers.GetHandlerType(shapeType));
	}

	/// <summary>
	/// <c>AddTizenShapeHandlers</c> registers exactly the eight shapes, each on its Tizen handler.
	/// </summary>
	/// <remarks>
	/// A source check because the registration is generic and the handler types cannot be loaded
	/// here. It is precise about both halves of each pair, so neither a missing shape nor a
	/// mis-paired handler passes.
	/// </remarks>
	[Fact]
	public void TheRegistrationCoversEveryShapeExactlyOnce()
	{
		var source = File.ReadAllText(RepoPaths.Combine(
			"src", "Maui.Tizen.Controls", "Hosting", "TizenShapeHandlerCollectionExtensions.cs"));

		var missing = ExpectedRegistrations
			.Where(pair => !source.Contains($"AddHandler<{pair.Shape}, {pair.Handler}>", StringComparison.Ordinal)
				&& !source.Contains($"AddHandler<Microsoft.Maui.Controls.Shapes.{pair.Shape}, {pair.Handler}>", StringComparison.Ordinal))
			.Select(pair => $"{pair.Shape} -> {pair.Handler}")
			.ToList();

		Assert.Empty(missing);

		var registrationCount = source.Split("AddHandler<", StringSplitOptions.None).Length - 1;
		Assert.Equal(ExpectedRegistrations.Length, registrationCount);
	}

	/// <summary>
	/// The production entry points actually call the registration.
	/// </summary>
	/// <remarks>
	/// The blocker this whole file exists for: <c>AddTizenShapeHandlers</c> compiled cleanly and was
	/// covered by metadata assertions while having no call site anywhere in the product. Read from
	/// the ref-pack lane's IL, because the entry point is Tizen-only and cannot be invoked here.
	/// </remarks>
	[Theory]
	[InlineData("ConfigureTizenControls")]
	[InlineData("UseMauiAppTizenControls")]
	public void TheProductionEntryPointCallsTheShapeRegistration(string entryPoint)
	{
		var called = CalledMethodNames("Microsoft.Maui.Platforms.Tizen.Hosting.TizenControlsMauiAppBuilderExtensions", entryPoint);

		Assert.Contains("AddTizenShapeHandlers", called);
	}

	/// <summary>Names of every method called by <paramref name="methodName"/> or its closures.</summary>
	static IReadOnlyCollection<string> CalledMethodNames(string declaringTypeFullName, string methodName)
	{
		using var stream = File.OpenRead(ControlsRefPackAssembly.Path);
		using var pe = new PEReader(stream);
		var reader = pe.GetMetadataReader();

		var names = new HashSet<string>(StringComparer.Ordinal);
		var found = false;

		foreach (var typeHandle in reader.TypeDefinitions)
		{
			var type = reader.GetTypeDefinition(typeHandle);
			var full = reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);

			if (!string.Equals(full, declaringTypeFullName, StringComparison.Ordinal))
				continue;

			foreach (var methodHandle in type.GetMethods())
			{
				var method = reader.GetMethodDefinition(methodHandle);

				if (!string.Equals(reader.GetString(method.Name), methodName, StringComparison.Ordinal))
					continue;

				found = true;
				Collect(reader, pe, method, names);
			}

			// The registration is invoked from inside a ConfigureMauiHandlers lambda, which the
			// compiler emits into a nested closure class rather than into the method itself.
			foreach (var nestedHandle in type.GetNestedTypes())
			{
				var nested = reader.GetTypeDefinition(nestedHandle);

				foreach (var methodHandle in nested.GetMethods())
					Collect(reader, pe, reader.GetMethodDefinition(methodHandle), names);
			}
		}

		Assert.True(found, $"{declaringTypeFullName}.{methodName} is not present in the ref-pack assembly.");

		return names;
	}

	static void Collect(MetadataReader reader, PEReader pe, MethodDefinition method, HashSet<string> names)
	{
		if (method.RelativeVirtualAddress == 0)
			return;

		var il = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
		if (il is null)
			return;

		for (var i = 0; i + 4 < il.Length; i++)
		{
			// call (0x28) / callvirt (0x6F)
			if (il[i] != 0x28 && il[i] != 0x6F)
				continue;

			var name = MethodName(reader, BitConverter.ToInt32(il, i + 1));

			if (name is not null)
				names.Add(name);
		}
	}

	static string? MethodName(MetadataReader reader, int token)
	{
		var row = token & 0x00FFFFFF;

		if (row == 0)
			return null;

		try
		{
			switch (token >>> 24)
			{
				case 0x06:
					return reader.GetString(reader.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(row)).Name);

				case 0x0A:
					return reader.GetString(reader.GetMemberReference(MetadataTokens.MemberReferenceHandle(row)).Name);

				// MethodSpec: a generic instantiation. The name lives on the method it instantiates.
				case 0x2B:
					var spec = reader.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
					return spec.Method.Kind switch
					{
						HandleKind.MethodDefinition => reader.GetString(reader.GetMethodDefinition((MethodDefinitionHandle)spec.Method).Name),
						HandleKind.MemberReference => reader.GetString(reader.GetMemberReference((MemberReferenceHandle)spec.Method).Name),
						_ => null,
					};

				default:
					return null;
			}
		}
		catch (BadImageFormatException)
		{
			return null;
		}
	}
}
