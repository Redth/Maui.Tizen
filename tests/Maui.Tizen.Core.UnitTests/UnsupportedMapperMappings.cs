// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Maui.Platforms.Tizen.UnitTests
{
	/// <summary>
	/// Evidence for mapper entries that terminate in an empty Tizen body.
	/// </summary>
	public static class UnsupportedMapperMappings
	{
		public static readonly IReadOnlyList<UnsupportedMapping> All =
		[
			new("TizenApplicationHandler", "OpenWindow", "MapOpenWindow", "command",
				"Tizen exposes one NUI window per process, so another window cannot be opened."),
			new("TizenEditorHandler", "IsSpellCheckEnabled", "MapIsSpellCheckEnabled", "property",
				"Tizen's editor exposes no spell-check switch independent of text prediction.",
				"TizenEditorExtensions.cs", "UpdateIsSpellCheckEnabled"),
			new("TizenEntryHandler", "ClearButtonVisibility", "MapClearButtonVisibility", "property",
				"NUI Entry has no built-in clear affordance or internal drawing surface.",
				"TizenEntryExtensions.cs", "UpdateClearButtonVisibility"),
			new("TizenEntryHandler", "IsSpellCheckEnabled", "MapIsSpellCheckEnabled", "property",
				"Tizen's entry exposes no spell-check switch independent of text prediction.",
				"TizenEntryExtensions.cs", "UpdateIsSpellCheckEnabled"),
			new("TizenLabelHandler", "Padding", "MapPadding", "property",
				"dotnet/maui marks the Tizen label padding mapper as MissingMapper."),
			new("TizenPageHandler", "Title", "MapTitle", "property",
				"dotnet/maui marks the base Tizen page title mapper as MissingMapper."),
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
			new("TizenSearchBarHandler", "IsSpellCheckEnabled", "MapIsSpellCheckEnabled", "property",
				"Tizen's entry exposes no spell-check switch independent of text prediction.",
				"TizenEntryExtensions.cs", "UpdateIsSpellCheckEnabled"),
			new("TizenViewMappers", "FlowDirection", "MapFlowDirection", "property",
				"NUI exposes no implemented flow-direction update in the compiled backend.",
				"TizenPlatformExtensions.cs", "UpdateFlowDirection"),
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
			string Evidence,
			string? TerminalFile = null,
			string? TerminalMethod = null);
	}
}
