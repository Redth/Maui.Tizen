// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiShapeView.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using Tizen.UIExtensions.NUI.GraphicsView;
using Tizen.UIExtensions.Common;
using TSize = Tizen.UIExtensions.Common.Size;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenShapeView : SkiaGraphicsView, IMeasurable
	{
		/// <remarks>
		/// Returns zero, as upstream does: the shape is drawn by the Skia surface and its desired size
		/// comes from the cross-platform layout pass, not from the native view.
		/// </remarks>
		TSize IMeasurable.Measure(double availableWidth, double availableHeight)
		{
			return new TSize(0, 0);
		}
	}
}