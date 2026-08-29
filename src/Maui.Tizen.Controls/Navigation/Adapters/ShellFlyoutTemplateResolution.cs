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
	internal static class ShellFlyoutTemplateResolution
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
		/// never authored a template. Null here means "the app supplied no template, use the
		/// platform default", which is exactly what the redesigned upstream API means.
		/// </para>
		/// <para>
		/// The returned template MAY be a <see cref="DataTemplateSelector"/>. Resolving it is the
		/// caller's job, matching upstream - see the call sites in
		/// <c>TizenShellFlyoutItemAdaptor</c>. This method deliberately does NOT resolve selectors,
		/// because doing so would make its contract differ from the API it stands in for and turn
		/// adoption into a behaviour change rather than a body swap.
		/// </para>
		/// <para>
		/// An explicitly authored <c>null</c> template is honoured as an opt-out from any
		/// Shell-level template, rather than falling through to it.
		/// </para>
		/// </remarks>
		public static DataTemplate? ResolveFlyoutItemTemplate(Shell? shell, BindableObject flyoutItem)
		{
			ArgumentNullException.ThrowIfNull(flyoutItem);

			shell ??= (flyoutItem as Element).FindParentOfType<Shell>();

			BindableProperty templateProperty = flyoutItem is IMenuItemController
				? Shell.MenuItemTemplateProperty
				: Shell.ItemTemplateProperty;

			// Owner resolution is confined to this method and never surfaces to callers.
			BindableObject templateSource = ShellTemplateResolver.GetBindableObjectWithFlyoutItemTemplate(flyoutItem);

			// An explicitly set template wins even when its value is null, which is how an app opts a
			// single item out of a Shell-level template. A null value is reported as "no template" so
			// the caller falls back to the platform default.
			if (templateSource.IsSet(templateProperty))
			{
				return templateSource.GetValue(templateProperty) as DataTemplate;
			}

			if (shell is not null && shell.IsSet(templateProperty))
			{
				return shell.GetValue(templateProperty) as DataTemplate;
			}

			return null;
		}
	}
}
