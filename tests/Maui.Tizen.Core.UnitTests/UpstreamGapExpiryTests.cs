// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
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

			Assert.True(
				publicContract is null,
				$"""
				MAUI now exposes '{publicContract?.FullName}'. This is the upstream fix from
				dotnet/maui#37864 landing, and the image-background workaround should now be removed.

				Adopt it by pattern matching image-first in the background mapping:

				    if (paint is IImageSourcePaint imagePaint)
				        // route imagePaint.ImageSource through UpdateBackgroundImageSourceAsync
				    else if (paint is SolidPaint solid) ...

				Match the image case BEFORE the solid/ToColor fallback, or an image paint keeps
				flattening to a colour exactly as it does today. Use the interface directly - no
				reflection and no internal types. Then delete this test and update the
				"MAUI extensibility blockers" section of docs/wave-a-handlers.md.
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
		/// Only files that are actually compiled count.
		/// </summary>
		/// <remarks>
		/// Most of <c>src/Maui.Tizen.Core</c> is still the raw dotnet/maui import, which is not in
		/// the compile list. Those files legitimately reference the internal type - they were
		/// compiled inside MAUI - and flagging them would make this test permanently red for a
		/// reason nobody can act on.
		/// </remarks>
		static bool IsCompiled(string file)
		{
			var sources = System.IO.File.ReadAllText(System.IO.Path.Combine(
				TestRepositoryPaths.Root, "eng", "Maui.Tizen.Core.Sources.props"));

			return sources.Contains(System.IO.Path.GetFileName(file), StringComparison.Ordinal);
		}
	}
}
