// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Tizen.NUI;
using Tizen.UIExtensions.NUI;
using NColor = Tizen.NUI.Color;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>The platform view for <c>Button</c>.</summary>
	/// <remarks>
	/// Each Wave A control gets a named <c>Tizen*View</c> type, matching the convention
	/// <see cref="TizenLabelView"/> established. The handler's generic parameter then names a
	/// type this repository owns, which is what lets the same handler source compile both
	/// against real TizenFX and against the inert host stand-ins in the unit test project.
	/// </remarks>
	public class TizenButtonView : Button
	{
	}

	/// <summary>The platform view for <c>Entry</c>.</summary>
	public class TizenEntryView : Entry
	{
	}

	/// <summary>The platform view for <c>Editor</c>.</summary>
	public class TizenEditorView : Editor
	{
	}

	/// <summary>The platform view for <c>CheckBox</c>.</summary>
	public class TizenCheckBoxView : global::Tizen.UIExtensions.NUI.GraphicsView.CheckBox
	{
	}

	/// <summary>The platform view for <c>Switch</c>.</summary>
	public class TizenSwitchView : global::Tizen.UIExtensions.NUI.GraphicsView.Switch
	{
	}

	/// <summary>The platform view for <c>ProgressBar</c>.</summary>
	public class TizenProgressBarView : global::Tizen.UIExtensions.NUI.GraphicsView.ProgressBar
	{
	}

	/// <summary>The platform view for <c>ActivityIndicator</c>.</summary>
	public class TizenActivityIndicatorView : global::Tizen.UIExtensions.NUI.GraphicsView.ActivityIndicator
	{
	}

	/// <summary>The platform view for <c>Slider</c>.</summary>
	public class TizenSliderView : global::Tizen.NUI.Components.Slider
	{
	}

	/// <summary>
	/// The platform view for <c>RadioButton</c>.
	/// </summary>
	/// <remarks>
	/// A radio button presents templated content rather than a native radio glyph, so it reuses
	/// the content group the core slice already provides.
	/// </remarks>
	public class TizenRadioButtonView : TizenContentViewGroup
	{
		/// <param name="view">The cross-platform view being presented.</param>
		public TizenRadioButtonView(IView? view)
			: base(view)
		{
		}
	}
}
