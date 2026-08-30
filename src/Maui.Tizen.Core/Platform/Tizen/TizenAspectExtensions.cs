// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/AspectExtensions.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using TAspect = Tizen.UIExtensions.Common.Aspect;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static class TizenAspectExtensions
	{
		public static TAspect ToPlatform(this Aspect aspect) =>
			aspect switch
			{
				Aspect.AspectFit => TAspect.AspectFit,
				Aspect.AspectFill => TAspect.AspectFill,
				Aspect.Fill => TAspect.Fill,
				_ => TAspect.AspectFit,
			};
	}
}