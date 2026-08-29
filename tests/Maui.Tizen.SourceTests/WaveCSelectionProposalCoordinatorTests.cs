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
