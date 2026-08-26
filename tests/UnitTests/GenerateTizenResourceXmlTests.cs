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
}
