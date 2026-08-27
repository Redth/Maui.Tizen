using System.Collections;
using System.Reflection;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Reflection over the real MAUI assemblies, which are the authority for what a handler's mapper
/// is supposed to cover. Nothing here is hand-transcribed from upstream source.
/// </summary>
public static class NeutralMaui
{
	public static readonly Assembly Core = typeof(Microsoft.Maui.IView).Assembly;
	public static readonly Assembly Controls = typeof(Microsoft.Maui.Controls.Element).Assembly;

	/// <summary>
	/// Forces the Controls-level mapper remaps to run before any neutral mapper is read.
	/// </summary>
	/// <remarks>
	/// <para>
	/// NEUTRAL MAPPERS ARE MUTATED AT RUNTIME. Types such as <c>FlyoutPage</c> and <c>Toolbar</c>
	/// call <c>RemapForControls()</c>, which adds Controls-level keys - <c>FlyoutLayoutBehavior</c>
	/// among them - to the static <c>FlyoutViewHandler.Mapper</c>. That happens when a Controls host
	/// is built, not when the assembly loads.
	/// </para>
	/// <para>
	/// Reading a neutral mapper without forcing this first makes the answer depend on whether some
	/// earlier test happened to build a host. That is exactly how the generated parity manifest came
	/// to disagree between a local run and CI: same commit, same packages, different test order. A
	/// parity artifact whose contents depend on execution order is worse than none, because both
	/// answers look authoritative.
	/// </para>
	/// <para>
	/// Building the host is the supported way to trigger the remaps; poking the internal
	/// <c>RemapForControls</c> methods by reflection would just re-encode upstream's private wiring.
	/// </para>
	/// </remarks>
	/// <summary>Why the remap forcing failed, if it did.</summary>
	public static Exception? RemapFailure { get; private set; }

	static readonly object s_remapGate = new();
	static bool s_remapAttempted;

	/// <summary>
	/// Runs the Controls remaps once, before any mapper is read.
	/// </summary>
	/// <remarks>
	/// Called from every entry point that reads a mapper rather than from a static constructor,
	/// because a static constructor body runs AFTER static field initializers - so any snapshot
	/// taken in a field initializer would still miss the remaps.
	/// </remarks>
	public static void EnsureRemapsBeforeReadingMappers()
	{
		lock (s_remapGate)
		{
			if (s_remapAttempted)
			{
				return;
			}

			s_remapAttempted = true;
			EnsureControlsRemapsHaveRun();
		}
	}

	static void EnsureControlsRemapsHaveRun()
	{
		try
		{
			var builder = Microsoft.Maui.Hosting.MauiApp.CreateBuilder(useDefaults: false);

			// UseMauiApp<T> is what registers the Controls handlers and runs the remaps.
			//
			// Located by scanning rather than named directly: Microsoft.Maui.Controls and
			// Microsoft.Maui.Controls.Xaml both declare a
			// Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions, so referencing it by name is
			// CS0433-ambiguous, and the generic overload does not live in the assembly one would
			// first guess.
			var useMauiApp = new[] { Controls, typeof(Microsoft.Maui.Controls.Xaml.Extensions).Assembly }
				.Distinct()
				.SelectMany(a => a.GetType("Microsoft.Maui.Controls.Hosting.AppHostBuilderExtensions") is { } type
					? type.GetMethods(BindingFlags.Public | BindingFlags.Static)
					: Array.Empty<MethodInfo>())
				.FirstOrDefault(m => m.Name == "UseMauiApp"
					&& m.IsGenericMethodDefinition
					&& m.GetGenericArguments().Length == 1
					&& m.GetParameters().Length == 1);

			if (useMauiApp is null)
			{
				RemapFailure = new MissingMethodException(
					"Could not locate a static generic UseMauiApp<T>(MauiAppBuilder) on any "
						+ "AppHostBuilderExtensions. The Controls mapper remaps cannot be forced.");
				return;
			}

			useMauiApp
				.MakeGenericMethod(typeof(RemapTriggerApplication))
				.Invoke(null, new object[] { builder });
		}
		catch (Exception ex)
		{
			// Recorded rather than swallowed: a silent failure here leaves the neutral key set
			// half-populated, which is precisely the order-dependence this method exists to remove.
			// ControlsRemapsAreDeterministic surfaces it.
			RemapFailure = ex;
		}
	}

	/// <summary>Minimal application used only to trigger the Controls handler remaps.</summary>
	sealed class RemapTriggerApplication : Microsoft.Maui.Controls.Application
	{
	}

	/// <summary>Every public type name declared by the MAUI assemblies.</summary>
	static readonly Lazy<IReadOnlySet<string>> s_publicTypeNames = new(() =>
		new[] { Core, Controls }
			.SelectMany(a => a.GetExportedTypes())
			.Select(t => t.Name)
			.ToHashSet(StringComparer.Ordinal));

	/// <summary>Every public type name declared by the MAUI assemblies.</summary>
	public static IReadOnlySet<string> PublicTypeNames => s_publicTypeNames.Value;

	/// <summary>Keys contributed by the shared view mapper, which every view handler chains.</summary>
	static readonly Lazy<IReadOnlySet<string>> s_viewMapperKeys = new(() =>
	{
		EnsureRemapsBeforeReadingMappers();
		return LoadViewMapperKeys();
	});

	/// <summary>
	/// Keys contributed by the shared view mapper, which every view handler chains.
	/// </summary>
	/// <remarks>
	/// LAZY ON PURPOSE. A static field initializer would run BEFORE the static constructor body, so
	/// this snapshot would be taken before <see cref="EnsureControlsRemapsHaveRun"/> could run and
	/// would miss every Controls-level key. That is not theoretical: it made this suite pass only
	/// when some earlier test happened to initialize Controls first, and fail in a fresh process
	/// with false BackgroundColor gaps. Every mapper-derived snapshot here must stay lazy and go
	/// through <see cref="EnsureRemapsBeforeReadingMappers"/>.
	/// </remarks>
	public static IReadOnlySet<string> ViewMapperKeys => s_viewMapperKeys.Value;

	public static Type? FindHandler(string name) =>
		Core.GetExportedTypes().FirstOrDefault(t => t.Name == name)
		?? Controls.GetExportedTypes().FirstOrDefault(t => t.Name == name);

	/// <summary>Reads <c>GetKeys()</c> off a handler's static <paramref name="fieldName"/> mapper.</summary>
	public static IReadOnlyList<string> MapperKeys(Type handler, string fieldName)
	{
		EnsureRemapsBeforeReadingMappers();

		var field = handler.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
		var mapper = field?.GetValue(null);

		return Keys(mapper);
	}

	static IReadOnlyList<string> Keys(object? mapper)
	{
		if (mapper is null)
		{
			return Array.Empty<string>();
		}

		var getKeys = mapper.GetType().GetMethod("GetKeys", Type.EmptyTypes);

		if (getKeys?.Invoke(mapper, null) is not IEnumerable values)
		{
			return Array.Empty<string>();
		}

		return values.Cast<object?>().Select(v => v?.ToString() ?? string.Empty).ToList();
	}

	static IReadOnlySet<string> LoadViewMapperKeys()
	{
		var viewHandler = Core.GetExportedTypes().First(t => t.FullName == "Microsoft.Maui.Handlers.ViewHandler");
		var keys = MapperKeys(viewHandler, "ViewMapper").ToHashSet(StringComparer.Ordinal);

		var element = Core.GetExportedTypes().FirstOrDefault(t => t.FullName == "Microsoft.Maui.Handlers.ElementHandler");

		if (element is not null)
		{
			foreach (var key in MapperKeys(element, "ElementMapper"))
			{
				keys.Add(key);
			}
		}

		return keys;
	}
}
