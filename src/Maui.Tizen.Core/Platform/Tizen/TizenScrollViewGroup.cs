// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiScrollView.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using Tizen.UIExtensions.Common;
using TScrollView = Tizen.UIExtensions.NUI.ScrollView;
using TSize = Tizen.UIExtensions.Common.Size;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenScrollViewGroup : TScrollView, IMeasurable
	{
		IScrollView _virtualView;

		public TizenScrollViewGroup(IScrollView virtualView)
		{
			_virtualView = virtualView;
		}

		internal void Rebind(IScrollView virtualView) => _virtualView = virtualView;

		public TSize Measure(double availableWidth, double availableHeight)
		{
			return _virtualView.CrossPlatformMeasure(availableWidth.ToScaledDP(), availableHeight.ToScaledDP()).ToPixel();
		}
	}
}
