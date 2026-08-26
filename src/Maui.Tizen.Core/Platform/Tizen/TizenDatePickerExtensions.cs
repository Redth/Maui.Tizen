// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui;
using Microsoft.Maui.Platform;
using System;
using System.Globalization;
using Tizen.UIExtensions.NUI;

namespace Microsoft.Maui.Platforms.Tizen
{
	/// <summary>
	/// Tizen implementations of the <c>IDatePicker</c> property mappings.
	/// </summary>
	public static class TizenDatePickerExtensions
	{
		/// <remarks>Format and value both re-render the same text, so they share an implementation.</remarks>
		public static void UpdateFormat(this Entry platformDatePicker, IDatePicker datePicker) =>
			platformDatePicker.UpdateDate(datePicker);

		/// <summary>
		/// Renders the selected date, clamped to the picker's own minimum and maximum.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The clamp matters because Tizen's date dialog cannot express a valid range: the
		/// limits are enforced here and again when the dialog's result is accepted, so an
		/// out-of-range date can never be displayed as if it were selectable.
		/// </para>
		/// <para>
		/// <see cref="IDatePicker.Date"/> is nullable; a null date renders as empty text rather
		/// than as some substitute date, so an unset picker reads as unset.
		/// </para>
		/// </remarks>
		public static void UpdateDate(this Entry platformDatePicker, IDatePicker datePicker)
		{
			var date = datePicker.ClampDate(datePicker.Date);

			platformDatePicker.Text = date is null
				? string.Empty
				: date.Value.ToString(datePicker.Format, CultureInfo.CurrentCulture);
		}

		/// <summary>
		/// Constrains a date to <see cref="IDatePicker.MinimumDate"/>..<see cref="IDatePicker.MaximumDate"/>.
		/// </summary>
		/// <remarks>
		/// An inverted or absent range is treated as no constraint: rejecting the value would
		/// throw from inside a property mapper, and MAUI allows the bounds to be set in either
		/// order.
		/// </remarks>
		public static DateTime? ClampDate(this IDatePicker datePicker, DateTime? value)
		{
			if (value is not DateTime date)
				return null;

			var min = datePicker.MinimumDate;
			var max = datePicker.MaximumDate;

			if (min is not null && max is not null && max < min)
				return date;

			if (min is DateTime minimum && date < minimum)
				return minimum;

			if (max is DateTime maximum && date > maximum)
				return maximum;

			return date;
		}
	}
}
