// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.NUI;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IRadioButton"/>.
	/// </summary>
	/// <remarks>
	/// Tizen has no radio control that can host arbitrary content, so the platform view is a
	/// content group that presents whatever the radio button's template produced. The checked
	/// state is toggled from key input.
	/// </remarks>
	public class TizenRadioButtonHandler : TizenViewHandler<IRadioButton, TizenRadioButtonView>
	{
#if TIZEN
		IElementHandler? _contentHandler;
#endif

		/// <summary>The complete property mapper for <see cref="IRadioButton"/>.</summary>
		public static readonly IPropertyMapper<IRadioButton, TizenRadioButtonHandler> Mapper =
			new PropertyMapper<IRadioButton, TizenRadioButtonHandler>(ViewHandler.ViewMapper)
			{
				[nameof(IContentView.Content)] = MapContent,
				[nameof(IRadioButton.IsChecked)] = MapIsChecked,
				[nameof(ITextStyle.TextColor)] = MapTextColor,
				[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(ITextStyle.Font)] = MapFont,
				[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
				[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
				[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			};

		/// <summary>The complete command mapper for <see cref="IRadioButton"/>.</summary>
		public static readonly CommandMapper<IRadioButton, TizenRadioButtonHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenRadioButtonHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenRadioButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenRadioButtonView CreatePlatformView()
		{
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} must be set to create a {nameof(TizenRadioButtonView)}.");

			return new TizenRadioButtonView(VirtualView)
			{
#if TIZEN
				Focusable = true,
#endif
				CrossPlatformMeasure = VirtualView.CrossPlatformMeasure,
				CrossPlatformArrange = VirtualView.CrossPlatformArrange
			};
		}

		protected override void ConnectHandler(TizenRadioButtonView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.CrossPlatformMeasure = VirtualView.CrossPlatformMeasure;
			platformView.CrossPlatformArrange = VirtualView.CrossPlatformArrange;
			platformView.KeyEvent += OnKeyEvent;
#endif
		}

		protected override void DisconnectHandler(TizenRadioButtonView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
				platformView.KeyEvent -= OnKeyEvent;

#if TIZEN
			// The content's handler was created by this handler, so this handler disposes it.
			(_contentHandler as IDisposable)?.Dispose();
			_contentHandler = null;
#endif
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapContent(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
#if TIZEN
			handler.UpdateContent();
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// The checked state is expressed entirely by the radio button's own content template,
		/// which MAUI re-renders when <see cref="IRadioButton.IsChecked"/> changes. There is no
		/// separate native indicator for this handler to update. Deliberate no-op.
		/// </remarks>
		public static void MapIsChecked(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// Text styling applies to the templated content's own label, which has its own handler
		/// and mappings. Deliberate no-op.
		/// </remarks>
		public static void MapTextColor(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>See <see cref="MapTextColor"/>.</remarks>
		public static void MapCharacterSpacing(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>See <see cref="MapTextColor"/>.</remarks>
		public static void MapFont(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
		}

		/// <summary>Applies the border colour to the content group.</summary>
		public static void MapStrokeColor(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
#if TIZEN
			if (handler.PlatformView is { } platformView)
				platformView.BorderlineColor = radioButton.StrokeColor.ToTizenNativeColor() ?? global::Tizen.NUI.Color.Transparent;
#endif
		}

		/// <summary>Applies the border thickness to the content group.</summary>
		public static void MapStrokeThickness(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
#if TIZEN
			if (handler.PlatformView is { } platformView)
				platformView.BorderlineWidth = radioButton.StrokeThickness.ToScaledPixel();
#endif
		}

		/// <summary>Applies the corner radius to the content group.</summary>
		/// <remarks>MAUI uses -1 for "unset"; see <see cref="TizenButtonExtensions.UpdateCornerRadius"/>.</remarks>
		public static void MapCornerRadius(TizenRadioButtonHandler handler, IRadioButton radioButton)
		{
#if TIZEN
			if (handler.PlatformView is { } platformView && radioButton.CornerRadius != -1)
				platformView.CornerRadius = ((double)radioButton.CornerRadius).ToScaledPixel();
#endif
		}

#if TIZEN
		void UpdateContent()
		{
			_ = PlatformView ?? throw new InvalidOperationException($"{nameof(PlatformView)} should have been set by the base class.");
			_ = VirtualView ?? throw new InvalidOperationException($"{nameof(VirtualView)} should have been set by the base class.");
			_ = MauiContext ?? throw new InvalidOperationException($"{nameof(MauiContext)} should have been set by the base class.");

			PlatformView.Children.Clear();

			(_contentHandler as IDisposable)?.Dispose();
			_contentHandler = null;

			if (VirtualView.PresentedContent is not IView content)
				return;

			PlatformView.Children.Add(content.ToPlatformView(MauiContext));
			_contentHandler = content.Handler;
		}
#endif

#if TIZEN
		/// <remarks>
		/// A radio button is toggled on, never off, by activation: MAUI's group coordinator
		/// unchecks the previous member. Toggling here would let the user clear a group.
		/// </remarks>
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (VirtualView is null || !e.Key.IsAcceptKeyEvent())
				return false;

			if (!VirtualView.IsChecked)
				VirtualView.IsChecked = true;

			return true;
		}
#endif
	}
}
