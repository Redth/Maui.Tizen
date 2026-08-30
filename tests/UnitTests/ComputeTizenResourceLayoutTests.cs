using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

public class ComputeTizenResourceLayoutTests : TestBase
{
	private static string P(params string[] parts) => Path.Combine(parts);

	[Fact]
	public void MapsImageBucketsOntoTpkSubDirectories()
	{
		var root = CreateTempDirectory();
		var hdpi = P(root, "res", "contents", "default_All-HDPI", "image.png");
		var mdpi = P(root, "res", "contents", "default_All-MDPI", "image.png");

		var task = new ComputeTizenResourceLayout { ProcessedImages = new[] { Item(hdpi), Item(mdpi) } };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Equal(2, task.TpkFiles.Length);
		Assert.Equal(Path.GetFullPath(root), task.ResourceRoot.TrimEnd(Path.DirectorySeparatorChar));

		Assert.Equal(
			P("res", "contents", "default_All-HDPI") + Path.DirectorySeparatorChar,
			task.TpkFiles.Single(i => i.ItemSpec.Contains("HDPI")).GetMetadata("TizenTpkSubDir"));
	}

	[Fact]
	public void MapsAppIconsUnderSharedRes()
	{
		var root = CreateTempDirectory();
		var icon = P(root, "shared", "res", "xhdpi", "appicon.xhigh.png");

		var task = new ComputeTizenResourceLayout { ProcessedImages = new[] { Item(icon) } };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());

		// The outermost anchor wins, so shared/res/xhdpi is preserved rather than collapsing to res/xhdpi.
		Assert.Equal(
			P("shared", "res", "xhdpi") + Path.DirectorySeparatorChar,
			task.TpkFiles.Single().GetMetadata("TizenTpkSubDir"));
	}

	[Fact]
	public void SkipsFilesThatAreNotResources()
	{
		var root = CreateTempDirectory();
		var stray = P(root, "backend", "artifacts.items");

		var task = new ComputeTizenResourceLayout { ProcessedImages = new[] { Item(stray) } };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Empty(task.TpkFiles);
		Assert.Equal(string.Empty, task.ResourceRoot);
	}

	[Fact]
	public void UsesTheHintWhenNothingCanBeInferred()
	{
		var hint = CreateTempDirectory();

		var task = new ComputeTizenResourceLayout { ResourceRootHint = hint };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Equal(Path.GetFullPath(hint), task.ResourceRoot);
	}

	[Fact]
	public void PreservesOriginalMetadata()
	{
		var root = CreateTempDirectory();
		var image = P(root, "res", "contents", "default_All-LDPI", "image.png");

		var task = new ComputeTizenResourceLayout
		{
			ProcessedImages = new[] { Item(image, ("CustomMetadata", "kept")) },
		};
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Equal("kept", task.TpkFiles.Single().GetMetadata("CustomMetadata"));
	}

	[Fact]
	public void OrdersResultsDeterministically()
	{
		var root = CreateTempDirectory();
		var files = new[] { "b.png", "a.png", "c.png" }
			.Select(n => P(root, "res", "contents", "default_All-MDPI", n))
			.ToArray();

		var task = new ComputeTizenResourceLayout { ProcessedImages = files.Select(f => Item(f)).ToArray() };
		task.UseRecordingEngine();

		Assert.True(task.Execute());
		Assert.Equal(
			new[] { "a.png", "b.png", "c.png" },
			task.TpkFiles.Select(i => Path.GetFileName(i.ItemSpec)));
	}

	[Theory]
	[InlineData("/tmp/obj/resizetizer/r/res/contents/default_All-HDPI", "/tmp/obj/resizetizer/r", "res/contents/default_All-HDPI")]
	[InlineData("/tmp/obj/resizetizer/r/shared/res/hdpi", "/tmp/obj/resizetizer/r", "shared/res/hdpi")]
	[InlineData("/tmp/obj/resizetizer/r/res", "/tmp/obj/resizetizer/r", "res")]
	public void TrySplitAnchorsOnTheResourceRoot(string directory, string expectedRoot, string expectedSubDir)
	{
		var normalized = directory.Replace('/', Path.DirectorySeparatorChar);

		Assert.True(ComputeTizenResourceLayout.TrySplit(normalized, searchRoot: null, out var root, out var subDir));
		Assert.Equal(expectedRoot.Replace('/', Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar));
		Assert.Equal(expectedSubDir.Replace('/', Path.DirectorySeparatorChar), subDir);
	}

	/// <summary>
	/// An ancestor directory called "res" or "shared" must not be mistaken for the resource root.
	/// Anchoring on the FIRST such segment silently misplaced every resource for anyone whose
	/// checkout lived under one - for example /mnt/shared/work.
	/// </summary>
	[Theory]
	[InlineData("/mnt/shared/work/app/obj/resizetizer/r/res/contents/default_All-MDPI", "res/contents/default_All-MDPI")]
	[InlineData("/mnt/res/work/app/obj/resizetizer/r/res/contents/default_All-MDPI", "res/contents/default_All-MDPI")]
	[InlineData("/home/shared/res/app/obj/resizetizer/r/shared/res/xhdpi", "shared/res/xhdpi")]
	[InlineData("/srv/res/shared/app/obj/resizetizer/r/res", "res")]
	public void TrySplitIgnoresAncestorsNamedLikeResourceRoots(string directory, string expectedSubDir)
	{
		var normalized = directory.Replace('/', Path.DirectorySeparatorChar);

		Assert.True(ComputeTizenResourceLayout.TrySplit(normalized, searchRoot: null, out _, out var subDir));
		Assert.Equal(expectedSubDir.Replace('/', Path.DirectorySeparatorChar), subDir);
	}

	/// <summary>
	/// shared/res is a single anchor: anchoring on the inner "res" would drop "shared" and place
	/// application icons at res/{dpi} instead of shared/res/{dpi} in the TPK.
	/// </summary>
	[Fact]
	public void TrySplitKeepsTheSharedSegmentForAppIcons()
	{
		var directory = P("build", "obj", "r", "shared", "res", "hdpi");

		Assert.True(ComputeTizenResourceLayout.TrySplit(directory, searchRoot: null, out _, out var subDir));
		Assert.Equal(P("shared", "res", "hdpi"), subDir);
	}

	/// <summary>
	/// With a search root supplied, nothing at or above it can be chosen at all.
	/// </summary>
	[Fact]
	public void TrySplitRestrictsTheAnchorToTheSearchRoot()
	{
		var searchRoot = P(Path.GetTempPath(), "shared", "checkout", "obj");
		var directory = Path.Combine(searchRoot, "resizetizer", "r", "res", "contents", "default_All-HDPI");

		Assert.True(ComputeTizenResourceLayout.TrySplit(directory, searchRoot, out _, out var subDir));
		Assert.Equal(P("res", "contents", "default_All-HDPI"), subDir);
	}

	[Fact]
	public void TrySplitRejectsPathsOutsideTheSearchRoot()
	{
		var searchRoot = P(Path.GetTempPath(), "app", "obj");
		var directory = P(Path.GetTempPath(), "elsewhere", "res", "contents", "default_All-HDPI");

		Assert.False(ComputeTizenResourceLayout.TrySplit(directory, searchRoot, out _, out _));
	}

	[Fact]
	public void TrySplitRejectsPrefixSiblingOfTheSearchRoot()
	{
		var searchRoot = P(Path.GetTempPath(), "app", "obj");
		var directory = P(Path.GetTempPath(), "app", "obj-other", "res", "contents", "default_All-HDPI");

		Assert.False(ComputeTizenResourceLayout.TrySplit(directory, searchRoot, out _, out _));
	}

	[Fact]
	public void TrySplitDoesNotFoldPathCaseOnUnix()
	{
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			return;

		var searchRoot = P(Path.GetTempPath(), "app", "obj");
		var directory = P(Path.GetTempPath(), "App", "obj", "res", "contents", "default_All-HDPI");

		Assert.False(ComputeTizenResourceLayout.TrySplit(directory, searchRoot, out _, out _));
	}

	[Fact]
	public void TrySplitFailsWithoutAResourceRoot()
	{
		Assert.False(ComputeTizenResourceLayout.TrySplit(P("tmp", "obj", "backend"), searchRoot: null, out _, out _));
	}
}
