using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.NUI;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// Tizen handler for <see cref="ILabel"/>.
	/// </summary>
	/// <remarks>
	/// Ported from <c>Microsoft.Maui.Handlers.LabelHandler</c> (Tizen) in dotnet/maui. The mapper
	/// contents match the Tizen entries of MAUI's <c>LabelHandler.Mapper</c> exactly.
	/// </remarks>
	public class TizenLabelHandler : TizenViewHandler<ILabel, TizenLabelView>, ITizenLabelHandler
	{
		/// <summary>Property mapper for <see cref="ILabel"/> on Tizen.</summary>
		public static readonly IPropertyMapper<ILabel, ITizenLabelHandler> Mapper =
			new PropertyMapper<ILabel, ITizenLabelHandler>(ViewHandler.ViewMapper)
			{
				[nameof(ILabel.Background)] = MapBackground,
				[nameof(ILabel.Opacity)] = MapOpacity,
				[nameof(ILabel.Shadow)] = MapShadow,
				[nameof(ITextStyle.CharacterSpacing)] = MapCharacterSpacing,
				[nameof(ITextStyle.Font)] = MapFont,
				[nameof(ITextAlignment.HorizontalTextAlignment)] = MapHorizontalTextAlignment,
				[nameof(ITextAlignment.VerticalTextAlignment)] = MapVerticalTextAlignment,
				[nameof(ILabel.LineHeight)] = MapLineHeight,
				[nameof(ILabel.Padding)] = MapPadding,
				[nameof(ILabel.Text)] = MapText,
				[nameof(ITextStyle.TextColor)] = MapTextColor,
				[nameof(ILabel.TextDecorations)] = MapTextDecorations,
			};

		/// <summary>Command mapper for <see cref="ILabel"/> on Tizen.</summary>
		public static readonly CommandMapper<ILabel, ITizenLabelHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		/// <summary>Initializes a new instance of the <see cref="TizenLabelHandler"/> class.</summary>
		public TizenLabelHandler()
			: base(Mapper, CommandMapper)
		{
		}

		/// <summary>Initializes a new instance of the <see cref="TizenLabelHandler"/> class.</summary>
		/// <param name="mapper">An optional property mapper override.</param>
		/// <param name="commandMapper">An optional command mapper override.</param>
		public TizenLabelHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ILabel ITizenLabelHandler.VirtualView => VirtualView;

		TizenLabelView ITizenLabelHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenLabelView CreatePlatformView() => new();

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapBackground(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateBackground(label);
#endif
		}

		/// <summary>Maps <see cref="IView.Opacity"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapOpacity(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateOpacity(label);
#endif
		}

		/// <summary>Maps <see cref="IView.Shadow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapShadow(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateShadow(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.Text"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapText(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateLabelText(label);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.TextColor"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapTextColor(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(label);
#endif
		}

		/// <summary>Maps <see cref="ITextAlignment.HorizontalTextAlignment"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapHorizontalTextAlignment(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateHorizontalTextAlignment(label);
#endif
		}

		/// <summary>Maps <see cref="ITextAlignment.VerticalTextAlignment"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapVerticalTextAlignment(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateVerticalTextAlignment(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.TextDecorations"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapTextDecorations(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextDecorations(label);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.Font"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapFont(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			var fontManager = handler.GetRequiredService<IFontManager>();
			handler.PlatformView?.UpdateFont(label, fontManager);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.CharacterSpacing"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapCharacterSpacing(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.LineHeight"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapLineHeight(ITizenLabelHandler handler, ILabel label)
		{
#if TIZEN
			handler.PlatformView?.UpdateLineHeight(label);
#endif
		}

		/// <summary>
		/// Maps <see cref="ILabel.Padding"/>. Not implemented on Tizen, matching dotnet/maui, which
		/// marks the same mapper <c>[MissingMapper]</c>.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapPadding(ITizenLabelHandler handler, ILabel label)
		{
		}
	}
}
