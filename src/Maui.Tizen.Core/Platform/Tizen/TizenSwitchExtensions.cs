// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI.GraphicsView;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>ISwitch</c> property mappings.
	/// </summary>
	public static class TizenSwitchExtensions
	{
		public static void UpdateIsOn(this Switch platformSwitch, ISwitch view) =>
			platformSwitch.IsToggled = view.IsOn;

		/// <remarks>
		/// Tizen's switch drawable only exposes the "on" track colour; the off-state track is
		/// drawn from the theme and cannot be tinted.
		/// </remarks>
		public static void UpdateTrackColor(this Switch platformSwitch, ISwitch view) =>
			platformSwitch.OnColor = view.TrackColor.ToTizenCommonColor();

		public static void UpdateThumbColor(this Switch platformSwitch, ISwitch view) =>
			platformSwitch.ThumbColor = view.ThumbColor.ToTizenCommonColor();
	}
}
