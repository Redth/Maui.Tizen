// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/GraphicsExtensions.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using Microsoft.Maui.Graphics;
using Microsoft.Maui;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using Rect = Microsoft.Maui.Graphics.Rect;

namespace Microsoft.Maui.Platforms.Tizen
{
	internal static class TizenGraphicsDrawExtensions
	{
		public static Rect ExpandTo(this Rect geometry, Thickness shadowMargin)
		{
			var canvasGeometry = new Rect(
			geometry.X - shadowMargin.Left,
			geometry.Y - shadowMargin.Top,
			geometry.Width + shadowMargin.HorizontalThickness,
			geometry.Height + shadowMargin.VerticalThickness);

			return canvasGeometry;
		}

		public static Thickness GetShadowMargin(this IShadow shadow)
		{
			double left = 0;
			double top = 0;
			double right = 0;
			double bottom = 0;

			var offsetX = shadow == null ? 0 : shadow.Offset.X;
			var offsetY = shadow == null ? 0 : shadow.Offset.Y;
			var blurRadius = shadow == null ? 0 : ((double)shadow.Radius);
			var spreadSize = blurRadius * 3;
			var spreadLeft = offsetX - spreadSize;
			var spreadRight = offsetX + spreadSize;
			var spreadTop = offsetY - spreadSize;
			var spreadBottom = offsetY + spreadSize;
			if (left > spreadLeft)
				left = spreadLeft;
			if (top > spreadTop)
				top = spreadTop;
			if (right < spreadRight)
				right = spreadRight;
			if (bottom < spreadBottom)
				bottom = spreadBottom;

			return new Thickness(Math.Abs(left), Math.Abs(top), Math.Abs(right), Math.Abs(bottom));
		}
	}
}