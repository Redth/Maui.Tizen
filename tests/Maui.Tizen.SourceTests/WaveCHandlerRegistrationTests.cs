using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Invariants for Wave C handler registration.
/// </summary>
/// <remarks>
/// <para>
/// MAUI resolves handlers from a registry, so a handler that is implemented, mapped and tested but
/// never registered is unreachable - it silently falls back to whatever the neutral registry
/// provides, which on this backend is nothing. Every handler in Wave C was in exactly that state
/// until <c>TizenNavigationHandlers</c> was added.
/// </para>
/// <para>
/// The expected set is <b>derived from the source tree</b> rather than written down here, so adding
/// a handler and forgetting to register it fails this test instead of going unnoticed. A hardcoded
/// list would drift with the code and prove nothing.
/// </para>
/// </remarks>
public class WaveCHandlerRegistrationTests
{
	const string RegistrationFile = "TizenNavigationHandlers.cs";

	static string RegistrationSource()
		=> File.ReadAllText(WaveCSource.Files.Single(f => Path.GetFileName(f) == RegistrationFile));

	/// <summary>
	/// Every concrete (non-generic) Wave C handler discovered in the source tree.
	/// </summary>
	static IReadOnlyList<string> DiscoverConcreteHandlers()
	{
		var declaration = new Regex(
			@"class\s+(?<name>Tizen[A-Za-z0-9]+Handler)\s*(?<generic><[^>]*>)?\s*:",
			RegexOptions.Compiled);

		return WaveCSource.Files
			.Where(f => f.Replace('\\', '/').Contains("/Handlers/", StringComparison.Ordinal))
			.SelectMany(f => declaration.Matches(File.ReadAllText(f)).Cast<Match>())
			// Open generic handlers are base classes; only closed handlers are registrable.
			.Where(m => !m.Groups["generic"].Success)
			.Select(m => m.Groups["name"].Value)
			.Distinct()
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();
	}

	[Fact]
	public void TheRegistrationExtensionExists()
	{
		var source = RegistrationSource();

		Assert.Contains("AddMauiTizenNavigationHandlers", source, StringComparison.Ordinal);
		Assert.Contains("IMauiHandlersCollection", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// Every concrete handler in the tree is registered.
	/// </summary>
	/// <remarks>
	/// This is the invariant that actually matters: it fails when a handler is added without a
	/// registration, which is the state the whole assembly was in.
	/// </remarks>
	[Fact]
	public void EveryConcreteHandlerIsRegistered()
	{
		var source = RegistrationSource();
		var discovered = DiscoverConcreteHandlers();

		Assert.NotEmpty(discovered);

		var unregistered = discovered
			.Where(h => !source.Contains($", {h}>", StringComparison.Ordinal))
			.ToList();

		Assert.True(
			unregistered.Count == 0,
			"Handlers implemented but never registered, so unreachable at runtime: "
				+ string.Join(", ", unregistered));
	}

	/// <summary>
	/// No handler is registered twice.
	/// </summary>
	/// <remarks>
	/// A duplicate registration is not harmless: the later one wins, so a duplicate silently decides
	/// which handler a virtual view resolves to.
	/// </remarks>
	[Fact]
	public void NoHandlerIsRegisteredTwice()
	{
		var source = RegistrationSource();

		var duplicates = DiscoverConcreteHandlers()
			.Select(h => (Handler: h, Count: Regex.Matches(source, $@",\s*{Regex.Escape(h)}>").Count))
			.Where(x => x.Count > 1)
			.Select(x => $"{x.Handler} x{x.Count}")
			.ToList();

		Assert.True(duplicates.Count == 0, "Duplicate registrations: " + string.Join(", ", duplicates));
	}

	/// <summary>
	/// Registration replaces the neutral handler rather than chaining onto it.
	/// </summary>
	/// <remarks>
	/// Wave C handlers declare their own mappers instead of extending the neutral ones, so a
	/// chained registration would run both and double-apply every mapping.
	/// </remarks>
	[Fact]
	public void RegistrationsUseAddHandlerRatherThanTryAdd()
	{
		var source = RegistrationSource();

		Assert.Contains("AddHandler<", source, StringComparison.Ordinal);
		Assert.DoesNotContain("TryAddHandler", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// Registration is reachable from a <c>MauiAppBuilder</c>, not just a handler collection.
	/// </summary>
	[Fact]
	public void AnAppBuilderEntryPointExists()
	{
		var source = RegistrationSource();

		Assert.Contains("MauiAppBuilder", source, StringComparison.Ordinal);
		Assert.Contains("ConfigureMauiHandlers", source, StringComparison.Ordinal);
	}

	/// <summary>
	/// The registration file is in the compile set, or it would not ship.
	/// </summary>
	[Fact]
	public void TheRegistrationFileIsCompiled()
	{
		var props = File.ReadAllText(RepoPaths.Combine("eng", "Maui.Tizen.WaveC.Sources.props"));

		Assert.Contains(RegistrationFile, props, StringComparison.Ordinal);
	}
}

/// <summary>
/// Invariants for adaptor native-view registration.
/// </summary>
/// <remarks>
/// The adaptors are NUI types and cannot be instantiated in a host test, so these pin the structure
/// at source level. What they guard is not cosmetic: everything that makes a recycled row work -
/// rebinding, resolving the MAUI view in <c>UpdateViewState</c>, activating the current item,
/// teardown - is keyed off the base adaptor's registration. A parallel private table looks
/// equivalent and silently opts every row out of all of it.
/// </remarks>
public class WaveCAdaptorRegistrationTests
{
	static readonly string[] ShellAdaptors =
	{
		"TizenShellFlyoutItemAdaptor.cs",
		"TizenShellSectionItemAdaptor.cs",
		"TizenShellContentItemAdaptor.cs",
		"TizenShellSearchItemAdaptor.cs",
	};

	static string ReadWaveCSource(string fileName)
		=> File.ReadAllText(WaveCSource.Files.Single(f => Path.GetFileName(f) == fileName));

	/// <summary>
	/// No adaptor keeps its own native-to-MAUI table.
	/// </summary>
	[Fact]
	public void NoAdaptorKeepsAPrivateNativeViewTable()
	{
		var offenders = ShellAdaptors
			.Where(f => ReadWaveCSource(f).Contains("Dictionary<NView, View>", StringComparison.Ordinal))
			.ToList();

		Assert.True(
			offenders.Count == 0,
			"Adaptors bypassing the base registration with a private table: " + string.Join(", ", offenders));
	}

	/// <summary>
	/// Every Shell adaptor registers through the shared surface.
	/// </summary>
	[Fact]
	public void EveryShellAdaptorRegistersThroughTheBase()
	{
		foreach (var file in ShellAdaptors)
		{
			var source = ReadWaveCSource(file);

			Assert.True(
				source.Contains("RegisterNativeView", StringComparison.Ordinal),
				$"{file} never registers its created views with the base adaptor.");
		}
	}

	/// <summary>
	/// Every Shell adaptor unregisters on removal.
	/// </summary>
	/// <remarks>
	/// Leaving the entry behind keeps the view alive and lets a recycled native view resolve to a
	/// MAUI view whose handler is already disposed.
	/// </remarks>
	[Fact]
	public void EveryShellAdaptorUnregistersOnRemoval()
	{
		foreach (var file in ShellAdaptors)
		{
			var source = ReadWaveCSource(file);

			Assert.True(
				source.Contains("UnregisterNativeView", StringComparison.Ordinal),
				$"{file} removes native views without unregistering them.");
		}
	}

	/// <summary>
	/// The shared registration surface exists and tracks enabled state for every caller.
	/// </summary>
	[Fact]
	public void TheSharedRegistrationSurfaceTracksEnabledState()
	{
		var source = ReadWaveCSource("TizenItemTemplateAdaptor.cs");

		Assert.Contains("protected void RegisterNativeView", source, StringComparison.Ordinal);
		Assert.Contains("protected View? UnregisterNativeView", source, StringComparison.Ordinal);

		// Tracking lives inside registration so no caller has to remember it.
		var register = source[source.IndexOf("protected void RegisterNativeView", StringComparison.Ordinal)..];
		register = register[..register.IndexOf("\n\t\t}", StringComparison.Ordinal)];

		Assert.Contains("TrackEnabledState", register, StringComparison.Ordinal);
	}
}
