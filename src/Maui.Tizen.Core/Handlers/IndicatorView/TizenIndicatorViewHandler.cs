// Ported from dotnet/maui as part of the Maui.Tizen extraction.
//
// Upstream this file was the Tizen half of the neutral Microsoft.Maui.Handlers.IndicatorViewHandler
// partial class. .NET MAUI 11 ships no Tizen target framework, so this is a standalone
// handler that owns its own mappers. It is deliberately NOT named IndicatorViewHandler, which still
// exists in Microsoft.Maui.Core.

using Microsoft.Maui;
using Microsoft.Maui.Handlers;

using Microsoft.Maui.Platforms.Tizen;

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>Tizen handler for <see cref="IIndicatorView"/>.</summary>
	/// <remarks>
	/// Appearance properties (size, colours, shape) have no incremental native API on the Tizen page
	/// control, so they rebuild the indicator set via <c>ResetIndicators</c>.
	/// </remarks>
	public class TizenIndicatorViewHandler : TizenViewHandler<IIndicatorView, TizenPageControl>
	{
		public static IPropertyMapper<IIndicatorView, TizenIndicatorViewHandler> Mapper =
			new PropertyMapper<IIndicatorView, TizenIndicatorViewHandler>(ViewMapper)
			{
				[nameof(IIndicatorView.Count)] = MapCount,
				[nameof(IIndicatorView.Position)] = MapPosition,
				[nameof(IIndicatorView.HideSingle)] = MapHideSingle,
				[nameof(IIndicatorView.MaximumVisible)] = MapMaximumVisible,
				[nameof(IIndicatorView.IndicatorSize)] = MapIndicatorSize,
				[nameof(IIndicatorView.IndicatorColor)] = MapIndicatorColor,
				[nameof(IIndicatorView.SelectedIndicatorColor)] = MapSelectedIndicatorColor,
				[nameof(IIndicatorView.IndicatorsShape)] = MapIndicatorShape,
			};

		public static CommandMapper<IIndicatorView, TizenIndicatorViewHandler> CommandMapper =
			new(ViewCommandMapper)
			{
			};

		public TizenIndicatorViewHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenIndicatorViewHandler(IPropertyMapper? mapper)
			: base(mapper ?? Mapper, CommandMapper)
		{
		}

		public TizenIndicatorViewHandler(IPropertyMapper? mapper, CommandMapper? commandMapper)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		protected override TizenPageControl CreatePlatformView() => new TizenPageControl(VirtualView);

		public static void MapCount(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapPosition(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdatePosition();

		public static void MapHideSingle(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapMaximumVisible(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.UpdateCount();

		public static void MapIndicatorSize(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapIndicatorColor(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapSelectedIndicatorColor(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();

		public static void MapIndicatorShape(TizenIndicatorViewHandler handler, IIndicatorView indicator) => handler.PlatformView.ResetIndicators();
	}
}
