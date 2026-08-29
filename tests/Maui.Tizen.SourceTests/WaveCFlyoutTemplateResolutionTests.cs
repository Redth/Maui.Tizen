using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable regression tests for Shell flyout item template resolution.
/// </summary>
/// <remarks>
/// <para>
/// Almost all Wave C verification is source analysis plus an API15 type-check, because the backend
/// touches Tizen.NUI and cannot run on a host. The flyout template resolver is the exception: it is
/// pure <c>Microsoft.Maui.Controls</c> code with no NUI dependency, so it is compiled into this
/// project and <b>actually executed</b>.
/// </para>
/// <para>
/// That matters because this is where a real regression lived. An earlier revision passed a
/// <em>pre-resolved template owner</em> into <c>IShellController.GetFlyoutItemDataTemplate</c>.
/// That method picks between <see cref="Shell.MenuItemTemplateProperty"/> and
/// <see cref="Shell.ItemTemplateProperty"/> from its argument's own type, so handing it the owner -
/// which is not an <see cref="IMenuItemController"/> - silently selected the wrong property and
/// dropped <c>MenuItemTemplate</c> entirely. No amount of source analysis catches that; only
/// running the algorithm does.
/// </para>
/// </remarks>
public class WaveCFlyoutTemplateResolutionTests
{
	static DataTemplate NewTemplate(string marker) =>
		new(() => new Label { Text = marker, AutomationId = marker });

	static string MarkerOf(DataTemplate template) => ((Label)template.CreateContent()).AutomationId;

	// -----------------------------------------------------------------
	// The regression: MenuItemTemplate must survive the raw-item path
	// -----------------------------------------------------------------

	/// <summary>
	/// A <c>MenuItemTemplate</c> authored on the menu item itself must be found.
	/// </summary>
	[Fact]
	public void MenuItemTemplateAuthoredOnTheItemResolves()
	{
		var shell = new Shell();
		var menuItem = new MenuItem { Text = "About" };

		Shell.SetMenuItemTemplate(menuItem, NewTemplate("item-level"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, menuItem);

		Assert.NotNull(resolved);
		Assert.Equal("item-level", MarkerOf(resolved!));
	}

	/// <summary>
	/// A <c>MenuItemTemplate</c> authored on the item's parent must be found.
	/// </summary>
	/// <remarks>
	/// This is the branch the pre-resolved-owner bug broke: the template lives on the parent, so
	/// resolution has to consult the owner, but the menu-vs-item property choice must still be made
	/// from the <em>item</em>.
	/// </remarks>
	[Fact]
	public void MenuItemTemplateAuthoredOnTheParentResolves()
	{
		var shell = new Shell();
		var content = new ShellContent();
		var menuItem = new MenuItem { Text = "About" };

		content.MenuItems.Add(menuItem);
		Shell.SetMenuItemTemplate(content, NewTemplate("parent-level"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, menuItem);

		Assert.NotNull(resolved);
		Assert.Equal("parent-level", MarkerOf(resolved!));
	}

	/// <summary>
	/// A shell-level <c>MenuItemTemplate</c> applies to menu items with no template of their own.
	/// </summary>
	[Fact]
	public void ShellLevelMenuItemTemplateAppliesToMenuItems()
	{
		var shell = new Shell();
		var menuItem = new MenuItem { Text = "About" };

		Shell.SetMenuItemTemplate(shell, NewTemplate("shell-menu"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, menuItem);

		Assert.NotNull(resolved);
		Assert.Equal("shell-menu", MarkerOf(resolved!));
	}

	/// <summary>
	/// The regression itself: a shell-level <c>ItemTemplate</c> must NOT capture a menu item.
	/// </summary>
	/// <remarks>
	/// With the pre-resolved owner, the property choice fell to
	/// <see cref="Shell.ItemTemplateProperty"/>, so a menu item wrongly picked up the item template.
	/// </remarks>
	[Fact]
	public void ShellLevelItemTemplateDoesNotCaptureMenuItems()
	{
		var shell = new Shell();
		var menuItem = new MenuItem { Text = "About" };

		Shell.SetItemTemplate(shell, NewTemplate("shell-item"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, menuItem);

		Assert.True(
			resolved is null,
			"A Shell-level ItemTemplate must not apply to a MenuItem; MenuItemTemplate governs those.");
	}

	// -----------------------------------------------------------------
	// Non-menu items
	// -----------------------------------------------------------------

	[Fact]
	public void ItemTemplateAuthoredOnAShellItemResolves()
	{
		var shell = new Shell();
		var flyoutItem = new FlyoutItem { Title = "Browse" };

		Shell.SetItemTemplate(flyoutItem, NewTemplate("flyout-item"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, flyoutItem);

		Assert.NotNull(resolved);
		Assert.Equal("flyout-item", MarkerOf(resolved!));
	}

	[Fact]
	public void AnItemLevelTemplateWinsOverTheShellLevelTemplate()
	{
		var shell = new Shell();
		var flyoutItem = new FlyoutItem { Title = "Browse" };

		Shell.SetItemTemplate(shell, NewTemplate("shell-item"));
		Shell.SetItemTemplate(flyoutItem, NewTemplate("item-wins"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, flyoutItem);

		Assert.NotNull(resolved);
		Assert.Equal("item-wins", MarkerOf(resolved!));
	}

	// -----------------------------------------------------------------
	// Null means "use the Tizen platform default"
	// -----------------------------------------------------------------

	/// <summary>
	/// With no authored template anywhere, resolution returns <see langword="null"/>.
	/// </summary>
	/// <remarks>
	/// Load-bearing for Tizen. <c>IShellController.GetFlyoutItemDataTemplate</c> never returns null -
	/// it falls back to MAUI's generic <c>CreateDefaultFlyoutItemCell</c> - so if this returned a
	/// template, every app that never authored one would silently lose Tizen's own flyout item view.
	/// </remarks>
	[Fact]
	public void NoAuthoredTemplateReturnsNullSoTizenKeepsItsPlatformDefault()
	{
		var shell = new Shell();

		Assert.Null(ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, new FlyoutItem()));
		Assert.Null(ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, new MenuItem()));
	}

	/// <summary>
	/// An explicitly authored <see langword="null"/> template opts a single item out of the
	/// shell-level template, rather than falling through to it.
	/// </summary>
	[Fact]
	public void ExplicitNullTemplateOptsOutOfTheShellLevelTemplate()
	{
		var shell = new Shell();
		var flyoutItem = new FlyoutItem { Title = "Browse" };

		Shell.SetItemTemplate(shell, NewTemplate("shell-item"));
		Shell.SetItemTemplate(flyoutItem, null!);

		Assert.Null(ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, flyoutItem));
	}

	// -----------------------------------------------------------------
	// Contract details that must match the upstream API being adopted
	// -----------------------------------------------------------------

	[Fact]
	public void ANullFlyoutItemThrows()
		=> Assert.Throws<ArgumentNullException>(
			() => ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(new Shell(), null!));

	/// <summary>
	/// A null shell falls back to the item's own owning shell.
	/// </summary>
	[Fact]
	public void ANullShellResolvesTheOwningShellFromTheItem()
	{
		var shell = new Shell();
		var flyoutItem = new FlyoutItem { Title = "Browse" };

		shell.Items.Add(flyoutItem);
		Shell.SetItemTemplate(shell, NewTemplate("owning-shell"));

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(null, flyoutItem);

		Assert.NotNull(resolved);
		Assert.Equal("owning-shell", MarkerOf(resolved!));
	}

	/// <summary>
	/// The resolver must NOT resolve selectors; that is the caller's job upstream.
	/// </summary>
	/// <remarks>
	/// Pinned because resolving selectors internally would make adoption of the upstream API a
	/// silent behaviour change instead of a body swap.
	/// </remarks>
	[Fact]
	public void SelectorsAreReturnedUnresolvedForTheCallerToSelect()
	{
		var shell = new Shell();
		var flyoutItem = new FlyoutItem { Title = "Browse" };
		var selector = new MarkerSelector();

		Shell.SetItemTemplate(flyoutItem, selector);

		var resolved = ShellFlyoutTemplateResolution.ResolveFlyoutItemTemplate(shell, flyoutItem);

		Assert.Same(selector, resolved);
	}

	sealed class MarkerSelector : DataTemplateSelector
	{
		protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
			=> NewTemplate("selected");
	}
}
