using System;
using System.Linq;
using Microsoft.Build.Framework;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

/// <summary>
/// Unit coverage for the ordinal de-duplication that replaced MSBuild's RemoveDuplicates task.
/// </summary>
/// <remarks>
/// The MSBuild-level proof lives in <see cref="TizenTargetsTests"/>; these tests pin the rule
/// itself, cheaply and without a build, because the whole point of the task is a comparison
/// choice that is invisible in the target file.
/// </remarks>
public class SelectDistinctTizenResourcesTests : TestBase
{
	private static SelectDistinctTizenResources Task(params ITaskItem[] inputs)
		=> new()
		{
			BuildEngine = new RecordingBuildEngine(),
			Inputs = inputs,
			KeyMetadata = "TizenTpkFileName",
		};

	/// <summary>
	/// The regression. RemoveDuplicates and the Distinct() item function both compare with
	/// OrdinalIgnoreCase, so on Tizen - which is Linux - they silently discard a real file.
	/// </summary>
	[Fact]
	public void KeepsDestinationsThatDifferOnlyInCase()
	{
		var task = Task(
			Item("a.js", ("TizenTpkFileName", "wwwroot/Foo.js")),
			Item("b.js", ("TizenTpkFileName", "wwwroot/foo.js")));

		Assert.True(task.Execute());

		Assert.Equal(
			new[] { "wwwroot/Foo.js", "wwwroot/foo.js" },
			task.Filtered.Select(i => i.GetMetadata("TizenTpkFileName")));

		Assert.Empty(task.Duplicates);
	}

	/// <summary>
	/// De-duplication is still required: an app that picks the same conversion up from two
	/// packages contributes every file twice, and packing a destination twice corrupts the TPK.
	/// </summary>
	[Fact]
	public void CollapsesIdenticalDestinations()
	{
		var task = Task(
			Item("a.js", ("TizenTpkFileName", "wwwroot/foo.js"), ("SourcePath", "/first/foo.js")),
			Item("b.js", ("TizenTpkFileName", "wwwroot/foo.js"), ("SourcePath", "/first/foo.js")));

		Assert.True(task.Execute());

		var single = Assert.Single(task.Filtered);

		// First occurrence wins, so the result is stable rather than dependent on ordering.
		Assert.Equal("/first/foo.js", single.GetMetadata("SourcePath"));
		Assert.Single(task.Duplicates);
	}

	[Fact]
	public void RejectsDifferentSourcesForTheSameDestination()
	{
		var task = Task(
			Item("a.js", ("TizenTpkFileName", "wwwroot/foo.js"), ("SourcePath", "/first/foo.js")),
			Item("b.js", ("TizenTpkFileName", "wwwroot/foo.js"), ("SourcePath", "/second/foo.js")));
		var engine = (RecordingBuildEngine)task.BuildEngine;
		var firstSource = System.IO.Path.GetFullPath("/first/foo.js");
		var secondSource = System.IO.Path.GetFullPath("/second/foo.js");

		Assert.False(task.Execute());
		Assert.Contains("MAUITIZEN1021", engine.ErrorCodes);
		Assert.Contains(engine.Errors, error =>
			error.Contains(firstSource, StringComparison.Ordinal)
			&& error.Contains(secondSource, StringComparison.Ordinal)
			&& error.Contains("wwwroot/foo.js", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("wwwroot/foo.js", "wwwroot\\foo.js")]
	[InlineData("wwwroot/foo.js", "/wwwroot/foo.js")]
	public void CollapsesDestinationsThatDifferOnlyInSeparatorOrLeadingSlash(string first, string second)
	{
		var task = Task(
			Item("a.js", ("TizenTpkFileName", first), ("SourcePath", "/same/a.js")),
			Item("b.js", ("TizenTpkFileName", second), ("SourcePath", "/same/a.js")));

		Assert.True(task.Execute());
		Assert.Single(task.Filtered);
	}

	/// <summary>Metadata must survive, because the source path is re-attached from it.</summary>
	[Fact]
	public void PreservesMetadataOnTheKeptItems()
	{
		var task = Task(Item("a.js", ("TizenTpkFileName", "wwwroot/foo.js"), ("SourcePath", "/src/foo.js")));

		Assert.True(task.Execute());

		Assert.Equal("/src/foo.js", Assert.Single(task.Filtered).GetMetadata("SourcePath"));
	}

	/// <summary>
	/// An item with no destination metadata falls back to its item spec rather than being
	/// dropped; silently losing a file from the package is the failure this whole task exists to
	/// prevent.
	/// </summary>
	[Fact]
	public void FallsBackToTheItemSpecWhenTheKeyMetadataIsAbsent()
	{
		var task = Task(
			Item("wwwroot/Foo.js"),
			Item("wwwroot/foo.js"),
			Item("wwwroot/foo.js"));

		Assert.True(task.Execute());

		Assert.Equal(
			new[] { "wwwroot/Foo.js", "wwwroot/foo.js" },
			task.Filtered.Select(i => i.ItemSpec));
	}

	[Fact]
	public void HandlesNoInputs()
	{
		var task = new SelectDistinctTizenResources { BuildEngine = new RecordingBuildEngine() };

		Assert.True(task.Execute());
		Assert.Empty(task.Filtered);
		Assert.Empty(task.Duplicates);
	}

	/// <summary>
	/// The targets must not reintroduce a case-insensitive filter for resource destinations.
	/// </summary>
	[Fact]
	public void TheTargetsDoNotUseRemoveDuplicatesForResourceDestinations()
	{
		var targets = System.IO.File.ReadAllText(
			System.IO.Path.Combine(BuildTransitiveDirectory, "Maui.Tizen.Build.Tasks.targets"));

		Assert.DoesNotContain("<RemoveDuplicates", targets, StringComparison.Ordinal);
		Assert.Contains("SelectDistinctTizenResources", targets, StringComparison.Ordinal);
	}
}
