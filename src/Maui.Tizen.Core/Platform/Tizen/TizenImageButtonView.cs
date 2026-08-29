// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/maui src/Core/src/Platform/Tizen/MauiImageButton.cs.
// Renamed and renamespaced: the raw import declares public types in Microsoft.Maui.Platform,
// which collides by full name with the neutral Microsoft.Maui.Core assembly. The raw file is
// retained beside this one for provenance but is never compiled.
using System;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using TImage = Tizen.UIExtensions.NUI.Image;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen.Handlers;
using Tizen.UIExtensions.Common;
using Color = Microsoft.Maui.Graphics.Color;

namespace Microsoft.Maui.Platforms.Tizen
{
	public class TizenImageButtonView : TImage
	{
		bool _isPressed;

		public event EventHandler? Pressed;
		public event EventHandler? Released;
		public event EventHandler? Clicked;

		public TizenImageButtonView()
		{
			TouchEvent += OnTouched;
			KeyEvent += OnKeyEvent;
			Border = Border = new Rectangle(0, 0, 0, 0);
		}

		public void UpdateStrokeColor(IButtonStroke button)
		{
			BorderlineColor = button.StrokeColor.ToTizenNativeColor() ?? NColor.Transparent;
		}

		public void UpdateStrokeThickness(IButtonStroke button)
		{
			BorderlineWidth = button.StrokeThickness.ToScaledPixel();
		}

		public void UpdateCornerRadius(IButtonStroke button)
		{
			if (button.CornerRadius != -1)
				CornerRadius = ((double)button.CornerRadius).ToScaledPixel();
		}

		bool OnTouched(object source, TouchEventArgs e)
		{
			var state = e.Touch.GetState(0);

			if (state == PointStateType.Down)
			{
				_isPressed = true;
				Pressed?.Invoke(this, EventArgs.Empty);
				return true;
			}
			else if (state == PointStateType.Up)
			{
				Released?.Invoke(this, EventArgs.Empty);
				if (_isPressed && this.IsInside(e.Touch.GetLocalPosition(0)))
				{
					Clicked?.Invoke(this, EventArgs.Empty);
				}
				_isPressed = false;
				return true;
			}
			return false;
		}

		bool OnKeyEvent(object source, KeyEventArgs e)
		{
			if (e.Key.IsAcceptKeyEvent())
			{
				Clicked?.Invoke(this, EventArgs.Empty);
				return true;
			}
			return false;
		}
	}
}