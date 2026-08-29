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
	/// The Tizen handler for <see cref="ISlider"/>.
	/// </summary>
	public class TizenSliderHandler : TizenViewHandler<ISlider, TizenSliderView>, ISliderHandler
	{
		/// <summary>The complete property mapper for <see cref="ISlider"/>.</summary>
		public static readonly IPropertyMapper<ISlider, ISliderHandler> Mapper =
			new PropertyMapper<ISlider, ISliderHandler>(TizenHandlerMappers.Chain(SliderHandler.Mapper))
			{
				[nameof(ISlider.Minimum)] = MapMinimum,
				[nameof(ISlider.Maximum)] = MapMaximum,
				[nameof(ISlider.Value)] = MapValue,
				[nameof(ISlider.MinimumTrackColor)] = MapMinimumTrackColor,
				[nameof(ISlider.MaximumTrackColor)] = MapMaximumTrackColor,
				[nameof(ISlider.ThumbColor)] = MapThumbColor,
				[nameof(ISlider.ThumbImageSource)] = MapThumbImageSource,
			};

		/// <summary>The complete command mapper for <see cref="ISlider"/>.</summary>
		public static readonly CommandMapper<ISlider, ISliderHandler> CommandMapper =
			new CommandMapper<ISlider, ISliderHandler>(TizenHandlerMappers.ChainCommands(SliderHandler.CommandMapper));

#if TIZEN
		TizenImageLoader<TizenImageSource> _thumbLoader = new();
#endif

		/// <summary>Initializes a new instance of the <see cref="TizenSliderHandler"/> class.</summary>
		public TizenSliderHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSliderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ISlider ISliderHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="ISliderHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object ISliderHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="ISliderHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenSliderView? Platform(ISliderHandler handler) => handler.PlatformView as TizenSliderView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenSliderHandler AsHandler(ISliderHandler handler) => (TizenSliderHandler)handler;

		protected override TizenSliderView CreatePlatformView()
		{
#if TIZEN
			return new() { Focusable = true };
#else
			return new();
#endif
		}

		protected override void ConnectHandler(TizenSliderView platformView)
		{
#if TIZEN
			var replacement = new TizenImageLoader<TizenImageSource>();

			TizenCleanup.Run(
				_thumbLoader.Dispose,
				() => _thumbLoader = replacement,
				() => base.ConnectHandler(platformView),
				() => platformView.ValueChanged += OnControlValueChanged,
				() => platformView.SlidingStarted += OnSlidingStarted,
				() => platformView.SlidingFinished += OnSlidingFinished);
#else
			base.ConnectHandler(platformView);
#endif
		}

		protected override void DisconnectHandler(TizenSliderView platformView)
		{
#if TIZEN
			TizenCleanup.Run(
				_thumbLoader.Dispose,
				() =>
				{
					if (platformView.HasBody())
						platformView.ValueChanged -= OnControlValueChanged;
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.SlidingStarted -= OnSlidingStarted;
				},
				() =>
				{
					if (platformView.HasBody())
						platformView.SlidingFinished -= OnSlidingFinished;
				},
				() => base.DisconnectHandler(platformView));
#else
			base.DisconnectHandler(platformView);
#endif
		}

		public static void MapMinimum(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateMinimum(slider);
#endif
		}

		public static void MapMaximum(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateMaximum(slider);
#endif
		}

		public static void MapValue(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateValue(slider);
#endif
		}

		public static void MapMinimumTrackColor(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateMinimumTrackColor(slider);
#endif
		}

		public static void MapMaximumTrackColor(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateMaximumTrackColor(slider);
#endif
		}

		public static void MapThumbColor(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			Platform(handler)?.UpdateThumbColor(slider);
#endif
		}

		/// <summary>Maps <see cref="ISlider.ThumbImageSource"/>.</summary>
		/// <remarks>
		/// Supersession, source and view identity, failure clearing and disposal of the previous
		/// result are handled by <see cref="TizenImageLoader{TImage}"/>. The application is
		/// marshalled back to the main loop, because the load completes on a thread-pool thread
		/// and NUI is not thread-safe.
		/// </remarks>
		public static void MapThumbImageSource(ISliderHandler handler, ISlider slider)
		{
#if TIZEN
			MapThumbImageSourceAsync(handler, slider).FireAndForget(handler);
#endif
		}

#if TIZEN
		/// <remarks>
		/// The equality check stops the round trip: assigning <see cref="ISlider.Value"/> maps
		/// back to <c>CurrentValue</c>, which raises this event again.
		/// </remarks>
#if TIZEN
		/// <summary>
		/// Resolves and applies the slider's thumb image.
		/// </summary>
		/// <remarks>Awaitable so tests need not race an untracked <c>async void</c>.</remarks>
		/// <param name="handler">The handler.</param>
		/// <param name="slider">The slider.</param>
		/// <returns>A task that completes when the image has been applied or cleared.</returns>
		public static Task MapThumbImageSourceAsync(ISliderHandler handler, ISlider slider)
		{
			ArgumentNullException.ThrowIfNull(handler);

			var provider = handler.GetService<IImageSourceServiceProvider>();
			var source = slider.ThumbImageSource;
			var virtualView = handler.VirtualView;
			var target = Platform(handler);
			var commitOnUiThread = TizenDispatchExtensions.CaptureDispatcher(handler);

			return AsHandler(handler)._thumbLoader.LoadAsync(
				source,
				(imageSource, token) => provider is null
					? Task.FromResult<IImageSourceServiceResult<TizenImageSource>?>(null)
					: provider.GetTizenImageAsync(imageSource, token),
				commitOnUiThread,
				image => target?.UpdateThumbImageSource(image),
				() => ReferenceEquals(handler.VirtualView?.ThumbImageSource, source),
				() =>
					target is not null &&
					ReferenceEquals(handler.VirtualView, virtualView) &&
					ReferenceEquals(Platform(handler), target));
		}
#endif

		void OnControlValueChanged(object? sender, EventArgs e)
		{
			if (PlatformView is null || VirtualView is null)
				return;

			if (!VirtualView.Value.Equals(PlatformView.CurrentValue))
				VirtualView.Value = PlatformView.CurrentValue;
		}
#endif

#if TIZEN
		void OnSlidingStarted(object? sender, global::Tizen.NUI.Components.SliderSlidingStartedEventArgs e) =>
			VirtualView?.DragStarted();

		void OnSlidingFinished(object? sender, global::Tizen.NUI.Components.SliderSlidingFinishedEventArgs e) =>
			VirtualView?.DragCompleted();
#endif
	}
}
