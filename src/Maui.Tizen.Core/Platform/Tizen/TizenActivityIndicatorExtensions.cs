// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Tizen.UIExtensions.NUI.GraphicsView;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IActivityIndicator</c> property mappings.
	/// </summary>
	public static class TizenActivityIndicatorExtensions
	{
		public static void UpdateIsRunning(this ActivityIndicator platformView, IActivityIndicator activityIndicator) =>
			platformView.IsRunning = activityIndicator.IsRunning;

		public static void UpdateColor(this ActivityIndicator platformView, IActivityIndicator activityIndicator) =>
			platformView.Color = activityIndicator.Color.ToTizenCommonColor();
	}
}
