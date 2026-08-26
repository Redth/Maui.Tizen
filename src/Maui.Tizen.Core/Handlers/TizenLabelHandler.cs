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
	public class TizenLabelHandler : TizenViewHandler<ILabel, TizenLabelView>, ILabelHandler
	{
		/// <summary>Property mapper for <see cref="ILabel"/> on Tizen.</summary>
		public static readonly IPropertyMapper<ILabel, ILabelHandler> Mapper =
			new PropertyMapper<ILabel, ILabelHandler>(TizenViewMappers.ViewMapper, LabelHandler.Mapper)
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
		public static readonly CommandMapper<ILabel, ILabelHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

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

		ILabel ILabelHandler.VirtualView => VirtualView;

		// object, not TizenLabelView: on the neutral package MAUI declares PlatformView as object,
		// and an explicit interface implementation must match that exactly.
		object ILabelHandler.PlatformView => PlatformView;

		/// <inheritdoc />
		protected override TizenLabelView CreatePlatformView() => new();

		/// <summary>Maps <see cref="IView.Background"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapBackground(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateBackground(label);
#endif
		}

		/// <summary>Maps <see cref="IView.Opacity"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapOpacity(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateOpacity(label);
#endif
		}

		/// <summary>Maps <see cref="IView.Shadow"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapShadow(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateShadow(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.Text"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapText(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateLabelText(label);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.TextColor"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapTextColor(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateTextColor(label);
#endif
		}

		/// <summary>Maps <see cref="ITextAlignment.HorizontalTextAlignment"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapHorizontalTextAlignment(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateHorizontalTextAlignment(label);
#endif
		}

		/// <summary>Maps <see cref="ITextAlignment.VerticalTextAlignment"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapVerticalTextAlignment(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateVerticalTextAlignment(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.TextDecorations"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapTextDecorations(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateTextDecorations(label);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.Font"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapFont(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			var fontManager = handler.GetRequiredService<IFontManager>();
			((TizenLabelView?)handler.PlatformView)?.UpdateFont(label, fontManager);
#endif
		}

		/// <summary>Maps <see cref="ITextStyle.CharacterSpacing"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapCharacterSpacing(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateCharacterSpacing(label);
#endif
		}

		/// <summary>Maps <see cref="ILabel.LineHeight"/>.</summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapLineHeight(ILabelHandler handler, ILabel label)
		{
#if TIZEN
			((TizenLabelView?)handler.PlatformView)?.UpdateLineHeight(label);
#endif
		}

		/// <summary>
		/// Maps <see cref="ILabel.Padding"/>. Not implemented on Tizen, matching dotnet/maui, which
		/// marks the same mapper <c>[MissingMapper]</c>.
		/// </summary>
		/// <param name="handler">The handler.</param>
		/// <param name="label">The label.</param>
		public static void MapPadding(ILabelHandler handler, ILabel label)
		{
		}
	}
}
