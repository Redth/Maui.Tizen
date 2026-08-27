using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

/// <summary>
/// Executable tests for the toolbar drawer-toggle capability.
/// </summary>
/// <remarks>
/// <para>
/// The capability is read-only upstream (dotnet/maui#37863 adds
/// <c>IToolbarDrawerToggleVisible</c> additively; <c>IToolbar</c> is unchanged). The in-tree Tizen
/// backend instead <em>wrote</em> a latched <c>drawerToggle &amp;&amp; !backButton</c>, and an
/// earlier revision of this backend reproduced that.
/// </para>
/// <para>
/// These tests pin the two properties that correction has to preserve: the capability is
/// <b>independent of the back button</b>, and it <b>cannot go stale</b>. Both are runtime
/// behaviours, and the adapter is NUI-free, so they are executed here rather than asserted about
/// source.
/// </para>
/// </remarks>
public class WaveCToolbarDrawerToggleTests
{
	static Shell NewShell(FlyoutBehavior behaviour)
	{
		var shell = new Shell { FlyoutBehavior = behaviour };

		shell.Items.Add(new ShellContent { Content = new ContentPage() });

		return shell;
	}

		static IToolbar ToolbarOf(Shell shell) => ((IToolbarElement)shell).Toolbar!;

	static bool Capability(Shell shell) =>
		ToolbarDrawerToggle.GetDrawerToggleVisible(ToolbarOf(shell), shell);

	[Fact]
	public void AFlyoutShellOffersADrawerToggle()
		=> Assert.True(Capability(NewShell(FlyoutBehavior.Flyout)));

	[Theory]
	[InlineData(FlyoutBehavior.Disabled)]
	[InlineData(FlyoutBehavior.Locked)]
	public void ANonFlyoutShellOffersNoDrawerToggle(FlyoutBehavior behaviour)
		=> Assert.False(Capability(NewShell(behaviour)));

	/// <summary>
	/// The capability is independent of the back button - back-precedence, not mutual exclusivity.
	/// </summary>
	/// <remarks>
	/// The removed latch stored <c>drawerToggle &amp;&amp; !backButton</c>, so raising the back
	/// button made the capability itself report <see langword="false"/>. That conflated "a drawer
	/// toggle is available" with "a drawer toggle is what we draw". Only the renderer should apply
	/// precedence; the capability must keep reporting that a drawer exists.
	/// </remarks>
	[Fact]
	public void TheCapabilityStaysTrueWhileABackButtonIsShowing()
	{
		var shell = NewShell(FlyoutBehavior.Flyout);
		var toolbar = ToolbarOf(shell);

		Assert.True(Capability(shell));

		toolbar.BackButtonVisible = true;

		Assert.True(
			ToolbarDrawerToggle.GetDrawerToggleVisible(toolbar, shell),
			"The drawer still exists while a back button is showing. Rendering prefers the back "
				+ "button; the capability must not be forced false, which is what the old latch did.");
	}

	/// <summary>
	/// The value tracks flyout behaviour without anything having to write it.
	/// </summary>
	/// <remarks>
	/// This is the staleness the latch had: a stored flag is only as fresh as the last code path
	/// that remembered to update it. Nothing calls a setter here - the value is recomputed on read.
	/// </remarks>
	[Fact]
	public void TheCapabilityTracksFlyoutBehaviourWithoutAnyWrite()
	{
		var shell = NewShell(FlyoutBehavior.Flyout);

		Assert.True(Capability(shell));

		shell.FlyoutBehavior = FlyoutBehavior.Disabled;
		Assert.False(Capability(shell));

		shell.FlyoutBehavior = FlyoutBehavior.Flyout;
		Assert.True(Capability(shell));
	}

	/// <summary>
	/// A toolbar with no known flyout owner offers no drawer toggle.
	/// </summary>
	/// <remarks>
	/// There is no public path from a toolbar to its shell off-tree, so a caller that cannot supply
	/// the owner must get <see langword="false"/> rather than a guess.
	/// </remarks>
	[Fact]
	public void AToolbarWithNoKnownOwnerOffersNoDrawerToggle()
		=> Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(new Toolbar(new ContentPage()), null));

	[Fact]
	public void ANullToolbarOffersNoDrawerToggle()
		=> Assert.False(ToolbarDrawerToggle.GetDrawerToggleVisible(null, NewShell(FlyoutBehavior.Flyout)));

	/// <summary>
	/// The mapper key must match the upstream member name so adoption does not change it.
	/// </summary>
	[Fact]
	public void TheMapperKeyMatchesTheProposedUpstreamMemberName()
		=> Assert.Equal("DrawerToggleVisible", ToolbarDrawerToggle.DrawerToggleVisiblePropertyName);

	/// <summary>
	/// The adapter must expose no writer.
	/// </summary>
	/// <remarks>
	/// Upstream removed the write path, so re-adding one would reintroduce both the staleness and
	/// the mutual-exclusivity conflation, and would not survive adoption.
	/// </remarks>
	[Fact]
	public void TheAdapterExposesNoWriter()
	{
		var writers = typeof(ToolbarDrawerToggle)
			.GetMethods()
			.Where(m => m.Name.StartsWith("Set", StringComparison.Ordinal))
			.Select(m => m.Name)
			.ToList();

		Assert.True(
			writers.Count == 0,
			"The drawer-toggle capability is read-only upstream; found writer(s): "
				+ string.Join(", ", writers));
	}
}
