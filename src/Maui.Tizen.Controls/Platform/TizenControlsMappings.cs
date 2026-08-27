using System;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;

namespace Microsoft.Maui.Platforms.Tizen.Controls
{
	/// <summary>
	/// Binds the MAUI Controls properties that a backend package cannot reach on its own to the
	/// Tizen native implementations in <c>Maui.Tizen.Core</c>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This layer exists because several properties an app actually sets are declared on
	/// <b>Controls</b> types rather than on the Core interfaces a handler sees.
	/// <c>ILabel</c>, for example, carries only <c>TextDecorations</c> and <c>LineHeight</c> - not
	/// <c>LineBreakMode</c> - and the accessibility annotations are attached properties on
	/// <c>AutomationProperties</c>. Core cannot read any of them without referencing Controls and
	/// inverting the dependency direction.
	/// </para>
	/// <para>
	/// Upstream dotnet/maui solves this inside Controls itself, in per-platform partial classes
	/// such as <c>Label.Tizen.cs</c> and <c>Element.Tizen.cs</c>. An out-of-repo backend cannot
	/// contribute to those partials, so the same job is done here by appending to the static
	/// mappers - which is public API, and is the same mechanism Controls' own
	/// <c>RemapForControls</c> uses.
	/// </para>
	/// <para>
	/// Worth knowing when comparing behaviour: upstream's Tizen
	/// <c>MapAutomationPropertiesIsInAccessibleTree</c>,
	/// <c>MapAutomationPropertiesExcludedWithChildren</c> and <c>MapMaxLines</c> are all empty
	/// stubs (<c>//TODO : Need to impl</c> and <c>[MissingMapper]</c>), so the accessibility
	/// annotations bound here work where upstream's do not.
	/// </para>
	/// </remarks>
	public static class TizenControlsMappings
	{
		static bool _registered;

		/// <summary>
		/// Registers the Controls-to-Tizen mappings. Safe to call more than once.
		/// </summary>
		/// <remarks>
		/// Appends to the static Controls mappers, so it must run after Controls' own
		/// <c>RemapForControls</c> - which happens the first time a Controls type is constructed -
		/// and before a handler is connected. Calling it during application startup satisfies both.
		/// </remarks>
		public static void Register()
		{
			if (_registered)
				return;

			_registered = true;

			LabelHandler.Mapper.AppendToMapping(nameof(Label.LineBreakMode), MapLineBreakMode);

			// Both accessibility keys route to the SAME handler, which reads both properties.
			// They resolve onto one pair of NUI flags, so binding them independently would let
			// whichever key ran last overwrite the other.
			ViewHandler.ViewMapper.AppendToMapping(
				AutomationProperties.IsInAccessibleTreeProperty.PropertyName, MapAccessibility);

			ViewHandler.ViewMapper.AppendToMapping(
				AutomationProperties.ExcludedWithChildrenProperty.PropertyName, MapAccessibility);
		}

		/// <summary>Applies <c>Label.LineBreakMode</c> to the Tizen label.</summary>
		/// <param name="handler">The label handler.</param>
		/// <param name="label">The cross-platform label.</param>
		public static void MapLineBreakMode(ILabelHandler handler, ILabel label)
		{
			ArgumentNullException.ThrowIfNull(handler);

			if (label is not Label controlsLabel)
				return;

#if TIZEN
			if (handler.PlatformView is global::Tizen.UIExtensions.NUI.Label platformLabel)
				platformLabel.UpdateLineBreakMode(controlsLabel.LineBreakMode);
#endif
		}

		/// <summary>Applies both accessibility annotations to the Tizen view in one pass.</summary>
		/// <param name="handler">The view handler.</param>
		/// <param name="view">The cross-platform view.</param>
		public static void MapAccessibility(IViewHandler handler, IView view)
		{
			ArgumentNullException.ThrowIfNull(handler);

			if (view is not Element element)
				return;

#if TIZEN
			if (handler.PlatformView is global::Tizen.NUI.BaseComponents.View platformView)
			{
				platformView.UpdateAccessibility(
					AutomationProperties.GetIsInAccessibleTree(element),
					AutomationProperties.GetExcludedWithChildren(element));
			}
#endif
		}
	}
}
