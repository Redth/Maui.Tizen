using System;
using System.Collections.Generic;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Public-API replacements for the <c>Microsoft.Maui.Controls</c> internal helpers that the
	/// in-tree Tizen backend used to call.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Every member here exists because the in-tree Tizen backend compiled inside the
	/// <c>Microsoft.Maui.Controls</c> assembly and could therefore reach <c>internal</c> members. An
	/// out-of-tree backend cannot, so each one is reimplemented on top of published API.
	/// </para>
	/// <para>
	/// These are behaviour-preserving reimplementations, not reflection shims. If upstream ever
	/// makes the corresponding member public, the body here should be replaced by a direct call
	/// rather than kept as a parallel implementation - see <see cref="UpstreamApiRequests"/>.
	/// </para>
	/// </remarks>
	public static class ShellElementTree
	{
		/// <summary>
		/// Walks the logical parent chain looking for the closest ancestor of type
		/// <typeparamref name="T"/>.
		/// </summary>
		/// <remarks>
		/// Replaces the internal <c>Element.FindParentOfType&lt;T&gt;</c>. Upstream walks
		/// <c>RealParent</c> and stops at the <see cref="Application"/>. This overload is declared
		/// on <see cref="IElement"/> rather than <see cref="Element"/> because several things a
		/// handler needs to walk from - <see cref="Toolbar"/> among them - are not
		/// <see cref="Element"/>s, and <see cref="IElement.Parent"/> is the published chain that
		/// covers both.
		/// </remarks>
		public static T? FindParentOfType<T>(this IElement? element, bool includeThis = false)
			where T : class, IElement
		{
			if (element is null)
			{
				return null;
			}

			if (includeThis && element is T self)
			{
				return self;
			}

			for (IElement? current = element.Parent; current is not null; current = current.Parent)
			{
				if (current is T match)
				{
					return match;
				}

				if (current is Application)
				{
					break;
				}
			}

			return null;
		}

		/// <summary>
		/// Returns the page currently displayed by the shell.
		/// </summary>
		/// <remarks>
		/// Replaces the internal <c>Shell.GetCurrentShellPage()</c>. Upstream reads the top of the
		/// current section's navigation stack and falls back to the current content's page; both
		/// steps are expressible through <see cref="IShellContentController"/>, which is public.
		/// <see cref="Shell.CurrentPage"/> is deliberately not used on its own because it does not
		/// reproduce the navigation-stack-first ordering when a modal-free push is in flight.
		/// </remarks>
		public static Page? GetCurrentShellPage(this Shell? shell)
		{
			if (shell is null)
			{
				return null;
			}

			// Shell.CurrentSection is internal; CurrentItem.CurrentItem is the published path to
			// the same ShellSection.
			ShellSection? section = shell.CurrentItem?.CurrentItem;
			IReadOnlyList<Page>? navigationStack = section?.Navigation?.NavigationStack;

			if (navigationStack is { Count: > 0 })
			{
				Page? top = navigationStack[navigationStack.Count - 1];

				if (top is not null)
				{
					return top;
				}
			}

			return (section?.CurrentItem as IShellContentController)?.Page;
		}

		/// <summary>
		/// Resolves the effective value of <paramref name="property"/> by walking from the current
		/// shell page up to the shell itself, returning the first explicitly set value.
		/// </summary>
		/// <remarks>
		/// Replaces the internal <c>Shell.GetEffectiveValue&lt;T&gt;</c>. The walk, the
		/// <see cref="BindableObject.IsSet(BindableProperty)"/> test and the default fallback all
		/// mirror upstream. The <c>ignoreImplicit</c> overload is not reproduced because it depends
		/// on the internal <c>Routing.IsImplicit</c>; no Tizen call site needs it.
		/// </remarks>
		public static T? GetEffectiveValue<T>(this Shell? shell, BindableProperty property, T? defaultValue)
		{
			ArgumentNullException.ThrowIfNull(property);

			if (shell is null)
			{
				return defaultValue;
			}

			Element? element = shell.GetCurrentShellPage() ?? (Element?)shell.CurrentItem?.CurrentItem?.CurrentItem;

			while (element is not null && !ReferenceEquals(element, shell))
			{
				if (element.IsSet(property))
				{
					return (T?)element.GetValue(property);
				}

				element = element.Parent;
			}

			return shell.IsSet(property) ? (T?)shell.GetValue(property) : defaultValue;
		}

		/// <summary>
		/// Returns the shell's toolbar through the published <see cref="IToolbarElement"/> contract.
		/// </summary>
		/// <remarks>
		/// Replaces the internal <c>Shell.Toolbar</c> property. <see cref="IToolbarElement"/> is
		/// public and every shell implements it, so this is a pure accessibility fix rather than a
		/// reimplementation.
		/// </remarks>
		public static IToolbar? GetToolbar(this Shell? shell) => (shell as IToolbarElement)?.Toolbar;
	}
}
