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
		// Upstream settled on an ADDITIVE capability interface rather than a new IToolbar member, so
		// watching IToolbar alone would never fire. Both shapes are checked: the capability
		// interface that #37863 merged upstream but the pinned package lacks, and a direct IToolbar member in case review moves again.
		var capability = NeutralMaui.Core.GetType("Microsoft.Maui.IToolbarDrawerToggleVisible");

		Assert.True(
			capability is null,
			"Microsoft.Maui.IToolbarDrawerToggleVisible now exists (dotnet/maui#37863). Replace the "
				+ "body of ToolbarDrawerToggle.GetDrawerToggleVisible with "
				+ "'toolbar is IToolbarDrawerToggleVisible { DrawerToggleVisible: true }', switch the "
				+ "mapper key to nameof(IToolbarDrawerToggleVisible.DrawerToggleVisible), delete "
				+ "Adapters/ToolbarDrawerToggle.cs and remove MAUI-TIZEN-API-0009.");

		var toolbar = NeutralMaui.Core.GetType("Microsoft.Maui.IToolbar");

		Assert.NotNull(toolbar);

		Assert.True(
			toolbar!.GetProperty("DrawerToggleVisible", AnyMember) is null,
			"IToolbar now publishes DrawerToggleVisible directly. Adopt it and delete "
				+ "Adapters/ToolbarDrawerToggle.cs.");
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
	/// DELIBERATELY NAME-AGNOSTIC. The upstream API changed shape twice before merge while this
	/// adapter sat here: first as the internal <c>GetBindableObjectWithFlyoutItemTemplate</c>, then
	/// as a three-method contract (<c>IsFlyoutItemTemplateSet</c>,
	/// <c>GetFlyoutItemTemplateSource</c>, <c>GetFlyoutItemTemplateProperty</c>), and it is now
	/// being redesigned again toward a single nullable result-oriented resolver (null meaning "use
	/// the platform default"), with the template owner and property kept internal and the flyout
	/// item itself retained as the binding context. Independent review rejected the decomposed
	/// three-member shape.
	/// <para>
	/// The merged change settles on a single symbol, which is not in the pinned package:
	/// <c>public static DataTemplate? Shell.ResolveFlyoutItemTemplate(Shell? shell, BindableObject
	/// flyoutItem)</c>. <see cref="ExplicitlyRecognisesTheProposedResolverSymbol"/> pins that name so
	/// it is unmistakably covered, but the broad match below is retained rather than replaced by it:
	/// the change is merged upstream but absent from the pinned package, and narrowing to one name
	/// is what blinded this test twice already while the API shape was evolving.
	/// </para>
	/// <para>
	/// Each time this test named members explicitly it silently stopped detecting anything, which is
	/// worse than having no test at all - a green build then implies the adapter is still needed
	/// when it may not be. It therefore stays deliberately broad and covers BOTH the rejected
	/// three-member draft and the merged single-resolver replacement, so no packaged shape can
	/// slip past unnoticed.
	/// </para>
	/// </para>
	/// <para>
	/// So it matches on the <em>concept</em> instead: any new public member on <see cref="Shell"/>
	/// that talks about a flyout item template. That fires for a resolve-style API, for the
	/// three-method shape, or for whatever the review settles on, without needing to be revised
	/// every time the design moves.
	/// </para>
	/// <para>
	/// Firing is the signal to adopt, not permission to bake the API in early: the adapter stays
	/// provisional until the design is merged AND present in a referenced package.
	/// </para>
	/// </remarks>
	[Fact]
	public void ShellTemplateResolverExpiresWhenShellPublishesAFlyoutTemplateContract()
	{
		var shell = NeutralMaui.Controls.GetType("Microsoft.Maui.Controls.Shell");

		Assert.NotNull(shell);

		var landed = shell!
			.GetMembers(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
			.Select(m => m.Name)
			.Where(IsNewFlyoutTemplateContractMember)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(n => n, StringComparer.Ordinal)
			.ToList();

		Assert.True(
			landed.Count == 0,
			"Shell now publishes a flyout item template contract (" + string.Join(", ", landed)
				+ ", dotnet/maui#37862). Rewrite Adapters/ShellTemplateResolver.cs onto it - the shape "
				+ "differs from the internal helper, so this is a rewrite rather than a rename - "
				+ "delete the MenuShellItem workaround and its documented behaviour gap, and remove "
				+ "MAUI-TIZEN-API-0001 from Adapters/UpstreamApiRequests.cs.");
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

	/// <summary>Members that predate Wave C, so they cannot be the new contract.</summary>
	static readonly HashSet<string> PreexistingTemplateMembers = new(StringComparer.Ordinal)
	{
		"ItemTemplateProperty",
		"MenuItemTemplateProperty",
		"GetItemTemplate",
		"SetItemTemplate",
		"GetMenuItemTemplate",
		"SetMenuItemTemplate",
	};

	/// <summary>
	/// The concept match used to detect a newly published flyout item template contract.
	/// </summary>
	internal static bool IsNewFlyoutTemplateContractMember(string memberName) =>
		(memberName.Contains("FlyoutItemTemplate", StringComparison.Ordinal)
			|| memberName.Contains("FlyoutItemDataTemplate", StringComparison.Ordinal))
		&& !PreexistingTemplateMembers.Contains(memberName);

	/// <summary>
	/// Proves the detector actually fires. A detector is only worth having if it has been shown to
	/// trigger; the previous name-specific version passed happily while detecting nothing, which is
	/// exactly the failure this guards against.
	/// </summary>
	[Theory]
	// The single resolve-style API the design is currently moving toward.
	[InlineData("ResolveFlyoutItemTemplate", true)]
	// The three-method shape it is moving away from.
	[InlineData("IsFlyoutItemTemplateSet", true)]
	[InlineData("GetFlyoutItemTemplateSource", true)]
	[InlineData("GetFlyoutItemTemplateProperty", true)]
	// Anything else the review might land on.
	[InlineData("TryGetFlyoutItemTemplate", true)]
	[InlineData("GetFlyoutItemDataTemplate", true)]
	// Members that already existed must not trip it.
	[InlineData("ItemTemplateProperty", false)]
	[InlineData("MenuItemTemplateProperty", false)]
	[InlineData("GetItemTemplate", false)]
	[InlineData("CurrentItem", false)]
	[InlineData("FlyoutBehavior", false)]
	public void FlyoutTemplateContractDetectorMatchesTheConceptNotAName(string memberName, bool expected)
		=> Assert.Equal(expected, IsNewFlyoutTemplateContractMember(memberName));


	/// <summary>
	/// Pins the merged dotnet/maui#37862 symbol that the pinned package does not yet expose.
	/// </summary>
	/// <remarks>
	/// Belt and braces alongside the concept match: if the API merges under this name, this asserts
	/// the detector recognises it, and the table-driven test below asserts the matcher agrees.
	/// </remarks>
	[Fact]
	public void ExplicitlyRecognisesTheProposedResolverSymbol()
	{
		const string ProposedSymbol = "ResolveFlyoutItemTemplate";

		Assert.True(
			IsNewFlyoutTemplateContractMember(ProposedSymbol),
			$"The detector must recognise '{ProposedSymbol}', the symbol merged by dotnet/maui#37862 but absent from the pinned package "
				+ "head 6cc7f668f0.");

		// And it must not yet exist on the referenced package - adoption waits for merge AND a
		// package floor that actually contains it.
		var shell = NeutralMaui.Controls.GetType("Microsoft.Maui.Controls.Shell");

		Assert.NotNull(shell);
		Assert.Null(shell!.GetMethod(ProposedSymbol, BindingFlags.Public | BindingFlags.Static));
	}

}
