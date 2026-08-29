// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// The platform view for <c>Picker</c>, <c>DatePicker</c> and <c>TimePicker</c>.
	/// </summary>
	/// <remarks>
	/// Tizen has no drop-down or combo control, so all three are presented as a read-only entry
	/// with a Material-style underline that opens a modal dialog when activated.
	/// </remarks>
	public class TizenPickerView : Entry
	{
		static readonly NColor DefaultUnderlineColor = NColor.DarkGray;
		static readonly NColor DisabledUnderlineColor = NColor.LightGray;

		readonly NView _underline;

		public TizenPickerView()
		{
			_underline = new NView
			{
				BackgroundColor = DefaultUnderlineColor,
				SizeHeight = 1d.ToScaledPixel(),
				WidthResizePolicy = ResizePolicyType.FillToParent,
				ParentOrigin = Position.ParentOriginBottomLeft
			};

			// The value is chosen from a dialog, never typed, but the control must still be
			// focusable so it can be reached with the remote/keyboard.
			IsReadOnly = true;
			Focusable = true;
			FocusableInTouch = true;
			VerticalAlignment = VerticalAlignment.Center;

			Add(_underline);
		}

		protected override void OnEnabled(bool enabled)
		{
			base.OnEnabled(enabled);
			_underline.BackgroundColor = enabled ? DefaultUnderlineColor : DisabledUnderlineColor;
		}
	}
}
