// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IButton"/>.
	/// </summary>
	public class TizenButtonHandler : TizenViewHandler<IButton, TizenButtonView>
	{
		/// <summary>The complete property mapper for <see cref="IButton"/>.</summary>
		/// <remarks>
		/// <c>Text</c>, <c>TextColor</c>, <c>Font</c> and <c>CharacterSpacing</c> come from
		/// <see cref="ITextButton"/> and <c>Source</c> from <see cref="IImageButton"/>; a button
		/// may implement either, both, or neither, so the mappings are defensive about the cast.
		/// </remarks>
		public static readonly IPropertyMapper<IButton, TizenButtonHandler> Mapper =
			new PropertyMapper<IButton, TizenButtonHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IText.Text)] = MapText,
				[nameof(ITextStyle.TextColor)] = MapTextColor,
				[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(ITextStyle.Font)] = MapFont,
				[nameof(IImage.Source)] = MapImageSource,
				[nameof(IPadding.Padding)] = MapPadding,
				[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
				[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
				[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
			};

		/// <summary>The complete command mapper for <see cref="IButton"/>.</summary>
		public static readonly CommandMapper<IButton, TizenButtonHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

#if TIZEN
		readonly TizenImageLoader<TizenImageSource> _iconLoader = new();
#endif

		/// <summary>Initializes a new instance of the <see cref="TizenButtonHandler"/> class.</summary>
		public TizenButtonHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenButtonView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenButtonView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.TouchEvent += OnTouch;
			platformView.Clicked += OnClicked;
#endif
		}

		protected override void DisconnectHandler(TizenButtonView platformView)
		{
#if TIZEN
			// Cancels any load in flight and releases the native image it had loaded.
			_iconLoader.Dispose();
			if (platformView.HasBody())
			{
				platformView.TouchEvent -= OnTouch;
				platformView.Clicked -= OnClicked;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapText(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is IText text)
				handler.PlatformView?.UpdateText(text);
#endif
		}

		public static void MapTextColor(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				handler.PlatformView?.UpdateTextColor(textStyle);
#endif
		}

		public static void MapCharacterSpacing(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				handler.PlatformView?.UpdateCharacterSpacing(textStyle);
#endif
		}

		public static void MapFont(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				handler.PlatformView?.UpdateTizenFont(textStyle, handler.GetService<IFontManager>());
#endif
		}

		public static void MapPadding(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			handler.PlatformView?.UpdatePadding(button);
#endif
		}

		public static void MapStrokeColor(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			handler.PlatformView?.UpdateStrokeColor(button);
#endif
		}

		public static void MapStrokeThickness(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			handler.PlatformView?.UpdateStrokeThickness(button);
#endif
		}

		public static void MapCornerRadius(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			handler.PlatformView?.UpdateCornerRadius(button);
#endif
		}

		public static void MapImageSource(TizenButtonHandler handler, IButton button)
		{
#if TIZEN
			MapImageSourceAsync(handler, button).FireAndForget(handler);
#endif
		}

#if TIZEN
		/// <summary>
		/// Resolves and applies the button's icon.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Returned as a task so tests and callers that need to await the load can, rather than
		/// racing an untracked <c>async void</c>.
		/// </para>
		/// <para>
		/// Supersession, source and view identity, failure clearing and disposal of the previous
		/// result are all handled by <see cref="TizenImageLoader{TImage}"/>; see that type for why
		/// each one matters. The continuation is marshalled back to the UI thread before the NUI
		/// icon is touched.
		/// </para>
		/// </remarks>
		public static Task MapImageSourceAsync(TizenButtonHandler handler, IButton button)
		{
			ArgumentNullException.ThrowIfNull(handler);

			var source = (button as IImage)?.Source;
			var provider = handler.GetService<IImageSourceServiceProvider>();

			// Capture the view this load is for, so a reconnect can be detected on completion.
			var target = handler.PlatformView;

			return handler._iconLoader.LoadAsync(
				source,
				(imageSource, token) => provider is null
					? Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null)
					: provider.GetTizenImageAsync(imageSource, token),
				// The load completes on a thread-pool thread; NUI must only be touched on the
				// main loop, so the application is marshalled back.
				image => handler.DispatchIfRequired(() => handler.PlatformView?.UpdateImageSource(image)),
				() => target is not null && ReferenceEquals(handler.PlatformView, target));
		}
#endif

#if TIZEN
		/// <remarks>
		/// NUI's button raises <c>Clicked</c> but has no pressed/released events, so those are
		/// derived from the raw touch stream. Returns <see langword="false"/> so the event keeps
		/// propagating and the button's own click handling still runs.
		/// </remarks>
		bool OnTouch(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			switch (e.Touch.GetState(0))
			{
				case global::Tizen.NUI.PointStateType.Down:
					VirtualView?.Pressed();
					break;
				case global::Tizen.NUI.PointStateType.Up:
					VirtualView?.Released();
					break;
			}

			return false;
		}
#endif

		void OnClicked(object? sender, EventArgs e) => VirtualView?.Clicked();
	}
}
