// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Regressions for restoring a control's captured native defaults.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Both mappings previously handled "unset" by <em>skipping the write</em>. That is only
	/// correct before anything has been applied. Once a value has been set, skipping leaves the
	/// old one in place, so clearing a property silently keeps whatever it was last given - the
	/// control can never return to its themed appearance.
	/// </para>
	/// <para>
	/// The assertions are at the source level because both properties are NUI-typed and only exist
	/// in the Tizen compilation. What is being pinned is the shape of the fix: a default captured
	/// at construction, and an unset value restoring it rather than returning early.
	/// </para>
	/// </remarks>
	public class NativeDefaultRestorationTests
	{
		static string PlatformSource(string fileName) =>
			File.ReadAllText(Path.Combine(
				TestRepositoryPaths.Root, "src", "Maui.Tizen.Core", "Platform", "Tizen", fileName));

		/// <summary>
		/// The button captures its corner radius before anything overwrites it.
		/// </summary>
		[Fact]
		public void ButtonViewCapturesItsDefaultCornerRadius()
		{
			var source = PlatformSource("TizenControlViews.cs");

			Assert.Contains("DefaultCornerRadius", source, StringComparison.Ordinal);

			// Capturing on demand would read whatever had already been applied, so it has to
			// happen in the constructor.
			Assert.Contains("public TizenButtonView()", source, StringComparison.Ordinal);
		}

		/// <summary>
		/// An unset corner radius restores the default rather than returning early.
		/// </summary>
		[Fact]
		public void UnsetCornerRadiusRestoresTheCapturedDefault()
		{
			var source = PlatformSource("TizenButtonExtensions.cs");

			Assert.Contains("DefaultCornerRadius", source, StringComparison.Ordinal);

			// The defect was `if (button.CornerRadius != -1)` with no else: clearing the radius
			// left the previous one applied.
			Assert.DoesNotContain("if (button.CornerRadius != -1)", source, StringComparison.Ordinal);
		}

		/// <summary>
		/// The check box captures its themed check colour.
		/// </summary>
		[Fact]
		public void CheckBoxViewCapturesItsDefaultColor()
		{
			var source = PlatformSource("TizenControlViews.cs");

			Assert.Contains("DefaultColor", source, StringComparison.Ordinal);
			Assert.Contains("public TizenCheckBoxView()", source, StringComparison.Ordinal);
		}

		/// <summary>
		/// A null or non-solid foreground restores the themed colour.
		/// </summary>
		/// <remarks>
		/// The Skia drawable takes a single colour, so a gradient or image foreground cannot be
		/// honoured. Leaving the previous colour would make switching from a solid paint to a
		/// gradient appear to do nothing at all.
		/// </remarks>
		[Fact]
		public void UnsupportedForegroundRestoresTheCapturedDefault()
		{
			var source = PlatformSource("TizenCheckBoxExtensions.cs");

			Assert.Contains("DefaultColor", source, StringComparison.Ordinal);

			// The defect was `if (check.Foreground is SolidPaint solid)` with no else.
			Assert.DoesNotContain("if (check.Foreground is SolidPaint solid)", source, StringComparison.Ordinal);
		}
	}
}
