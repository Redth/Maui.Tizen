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
	public class TizenTimePickerHandler : TizenViewHandler<ITimePicker, TizenPickerView>
	{
#if TIZEN
		bool _isOpen;
#endif

		/// <summary>The complete property mapper for <see cref="ITimePicker"/>.</summary>
		public static readonly IPropertyMapper<ITimePicker, TizenTimePickerHandler> Mapper =
			new PropertyMapper<ITimePicker, TizenTimePickerHandler>(ViewHandler.ViewMapper)
			{
				[nameof(ITimePicker.Format)] = MapFormat,
				[nameof(ITimePicker.Time)] = MapTime,
				[nameof(ITimePicker.Font)] = MapFont,
				[nameof(ITimePicker.TextColor)] = MapTextColor,
				[nameof(ITimePicker.CharacterSpacing)] = MapCharacterSpacing,
				["IsOpen"] = MapIsOpen,
			};

		/// <summary>The complete command mapper for <see cref="ITimePicker"/>.</summary>
		public static readonly CommandMapper<ITimePicker, TizenTimePickerHandler> CommandMapper =
			new(ViewHandler.ViewCommandMapper);

		public TizenTimePickerHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenTimePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
			: base(mapper ?? Mapper, commandMapper ?? CommandMapper)
		{
		}

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

		public static void MapFormat(TizenTimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateFormat(timePicker);
#endif
		}

		public static void MapTime(TizenTimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTime(timePicker);
#endif
		}

		public static void MapFont(TizenTimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTizenFont(timePicker, handler.GetService<IFontManager>());
#endif
		}

		public static void MapTextColor(TizenTimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(timePicker);
#endif
		}

		public static void MapCharacterSpacing(TizenTimePickerHandler handler, ITimePicker timePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(timePicker);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// <c>IsOpen</c> is internal to <c>ITimePicker</c>, so an out-of-tree backend cannot
		/// read it. Deliberate no-op; the key is present so the mapper stays complete.
		/// </remarks>
		public static void MapIsOpen(TizenTimePickerHandler handler, ITimePicker timePicker)
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
						VirtualView.Time = selected.TimeOfDay;
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
