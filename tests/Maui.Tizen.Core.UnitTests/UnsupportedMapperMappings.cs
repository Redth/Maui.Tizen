// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Evidence for mapper entries that intentionally resolve to an empty Tizen body.
	/// </summary>
	public static class UnsupportedMapperMappings
	{
		public static readonly IReadOnlyList<UnsupportedMapping> All =
		[
			new("TizenApplicationHandler", "OpenWindow", "MapOpenWindow", "command",
				"Tizen exposes one NUI window per process, so another window cannot be opened."),
			new("TizenDatePickerHandler", "IsOpen", "MapIsOpen", "property",
				"IDatePicker.IsOpen is internal and cannot be read by an out-of-tree backend."),
			new("TizenLabelHandler", "Padding", "MapPadding", "property",
				"dotnet/maui marks the Tizen label padding mapper as MissingMapper."),
			new("TizenPageHandler", "Title", "MapTitle", "property",
				"dotnet/maui marks the base Tizen page title mapper as MissingMapper."),
			new("TizenPickerHandler", "IsOpen", "MapIsOpen", "property",
				"IPicker.IsOpen is internal and cannot be read by an out-of-tree backend."),
			new("TizenRadioButtonHandler", "CharacterSpacing", "MapCharacterSpacing", "property",
				"Text styling belongs to the templated content's own label handler."),
			new("TizenRadioButtonHandler", "Font", "MapFont", "property",
				"Text styling belongs to the templated content's own label handler."),
			new("TizenRadioButtonHandler", "IsChecked", "MapIsChecked", "property",
				"The checked visual is supplied by the radio button content template, not a native indicator."),
			new("TizenRadioButtonHandler", "TextColor", "MapTextColor", "property",
				"Text styling belongs to the templated content's own label handler."),
			new("TizenSearchBarHandler", "CancelButtonColor", "MapCancelButtonColor", "property",
				"The Tizen search bar has no cancel affordance to tint."),
			new("TizenSearchBarHandler", "SearchIconColor", "MapSearchIconColor", "property",
				"The search icon drawable exposes no tint property."),
			new("TizenTimePickerHandler", "IsOpen", "MapIsOpen", "property",
				"ITimePicker.IsOpen is internal and cannot be read by an out-of-tree backend."),
			new("TizenViewMappers", "MaximumHeight", "MapMaximumHeight", "property",
				"NUI MaximumSize does not behave correctly; dotnet/maui leaves this mapping empty."),
			new("TizenViewMappers", "MaximumWidth", "MapMaximumWidth", "property",
				"NUI MaximumSize does not behave correctly; dotnet/maui leaves this mapping empty."),
			new("TizenViewMappers", "ToolTip", "MapToolTip", "property",
				"NUI has no tooltip primitive; dotnet/maui leaves this mapping empty."),
			new("TizenWindowHandler", "Title", "MapTitle", "property",
				"dotnet/maui leaves the Tizen window title mapper empty."),
		];

		public static bool IsUnsupported(string owner, string key) =>
			All.Any(mapping =>
				mapping.Owner == owner &&
				mapping.Key == key);

		public sealed record UnsupportedMapping(
			string Owner,
			string Key,
			string Method,
			string Kind,
			string Evidence);
	}
}
