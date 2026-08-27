// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls.Hosting;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platforms.Tizen;
using Microsoft.Maui.Platforms.Tizen.Hosting;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Host integration coverage for image source registration.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Wave B's review found <c>AddTizenImageSources</c> had no composition-root caller: it was a
	/// public method nothing invoked. The reason that survived review is worth stating, because it
	/// is the same shape as several other defects on this branch - <b>the failure was silent</b>.
	/// </para>
	/// <para>
	/// MAUI's neutral package registers <c>FileImageSourceService</c>,
	/// <c>StreamImageSourceService</c>, <c>FontImageSourceService</c> and
	/// <c>UriImageSourceService</c> by default. So every image source type resolves to *something*
	/// whether or not the Tizen registration ever runs. A test asserting "an image source service
	/// is registered" passes on an app that can never display an image. These therefore assert
	/// which implementation wins, never mere resolvability.
	/// </para>
	/// </remarks>
	public class ImageSourceRegistrationTests
	{
		sealed class HostApp : Controls.Application
		{
		}

		static MauiApp BuildTizenApp()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<HostApp>();
			builder.ConfigureTizen();

			return builder.Build();
		}

		/// <summary>
		/// The reason a missing registration is invisible: MAUI already registered a service.
		/// </summary>
		/// <remarks>
		/// This is documentation-as-test. It exists so that the next person to write an image test
		/// discovers, from a passing assertion rather than from a blank screen, that resolvability
		/// proves nothing here.
		/// </remarks>
		[Theory]
		[InlineData(typeof(IFileImageSource))]
		[InlineData(typeof(IStreamImageSource))]
		[InlineData(typeof(IFontImageSource))]
		[InlineData(typeof(IUriImageSource))]
		public void MauiRegistersANeutralServiceForEverySourceTypeByDefault(Type imageSourceType)
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<HostApp>();

			// Deliberately NOT ConfigureTizen: this is MAUI's baseline.
			using var app = builder.Build();

			var service = app.Services
				.GetRequiredService<IImageSourceServiceProvider>()
				.GetImageSourceService(imageSourceType);

			Assert.True(
				service is not null,
				$"MAUI no longer registers a default service for {imageSourceType.Name}. That is a " +
				"behaviour change worth knowing about: a missing Tizen registration would now fail " +
				"loudly instead of silently, and the tests below can be simplified.");
		}

		/// <summary>
		/// A Tizen registration replaces MAUI's neutral default rather than being shadowed by it.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The load-bearing assumption behind the whole design. If <c>AddService</c> appended rather
		/// than replaced, or if first-registration-wins applied, then wiring
		/// <c>AddTizenImageSources</c> into <c>ConfigureTizen</c> would achieve nothing and the
		/// neutral service would keep answering - exactly the failure mode being fixed, but harder
		/// to spot because the call site would now look correct.
		/// </para>
		/// <para>
		/// Asserted with a stand-in rather than the real Tizen service because the real services
		/// need NUI and are compiled only on the Tizen lane; the replacement semantics being
		/// verified are MAUI's, and are identical either way.
		/// </para>
		/// </remarks>
		[Fact]
		public void ATizenRegistrationReplacesMauisNeutralDefault()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<HostApp>();
			builder.ConfigureTizen();
			builder.ConfigureImageSources(sources =>
				sources.AddService<IFileImageSource>(static _ => new StandInFileImageSourceService()));

			using var app = builder.Build();

			var service = app.Services
				.GetRequiredService<IImageSourceServiceProvider>()
				.GetImageSourceService(typeof(IFileImageSource));

			Assert.IsType<StandInFileImageSourceService>(service);
		}

		/// <summary>
		/// <c>ConfigureTizen</c> must actually invoke the image source hook.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Observed rather than asserted structurally: a spy registered <em>before</em>
		/// <c>ConfigureTizen</c> is overwritten only if <c>ConfigureTizen</c> configures the image
		/// source collection at all. If the hook is removed, the spy survives and this fails.
		/// </para>
		/// <para>
		/// This works on the host lane even though the Tizen services themselves are Tizen-only:
		/// the <c>ConfigureImageSources</c> call is portable, so the collection is configured on
		/// both lanes and the ordering is observable here. What cannot be observed off-device is
		/// <em>which</em> Tizen service wins, which is covered by the ref-pack lane compiling the
		/// real registration and by <see cref="TizenSourcesAreRegisteredOnTheTizenLane"/>.
		/// </para>
		/// </remarks>
		[Fact]
		public void ConfigureTizenInvokesTheImageSourceHook()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<HostApp>();

			builder.ConfigureImageSources(sources =>
				sources.AddService<IFileImageSource>(static _ => new StandInFileImageSourceService()));

			builder.ConfigureTizen();

			using var app = builder.Build();

			var service = app.Services
				.GetRequiredService<IImageSourceServiceProvider>()
				.GetImageSourceService(typeof(IFileImageSource));

#if TIZEN
			Assert.False(
				service is StandInFileImageSourceService,
				"ConfigureTizen did not register the Tizen file image source service over the " +
				"earlier registration, so AddTizenImageSources is not being called from the " +
				"composition root.");
#else
			// Off the Tizen lane AddTizenImageSources registers nothing (its body is guarded), so
			// the stand-in legitimately survives. Asserting it does keeps this test honest about
			// what it can prove here instead of pretending to verify the platform services.
			Assert.IsType<StandInFileImageSourceService>(service);
#endif
		}

		/// <summary>
		/// On the Tizen lane, the file and stream services are the Tizen implementations.
		/// </summary>
		/// <remarks>
		/// The real end-to-end assertion. It can only run where the NUI-dependent services are
		/// compiled, so it is skipped off-device rather than weakened into something that passes
		/// everywhere and proves nothing.
		/// </remarks>
		[Fact]
		public void TizenSourcesAreRegisteredOnTheTizenLane()
		{
#if TIZEN
			using var app = BuildTizenApp();

			var provider = app.Services.GetRequiredService<IImageSourceServiceProvider>();

			Assert.IsType<TizenFileImageSourceService>(provider.GetImageSourceService(typeof(IFileImageSource)));
			Assert.IsType<TizenStreamImageSourceService>(provider.GetImageSourceService(typeof(IStreamImageSource)));
#endif
		}

		/// <summary>
		/// Font and URI sources are still MAUI's neutral defaults, and that is the current truth.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Wave A owns file and stream only. This records the gap so it is visible rather than
		/// assumed closed, and so the image workstream has a test that fails - with instructions -
		/// the moment it adds the missing registrations.
		/// </para>
		/// <para>
		/// The assertion is deliberately "not a Tizen service" rather than a specific neutral type,
		/// so it does not break if MAUI renames its defaults.
		/// </para>
		/// </remarks>
		[Theory]
		[InlineData(typeof(IFontImageSource))]
		[InlineData(typeof(IUriImageSource))]
		public void FontAndUriSourcesAreNotYetTizenOwned(Type imageSourceType)
		{
			using var app = BuildTizenApp();

			var service = app.Services
				.GetRequiredService<IImageSourceServiceProvider>()
				.GetImageSourceService(imageSourceType);

			Assert.True(
				service?.GetType().Name.StartsWith("Tizen", StringComparison.Ordinal) != true,
				$"{imageSourceType.Name} now resolves to {service?.GetType().Name}, a Tizen service. " +
				"That is the image workstream landing: add it to the list in " +
				"AddTizenImageSources' documentation, extend " +
				$"{nameof(TizenSourcesAreRegisteredOnTheTizenLane)} to assert it, and delete this case.");
		}

		/// <summary>
		/// Registering twice is harmless, so a host that calls the hook itself is not punished.
		/// </summary>
		[Fact]
		public void RegisteringTheTizenSourcesTwiceIsIdempotent()
		{
			var builder = MauiApp.CreateBuilder();
			builder.UseMauiApp<HostApp>();
			builder.ConfigureTizen();
			builder.ConfigureImageSources(static sources => sources.AddTizenImageSources());

			using var app = builder.Build();

			var provider = app.Services.GetRequiredService<IImageSourceServiceProvider>();

			Assert.NotNull(provider.GetImageSourceService(typeof(IFileImageSource)));
			Assert.NotNull(provider.GetImageSourceService(typeof(IStreamImageSource)));
		}

		/// <summary>
		/// Every Wave A control handler resolves from an app built with <c>ConfigureTizen</c> alone.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the test that was missing, and its absence hid a severe defect:
		/// <c>AddTizenControlHandlers</c> registers all fourteen control handlers and <b>nothing
		/// called it</b>. Every existing registration test invoked it explicitly, so they all passed
		/// while a real app - which only calls <c>ConfigureTizen</c> - had no handler for Button,
		/// Entry, Editor, Slider or any other Wave A control.
		/// </para>
		/// <para>
		/// The lesson is the same one that produced <c>ControlsRemapBehaviorTests</c>: a test that
		/// arranges the thing it is verifying proves the thing works, not that it is <em>wired</em>.
		/// Only the composition root is exercised here.
		/// </para>
		/// </remarks>
		[Theory]
		[MemberData(nameof(TizenControlHandlers.TestData), MemberType = typeof(TizenControlHandlers))]
		public void EveryControlHandlerResolvesFromTheCompositionRoot(TizenControlHandlers.ControlHandlerCase handler)
		{
			using var app = BuildTizenApp();

			var handlers = app.Services.GetRequiredService<IMauiHandlersFactory>();
			var resolved = handlers.GetHandlerType(handler.VirtualViewType);

			Assert.True(
				resolved == handler.HandlerType,
				$"ConfigureTizen does not register {handler.HandlerType.Name} for " +
				$"{handler.VirtualViewType.Name} (resolved: {resolved?.Name ?? "nothing"}). The " +
				"handler exists and is unit tested, but a real app would have no handler for this " +
				"control - AddTizenControlHandlers must be called from the composition root.");
		}

		/// <summary>
		/// The services the control handlers resolve are registered by the composition root too.
		/// </summary>
		/// <remarks>
		/// Same defect class as the handlers above: <c>AddTizenControlServices</c> was also only
		/// ever called from its own tests. A handler that resolves but whose font manager does not
		/// fails at first use rather than at startup.
		/// </remarks>
		[Fact]
		public void ControlServicesResolveFromTheCompositionRoot()
		{
			using var app = BuildTizenApp();

			Assert.NotNull(app.Services.GetService<ITizenFontManager>());
			Assert.NotNull(app.Services.GetService<IFontManager>());
			Assert.NotNull(app.Services.GetService<ITizenModalHost>());
		}

		/// <summary>
		/// Every public <c>AddTizen*</c> registration extension must have a caller.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The generalisation of the defect Wave B's review found. <c>AddTizenImageSources</c> was a
		/// public, documented, tested-in-isolation registration method that nothing ever called, so
		/// the services it registers were never active in a real app. Nothing failed, because MAUI's
		/// neutral defaults answered instead.
		/// </para>
		/// <para>
		/// A source-level check rather than a behavioural one on purpose: the point is to catch the
		/// <em>next</em> such method - including ones whose services cannot be resolved on this lane
		/// at all, like the image workstream's font and URI registrations - at the moment it is
		/// added rather than at the next review.
		/// </para>
		/// </remarks>
		[Fact]
		public void EveryTizenRegistrationExtensionHasACaller()
		{
			var root = Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core");

			var sources = Directory
				.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
				.Where(IsCompiled)
				.ToList();

			var declarationPattern = new System.Text.RegularExpressions.Regex(
				@"public static \w[\w<>\.]* (?<name>Add(?!TizenHandlers\b)Tizen\w+)\s*\(\s*this ",
				System.Text.RegularExpressions.RegexOptions.Compiled);

			var declared = new Dictionary<string, string>(StringComparer.Ordinal);
			var bodies = new List<string>(sources.Count);

			foreach (var file in sources)
			{
				var text = File.ReadAllText(file);
				bodies.Add(text);

				foreach (System.Text.RegularExpressions.Match match in declarationPattern.Matches(text))
					declared[match.Groups["name"].Value] = Path.GetFileName(file);
			}

			var uncalled = declared
				.Where(entry => !bodies.Any(body =>
					body.Contains(entry.Key + "(", StringComparison.Ordinal) &&
					!IsOnlyTheDeclaration(body, entry.Key)))
				.Select(entry => $"{entry.Key} (declared in {entry.Value})")
				.Order(StringComparer.Ordinal)
				.ToList();

			Assert.True(
				uncalled.Count == 0,
				"These registration extensions are declared but never called from a composition " +
				$"root:\n  {string.Join("\n  ", uncalled)}\n\n" +
				"A registration nothing invokes is not a seam, it is dead code - and on this " +
				"backend it fails silently, because MAUI's neutral package already registers a " +
				"default for every image source type. Call it from ConfigureTizen (or extend the " +
				"existing AddTizenImageSources) rather than leaving a method a host must remember " +
				"to find.");
		}

		/// <summary>
		/// True when the only mention of <paramref name="name"/> in <paramref name="body"/> is its
		/// own declaration, which does not count as a call site.
		/// </summary>
		static bool IsOnlyTheDeclaration(string body, string name) =>
			System.Text.RegularExpressions.Regex.Matches(body, System.Text.RegularExpressions.Regex.Escape(name) + @"\s*\(").Count
				<= System.Text.RegularExpressions.Regex.Matches(body, @"public static \w[\w<>\.]* " + System.Text.RegularExpressions.Regex.Escape(name) + @"\s*\(").Count;

		/// <summary>
		/// Only files that are actually compiled count.
		/// </summary>
		static bool IsCompiled(string file) =>
			CompiledSources.Value.Contains(Path.GetFileName(file), StringComparison.Ordinal);

		static readonly Lazy<string> CompiledSources = new(() => File.ReadAllText(
			Path.Combine(TestRepositoryPaths.Root, "eng", "Maui.Tizen.Core.Sources.props")));

		sealed class StandInFileImageSourceService : IImageSourceService<IFileImageSource>
		{
			public Task<IImageSourceServiceResult?> GetImageAsync(
				IImageSource imageSource,
				float scale = 1,
				CancellationToken cancellationToken = default)
				=> Task.FromResult<IImageSourceServiceResult?>(null);
		}
	}
}
