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
	/// Tizen implementations of the <c>ITimePicker</c> property mappings.
	/// </summary>
	public static class TizenTimePickerExtensions
	{
		/// <remarks>Format and value both re-render the same text, so they share an implementation.</remarks>
		public static void UpdateFormat(this Entry platformTimePicker, ITimePicker timePicker) =>
			platformTimePicker.UpdateTime(timePicker);

		/// <summary>
		/// Renders the selected time.
		/// </summary>
		/// <remarks>
		/// <para>
		/// MAUI documents <c>TimePicker.Format</c> as a <see cref="DateTime"/> format string
		/// rather than a <see cref="TimeSpan"/> one, so the value is projected onto a
		/// <see cref="DateTime"/> before formatting. An unset format falls back to the current
		/// culture's short time pattern, read per call so a culture change is picked up.
		/// </para>
		/// <para>
		/// <see cref="ITimePicker.Time"/> is nullable; a null time renders as empty text rather
		/// than as midnight, so an unset picker reads as unset.
		/// </para>
		/// </remarks>
		public static void UpdateTime(this Entry platformTimePicker, ITimePicker timePicker)
		{
			var format = string.IsNullOrEmpty(timePicker.Format)
				? CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern
				: timePicker.Format;

			platformTimePicker.Text = timePicker.Time is TimeSpan time
				? new DateTime(time.Ticks).ToString(format, CultureInfo.CurrentCulture)
				: string.Empty;
		}
	}
}
