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
	/// The Tizen handler for <see cref="IDatePicker"/>.
	/// </summary>
	/// <remarks>
	/// Presented as a read-only entry that opens <see cref="TizenDateTimePicker"/>.
	/// </remarks>
	public class TizenDatePickerHandler : TizenViewHandler<IDatePicker, TizenPickerView>
	{
#if TIZEN
		bool _isOpen;
#endif

		/// <summary>The complete property mapper for <see cref="IDatePicker"/>.</summary>
		public static readonly IPropertyMapper<IDatePicker, TizenDatePickerHandler> Mapper =
			new PropertyMapper<IDatePicker, TizenDatePickerHandler>(TizenViewMappers.ViewMapper)
			{
				[nameof(IDatePicker.Format)] = MapFormat,
				[nameof(IDatePicker.Date)] = MapDate,
				[nameof(IDatePicker.MinimumDate)] = MapMinimumDate,
				[nameof(IDatePicker.MaximumDate)] = MapMaximumDate,
				[nameof(IDatePicker.Font)] = MapFont,
				[nameof(IDatePicker.TextColor)] = MapTextColor,
				[nameof(IDatePicker.CharacterSpacing)] = MapCharacterSpacing,
				["IsOpen"] = MapIsOpen,
			};

		/// <summary>The complete command mapper for <see cref="IDatePicker"/>.</summary>
		public static readonly CommandMapper<IDatePicker, TizenDatePickerHandler> CommandMapper =
			new(TizenViewMappers.ViewCommandMapper);

		public TizenDatePickerHandler()
			: base(Mapper, CommandMapper)
		{
		}

		public TizenDatePickerHandler(IPropertyMapper? mapper, CommandMapper? commandMapper = null)
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

		public static void MapFormat(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateFormat(datePicker);
#endif
		}

		public static void MapDate(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateDate(datePicker);
#endif
		}

		/// <summary>
		/// Re-renders the date so it reflects a changed lower bound.
		/// </summary>
		/// <remarks>
		/// Upstream leaves the bounds unimplemented. Tizen's date dialog cannot be given a
		/// range, so the bound is enforced by clamping instead: on display here, and again when
		/// the dialog's result is accepted. That makes the limit real rather than advisory.
		/// </remarks>
		public static void MapMinimumDate(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateDate(datePicker);
#endif
		}

		/// <summary>Re-renders the date so it reflects a changed upper bound.</summary>
		/// <remarks>See <see cref="MapMinimumDate"/>.</remarks>
		public static void MapMaximumDate(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateDate(datePicker);
#endif
		}

		public static void MapFont(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTizenFont(datePicker, handler.GetService<IFontManager>());
#endif
		}

		public static void MapTextColor(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateTextColor(datePicker);
#endif
		}

		public static void MapCharacterSpacing(TizenDatePickerHandler handler, IDatePicker datePicker)
		{
#if TIZEN
			handler.PlatformView?.UpdateCharacterSpacing(datePicker);
#endif
		}

		/// <summary>Not supported on Tizen.</summary>
		/// <remarks>
		/// <c>IsOpen</c> is internal to <c>IDatePicker</c>, so an out-of-tree backend cannot
		/// read it. Deliberate no-op; the key is present so the mapper stays complete.
		/// </remarks>
		public static void MapIsOpen(TizenDatePickerHandler handler, IDatePicker datePicker)
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
		async Task OpenPopupAsync()
		{
			if (VirtualView is null || _isOpen)
				return;

			_isOpen = true;

			try
			{
				await this.GetModalHost().RunModalAsync(async () =>
				{
					using var popup = new TizenDateTimePicker(
						VirtualView.Date ?? DateTime.Today,
						isTimePicker: false,
						VirtualView.MinimumDate,
						VirtualView.MaximumDate);

					try
					{
						var selected = await popup.Open().ConfigureAwait(false);

						// See TizenPickerHandler: a virtual-view write runs the mapper, so it
						// must be marshalled back to the main loop.
						this.DispatchIfRequired(() => VirtualView.Date = selected);
					}
					catch (OperationCanceledException)
					{
						// Dismissed without choosing; leave the date alone.
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
