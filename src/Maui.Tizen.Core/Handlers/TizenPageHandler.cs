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
	public class TizenPageHandler : TizenContentViewHandler, IPageHandler
	{
		/// <summary>Property mapper for a page on Tizen.</summary>
		public static readonly IPropertyMapper<IContentView, IPageHandler> PageMapper =
			new PropertyMapper<IContentView, IPageHandler>(Mapper, PageHandler.Mapper)
			{
				[nameof(IContentView.Background)] = MapPageBackground,
				[nameof(ITitledElement.Title)] = MapTitle,
			};

		/// <summary>Command mapper for a page on Tizen.</summary>
		public static readonly CommandMapper<IContentView, IPageHandler> PageCommandMapper =
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
		/// <summary>
		/// Tracks whether an explicit background has ever been applied to this page.
		/// </summary>
		/// <remarks>
		/// A page has two different "no background" states that must not be treated alike, and the
		/// difference is only visible over time:
		/// <list type="bullet">
		/// <item><description>
		/// Never set. The page keeps the opaque white it was created with. Clearing here would
		/// repaint every page transparent at launch, because this mapper runs immediately.
		/// </description></item>
		/// <item><description>
		/// Set to a colour and then cleared. The page must go back to opaque white. Leaving the old
		/// colour is what previously happened, so a page could never lose a background once given
		/// one - and clearing to transparent would be wrong too, since white is the page default.
		/// </description></item>
		/// </list>
		/// </remarks>
		/// <remarks>
		/// Keyed off the handler rather than held as an instance field so it works for ANY
		/// <see cref="IPageHandler"/> - including a subclass, or a test double - since the mapper
		/// is static and only ever sees the interface. A weak table also means a discarded handler
		/// takes its entry with it.
		/// </remarks>
		static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IPageHandler, StrongBoolean>
			ExplicitBackgroundState = new();

		sealed class StrongBoolean
		{
			public bool Value;
		}

		/// <summary>Maps <see cref="IView.Background"/> for a page.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="page">The page.</param>
		public static void MapPageBackground(IPageHandler handler, IContentView page)
		{
			var state = ExplicitBackgroundState.GetOrCreateValue(handler);
			var hadExplicitBackground = state.Value;

			if (page.Background is not null)
				state.Value = true;

			RecordDecision(handler, page.Background is null && hadExplicitBackground);

#if TIZEN
			var platformView = (TizenContentViewGroup?)handler.PlatformView;

			if (platformView is null)
				return;

			if (page.Background is null)
			{
				// Restore the page default, but only if something replaced it. An initial null
				// leaves the white the page was created with.
				if (hadExplicitBackground)
				{
					platformView.UpdateBackgroundColor(TColor.White);
					state.Value = false;
				}

				return;
			}

			if (platformView.BackgroundColor != global::Tizen.NUI.Color.Transparent)
				platformView.UpdateBackgroundColor(TColor.Transparent);

			// clearWhenNull:false - the null case is handled above, with the page default rather
			// than transparent.
			platformView.UpdateBackground(page, clearWhenNull: false);
#endif
		}

		static void RecordDecision(IPageHandler handler, bool restoringDefault)
		{
#if !TIZEN
			(((IElementHandler)handler).PlatformView as TizenPlatformView)?
				.Record($"{nameof(IContentView.Background)}:clearWhenNull=False");

			if (restoringDefault)
			{
				(((IElementHandler)handler).PlatformView as TizenPlatformView)?
					.Record($"{nameof(IContentView.Background)}:restoreDefault=True");
			}
#endif
		}

		/// <summary>
		/// Maps <see cref="ITitledElement.Title"/>. Not implemented on Tizen, matching dotnet/maui,
		/// which marks the same mapper <c>[MissingMapper]</c>.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="page">The page.</param>
		public static void MapTitle(IPageHandler handler, IContentView page)
		{
		}
	}
}
