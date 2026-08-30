using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCBidirectionalUpdateGateTests
{
	[Fact]
	public void ManagedUpdateSuppressesNativeEcho()
	{
		var gate = new BidirectionalUpdateGate();
		var echoed = true;

		Assert.True(gate.ApplyManaged(() => echoed = gate.ApplyNative(() => { })));
		Assert.False(echoed);
		Assert.False(gate.IsApplyingManaged);
		Assert.False(gate.IsApplyingNative);
	}

	[Fact]
	public void NativeUpdateSuppressesManagedEcho()
	{
		var gate = new BidirectionalUpdateGate();
		var echoed = true;

		Assert.True(gate.ApplyNative(() => echoed = gate.ApplyManaged(() => { })));
		Assert.False(echoed);
		Assert.False(gate.IsApplyingManaged);
		Assert.False(gate.IsApplyingNative);
	}

	[Fact]
	public void ExceptionsReleaseTheGate()
	{
		var gate = new BidirectionalUpdateGate();

		Assert.Throws<InvalidOperationException>(() =>
			gate.ApplyManaged(() => throw new InvalidOperationException()));

		Assert.True(gate.ApplyNative(() => { }));
	}
}
