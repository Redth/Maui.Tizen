// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading.Tasks;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platforms.Tizen;
#if TIZEN
using Tizen.UIExtensions.NUI;
#endif

namespace Microsoft.Maui.Platforms.Tizen.Handlers
{
	/// <summary>
	/// The Tizen handler for <see cref="ITimePicker"/>.
	/// </summary>
	/// <remarks>
	/// Presented as a read-only entry that opens <see cref="TizenDateTimePicker"/>.
	/// </remarks>
	public class TizenTimePickerHandler : TizenViewHandler<ITimePicker, TizenPickerView>, ITimePickerHandler
	{
#if TIZEN
		bool _isOpen;
#endif

		/// <summary>The complete property mapper for <see cref="ITimePicker"/>.</summary>
		public static readonly IPropertyMapper<ITimePicker, ITimePickerHandler> Mapper =
			new PropertyMapper<ITimePicker, ITimePickerHandler>(TizenHandlerMappers.Chain(TimePickerHandler.Mapper))
			{
				[nameof(ITimePicker.Format)] = MapFormat,
				[nameof(ITimePicker.Time)] = MapTime,
				[nameof(ITimePicker.Font)] = MapFont,
				[nameof(ITimePicker.TextColor)] = MapTextColor,
				[nameof(ITimePicker.CharacterSpacing)] = MapCharacterSpacing,
				["IsOpen"] = MapIsOpen,
			};

		/// <summary>The complete command mapper for <see cref="ITimePicker"/>.</summary>
		public static readonly CommandMapper<ITimePicker, ITimePickerHandler> CommandMapper =
			new CommandMapper<ITimePicker, ITimePickerHandler>(TizenHandlerMappers.ChainCommands(TimePickerHandler.CommandMapper));

		public TizenTimePickerHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenTimePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

		ITimePicker ITimePickerHandler.VirtualView => VirtualView;

		/// <remarks>
		/// <see cref="ITimePickerHandler"/> types this as <see cref="object"/>. MAUI ships no Tizen asset,
		/// so this backend resolves the neutral <c>net11.0</c> assembly on every target framework
		/// and the interface is implementable without the per-platform alias mismatch that would
		/// otherwise occur.
		/// </remarks>
		object ITimePickerHandler.PlatformView => PlatformView;

		/// <summary>
		/// The typed platform view for a mapping.
		/// </summary>
		/// <remarks>
		/// <see cref="ITimePickerHandler"/> types <c>PlatformView</c> as <see cref="object"/>, because MAUI's
		/// neutral assembly has no Tizen alias. Mappings therefore narrow it here rather than at
		/// every call site.
		/// </remarks>
		/// <param name="handler">The handler.</param>
		/// <returns>The platform view, or <see langword="null"/> if it is not yet created.</returns>
		static TizenPickerView? Platform(ITimePickerHandler handler) => handler.PlatformView as TizenPickerView;

		/// <summary>The concrete handler, for mappings that need its own state.</summary>
		/// <param name="handler">The handler.</param>
		/// <returns>The concrete handler.</returns>
		static TizenTimePickerHandler AsHandler(ITimePickerHandler handler) => (TizenTimePickerHandler)handler;

		protected override TizenPickerView CreatePlatformView() => new TizenPickerView();

		protected override void ConnectHandler(TizenPickerView platformView)
		{
			base.ConnectHandler(platformView);
#if TIZEN
			platformView.TouchEvent += OnTouch;
			platformView.KeyEvent += OnKeyEvent;
#endif
		}

		protected override void DisconnectHandler(TizenPickerView platformView)
		{
#if TIZEN
			if (platformView.HasBody())
			{
				platformView.TouchEvent -= OnTouch;
				platformView.KeyEvent -= OnKeyEvent;
			}
#endif
			base.DisconnectHandler(platformView);
		}

		public static void MapFormat(ITimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateFormat(timePicker);
#endif
		}

		public static void MapTime(ITimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateTime(timePicker);
#endif
		}

		public static void MapFont(ITimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateTizenFont(timePicker, handler.GetService<IFontManager>());
#endif
		}

		public static void MapTextColor(ITimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateTextColor(timePicker);
#endif
		}

		public static void MapCharacterSpacing(ITimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			Platform(handler)?.UpdateCharacterSpacing(timePicker);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// <c>IsOpen</c> is internal to <c>ITimePicker</c>, so an out-of-tree backend cannot
		/// read it. Deliberate no-op; the key is present so the mapper stays complete.
		/// </remarks>
		public static void MapIsOpen(ITimePickerHandler handler, ITimePicker timePicker)
		{
		}

#if TIZEN
		bool OnTouch(object source, global::Tizen.NUI.BaseComponents.View.TouchEventArgs e)
		{
			if (e.Touch.GetState(0) != global::Tizen.NUI.PointStateType.Up)
				return false;

			if (VirtualView is null)
				return false;

			_ = OpenPopupAsync();
			return true;
		}
#endif

#if TIZEN
		bool OnKeyEvent(object source, global::Tizen.NUI.BaseComponents.View.KeyEventArgs e)
		{
			if (!e.Key.IsAcceptKeyEvent())
				return false;

			_ = OpenPopupAsync();
			return true;
		}
#endif

#if TIZEN
		/// <remarks>
		/// The dialog works in <see cref="DateTime"/>, so the time is projected onto the zero
		/// date on the way in and reduced back to a <see cref="TimeSpan"/> on the way out. Only
		/// the time-of-day component is taken, so a dialog that rolls past midnight cannot
		/// produce a value greater than 24 hours.
		/// </remarks>
		async Task OpenPopupAsync()
		{
			if (VirtualView is null || _isOpen)
				return;

			_isOpen = true;

			try
			{
				await this.GetModalHost().RunModalAsync(async () =>
				{
					using var popup = new TizenDateTimePicker(default(DateTime) + (VirtualView.Time ?? TimeSpan.Zero), isTimePicker: true);

					try
					{
						var selected = await popup.Open().ConfigureAwait(false);

						// See TizenPickerHandler: a virtual-view write runs the mapper, so it
						// must be marshalled back to the main loop.
						this.DispatchIfRequired(() => VirtualView.Time = selected.TimeOfDay);
					}
					catch (OperationCanceledException)
					{
						// Dismissed without choosing; leave the time alone.
					}
				}).ConfigureAwait(false);
			}
			finally
			{
				_isOpen = false;
			}
		}
#endif
	}
}
