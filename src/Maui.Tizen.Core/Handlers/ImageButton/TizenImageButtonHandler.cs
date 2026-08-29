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
			new PropertyMapper<IImageButton, TizenImageButtonHandler>(TizenViewMappers.ViewMapper)
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
			new(TizenViewMappers.ViewCommandMapper)
			{
			};

		TizenImageLoader<TizenImageSource> _sourceLoader = new();
		readonly TizenImageLoadEvents _sourceEvents = new();

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
			var replacement = new TizenImageLoader<TizenImageSource>();

			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				() => _sourceLoader = replacement,
				() => base.ConnectHandler(platformView),
				() => platformView.Clicked += OnClicked,
				() => platformView.Pressed += OnPressed,
				() => platformView.Released += OnReleased);
		}

		protected override void DisconnectHandler(TizenImageButtonView platformView)
		{
			TizenCleanup.Run(
				_sourceEvents.Invalidate,
				_sourceLoader.Dispose,
				() =>
				{
					if (platformView.HasBody())
						platformView.Clicked -= OnClicked;
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.Pressed -= OnPressed;
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.Released -= OnReleased;
				},
				() => base.DisconnectHandler(platformView));
		}

		void OnReleased(object? sender, EventArgs e) => VirtualView?.Released();

		void OnPressed(object? sender, EventArgs e) => VirtualView?.Pressed();

		void OnClicked(object? sender, EventArgs e) => VirtualView?.Clicked();

		public static void MapAspect(TizenImageButtonHandler handler, IImageButton imageButton) =>
			handler.PlatformView?.UpdateAspect(imageButton);

		public static void MapIsAnimationPlaying(TizenImageButtonHandler handler, IImageButton imageButton) =>
			handler.PlatformView?.UpdateIsAnimationPlaying(imageButton);

		public static void MapSource(TizenImageButtonHandler handler, IImageButton imageButton)
		{
#if TIZEN
			MapSourceAsync(handler, imageButton).FireAndForget(handler);
#endif
		}

		public static Task MapSourceAsync(TizenImageButtonHandler handler, IImageButton imageButton)
		{
			if (handler.MauiContext is null)
			{
				return Task.CompletedTask;
			}

			var provider = handler.GetRequiredService<IImageSourceServiceProvider>();
			var source = imageButton.Source;
			var virtualView = handler.VirtualView;
			var target = handler.PlatformView;
			var commitOnUiThread = TizenDispatchExtensions.CaptureDispatcher(handler);

			return handler._sourceLoader.LoadPartAsync(
				imageButton,
				handler._sourceEvents,
				(imageSource, token) => provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				(platformImage, token) => target.ApplyAndWaitForReadyAsync(platformImage, token),
				() =>
					ReferenceEquals(handler.VirtualView, virtualView) &&
					ReferenceEquals(handler.PlatformView, target));
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
		/// Intentional no-op, carried over from dotnet/maui.
		/// </summary>
		/// <remarks>
		/// The platform view is a <c>Tizen.UIExtensions.NUI.Image</c>, i.e. a NUI <c>ImageView</c>.
		/// <c>View.Padding</c> does exist and is settable, so the earlier claim that there is "no
		/// content-inset API" was imprecise. It is nonetheless the wrong tool: NUI padding insets a
		/// view's <em>children</em> during layout, and an <c>ImageView</c> renders its image as a
		/// visual rather than as a child, so writing padding would move nothing while making the
		/// view report a larger measured size.
		/// <para>
		/// Insetting the image itself would mean wrapping it in a container view, which this
		/// backend cannot do: MAUI exposes no settable container hook to an out-of-repo assembly,
		/// so <c>TizenViewHandler</c> pins <c>NeedsContainer</c> to false. Recorded as a gap rather
		/// than faked. Not verified on a device — see docs/net11-status.md.
		/// </para>
		/// </remarks>
		public static void MapPadding(TizenImageButtonHandler handler, IImageButton imageButton)
		{
		}
	}
}
