// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/PaintExtensions.Tizen.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using TColor = Tizen.UIExtensions.Common.Color;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using Color = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static partial class TizenPaintExtensions
	{
		public static TColor ToPlatform(this Paint paint)
		{
			var color = paint.ToColor();
			return color != null ? color.ToTizenCommonColor() : TColor.Default;
		}

		public static TizenDrawable? ToDrawable(this Paint paint)
		{
			if (paint is SolidPaint solidPaint)
				return solidPaint.CreateDrawable();

			if (paint is LinearGradientPaint linearGradientPaint)
				return linearGradientPaint.CreateDrawable();

			if (paint is RadialGradientPaint radialGradientPaint)
				return radialGradientPaint.CreateDrawable();

			if (paint is ImagePaint imagePaint)
				return imagePaint.CreateDrawable();

			if (paint is PatternPaint patternPaint)
				return patternPaint.CreateDrawable();

			return null;
		}

		public static TizenDrawable? CreateDrawable(this SolidPaint solidPaint)
		{
			return new TizenDrawable
			{
				Background = solidPaint
			};
		}

		public static TizenDrawable? CreateDrawable(this LinearGradientPaint linearGradientPaint)
		{
			if (!linearGradientPaint.IsValid())
				return null;

			return new TizenDrawable
			{
				Background = linearGradientPaint
			};
		}

		public static TizenDrawable? CreateDrawable(this RadialGradientPaint radialGradientPaint)
		{
			if (!radialGradientPaint.IsValid())
				return null;

			return new TizenDrawable
			{
				Background = radialGradientPaint
			};
		}

		public static TizenDrawable? CreateDrawable(this ImagePaint imagePaint)
		{
			return new TizenDrawable
			{
				Background = imagePaint
			};
		}

		public static TizenDrawable? CreateDrawable(this PatternPaint patternPaint)
		{
			return new TizenDrawable
			{
				Background = patternPaint
			};
		}

		static bool IsValid(this GradientPaint? gradientPaint) =>
			gradientPaint?.GradientStops?.Length > 0;
	}
}