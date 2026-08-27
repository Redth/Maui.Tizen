using System;
using Microsoft.Maui.Controls;

namespace Microsoft.Maui.Platforms.Tizen.Adapters
{
	/// <summary>
	/// Resolves the <see cref="DataTemplate"/> that renders a Shell flyout item.
	/// </summary>
	/// <remarks>
	/// <para>
	/// This is the <b>adoption seam</b> for dotnet/maui#37862. Its signature is deliberately
	/// identical to the redesigned upstream API:
	/// </para>
	/// <code>
	/// static DataTemplate? Shell.ResolveFlyoutItemTemplate(Shell? shell, BindableObject flyoutItem)
	/// </code>
	/// <para>
	/// so that adopting it is a one-line body swap here, with no call-site churn and no behaviour
	/// re-derivation at a point where the design is no longer fresh in anyone's mind. The PR is
	/// still open, so nothing is bound to it yet.
	/// </para>
	/// <para>
	/// <b>Do not surface the template owner.</b> Earlier revisions of this backend exposed the
	/// "bindable object that owns the template" as a separate step and passed it around. That is an
	/// implementation detail of the resolution, not a value callers should hold: the binding context
	/// for a flyout item is always the <em>item</em>, never its template owner. Keeping the owner
	/// private to this method is what makes the upstream swap safe.
	/// </para>
	/// </remarks>
	public static class ShellFlyoutTemplateResolution
	{
		/// <summary>
		/// Returns the authored flyout item template for <paramref name="flyoutItem"/>, or
		/// <see langword="null"/> when the app has not authored one.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Returning <see langword="null"/> rather than a default template is load-bearing. The
		/// public <c>IShellController.GetFlyoutItemDataTemplate</c> never returns null - it falls
		/// back to <c>BaseShellItem.CreateDefaultFlyoutItemCell</c> - so calling it unguarded would
		/// silently replace Tizen's own flyout item view with MAUI's generic cell for every app that
		/// never authored a template. Upstream's in-tree selector guards it with the same
		/// <c>IsSet</c> probe reproduced below, and the redesigned API returns
		/// <c>DataTemplate?</c> for exactly this reason.
		/// </para>
		/// <para>
		/// The raw <paramref name="flyoutItem"/> is passed to
		/// <c>GetFlyoutItemDataTemplate</c> deliberately. That method re-derives the template owner
		/// itself, <em>including</em> the <c>MenuShellItem</c> branch that is unreachable from
		/// outside <c>Microsoft.Maui.Controls</c> - so handing it a pre-resolved owner both loses
		/// that branch and picks the wrong <see cref="BindableProperty"/>, because the
		/// menu-vs-item choice is made from the argument's own type. An earlier revision of this
		/// backend did exactly that and silently dropped <c>MenuItemTemplate</c>.
		/// </para>
		/// </remarks>
		public static DataTemplate? ResolveFlyoutItemTemplate(Shell? shell, BindableObject? flyoutItem)
		{
			if (shell is null || flyoutItem is null)
			{
				return null;
			}

			BindableProperty templateProperty = flyoutItem is IMenuItemController
				? Shell.MenuItemTemplateProperty
				: Shell.ItemTemplateProperty;

			// Owner resolution is confined to this probe and never leaves the method.
			BindableObject owner = ShellTemplateResolver.GetBindableObjectWithFlyoutItemTemplate(flyoutItem);

			if (!owner.IsSet(templateProperty) && !shell.IsSet(templateProperty))
			{
				return null;
			}

			DataTemplate? template = ((IShellController)shell).GetFlyoutItemDataTemplate(flyoutItem);

			// The template may itself be a selector; the item is both the data and the selector
			// input, and the shell is the container - matching upstream.
			return template.SelectDataTemplate(flyoutItem, shell);
		}
	}
}
