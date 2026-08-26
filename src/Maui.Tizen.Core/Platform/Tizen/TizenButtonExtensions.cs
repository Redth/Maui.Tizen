// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IButton</c> property mappings.
	/// </summary>
	public static class TizenButtonExtensions
	{
		public static void UpdateText(this Button platformButton, IText button) =>
			platformButton.Text = button.Text ?? string.Empty;

		public static void UpdateTextColor(this Button platformButton, ITextStyle button) =>
			platformButton.TextColor = button.TextColor.ToTizenCommonColor();

		public static void UpdateCharacterSpacing(this Button platformButton, ITextStyle button) =>
			platformButton.TextLabel.CharacterSpacing = button.CharacterSpacing.ToScaledPixel();

		public static void UpdateStrokeColor(this Button platformButton, IButtonStroke button) =>
			platformButton.BorderlineColor = button.StrokeColor.ToTizenNativeColor() ?? NColor.Transparent;

		public static void UpdateStrokeThickness(this Button platformButton, IButtonStroke button) =>
			platformButton.BorderlineWidth = button.StrokeThickness.ToScaledPixel();

		/// <remarks>
		/// MAUI uses -1 to mean "unset". Writing that through would give NUI a negative radius,
		/// so the default corner treatment is left untouched instead.
		/// </remarks>
		public static void UpdateCornerRadius(this Button platformButton, IButtonStroke button)
		{
			if (button.CornerRadius != -1)
				platformButton.CornerRadius = ((double)button.CornerRadius).ToScaledPixel();
		}

		/// <summary>
		/// Applies <see cref="IPadding.Padding"/> to the button's content insets.
		/// </summary>
		/// <remarks>
		/// Upstream leaves this unimplemented. NUI's <c>Padding</c> takes unsigned extents, so a
		/// negative MAUI padding (which is legal) is clamped to zero rather than wrapping around
		/// to an enormous inset.
		/// </remarks>
		public static void UpdatePadding(this Button platformButton, IPadding button)
		{
			var padding = button.Padding;

			platformButton.Padding = new global::Tizen.NUI.Extents(
				ToExtent(padding.Left),
				ToExtent(padding.Right),
				ToExtent(padding.Top),
				ToExtent(padding.Bottom));

			static ushort ToExtent(double value) =>
				value <= 0 ? (ushort)0 : (ushort)value.ToScaledPixel();
		}

		/// <summary>
		/// Applies a resolved image to the button's icon.
		/// </summary>
		public static void UpdateImageSource(this Button platformButton, TizenImageSource? image)
		{
			if (platformButton.Icon is { } icon)
				icon.ResourceUrl = image?.ResourceUrl ?? string.Empty;
		}
	}
}
