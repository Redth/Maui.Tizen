using Microsoft.Maui.Platforms.Tizen.Adapters;

namespace Maui.Tizen.SourceTests;

public class WaveCExceptionSafeCleanupTests
{
	[Fact]
	public void RunsEveryCleanupAndAggregatesFailures()
	{
		var calls = new List<int>();

		var error = Assert.Throws<AggregateException>(() => ExceptionSafeCleanup.Run(
			() => { calls.Add(1); throw new InvalidOperationException("first"); },
			() => calls.Add(2),
			() => { calls.Add(3); throw new ArgumentException("third"); }));

		Assert.Equal([1, 2, 3], calls);
		Assert.Equal(2, error.InnerExceptions.Count);
	}
}
