using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using TColor = Tizen.UIExtensions.Common.Color;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for a page (an <see cref="IContentView"/> hosted directly by a window).
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.PageHandler</c> (Tizen) in dotnet/maui, including its
	/// opaque-white default background and its deliberately empty <c>PlatformArrange</c>.
	/// </remarks>
	public class TizenPageHandler : TizenContentViewHandler, ITizenPageHandler
	{
		/// <summary>Property mapper for a page on Tizen.</summary>
		public static readonly IPropertyMapper<IContentView, ITizenPageHandler> PageMapper =
			new PropertyMapper<IContentView, ITizenPageHandler>(Mapper)
			{
				[nameof(IContentView.Background)] = MapPageBackground,
				[nameof(ITitledElement.Title)] = MapTitle,
			};

		/// <summary>Command mapper for a page on Tizen.</summary>
		public static readonly CommandMapper<IContentView, ITizenPageHandler> PageCommandMapper =
			new(CommandMapper);

		/// <summary>Initializes a new instance of the <see cref="TizenPageHandler"/> class.</summary>
		public TizenPageHandler()
			: base(PageMapper, PageCommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenPageHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenPageHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? PageMapper, commandMapper ?? PageCommandMapper)
		{
		}

		/// <inheritdoc />
		/// <remarks>
		/// Empty on purpose - a page always fills its window, so the platform window owns its
		/// geometry. This matches dotnet/maui's Tizen <c>PageHandler</c>.
		/// </remarks>
		public override void PlatformArrange(Rect frame)
		{
		}

		/// <inheritdoc />
		protected override TizenContentViewGroup CreatePlatformView()
		{
			var view = base.CreatePlatformView();
#if TIZEN
			view.UpdateBackgroundColor(TColor.White);
#endif
			return view;
		}

		/// <summary>Maps <see cref="IView.Background"/> for a page.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="page">The page.</param>
		public static void MapPageBackground(ITizenPageHandler handler, IContentView page)
		{
#if TIZEN
			if (page.Background is not null &&
				handler.PlatformView.BackgroundColor != global::Tizen.NUI.Color.Transparent)
			{
				handler.PlatformView.UpdateBackgroundColor(TColor.Transparent);
			}

			// clearWhenNull:false on purpose - a page is created opaque white and this mapper runs
			// immediately, so clearing on a null background would repaint every page transparent.
			handler.PlatformView?.UpdateBackground(page, clearWhenNull: false);
#endif
		}

		/// <summary>
		/// Maps <see cref="ITitledElement.Title"/>. Not implemented on Tizen, matching dotnet/maui,
		/// which marks the same mapper <c>[MissingMapper]</c>.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="page">The page.</param>
		public static void MapTitle(ITizenPageHandler handler, IContentView page)
		{
		}
	}
}
