// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using Tizen.UIExtensions.NUI.GraphicsView;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IProgress</c> property mappings.
	/// </summary>
	public static class TizenProgressBarExtensions
	{
		/// <remarks>
		/// Clamped to 0..1. The drawable scales the filled track by this value directly, so an
		/// out-of-range progress would paint outside the control's bounds.
		/// </remarks>
		public static void UpdateProgress(this ProgressBar platformProgressBar, IProgress progress) =>
			platformProgressBar.Progress = Math.Clamp(progress.Progress, 0d, 1d);

		public static void UpdateProgressColor(this ProgressBar platformProgressBar, IProgress progress) =>
			platformProgressBar.ProgressColor = progress.ProgressColor.ToTizenCommonColor();
	}
}
