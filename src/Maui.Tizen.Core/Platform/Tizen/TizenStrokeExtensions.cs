// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/StrokeExtensions.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using NView = Tizen.NUI.BaseComponents.View;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;

namespace Microsoft.Maui.Platforms.Tizen
{
	public static class TizenStrokeExtensions
	{
		public static void UpdateStrokeShape(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStroke(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeThickness(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeDashPattern(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeDashOffset(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeMiterLimit(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeLineCap(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		public static void UpdateStrokeLineJoin(this NView platformView, IBorderStroke border)
		{
			var wrapperView = platformView.GetParent() as TizenWrapperView;
			if (wrapperView == null)
				return;

			wrapperView.UpdateMauiDrawable(border);
		}

		internal static void UpdateMauiDrawable(this TizenWrapperView wrapperView, IBorderStroke border)
		{
			bool hasBorder = border.Shape != null && border.Stroke != null;
			if (!hasBorder)
				return;

			wrapperView.UpdateBorder(border);
		}
	}
}
