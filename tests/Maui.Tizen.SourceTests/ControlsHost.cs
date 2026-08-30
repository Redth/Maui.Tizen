using System.Collections;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Builds a real .NET MAUI Controls host once per test run, and exposes the resulting mapper state.
/// </summary>
/// <remarks>
/// <para>
/// A real host is required, not just touching handler types: <c>ConfigureControls</c> applies its
/// remaps through the app builder, and not every remap is driven by a static constructor. Without
/// building the host, <c>ViewHandler.ViewMapper</c> reports 29 keys; after building it reports 36.
/// Any analysis done without the host would simply not see the Controls-contributed keys.
/// </para>
/// <para>
/// Mapper state is process-global, so the host is built exactly once and shared.
/// </para>
/// </remarks>
public static class ControlsHost
{
	sealed class HostApplication : Application
	{
		protected override Window CreateWindow(IActivationState? activationState) =>
			new Window(new ContentPage());
	}

	static readonly Lazy<bool> _built = new(() =>
	{
		var builder = MauiApp.CreateBuilder();
		builder.UseMauiApp<HostApplication>();
		builder.Build();
		return true;
	}, LazyThreadSafetyMode.ExecutionAndPublication);

	/// <summary>Ensures the Controls host has been built and its remaps applied.</summary>
	public static void EnsureBuilt() => _ = _built.Value;

	/// <summary>A mapping entry read out of a live mapper.</summary>
	/// <param name="Owner">The declaring handler type name.</param>
	/// <param name="Field">The static mapper field name.</param>
	/// <param name="Key">The property or command key.</param>
	/// <param name="HandlerType">The handler type the mapping will cast to, when discoverable.</param>
	/// <param name="Target">The method the mapping ultimately invokes, when discoverable.</param>
	public sealed record Mapping(string Owner, string Field, string Key, Type? HandlerType, MethodInfo? Target)
	{
		/// <summary>
		/// Whether the mapping's target has an empty body, i.e. setting the property does nothing.
		/// </summary>
		/// <remarks>
		/// An empty body compiles to a bare <c>ret</c>. This matters because MAUI 11 ships no Tizen
		/// target framework, so this repository consumes the NEUTRAL <c>net11.0</c> assembly, in
		/// which <c>PlatformView</c> is <see cref="object"/> and the platform-specific halves of
		/// these mappers simply do not exist. A key can therefore be present, dispatch cleanly, and
		/// still do absolutely nothing.
		/// </remarks>
		public bool HasInertBody
		{
			get
			{
				var il = Target?.GetMethodBody()?.GetILAsByteArray();
				return il is not null && il.Length <= 2;
			}
		}

		/// <summary>
		/// Whether this mapping hard-casts to a concrete handler class.
		/// </summary>
		/// <remarks>
		/// <c>PropertyMapper&lt;TVirtualView, TViewHandler&gt;.Add</c> wraps every mapping in a
		/// closure that performs <c>(TViewHandler)h</c>, guarded only by a check on the VIRTUAL VIEW
		/// type. When <c>TViewHandler</c> is a concrete built-in handler, any other handler reaching
		/// that key throws <see cref="InvalidCastException"/>.
		/// </remarks>
		public bool CastsToConcreteHandler => HandlerType is { IsInterface: false };
	}

	/// <summary>Every mapping declared directly on a public MAUI handler mapper, after Controls remaps.</summary>
	public static IReadOnlyList<Mapping> AllMappings { get; } = ReadAllMappings();

	/// <summary>Reads the mappings declared directly on <paramref name="mapper"/>.</summary>
	public static IReadOnlyList<Mapping> ReadMappings(object mapper, string owner, string field)
	{
		var results = new List<Mapping>();

		var storage = FindField(mapper.GetType(), "_mapper") ?? FindField(mapper.GetType(), "_commandMapper");
		if (storage?.GetValue(mapper) is not IDictionary entries)
			return results;

		foreach (DictionaryEntry entry in entries)
		{
			var key = entry.Key.ToString() ?? string.Empty;
			var stored = entry.Value as Delegate;
			results.Add(new Mapping(owner, field, key, HandlerTypeOf(stored), TargetOf(stored)));
		}

		return results;
	}

	/// <summary>
	/// Recovers the <c>TViewHandler</c> a mapping will cast to.
	/// </summary>
	/// <remarks>
	/// The generic argument is not on the stored delegate — the stored delegate is always
	/// <c>Action&lt;IElementHandler, IElement&gt;</c>. It survives only on the closure's captured
	/// <c>Action&lt;TViewHandler, TVirtualView&gt;</c> field, which is what this reads.
	/// </remarks>
	static Type? HandlerTypeOf(Delegate? del)
	{
		var target = del?.Target;
		if (target is null)
			return null;

		var captured = target.GetType()
			.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			.FirstOrDefault(f => f.FieldType.IsGenericType && f.FieldType.Name.StartsWith("Action`", StringComparison.Ordinal));

		return captured?.FieldType.GetGenericArguments().FirstOrDefault();
	}

	/// <summary>Recovers the method a mapping ultimately invokes.</summary>
	/// <remarks>
	/// The stored delegate is always the wrapper closure, so its own <c>Method</c> is the wrapper.
	/// The real target lives on the captured <c>Action&lt;TViewHandler, TVirtualView&gt;</c>.
	/// </remarks>
	static MethodInfo? TargetOf(Delegate? del)
	{
		var target = del?.Target;
		if (target is null)
			return del?.Method;

		var captured = target.GetType()
			.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
			.FirstOrDefault(f => f.FieldType.IsGenericType && f.FieldType.Name.StartsWith("Action`", StringComparison.Ordinal));

		return (captured?.GetValue(target) as Delegate)?.Method ?? del?.Method;
	}

	static IReadOnlyList<Mapping> ReadAllMappings()
	{
		EnsureBuilt();

		var results = new List<Mapping>();

		var assemblies = new[]
		{
			typeof(Microsoft.Maui.IView).Assembly,
			typeof(Microsoft.Maui.Controls.Element).Assembly,
		};

		foreach (var assembly in assemblies)
		{
			foreach (var type in assembly.GetExportedTypes().Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal)))
			{
				foreach (var field in new[] { "Mapper", "CommandMapper", "ImageMapper", "ViewMapper", "ViewCommandMapper", "ElementMapper", "ElementCommandMapper" })
				{
					var info = type.GetField(field, BindingFlags.Public | BindingFlags.Static);
					if (info is null)
						continue;

					object? mapper;
					try
					{
						mapper = info.GetValue(null);
					}
					catch (TargetInvocationException)
					{
						// A static constructor that needs a platform cannot run on the host.
						continue;
					}

					if (mapper is not null)
						results.AddRange(ReadMappings(mapper, type.Name, field));
				}
			}
		}

		return results;
	}

	static FieldInfo? FindField(Type? type, string name)
	{
		while (type is not null)
		{
			var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
			if (field is not null)
				return field;

			type = type.BaseType;
		}

		return null;
	}
}
