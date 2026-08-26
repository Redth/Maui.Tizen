// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/ImageExtensions.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using Tizen.UIExtensions.NUI;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static class TizenImageViewExtensions
	{
		public static void Clear(this Image platformImage)
		{
			platformImage.ResourceUrl = null;
		}

		public static void UpdateAspect(this Image platformImage, IImage image)
		{
			platformImage.Aspect = image.Aspect.ToPlatform();
		}

		public static void UpdateIsAnimationPlaying(this Image platformImage, IImageSourcePart image)
		{
			platformImage.SetIsAnimationPlaying(image.IsAnimationPlaying);
		}
	}
}
