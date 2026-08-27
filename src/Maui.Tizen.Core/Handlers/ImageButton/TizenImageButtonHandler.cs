// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.ImageButtonHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named ImageButtonHandler, which still
// exists in Microsoft.Maui.Core.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IImageButton"/>.</summary>
	public class TizenImageButtonHandler : TizenViewHandler<IImageButton, TizenImageButtonView>
	{
		public static IPropertyMapper<IImageButton, TizenImageButtonHandler> Mapper =
			new PropertyMapper<IImageButton, TizenImageButtonHandler>(ViewMapper)
			{
				[nameof(IImage.Aspect)] = MapAspect,
				[nameof(IImage.IsAnimationPlaying)] = MapIsAnimationPlaying,
				[nameof(IImage.Source)] = MapSource,
				[nameof(IButtonStroke.StrokeThickness)] = MapStrokeThickness,
				[nameof(IButtonStroke.StrokeColor)] = MapStrokeColor,
				[nameof(IButtonStroke.CornerRadius)] = MapCornerRadius,
				[nameof(IImageButton.Padding)] = MapPadding,
			};

		public static CommandMapper<IImageButton, TizenImageButtonHandler> CommandMapper =
			new(ViewCommandMapper)
			{
			};

		/// <summary>Cancels a superseded or disconnected image load.</summary>
		readonly TizenImageSourceLoader _sourceLoader = new();

		public TizenImageButtonHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenImageButtonHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenImageButtonHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}


		protected override TizenImageButtonView CreatePlatformView() =>
			new TizenImageButtonView
			{
				Focusable = true,
			};

		protected override void ConnectHandler(TizenImageButtonView platformView)
		{
			platformView.Clicked += OnClicked;
			platformView.Pressed += OnPressed;
			platformView.Released += OnReleased;
			base.ConnectHandler(platformView);
		}

		protected override void DisconnectHandler(TizenImageButtonView platformView)
		{
			// A pending load must not write to a view that is being released.
			_sourceLoader.Cancel();

			if (!platformView.HasBody())
				return;

			platformView.Clicked -= OnClicked;
			platformView.Pressed -= OnPressed;
			platformView.Released -= OnReleased;
			base.DisconnectHandler(platformView);
		}

		void OnReleased(object? sender, EventArgs e) => VirtualView?.Released();

		void OnPressed(object? sender, EventArgs e) => VirtualView?.Pressed();

		void OnClicked(object? sender, EventArgs e) => VirtualView?.Clicked();

		public static void MapAspect(TizenImageButtonHandler handler, IImageButton imageButton) =>
			handler.PlatformView?.UpdateAspect(imageButton);

		public static void MapIsAnimationPlaying(TizenImageButtonHandler handler, IImageButton imageButton) =>
			handler.PlatformView?.UpdateIsAnimationPlaying(imageButton);

		public static void MapSource(TizenImageButtonHandler handler, IImageButton imageButton) =>
			_ = MapSourceAsync(handler, imageButton);

		public static Task MapSourceAsync(TizenImageButtonHandler handler, IImageButton imageButton)
		{
			if (handler.MauiContext is null)
			{
				return Task.CompletedTask;
			}

			var provider = handler.GetRequiredService<IImageSourceServiceProvider>();

			// The loader cancels any load still in flight, so a slow earlier source cannot
			// finish last and overwrite this one.
			return handler._sourceLoader.LoadAsync(
				imageButton,
				provider,
				(platformImage, cancellationToken) =>
				{
					// The handler may have been disconnected while the source was resolving, in
					// which case there is no view left to write to.
					var platformView = handler.PlatformView;
					if (platformView is null || cancellationToken.IsCancellationRequested)
					{
						return Task.CompletedTask;
					}

					return platformView.ApplyImageSourceAsync(
						platformImage,
						image =>
						{
							if (image is not null)
							{
								platformView.ResourceUrl = image.ResourceUrl;
							}
						},
						cancellationToken);
				});
		}

		public static void MapStrokeColor(TizenImageButtonHandler handler, IButtonStroke buttonStroke) =>
			handler.PlatformView.UpdateStrokeColor(buttonStroke);

		public static void MapStrokeThickness(TizenImageButtonHandler handler, IButtonStroke buttonStroke)
		{
			handler.PlatformView.UpdateStrokeThickness(buttonStroke);
			handler.UpdateValue(nameof(IImageButton.Padding));
		}

		public static void MapCornerRadius(TizenImageButtonHandler handler, IButtonStroke buttonStroke)
		{
			handler.PlatformView.UpdateCornerRadius(buttonStroke);
			handler.UpdateValue(nameof(IImageButton.Padding));
		}

		/// <summary>
		/// Intentional no-op, carried over from dotnet/maui. The Tizen image button draws its image
		/// edge to edge and exposes no content-inset API, so <see cref="IImageButton.Padding"/> cannot
		/// be applied natively. See docs/wave-b-mapper-parity.md.
		/// </summary>
		public static void MapPadding(TizenImageButtonHandler handler, IImageButton imageButton)
		{
		}
	}
}
