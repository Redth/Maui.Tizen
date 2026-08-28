// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="IButton"/>.
	/// </summary>
	public class TizenButtonHandler : TizenViewHandler<IButton, TizenButtonView>, IButtonHandler, IImageSourcePartSetter
	{
		/// <summary>The complete property mapper for <see cref="IButton"/>.</summary>
		/// <remarks>
		/// <c>Text</c>, <c>TextColor</c>, <c>Font</c> and <c>CharacterSpacing</c> come from
		/// <see cref="ITextButton"/> and <c>Source</c> from <see cref="IImageButton"/>; a button
		/// may implement either, both, or neither, so the mappings are defensive about the cast.
		/// </remarks>
		public static readonly IPropertyMapper<IButton, IButtonHandler> Mapper =
			new PropertyMapper<IButton, IButtonHandler>(TizenHandlerMappers.Chain(ButtonHandler.Mapper))
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
		public static readonly CommandMapper<IButton, IButtonHandler> CommandMapper =
			new CommandMapper<IButton, IButtonHandler>(TizenHandlerMappers.ChainCommands(ButtonHandler.CommandMapper));

#if TIZEN
		TizenImageLoader<TizenImageSource> _iconLoader = new();
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

		IButton IButtonHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="IButtonHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object IButtonHandler.PlatformView => PlatformView;

		/// <summary>
		/// MAUI's image-source loader for this button.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Required by <see cref="IButtonHandler"/>. <c>ImageSourcePartLoader</c> became public in
		/// MAUI 11, which is precisely what unblocked implementing the real interface.
		/// </para>
		/// <para>
		/// It is exposed so callers that go through MAUI's own plumbing work, but the icon
		/// mapping does <em>not</em> route through it. <c>ImageSourcePartLoader</c> offers no
		/// supersession, source or view identity check, failure clearing, or disposal of the
		/// previous native result; <see cref="TizenImageLoader{TImage}"/> provides all of those
		/// and owns the actual load. Both paths converge on
		/// <see cref="SetImageSource(object?)"/>, so they cannot disagree about what is applied.
		/// </para>
		/// </remarks>
		public ImageSourcePartLoader ImageSourceLoader =>
			_imageSourceLoader ??= new ImageSourcePartLoader(this);

		ImageSourcePartLoader? _imageSourceLoader;

		IElementHandler IImageSourcePartSetter.Handler => this;

		IImageSourcePart? IImageSourcePartSetter.ImageSourcePart => VirtualView as IImageSourcePart;

		/// <summary>Applies a resolved platform image to the button's icon.</summary>
		/// <param name="platformImage">The resolved image, or <see langword="null"/> to clear.</param>
		public void SetImageSource(object? platformImage)
		{
#if TIZEN
			PlatformView?.UpdateImageSource(platformImage as TizenImageSource);
#endif
		}

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="IButtonHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenButtonView? Platform(IButtonHandler handler) => handler.PlatformView as TizenButtonView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenButtonHandler AsHandler(IButtonHandler handler) => (TizenButtonHandler)handler;

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
			// Disconnect permanently invalidates its loader so queued callbacks cannot revive it.
			// A reconnect therefore gets a fresh generation/ownership scope.
			_iconLoader.Dispose();
			_iconLoader = new();
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

		public static void MapText(IButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is IText text)
				Platform(handler)?.UpdateText(text);
#endif
		}

		public static void MapTextColor(IButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				Platform(handler)?.UpdateTextColor(textStyle);
#endif
		}

		public static void MapCharacterSpacing(IButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				Platform(handler)?.UpdateCharacterSpacing(textStyle);
#endif
		}

		public static void MapFont(IButtonHandler handler, IButton button)
		{
#if TIZEN
			if (button is ITextStyle textStyle)
				Platform(handler)?.UpdateTizenFont(textStyle, handler.GetService<IFontManager>());
#endif
		}

		public static void MapPadding(IButtonHandler handler, IButton button)
		{
#if TIZEN
			Platform(handler)?.UpdatePadding(button);
#endif
		}

		public static void MapStrokeColor(IButtonHandler handler, IButton button)
		{
#if TIZEN
			Platform(handler)?.UpdateStrokeColor(button);
#endif
		}

		public static void MapStrokeThickness(IButtonHandler handler, IButton button)
		{
#if TIZEN
			Platform(handler)?.UpdateStrokeThickness(button);
#endif
		}

		public static void MapCornerRadius(IButtonHandler handler, IButton button)
		{
#if TIZEN
			Platform(handler)?.UpdateCornerRadius(button);
#endif
		}

		public static void MapImageSource(IButtonHandler handler, IButton button)
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
		public static Task MapImageSourceAsync(IButtonHandler handler, IButton button)
		{
			ArgumentNullException.ThrowIfNull(handler);

			var source = (button as IImage)?.Source;
			var provider = handler.GetService<IImageSourceServiceProvider>();

			// Capture both ends of the binding. The commit rechecks them after it reaches the UI
			// thread, not merely before queueing there.
			var virtualView = handler.VirtualView;
			var target = Platform(handler);

			return AsHandler(handler)._iconLoader.LoadAsync(
				source,
				(imageSource, token) => provider is null
					? Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null)
					: provider.GetTizenImageAsync(imageSource, token),
				handler.DispatchIfRequiredAsync,
				image => target?.UpdateImageSource(image),
				() => ReferenceEquals((handler.VirtualView as IImage)?.Source, source),
				() =>
					target is not null &&
					ReferenceEquals(handler.VirtualView, virtualView) &&
					ReferenceEquals(Platform(handler), target));
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
