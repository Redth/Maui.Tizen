using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen.Adapters;
using Microsoft.Maui.Platforms.Tizen.Platform;
using Tizen.UIExtensions.NUI;
using NView = Tizen.NUI.BaseComponents.View;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Handler for <see cref="CarouselView"/> in the Tizen backend.
	/// </summary>
	/// <remarks>
	/// <para>
	/// CarouselView displays items in a horizontal scrollable layout with snap-to-item behavior.
	/// This handler provides support for Position, CurrentItem, and IsSwipeEnabled properties.
	/// </para>
	/// <para>
	/// Unsupported features:
	/// - IsBounceEnabled: Tizen CollectionView does not support bounce effects.
	/// - PeekAreaInsets: No platform support for visible adjacent items.
	/// - Loop: Infinite looping is not supported on Tizen.
	/// </para>
	/// </remarks>
	public class TizenCarouselViewHandler : TizenItemsViewHandler<CarouselView>
	{
		readonly CarouselFeedbackCoordinator _feedback = new();
		/// <summary>
		/// Property mapper for <see cref="CarouselView"/>.
		/// </summary>
		public static IPropertyMapper<CarouselView, TizenCarouselViewHandler> CarouselViewMapper =
			new PropertyMapper<CarouselView, TizenCarouselViewHandler>(ItemsViewMapper)
			{
				[nameof(CarouselView.CurrentItem)] = MapCurrentItem,
				[nameof(CarouselView.Position)] = MapPosition,
				[nameof(CarouselView.IsBounceEnabled)] = MapIsBounceEnabled,
				[nameof(CarouselView.IsSwipeEnabled)] = MapIsSwipeEnabled,
				[nameof(CarouselView.PeekAreaInsets)] = MapPeekAreaInsets,
				[nameof(CarouselView.Loop)] = MapLoop,
				[nameof(CarouselView.ItemsLayout)] = MapItemsLayout,
			};

		/// <summary>
		/// Command mapper for <see cref="CarouselView"/> commands.
		/// </summary>
		public static CommandMapper<CarouselView, TizenCarouselViewHandler> CarouselViewCommandMapper =
			new CommandMapper<CarouselView, TizenCarouselViewHandler>(ItemsViewCommandMapper);

		/// <summary>
		/// Initializes a new instance of <see cref="TizenCarouselViewHandler"/> using default mappers.
		/// </summary>
		public TizenCarouselViewHandler()
			: base(CarouselViewMapper, CarouselViewCommandMapper)
		{
		}

		/// <summary>
		/// Initializes a new instance of <see cref="TizenCarouselViewHandler"/> with custom mappers.
		/// </summary>
		/// <param name="mapper">The property mapper.</param>
		/// <param name="commandMapper">Optional command mapper.</param>
		public TizenCarouselViewHandler(IPropertyMapper mapper, CommandMapper? commandMapper = null)
			: base(mapper, commandMapper)
		{
		}

		/// <summary>
		/// Gets the typed platform view for CarouselView.
		/// </summary>
		protected new TizenCarouselViewControl? PlatformView
			=> base.PlatformView as TizenCarouselViewControl;

		/// <summary>
		/// Creates the platform view for the CarouselView.
		/// </summary>
		/// <returns>A <see cref="TizenCarouselViewControl"/> instance.</returns>
		protected override NView CreatePlatformView()
		{
			return new TizenCarouselViewControl(VirtualView);
		}

		protected override ItemAdaptor CreateAdaptor()
		{
			return new TizenCarouselViewItemTemplateAdaptor(VirtualView);
		}

		protected override void ConnectHandler(NView platformView)
		{
			base.ConnectHandler(platformView);
			try
			{
				if (PlatformView is { } carousel)
					carousel.Scrolled += OnCarouselScrolled;
				UpdateItemsLayout();
				UpdateIsSwipeEnabled();
				UpdateCurrentItemFromManaged();
			}
			catch
			{
				if (PlatformView is { } carousel)
					carousel.Scrolled -= OnCarouselScrolled;
				base.DisconnectHandler(platformView);
				throw;
			}
		}

		protected override void DisconnectHandler(NView platformView)
		{
			try
			{
				if (PlatformView is { } carousel)
					carousel.Scrolled -= OnCarouselScrolled;
			}
			finally
			{
				base.DisconnectHandler(platformView);
			}
		}

		protected override void OnAdaptorInstalled()
		{
			base.OnAdaptorInstalled();
			UpdateCurrentItemFromManaged();
		}

		protected virtual void UpdateItemsLayout()
		{
			PlatformView?.UpdateLayoutManager();
		}

		#region Mapper Methods

		/// <summary>
		/// Maps <see cref="CarouselView.CurrentItem"/> to the platform.
		/// </summary>
		public static void MapCurrentItem(TizenCarouselViewHandler handler, CarouselView view)
		{
			handler.UpdateCurrentItemFromManaged();
		}

		/// <summary>
		/// Maps <see cref="CarouselView.Position"/> to the platform.
		/// </summary>
		public static void MapPosition(TizenCarouselViewHandler handler, CarouselView view)
		{
			handler.UpdatePositionFromManaged();
		}

		/// <summary>
		/// No-op: IsBounceEnabled is not supported on Tizen.
		/// </summary>
		/// <remarks>
		/// Tizen.UIExtensions.NUI.CollectionView does not support bounce/overscroll effects.
		/// This mapper is declared for API completeness but performs no operation.
		/// </remarks>
		public static void MapIsBounceEnabled(TizenCarouselViewHandler handler, CarouselView view)
		{
			// No-op: Bounce effect not supported on Tizen
		}

		/// <summary>
		/// Maps IsSwipeEnabled to the native scroll input switch.
		/// </summary>
		/// <remarks>
		/// Programmatic position changes continue to work while user drag input is disabled.
		/// </remarks>
		public static void MapIsSwipeEnabled(TizenCarouselViewHandler handler, CarouselView view)
		{
			handler.UpdateIsSwipeEnabled();
		}

		/// <summary>
		/// No-op: PeekAreaInsets is not supported on Tizen.
		/// </summary>
		/// <remarks>
		/// Tizen CollectionView does not support showing parts of adjacent items.
		/// This mapper is declared for API completeness but performs no operation.
		/// </remarks>
		public static void MapPeekAreaInsets(TizenCarouselViewHandler handler, CarouselView view)
		{
			// No-op: Peek area not supported on Tizen
		}

		/// <summary>
		/// No-op: Loop is not supported on Tizen.
		/// </summary>
		/// <remarks>
		/// Tizen.UIExtensions.NUI.CollectionView does not support infinite looping.
		/// This mapper is declared for API completeness but performs no operation.
		/// </remarks>
		public static void MapLoop(TizenCarouselViewHandler handler, CarouselView view)
		{
			// No-op: Loop not supported on Tizen
		}

		/// <summary>
		/// Maps <see cref="CarouselView.ItemsLayout"/> to the platform.
		/// </summary>
		public static void MapItemsLayout(TizenCarouselViewHandler handler, CarouselView view)
		{
			handler.UpdateItemsLayout();
		}

		void UpdateCurrentItemFromManaged()
		{
			if (_feedback.IsApplyingNative || PlatformView is null)
				return;

			var expectedPosition = VirtualView.CurrentItem is not null && Adaptor is not null
				? Adaptor.GetItemIndex(VirtualView.CurrentItem)
				: -1;

			_feedback.ApplyManaged(expectedPosition, () =>
				PlatformView.UpdateCurrentItem(VirtualView.CurrentItem));
		}

		void UpdatePositionFromManaged()
		{
			if (_feedback.IsApplyingNative || PlatformView is null)
				return;

			_feedback.ApplyManaged(
				VirtualView.Position,
				() => PlatformView.UpdatePosition(VirtualView.Position));
		}

		void UpdateIsSwipeEnabled()
		{
			if (PlatformView is not null)
				PlatformView.CollectionView.ScrollView.ScrollEnabled = VirtualView.IsSwipeEnabled;
		}

		void OnCarouselScrolled(object? sender, int position)
		{
			if (Adaptor is null)
				return;

			_feedback.ApplyNative(
				position,
				Adaptor.Count,
				index => Adaptor[index],
				value => VirtualView.Position = value,
				value => VirtualView.CurrentItem = value);
			VirtualView.IsScrolling = false;
		}

		#endregion
	}
}
