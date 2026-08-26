using System.Reflection;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Expiry tests for the provisional Tizen-owned adapters that stand in for missing upstream API.
/// </summary>
/// <remarks>
/// <para>
/// Wave C replaced nine <c>Microsoft.Maui.Controls</c> internals with public-API implementations.
/// Two of those replacements are <em>provisional</em>: they exist only because upstream publishes
/// no equivalent, and requests to publish one are open. When those land, the adapter should be
/// deleted rather than kept as a parallel implementation that slowly diverges.
/// </para>
/// <para>
/// A provisional adapter with a TODO is indistinguishable from a permanent one after a few months.
/// These tests reflect over the <em>actual referenced MAUI assemblies</em> and fail the moment the
/// upstream API appears, so the cleanup is forced rather than remembered.
/// </para>
/// </remarks>
public class WaveCUpstreamExpiryTests
{
	const BindingFlags AnyMember =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

	/// <summary>
	/// Expires <c>Adapters/ToolbarDrawerToggle.cs</c> (request MAUI-TIZEN-API-0009).
	/// </summary>
	/// <remarks>
	/// <c>IToolbar</c> publishes <c>BackButtonVisible</c> and <c>IsVisible</c> but not
	/// <c>DrawerToggleVisible</c>, the third member of the same concept. Until it does, Tizen keeps
	/// the flag in a <c>ConditionalWeakTable</c> attached to the toolbar instance.
	/// </remarks>
	[Fact]
	public void ToolbarDrawerToggleAdapterExpiresWhenIToolbarPublishesTheProperty()
	{
		var toolbar = NeutralMaui.Core.GetType("Microsoft.Maui.IToolbar");

		Assert.NotNull(toolbar);

		var published = toolbar!.GetProperty("DrawerToggleVisible", AnyMember);

		Assert.True(
			published is null,
			"IToolbar now publishes DrawerToggleVisible. Delete Adapters/ToolbarDrawerToggle.cs, "
				+ "re-point its call sites at the property, and remove MAUI-TIZEN-API-0009 from "
				+ "Adapters/UpstreamApiRequests.cs.");
	}

	/// <summary>
	/// Expires the flyout-template half of <c>Adapters/ShellTemplateResolver.cs</c>
	/// (request MAUI-TIZEN-API-0001, upstream dotnet/maui#37862).
	/// </summary>
	/// <remarks>
	/// <para>
	/// This adapter is only a <em>partial</em> reimplementation. Upstream's internal helper also
	/// redirects a <c>MenuShellItem</c> to the <c>MenuItem</c> it wraps, and both that type and its
	/// <c>MenuItem</c> property are internal, so the branch cannot be expressed off-tree at all. A
	/// bare <c>MenuItem</c> in a flyout therefore falls back to the shell-level template.
	/// </para>
	/// <para>
	/// dotnet/maui#37862 ("Add public Shell flyout item template contract for external backends")
	/// is open and proposes a different shape to the internal helper Wave C reimplemented:
	/// <c>Shell.IsFlyoutItemTemplateSet</c>, <c>Shell.GetFlyoutItemTemplateSource</c> and
	/// <c>Shell.GetFlyoutItemTemplateProperty</c>, used alongside the already-public
	/// <c>IShellController.GetFlyoutItemDataTemplate</c>.
	/// </para>
	/// <para>
	/// So this test watches for the <em>proposed</em> members, not the internal one. Watching
	/// <c>GetBindableObjectWithFlyoutItemTemplate</c> - which is what an earlier revision did -
	/// would never have fired, because upstream is not planning to publish that name at all, and
	/// the adapter would have quietly become permanent.
	/// </para>
	/// <para>
	/// The adapter stays provisional until the API is merged AND available in a referenced package;
	/// this test firing is the signal to adopt it, not a reason to bake it in early.
	/// </para>
	/// </remarks>
	[Fact]
	public void ShellTemplateResolverExpiresWhenShellPublishesTheFlyoutTemplateContract()
	{
		var shell = NeutralMaui.Controls.GetType("Microsoft.Maui.Controls.Shell");

		Assert.NotNull(shell);

		string[] proposed =
		{
			"IsFlyoutItemTemplateSet",
			"GetFlyoutItemTemplateSource",
			"GetFlyoutItemTemplateProperty",
		};

		var landed = proposed
			.Where(name => shell!.GetMethod(name, BindingFlags.Public | BindingFlags.Static) is not null)
			.ToList();

		Assert.True(
			landed.Count == 0,
			"Shell now publishes " + string.Join(", ", landed) + " (dotnet/maui#37862). Re-point "
				+ "Adapters/ShellTemplateResolver.cs at the public contract alongside "
				+ "IShellController.GetFlyoutItemDataTemplate, delete the MenuShellItem workaround "
				+ "and its documented behaviour gap, and remove MAUI-TIZEN-API-0001 from "
				+ "Adapters/UpstreamApiRequests.cs.");
	}

	/// <summary>
	/// Expires the whole modal-navigation block once upstream opens a public seam.
	/// </summary>
	/// <remarks>
	/// <c>ModalNavigationManager</c> is an internal partial class whose per-platform half must be
	/// supplied from inside <c>Microsoft.Maui.Controls</c>. Wave C therefore ports nothing here and
	/// ships no provisional behaviour; the gap is tracked upstream. This test fires when a public
	/// seam appears so the port can actually be written.
	/// </remarks>
	[Fact]
	public void ModalNavigationRemainsBlockedUntilUpstreamPublishesASeam()
	{
		var seam = NeutralMaui.Controls
			.GetTypes()
			.Where(t => t.IsPublic)
			.FirstOrDefault(t =>
				t.Name.Contains("ModalNavigation", StringComparison.Ordinal)
				&& t.Namespace?.StartsWith("Microsoft.Maui.Controls", StringComparison.Ordinal) == true);

		Assert.True(
			seam is null,
			$"A public modal-navigation seam ({seam?.FullName}) now exists. Wave C can implement Tizen "
				+ "modal push/pop; coordinate with the alerts/gestures workstream before doing so.");
	}

	/// <summary>
	/// The secondary-action seam must stay a seam: Wave C declares it, the alerts workstream
	/// implements it.
	/// </summary>
	/// <remarks>
	/// Upstream presented secondary toolbar items by pushing an action sheet onto the modal stack.
	/// With modal blocked, shipping an inert or half-working presenter here would be worse than
	/// shipping none - the overflow button is simply not created when no presenter is registered.
	/// </remarks>
	[Fact]
	public void WaveCDeclaresTheSecondaryActionSeamButDoesNotImplementIt()
	{
		var implementations = WaveCSource.Files
			.Where(f => !f.EndsWith("IToolbarSecondaryActionPresenter.cs", StringComparison.Ordinal))
			.Where(f => File.ReadAllText(f).Contains(": IToolbarSecondaryActionPresenter", StringComparison.Ordinal))
			.Select(f => Path.GetRelativePath(RepoPaths.Root, f))
			.ToList();

		Assert.True(
			implementations.Count == 0,
			"Wave C must not implement IToolbarSecondaryActionPresenter; the alerts/gestures "
				+ "workstream owns the action-sheet presentation: " + string.Join(", ", implementations));
	}
}
