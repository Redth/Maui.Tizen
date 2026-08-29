using System;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Public-API replacements for the template-resolution helpers the in-tree Tizen backend
	/// reached through <c>internal</c> members of <c>Microsoft.Maui.Controls</c>.
	/// </summary>
	internal static class ShellTemplateResolver
	{
		/// <summary>
		/// Returns the bindable object that actually carries the flyout item template for
		/// <paramref name="bindable"/>.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Replaces the internal <c>Shell.GetBindableObjectWithFlyoutItemTemplate</c>. For menu
		/// items the template is authored on the owning element rather than on the menu item
		/// itself, so upstream redirects to the parent when that owner sets
		/// <see cref="Shell.MenuItemTemplateProperty"/>.
		/// </para>
		/// <para>
		/// KNOWN BEHAVIOUR GAP: upstream has a second redirect for <c>MenuShellItem</c>, which
		/// forwards to the <see cref="MenuItem"/> it wraps. <c>MenuShellItem</c> and its
		/// <c>MenuItem</c> property are both <c>internal</c>, so an out-of-tree backend cannot
		/// reproduce that branch at all - not even awkwardly. A flyout built from
		/// <c>Shell.Items</c> containing a bare <see cref="MenuItem"/> therefore falls back to the
		/// shell-level template instead of a menu-item-level one. This is the strongest argument in
		/// <see cref="UpstreamApiRequests"/> entry <c>MAUI-TIZEN-API-0001</c>.
		/// </para>
		/// </remarks>
		public static BindableObject GetBindableObjectWithFlyoutItemTemplate(BindableObject bindable)
		{
			ArgumentNullException.ThrowIfNull(bindable);

			if (bindable is MenuItem menuItem &&
				menuItem.Parent is BindableObject owner &&
				owner.IsSet(Shell.MenuItemTemplateProperty))
			{
				return owner;
			}

			return bindable;
		}

		/// <summary>
		/// Returns the flyout item template property that applies to <paramref name="bindable"/>.
		/// </summary>
		public static BindableProperty GetFlyoutItemTemplateProperty(BindableObject bindable)
			=> bindable is IMenuItemController ? Shell.MenuItemTemplateProperty : Shell.ItemTemplateProperty;

		/// <summary>
		/// Resolves <paramref name="template"/> against <paramref name="item"/>, unwrapping data
		/// template selectors.
		/// </summary>
		/// <remarks>
		/// Replaces the internal <c>DataTemplateExtensions.SelectDataTemplate</c>. Upstream simply
		/// forwards to <see cref="DataTemplateSelector.SelectTemplate"/> when the template is a
		/// selector, which is public. Selectors are resolved repeatedly because
		/// <see cref="DataTemplateSelector.SelectTemplate"/> is allowed to return another selector.
		/// </remarks>
		public static DataTemplate? SelectDataTemplate(this DataTemplate? template, object? item, BindableObject container)
		{
			DataTemplate? resolved = template;

			// Guard against a selector cycle rather than trusting authored templates to terminate.
			for (int depth = 0; resolved is DataTemplateSelector selector && depth < 8; depth++)
			{
				DataTemplate? next = selector.SelectTemplate(item, container);

				if (next is null || ReferenceEquals(next, resolved))
				{
					break;
				}

				resolved = next;
			}

			return resolved;
		}

		public static View CreateViewFromTemplate(
			this DataTemplate template,
			object? item,
			BindableObject container,
			string description)
		{
			ArgumentNullException.ThrowIfNull(template);
			ArgumentNullException.ThrowIfNull(container);

			var selected = template.SelectDataTemplate(item, container);
			if (selected is null || selected is DataTemplateSelector)
				throw new InvalidOperationException($"The {description} template selector did not return a concrete template.");

			return selected.CreateContent() as View
				?? throw new InvalidOperationException($"The {description} template must create a View.");
		}
	}
}
