// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Forces MAUI Controls' static handler remaps to run.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>Microsoft.Maui.Controls</c> calls <c>RemapForControls</c> from each control's static
	/// constructor, mutating MAUI's static handler mappers in place. Until that has happened,
	/// <c>LabelHandler.Mapper</c> has no <c>FormattedText</c>, <c>CheckBoxHandler.Mapper</c> has
	/// no <c>Color</c>, and none of the accessibility keys exist.
	/// </para>
	/// <para>
	/// A static constructor runs at a time the CLR chooses, so a parity test that merely
	/// references Controls types is racing it. Forcing the constructors makes the measurement
	/// deterministic, and makes the resulting numbers mean "parity with what an application
	/// actually sees" rather than "parity with Core in isolation".
	/// </para>
	/// </remarks>
	public static class ControlsRemap
	{
		static readonly object _gate = new();
		static bool _forced;

		/// <summary>
		/// Runs every relevant Controls static constructor exactly once.
		/// </summary>
		public static void Force()
		{
			lock (_gate)
			{
				if (_forced)
					return;

				foreach (var type in ControlTypes)
					RuntimeHelpers.RunClassConstructor(type.TypeHandle);

				_forced = true;
			}
		}

		/// <summary>
		/// The Controls types whose static constructors remap a Wave A handler.
		/// </summary>
		public static IReadOnlyList<Type> ControlTypes { get; } =
		[
			typeof(Controls.Button),
			typeof(Controls.CheckBox),
			typeof(Controls.DatePicker),
			typeof(Controls.Editor),
			typeof(Controls.Entry),
			typeof(Controls.Label),
			typeof(Controls.Picker),
			typeof(Controls.ProgressBar),
			typeof(Controls.RadioButton),
			typeof(Controls.SearchBar),
			typeof(Controls.Slider),
			typeof(Controls.Stepper),
			typeof(Controls.Switch),
			typeof(Controls.TimePicker),
			typeof(Controls.ActivityIndicator),
		];
	}
}
