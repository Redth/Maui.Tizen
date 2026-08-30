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
		/// <summary>
		/// The corner radius the control shipped with, captured before anything overrides it.
		/// </summary>
		/// <remarks>
		/// MAUI uses <c>-1</c> to mean "unset", and unset must restore the themed appearance
		/// rather than leave whatever radius was last applied. That is only possible if the
		/// original is captured before the first write, which is why it is read in the
		/// constructor rather than looked up on demand.
		/// </remarks>
		public global::Tizen.NUI.Vector4 DefaultCornerRadius { get; }

		/// <summary>Initializes a new instance of the <see cref="TizenButtonView"/> class.</summary>
		/// <remarks>
		/// The radius is copied, not aliased: <c>CornerRadius</c> returns a live
		/// <c>Vector4</c> that NUI mutates in place, so holding the reference would capture
		/// whatever the value later became rather than the original.
		/// </remarks>
		public TizenButtonView()
		{
			var radius = CornerRadius;
			DefaultCornerRadius = radius is null
				? new global::Tizen.NUI.Vector4(0, 0, 0, 0)
				: new global::Tizen.NUI.Vector4(radius.X, radius.Y, radius.Z, radius.W);
		}
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
		/// <summary>
		/// The check colour the control shipped with, captured before anything overrides it.
		/// </summary>
		/// <remarks>
		/// A null or non-solid <c>Foreground</c> means "no override", which has to restore the
		/// themed colour. Leaving the previous one would make clearing a foreground silently
		/// keep the colour it was last given.
		/// </remarks>
		public global::Tizen.UIExtensions.Common.Color DefaultColor { get; }

		public TizenCheckBoxView() => DefaultColor = Color;
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
