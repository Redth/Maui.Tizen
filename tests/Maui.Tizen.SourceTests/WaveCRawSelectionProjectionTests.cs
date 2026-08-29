using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCRawSelectionProjectionTests
{
	[Fact]
	public void RejectsHeaderAndFooterIndexesBeforeProjectingItems()
	{
		var items = new object[] { "header", "a", "b", "footer" };

		var selected = RawSelectionProjection.ToItems(
			[0, 2, 3],
			items.Length,
			index => index is 1 or 2,
			index => items[index]);

		Assert.Equal(["b"], selected);
	}

	[Fact]
	public void DropsStaleNativeIndexes()
	{
		var items = new object[] { "a" };

		var selected = RawSelectionProjection.ToItems(
			[-1, 0, 5],
			items.Length,
			_ => true,
			index => items[index]);

		Assert.Equal(["a"], selected);
	}
}
