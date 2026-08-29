using Microsoft.Maui.Controls;
using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCSelectionProposalCoordinatorTests
{
	[Fact]
	public void ManagedSynchronizationClearsThenSelectsWithoutNativeEcho()
	{
		var coordinator = new SelectionProposalCoordinator<string>();
		var calls = new List<string>();

		coordinator.Synchronize(
			"b",
			item => item == "b" ? 2 : -1,
			() => calls.Add("clear"),
			index => calls.Add($"select:{index}"));

		Assert.Equal(["clear", "select:2"], calls);
		Assert.False(coordinator.IsApplyingManaged);
	}

	[Fact]
	public void DeferredNativeEchoOfManagedSelectionIsConsumed()
	{
		var coordinator = new SelectionProposalCoordinator<string>();
		coordinator.Synchronize("b", item => item == "b" ? 2 : -1, () => { }, _ => { });

		Assert.True(coordinator.ConsumeManagedEcho(2));
		Assert.False(coordinator.ConsumeManagedEcho(1));
	}

	[Fact]
	public void DeferredClearBeforeSelectKeepsTheExpectedSelection()
	{
		var coordinator = new SelectionProposalCoordinator<string>();
		coordinator.Synchronize("b", _ => 2, () => { }, _ => { });

		Assert.True(coordinator.ConsumeManagedEcho(-1));
		Assert.True(coordinator.ConsumeManagedEcho(2));
	}

	[Fact]
	public void SynchronousClearAndFinalSelectionConsumeTheEchoButNotTheNextUserTap()
	{
		var coordinator = new SelectionProposalCoordinator<string>();

		coordinator.Synchronize(
			"more",
			_ => 4,
			() => Assert.True(coordinator.ConsumeManagedEcho(-1)),
			index => Assert.True(coordinator.ConsumeManagedEcho(index)));

		Assert.False(coordinator.ConsumeManagedEcho(4));
	}

	[Fact]
	public void GeneratedFlyoutSelectionPrefersTheMostSpecificActiveEntry()
	{
		var root = new FlyoutItem();
		var section = new ShellSection();
		var content = new ShellContent { Content = new ContentPage() };
		section.Items.Add(content);
		root.Items.Add(section);

		Assert.Same(
			content,
			HierarchySelectionResolver.Resolve<Element>(
				new Element[] { root, section, content },
				root,
				section,
				content));
		Assert.Same(
			section,
			HierarchySelectionResolver.Resolve<Element>(
				new Element[] { root, section },
				root,
				section,
				content));
		Assert.Same(
			root,
			HierarchySelectionResolver.Resolve<Element>(
				new Element[] { root },
				root,
				section,
				content));
	}

	[Fact]
	public async Task AsyncSelectionAlwaysResynchronizesAfterACanceledProposal()
	{
		var coordinator = new AsyncSelectionResynchronizer<object>();
		var owner = new object();
		var synchronized = 0;

		var current = await coordinator.RunAsync(
			owner,
			() => Task.CompletedTask,
			current => ReferenceEquals(current, owner),
			() => synchronized++);

		Assert.True(current);
		Assert.Equal(1, synchronized);
	}

	[Fact]
	public async Task AsyncSelectionDoesNotApplyAStaleCompletion()
	{
		var coordinator = new AsyncSelectionResynchronizer<object>();
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var synchronized = 0;
		var pending = coordinator.RunAsync(new object(), () => completion.Task, _ => true, () => synchronized++);

		coordinator.Invalidate();
		completion.SetResult();
		Assert.False(await pending);

		Assert.Equal(0, synchronized);
	}

	[Fact]
	public async Task AsyncSelectionDoesNotApplyAfterOwnerReplacement()
	{
		var coordinator = new AsyncSelectionResynchronizer<object>();
		var original = new object();
		var replacement = new object();
		var current = original;
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var synchronized = 0;
		var pending = coordinator.RunAsync(
			original,
			() => completion.Task,
			owner => ReferenceEquals(owner, current),
			() => synchronized++);

		current = replacement;
		completion.SetResult();

		Assert.False(await pending);
		Assert.Equal(0, synchronized);
	}

	[Fact]
	public void CustomFlyoutContentExclusivelyDisablesGeneratedContent()
	{
		Assert.True(FlyoutContentMode.UsesGeneratedContent<object>(null));
		Assert.False(FlyoutContentMode.UsesGeneratedContent(new object()));
	}

	[Theory]
	[InlineData(true, true, true, false)]
	[InlineData(false, true, false, true)]
	[InlineData(true, false, false, true)]
	[InlineData(false, false, false, true)]
	public void FlyoutHeaderAlwaysHasExactlyOneOwner(
		bool headerOnMenu,
		bool generated,
		bool scrolling,
		bool fixedSlot)
	{
		Assert.Equal(scrolling, FlyoutHeaderOwnership.UseScrollingHeader(headerOnMenu, generated));
		Assert.Equal(fixedSlot, FlyoutHeaderOwnership.UseFixedHeader(headerOnMenu, generated));
		Assert.NotEqual(scrolling, fixedSlot);
	}

	[Fact]
	public void RejectedProposalRestoresManagedSelection()
	{
		var coordinator = new SelectionProposalCoordinator<string>();
		var restored = 0;

		var accepted = coordinator.Propose("candidate", _ => false, () => restored++);

		Assert.False(accepted);
		Assert.Equal(1, restored);
	}

	[Fact]
	public void AcceptedProposalDoesNotRestore()
	{
		var coordinator = new SelectionProposalCoordinator<string>();
		var restored = 0;

		var accepted = coordinator.Propose("candidate", _ => true, () => restored++);

		Assert.True(accepted);
		Assert.Equal(0, restored);
	}
}
