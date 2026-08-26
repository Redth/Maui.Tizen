// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="ISlider"/>.
	/// </summary>
	public class TizenSliderHandler : TizenViewHandler<ISlider, TizenSliderView>
	{
		/// <summary>The complete property mapper for <see cref="ISlider"/>.</summary>
		public static readonly IPropertyMapper<ISlider, TizenSliderHandler> Mapper =
			new PropertyMapper<ISlider, TizenSliderHandler>(ViewHandler.ViewMapper)
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
		public static readonly CommandMapper<ISlider, TizenSliderHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenSliderHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenSliderHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

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
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.ValueChanged += OnControlValueChanged;
			platformView.SlidingStarted += OnSlidingStarted;
			platformView.SlidingFinished += OnSlidingFinished;
#endif
		}

		protected override void DisconnectHandler(TizenSliderView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.ValueChanged -= OnControlValueChanged;
				platformView.SlidingStarted -= OnSlidingStarted;
				platformView.SlidingFinished -= OnSlidingFinished;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapMinimum(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateMinimum(slider);
#endif
		}

		public static void MapMaximum(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateMaximum(slider);
#endif
		}

		public static void MapValue(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateValue(slider);
#endif
		}

		public static void MapMinimumTrackColor(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateMinimumTrackColor(slider);
#endif
		}

		public static void MapMaximumTrackColor(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateMaximumTrackColor(slider);
#endif
		}

		public static void MapThumbColor(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			handler.PlatformView?.UpdateThumbColor(slider);
#endif
		}

		public static void MapThumbImageSource(TizenSliderHandler handler, ISlider slider)
		{
#if TIZEN
			var provider = handler.GetService<IImageSourceServiceProvider>();
			handler.PlatformView?.UpdateThumbImageSourceAsync(slider, provider).FireAndForget(handler);
#endif
		}

#if TIZEN
		/// <remarks>
		/// The equality check stops the round trip: assigning <see cref="ISlider.Value"/> maps
		/// back to <c>CurrentValue</c>, which raises this event again.
		/// </remarks>
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
