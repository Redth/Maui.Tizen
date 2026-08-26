// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using Microsoft.Maui;
using Microsoft.Maui.Platforms.Tizen.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// The control-handler inventory shared by the Wave A tests.
	/// </summary>
	/// <remarks>
	/// Written out by hand rather than discovered by reflection. A test that discovers whatever
	/// exists and then checks it is consistent with itself cannot fail when a handler is
	/// accidentally dropped from registration; this one can.
	/// </remarks>
	public static class TizenControlHandlers
	{
		/// <summary>
		/// Every control handler, with the MAUI interface it serves and the neutral MAUI handler
		/// whose mapper it has to remain a superset of.
		/// </summary>
		public static readonly IReadOnlyList<ControlHandlerCase> All =
		[
			new(typeof(TizenActivityIndicatorHandler), typeof(IActivityIndicator), "ActivityIndicatorHandler"),
			new(typeof(TizenButtonHandler), typeof(IButton), "ButtonHandler"),
			new(typeof(TizenCheckBoxHandler), typeof(ICheckBox), "CheckBoxHandler"),
			new(typeof(TizenDatePickerHandler), typeof(IDatePicker), "DatePickerHandler"),
			new(typeof(TizenEditorHandler), typeof(IEditor), "EditorHandler"),
			new(typeof(TizenEntryHandler), typeof(IEntry), "EntryHandler"),
			new(typeof(TizenPickerHandler), typeof(IPicker), "PickerHandler"),
			new(typeof(TizenProgressBarHandler), typeof(IProgress), "ProgressBarHandler"),
			new(typeof(TizenRadioButtonHandler), typeof(IRadioButton), "RadioButtonHandler"),
			new(typeof(TizenSearchBarHandler), typeof(ISearchBar), "SearchBarHandler"),
			new(typeof(TizenSliderHandler), typeof(ISlider), "SliderHandler"),
			new(typeof(TizenStepperHandler), typeof(IStepper), "StepperHandler"),
			new(typeof(TizenSwitchHandler), typeof(ISwitch), "SwitchHandler"),
			new(typeof(TizenTimePickerHandler), typeof(ITimePicker), "TimePickerHandler"),
		];

		/// <summary>xUnit member data over <see cref="All"/>.</summary>
		public static IEnumerable<object[]> TestData() => All.Select(h => new object[] { h });

		/// <summary>Reads the keys of a public static property mapper field.</summary>
		public static IReadOnlySet<string> GetMapperKeys(Type handlerType, string fieldName = "Mapper")
		{
			var field = handlerType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static)
				?? throw new InvalidOperationException($"{handlerType.Name} has no public static '{fieldName}' field.");

			var mapper = field.GetValue(null) as IPropertyMapper
				?? throw new InvalidOperationException($"{handlerType.Name}.{fieldName} is not an {nameof(IPropertyMapper)}.");

			return mapper.GetKeys().ToHashSet(StringComparer.Ordinal);
		}

		/// <summary>
		/// Reads the keys of MAUI's own handler mapper for the same control.
		/// </summary>
		/// <remarks>
		/// MAUI splits some mappers (a button also has <c>TextButtonMapper</c> and
		/// <c>ImageButtonMapper</c>), but <c>Mapper</c> is the composed one actually used at
		/// runtime, so it is the correct comparison target.
		/// </remarks>
		public static IReadOnlySet<string> GetNeutralMapperKeys(string neutralHandlerName)
		{
			var handlerType = typeof(IView).Assembly.GetType($"Microsoft.Maui.Handlers.{neutralHandlerName}")
				?? throw new InvalidOperationException($"Microsoft.Maui.Handlers.{neutralHandlerName} was not found.");

			return GetMapperKeys(handlerType);
		}

		/// <summary>A control handler and the MAUI types it must stay in step with.</summary>
		/// <param name="HandlerType">The Tizen handler.</param>
		/// <param name="VirtualViewType">The MAUI interface it serves.</param>
		/// <param name="NeutralHandlerName">MAUI's own handler for the same control.</param>
		public sealed record ControlHandlerCase(Type HandlerType, Type VirtualViewType, string NeutralHandlerName)
		{
			/// <inheritdoc />
			public override string ToString() => HandlerType.Name;
		}
	}
}
