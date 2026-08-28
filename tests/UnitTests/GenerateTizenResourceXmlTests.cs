using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

using Maui.Tizen.Build.Tasks;

namespace Maui.Tizen.UnitTests;

public class GenerateTizenResourceXmlTests : TestBase
{
	private static readonly XNamespace Ns = "http://tizen.org/ns/rm";

	private static string BucketFile(string root, string bucket, string name = "image.png")
	{
		var path = Path.Combine(root, "res", "contents", bucket, name);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		File.WriteAllText(path, string.Empty);
		return path;
	}

	[Fact]
	public void DerivesBucketsFromProcessedImages()
	{
		var root = CreateTempDirectory();
		var hdpi = BucketFile(root, "default_All-HDPI");
		var mdpi = BucketFile(root, "default_All-MDPI");

		var task = new GenerateTizenResourceXml
		{
			IntermediateOutputPath = root,
			ProcessedImages = new[] { Item(hdpi), Item(mdpi) },
		};
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.NotNull(task.GeneratedResourceXml);

		var doc = XDocument.Load(task.GeneratedResourceXml!.ItemSpec);
		var groups = doc.Root!.Elements().ToList();

		Assert.Equal(
			new[] { "group-image", "group-layout", "group-sound", "group-bin" },
			groups.Select(g => g.Name.LocalName));

		foreach (var group in groups)
		{
			Assert.Equal("contents", group.Attribute("folder")!.Value);

			var nodes = group.Elements(Ns + "node").ToList();
			Assert.Equal(2, nodes.Count);

			// SortedSet ordering: HDPI sorts before MDPI.
			Assert.Equal("contents/default_All-HDPI", nodes[0].Attribute("folder")!.Value);
			Assert.Equal("from 301 to 380", nodes[0].Attribute("screen-dpi-range")!.Value);
			Assert.Equal("contents/default_All-MDPI", nodes[1].Attribute("folder")!.Value);
			Assert.Equal("from 241 to 300", nodes[1].Attribute("screen-dpi-range")!.Value);
		}
	}

	[Fact]
	public void FallsBackToDirectoryScanWhenNoItemsAreSupplied()
	{
		var root = CreateTempDirectory();
		BucketFile(root, "default_All-XHDPI");

		var task = new GenerateTizenResourceXml { IntermediateOutputPath = root };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());

		var doc = XDocument.Load(task.GeneratedResourceXml!.ItemSpec);
		var node = doc.Root!.Element(Ns + "group-image")!.Element(Ns + "node")!;
		Assert.Equal("contents/default_All-XHDPI", node.Attribute("folder")!.Value);
		Assert.Equal("from 381 to 480", node.Attribute("screen-dpi-range")!.Value);
	}

	/// <summary>
	/// Backend written side artifacts (for example the *.items files the PR 36653 regression test
	/// covers) live outside res/contents and must never become resource buckets.
	/// </summary>
	[Fact]
	public void IgnoresFilesOutsideResourceContents()
	{
		var root = CreateTempDirectory();
		var valid = BucketFile(root, "default_All-LDPI");

		var strayPath = Path.Combine(root, "backend", "artifacts.items");
		Directory.CreateDirectory(Path.GetDirectoryName(strayPath)!);
		File.WriteAllText(strayPath, string.Empty);

		var task = new GenerateTizenResourceXml
		{
			IntermediateOutputPath = root,
			ProcessedImages = new[] { Item(valid), Item(strayPath) },
		};
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());

		var nodes = XDocument.Load(task.GeneratedResourceXml!.ItemSpec)
			.Root!.Element(Ns + "group-image")!
			.Elements(Ns + "node")
			.ToList();

		Assert.Single(nodes);
		Assert.Equal("contents/default_All-LDPI", nodes[0].Attribute("folder")!.Value);
	}

	[Fact]
	public void SkipsUnknownResolutionBuckets()
	{
		var root = CreateTempDirectory();
		var unknown = BucketFile(root, "default_All-ENORMOUSDPI");

		var task = new GenerateTizenResourceXml
		{
			IntermediateOutputPath = root,
			ProcessedImages = new[] { Item(unknown) },
		};
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Empty(XDocument.Load(task.GeneratedResourceXml!.ItemSpec).Root!.Element(Ns + "group-image")!.Elements());
	}

	[Fact]
	public void ProducesNothingWhenThereAreNoResources()
	{
		var task = new GenerateTizenResourceXml { IntermediateOutputPath = CreateTempDirectory() };
		var engine = task.UseRecordingEngine();

		Assert.True(task.Execute(), engine.AllErrors());
		Assert.Null(task.GeneratedResourceXml);
	}

	[Fact]
	public void IsDeterministicRegardlessOfInputOrder()
	{
		var root = CreateTempDirectory();
		var a = BucketFile(root, "default_All-LDPI");
		var b = BucketFile(root, "default_All-XXHDPI");
		var c = BucketFile(root, "default_All-MDPI");

		string Run(params string[] files)
		{
			var task = new GenerateTizenResourceXml
			{
				IntermediateOutputPath = root,
				ProcessedImages = files.Select(f => Item(f)).ToArray(),
			};
			task.UseRecordingEngine();
			Assert.True(task.Execute());
			return File.ReadAllText(task.GeneratedResourceXml!.ItemSpec);
		}

		Assert.Equal(Run(a, b, c), Run(c, a, b));
	}

	/// <summary>
	/// Regenerating identical content must leave the existing file completely untouched.
	/// </summary>
	/// <remarks>
	/// res.xml is a TPK packaging input. Saving a byte-identical copy still moves its timestamp,
	/// which re-stamps everything downstream and turns a no-op build into a partial repackage.
	/// The generating target is incremental, which is the first line of defence; this is the
	/// second, for the builds where the target legitimately runs and has nothing new to say.
	///
	/// Asserted on the file's last-write time rather than on its content, because content
	/// stability is not the claim - "the file was not replaced" is.
	/// </remarks>
	[Fact]
	public void DoesNotRewriteAnIdenticalResourceXml()
	{
		var root = CreateTempDirectory();
		var hdpi = BucketFile(root, "default_All-HDPI");

		GenerateTizenResourceXml Run()
		{
			var task = new GenerateTizenResourceXml
			{
				IntermediateOutputPath = root,
				ProcessedImages = new[] { Item(hdpi) },
			};
			var engine = task.UseRecordingEngine();
			Assert.True(task.Execute(), engine.AllErrors());
			return task;
		}

		var first = Run();
		var destination = first.GeneratedResourceXml!.ItemSpec;
		Assert.True(first.ResourceXmlChanged);

		var stamp = File.GetLastWriteTimeUtc(destination);
		var contents = File.ReadAllBytes(destination);

		// Coarse filesystem timestamps would make an immediate rewrite look like a no-op.
		System.Threading.Thread.Sleep(1100);

		var second = Run();

		Assert.False(second.ResourceXmlChanged, "res.xml was replaced with identical content.");
		Assert.Equal(stamp, File.GetLastWriteTimeUtc(destination));
		Assert.Equal(contents, File.ReadAllBytes(destination));

		// The item must still be published, otherwise packaging loses res.xml on the very builds
		// where nothing needed to change.
		Assert.Equal(destination, second.GeneratedResourceXml!.ItemSpec);

		// And no temporary file is left behind next to the resource.
		Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(destination)!, "*.tmp"));
	}

	/// <summary>
	/// A different bucket set must produce a different file, and must actually replace it.
	/// </summary>
	[Fact]
	public void RewritesResourceXmlWhenTheBucketSetChanges()
	{
		var root = CreateTempDirectory();
		var hdpi = BucketFile(root, "default_All-HDPI");
		var mdpi = BucketFile(root, "default_All-MDPI");

		var first = new GenerateTizenResourceXml
		{
			IntermediateOutputPath = root,
			ProcessedImages = new[] { Item(hdpi) },
		};
		first.UseRecordingEngine();
		Assert.True(first.Execute());

		var before = File.ReadAllText(first.GeneratedResourceXml!.ItemSpec);

		var second = new GenerateTizenResourceXml
		{
			IntermediateOutputPath = root,
			ProcessedImages = new[] { Item(hdpi), Item(mdpi) },
		};
		second.UseRecordingEngine();
		Assert.True(second.Execute());

		Assert.True(second.ResourceXmlChanged);
		Assert.NotEqual(before, File.ReadAllText(second.GeneratedResourceXml!.ItemSpec));
	}

	/// <summary>
	/// The bucket set the layout task publishes for the incremental state must be exactly the one
	/// the generator consumes.
	/// </summary>
	/// <remarks>
	/// These are the two halves of res.xml's incrementality: ComputeTizenResourceLayout records
	/// what res.xml would be derived from, and GenerateTizenResourceXml derives it. Two
	/// implementations of "which bucket is this image in" that disagree would produce recorded
	/// state saying nothing changed while the generated file would have changed - the exact
	/// failure incremental state exists to prevent - so they share one implementation and this
	/// pins the agreement.
	/// </remarks>
	[Fact]
	public void TheRecordedBucketStateMatchesTheGeneratedDocument()
	{
		var root = CreateTempDirectory();
		var images = new[]
		{
			Item(BucketFile(root, "default_All-XXHDPI")),
			Item(BucketFile(root, "default_All-LDPI")),
			Item(BucketFile(root, "default_All-HDPI")),
			// An app icon: below shared/res, in no bucket at all.
			Item(Path.Combine(root, "shared", "res", "xhdpi", "appicon.xhigh.png")),
		};

		var layout = new ComputeTizenResourceLayout { ProcessedImages = images, SearchRoot = root };
		layout.UseRecordingEngine();
		Assert.True(layout.Execute());

		var generator = new GenerateTizenResourceXml { IntermediateOutputPath = root, ProcessedImages = images };
		generator.UseRecordingEngine();
		Assert.True(generator.Execute());

		var documented = XDocument.Load(generator.GeneratedResourceXml!.ItemSpec)
			.Root!.Element(Ns + "group-image")!
			.Elements(Ns + "node")
			.Select(n => n.Attribute("folder")!.Value.Substring("contents/".Length))
			.ToList();

		Assert.Equal(new[] { "default_All-HDPI", "default_All-LDPI", "default_All-XXHDPI" }, layout.ResourceBuckets);
		Assert.Equal(layout.ResourceBuckets, documented);
	}
}
