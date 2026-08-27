// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/ScrollViewExtensions.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using Tizen.UIExtensions.NUI;
using TScrollBarVisibility = Tizen.UIExtensions.Common.ScrollBarVisibility;
using TScrollOrientation = Tizen.UIExtensions.Common.ScrollOrientation;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static class TizenScrollViewExtensions
	{
		public static void UpdateVerticalScrollBarVisibility(this ScrollView scrollView, ScrollBarVisibility scrollBarVisibility)
		{
			scrollView.VerticalScrollBarVisibility = scrollBarVisibility.ToPlatform();
		}

		public static void UpdateHorizontalScrollBarVisibility(this ScrollView scrollView, ScrollBarVisibility scrollBarVisibility)
		{
			scrollView.HorizontalScrollBarVisibility = scrollBarVisibility.ToPlatform();
		}

		public static void UpdateOrientation(this ScrollView scrollView, ScrollOrientation scrollOrientation)
		{
			scrollView.ScrollOrientation = scrollOrientation.ToNative();
		}

		public static TScrollOrientation ToNative(this ScrollOrientation scrollOrientation)
		{
			switch (scrollOrientation)
			{
				case ScrollOrientation.Horizontal:
					return TScrollOrientation.Horizontal;
				case ScrollOrientation.Vertical:
					return TScrollOrientation.Vertical;
				case ScrollOrientation.Neither:
					// Neither means "do not scroll". The imported code fell through to Both here, so
					// disabling scrolling actually enabled it on both axes.
					return TScrollOrientation.Neither;
				default:
					return TScrollOrientation.Both;
			}
		}

		public static TScrollBarVisibility ToPlatform(this ScrollBarVisibility visibility)
		{
			switch (visibility)
			{
				case ScrollBarVisibility.Default:
					return TScrollBarVisibility.Default;
				case ScrollBarVisibility.Always:
					return TScrollBarVisibility.Always;
				case ScrollBarVisibility.Never:
					return TScrollBarVisibility.Never;
				default:
					return TScrollBarVisibility.Default;
			}
		}
	}
}