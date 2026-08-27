// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Expiry tests: these fail when an upstream gap is closed, so the workaround gets removed.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A workaround for a missing upstream API has a failure mode that ordinary tests cannot
	/// catch: it keeps working after the API ships, so nobody notices, and the backend carries a
	/// worse implementation indefinitely. These assert the gap <em>still exists</em> and fail
	/// loudly the moment it does not.
	/// </para>
	/// <para>
	/// A failure here is good news. It means the upstream fix landed and the corresponding
	/// workaround can be deleted; the message says exactly what to do.
	/// </para>
	/// </remarks>
	public class UpstreamGapExpiryTests
	{
		/// <summary>
		/// Fails once MAUI exposes a public contract for image-source background paints.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>The gap.</b> MAUI models an image background with <c>Microsoft.Maui.ImageSourcePaint</c>,
		/// which is <c>internal</c>. An out-of-repo backend therefore cannot detect that a view's
		/// <see cref="IView.Background"/> is an image at all: the paint flattens through
		/// <c>Paint.ToColor()</c>, so the image simply never renders. The Tizen code to apply one
		/// exists and works - it just cannot be reached from the background mapping.
		/// </para>
		/// <para>
		/// <b>The rule this encodes.</b> The gap is worked around by falling through, and
		/// deliberately <em>not</em> by reflecting over the internal type or matching it by name.
		/// Private reflection would bind this backend to MAUI's internals and break silently on any
		/// servicing update.
		/// </para>
		/// <para>
		/// <b>The fix being tracked.</b> dotnet/maui#37864 adds a public read-only
		/// <c>IImageSourcePaint</c>. Once a package contains it, the adoption is an image-first
		/// pattern match on <c>IImageSourcePaint.ImageSource</c> in the background mapping - no
		/// reflection, no internal types - routed to the existing
		/// <c>UpdateBackgroundImageSourceAsync</c>.
		/// </para>
		/// </remarks>
		[Fact]
		public void ImageSourcePaintIsStillInternal()
		{
			var assembly = typeof(IView).Assembly;

			var publicContract =
				assembly.GetType("Microsoft.Maui.IImageSourcePaint")
				?? assembly.GetType("Microsoft.Maui.Graphics.IImageSourcePaint");

			// Reported in the failure message so the adopter learns in one run whether the shipped
			// shape still matches the planned pattern match, rather than discovering it mid-edit.
			var imageSourceProperty = publicContract?.GetProperty("ImageSource");
			var shape = publicContract is null
				? string.Empty
				: imageSourceProperty is null
					? "\n\nNOTE: the shipped interface has NO 'ImageSource' property, so the planned " +
					  "adoption below does not apply as written - re-read the final upstream shape first."
					: $"\n\nShipped shape: {imageSourceProperty.PropertyType.Name} ImageSource " +
					  $"{{ {(imageSourceProperty.CanRead ? "get; " : "")}{(imageSourceProperty.CanWrite ? "set; " : "")}}} " +
					  "- matches the planned consumption-only adoption.";

			Assert.True(
				publicContract is null,
				$"""
				MAUI now exposes '{publicContract?.FullName}'. This is the upstream fix from
				dotnet/maui#37864 landing, and the image-background workaround should now be removed.

				Adopt it at the ADOPTION SEAM comment in TizenPlatformExtensions.UpdateBackground, by
				pattern matching image-first:

				    if (paint is IImageSourcePaint imagePaint)
				        // route imagePaint.ImageSource through UpdateBackgroundImageSourceAsync
				    else if (paint is SolidPaint solid) ...

				Match the image case BEFORE the solid/ToColor fallback, or an image paint keeps
				flattening to a colour exactly as it does today. Use the interface directly - no
				reflection and no internal types. Then delete this test and update the
				"MAUI extensibility blockers" section of docs/wave-a-handlers.md.{shape}
				""");

			// The gap is only real while the concrete type is genuinely inaccessible. If this
			// stops being true some other way, the workaround's justification is gone too.
			var internalPaint = assembly.GetType("Microsoft.Maui.ImageSourcePaint");

			Assert.True(
				internalPaint is null || !internalPaint.IsVisible,
				"Microsoft.Maui.ImageSourcePaint is now publicly visible, so an image background " +
				"can be detected without the workaround. See the guidance above.");
		}

		/// <summary>
		/// The backend must never reach an internal MAUI type by name.
		/// </summary>
		/// <remarks>
		/// The workaround above is only acceptable because it degrades honestly rather than
		/// reflecting. This keeps that promise: a future edit that "fixes" image backgrounds by
		/// looking the internal type up by name fails here.
		/// </remarks>
		[Fact]
		public void BackendDoesNotReachImageSourcePaintByName()
		{
			var root = System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core");

			var offenders = System.IO.Directory
				.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories)
				.Where(file => IsCompiled(file))
				.Where(file =>
				{
					var text = System.IO.File.ReadAllText(file);

					// A string literal naming the internal type can only be a reflection lookup.
					return text.Contains("\"Microsoft.Maui.ImageSourcePaint\"", StringComparison.Ordinal)
						|| text.Contains("GetType(\"Microsoft.Maui.ImageSourcePaint", StringComparison.Ordinal);
				})
				.Select(System.IO.Path.GetFileName)
				.ToList();

			Assert.True(
				offenders.Count == 0,
				$"These files look up MAUI's internal ImageSourcePaint by name: " +
				$"{string.Join(", ", offenders)}. Reflecting over MAUI internals binds this backend " +
				"to implementation detail and breaks silently on servicing updates. Wait for the " +
				"public IImageSourcePaint contract instead - see ImageSourcePaintIsStillInternal.");
		}

		/// <summary>
		/// The backend must never implement <c>IImageSourcePaint</c> itself.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Upstream states the contract is <b>consumption only</b>: external implementation is
		/// unsupported and members may be added to it, so a custom <c>Paint : IImageSourcePaint</c>
		/// here would compile today and break on a servicing update that adds a member.
		/// </para>
		/// <para>
		/// This is a source-level check because it has to keep working <em>after</em> the contract
		/// ships - at which point the type resolves and the temptation to implement it becomes
		/// real. The other tests in this class expire; this one does not.
		/// </para>
		/// </remarks>
		[Fact]
		public void BackendDoesNotImplementImageSourcePaint()
		{
			var root = System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core");

			var offenders = System.IO.Directory
				.EnumerateFiles(root, "*.cs", System.IO.SearchOption.AllDirectories)
				.Where(IsCompiled)
				.Where(file =>
				{
					var text = System.IO.File.ReadAllText(file);

					// A declaration, not a pattern match: `: IImageSourcePaint` in a type header.
					return System.Text.RegularExpressions.Regex.IsMatch(
						text,
						@"(class|struct|record)\s+\w+\s*(<[^>]*>)?\s*:[^\{\r\n]*\bIImageSourcePaint\b");
				})
				.Select(System.IO.Path.GetFileName)
				.ToList();

			Assert.True(
				offenders.Count == 0,
				$"These files implement IImageSourcePaint: {string.Join(", ", offenders)}. Upstream " +
				"supports the contract for consumption only - it may gain members, which would break " +
				"an external implementation. Pattern match MAUI's built-in paint instead.");
		}

		/// <summary>
		/// Wave A background mappings must pass the view, not just its paint.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Resolving an image-source background needs an <c>IImageSourceServiceProvider</c>, which is
		/// reached through <c>view.Handler</c>. A mapping that calls
		/// <c>UpdateBackground(view.Background)</c> has already thrown that away, so it can never
		/// render an image background however the extension is later fixed - and the failure is
		/// silent, because the paint still flattens to a colour.
		/// </para>
		/// <para>
		/// The distinction is invisible today (both overloads behave identically while
		/// <c>ImageSourcePaint</c> is internal), which is exactly why it needs a test rather than a
		/// comment: nothing else would notice the regression until dotnet/maui#37864 landed and the
		/// fix mysteriously failed to work for these controls.
		/// </para>
		/// </remarks>
		[Fact]
		public void BackgroundMappingsKeepTheViewInScope()
		{
			var handlers = System.IO.Path.Combine(TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Handlers");

			var offenders = new List<string>();

			foreach (var file in System.IO.Directory.EnumerateFiles(handlers, "Tizen*Handler.cs"))
			{
				if (!IsCompiled(file))
					continue;

				foreach (var line in System.IO.File.ReadAllLines(file))
				{
					// UpdateBackground(<something>.Background) - the paint-only overload.
					if (System.Text.RegularExpressions.Regex.IsMatch(line, @"UpdateBackground\(\s*\w+\.Background\s*[,)]"))
						offenders.Add($"{System.IO.Path.GetFileName(file)}: {line.Trim()}");
				}
			}

			Assert.True(
				offenders.Count == 0,
				"These background mappings pass only the paint, discarding the view that an " +
				"image-source background needs to resolve its service provider:\n  " +
				string.Join("\n  ", offenders) +
				"\n\nPass the view instead - UpdateBackground(view, clearWhenNull: <same as before>) - " +
				"so the image-first pattern match has what it needs when dotnet/maui#37864 is adopted.");
		}

		/// <summary>
		/// Only files that are actually compiled count.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Most of <c>src/Maui.Tizen.Core</c> is still the raw dotnet/maui import, which is not in
		/// the compile list. Those files legitimately reference the internal type - they were
		/// compiled inside MAUI - and flagging them would make this test permanently red for a
		/// reason nobody can act on.
		/// </para>
		/// <para>
		/// The manifest is read once and cached. It used to be re-read per file, which is
		/// quadratic against an imported tree of several thousand sources and took this suite from
		/// three seconds to over five minutes - slow enough that people stop running it locally,
		/// which costs more than the tests are worth.
		/// </para>
		/// </remarks>
		static readonly Lazy<string> CompiledSources = new(() => System.IO.File.ReadAllText(
			System.IO.Path.Combine(TestRepositoryPaths.Root, "eng", "Maui.Tizen.Core.Sources.props")));

		static bool IsCompiled(string file) =>
			CompiledSources.Value.Contains(System.IO.Path.GetFileName(file), StringComparison.Ordinal);
	}
}
