#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Generates the Tizen <c>res.xml</c> resource manifest describing which resource bucket applies
	/// to which screen DPI range.
	/// </summary>
	/// <remarks>
	/// Ported from <c>TizenResourceXmlGenerator</c> in dotnet/maui. The upstream implementation
	/// enumerated the intermediate directory; this port can additionally be driven from the
	/// <c>MauiProcessedImage</c> items so the generated file is a pure function of the declared
	/// resources rather than of whatever happens to be left on disk.
	/// </remarks>
	public class GenerateTizenResourceXml : Task
	{
		const string NamespaceUri = "http://tizen.org/ns/rm";

		static readonly string[] GroupNames = { "group-image", "group-layout", "group-sound", "group-bin" };

		/// <summary>The Resizetizer image output root, i.e. the folder containing <c>res/contents</c>.</summary>
		[Required]
		public string IntermediateOutputPath { get; set; } = null!;

		/// <summary>
		/// Optional <c>MauiProcessedImage</c> items. When supplied, resource buckets are derived
		/// from these items instead of from a directory scan.
		/// </summary>
		public ITaskItem[]? ProcessedImages { get; set; }

		[Output]
		public ITaskItem? GeneratedResourceXml { get; set; }

		public override bool Execute()
		{
			try
			{
				ExecuteCore();
			}
			catch (Exception ex)
			{
				Log.LogErrorFromException(ex);
			}

			return !Log.HasLoggedErrors;
		}

		void ExecuteCore()
		{
			var outputResourceDir = Path.Combine(IntermediateOutputPath, "res");
			var outputContentsDir = Path.Combine(outputResourceDir, "contents");
			var destination = Path.Combine(outputResourceDir, "res.xml");

			var folders = GetResourceFolders(outputContentsDir);
			if (folders.Count == 0)
			{
				Log.LogMessage(MessageImportance.Low, "No 'res/contents/' resource buckets were found; skipping res.xml generation.");
				return;
			}

			var doc = new XmlDocument();
			doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", "yes"));

			var rootNode = doc.CreateElement("res", NamespaceUri);
			doc.AppendChild(rootNode);

			var groups = new List<XmlElement>();
			foreach (var groupName in GroupNames)
			{
				var group = doc.CreateElement(groupName, NamespaceUri);
				group.SetAttribute("folder", "contents");
				rootNode.AppendChild(group);
				groups.Add(group);
			}

			foreach (var folder in folders)
			{
				var separator = folder.LastIndexOf('-');
				if (separator < 0)
					continue;

				var resolution = folder.Substring(separator + 1).ToUpperInvariant();
				if (!TizenDpiPath.ResolutionRanges.TryGetValue(resolution, out var dpiRange))
					continue;

				foreach (var group in groups)
				{
					var node = doc.CreateElement("node", NamespaceUri);
					node.SetAttribute("folder", $"contents/{folder}");
					node.SetAttribute("screen-dpi-range", dpiRange);
					group.AppendChild(node);
				}

				Log.LogMessage(MessageImportance.Low, $"Added Tizen resource bucket '{folder}' ({dpiRange}).");
			}

			Directory.CreateDirectory(outputResourceDir);
			doc.Save(destination);

			Log.LogMessage(MessageImportance.Low, $"res.xml has been saved to '{outputResourceDir}'.");

			GeneratedResourceXml = new TaskItem(destination);
		}

		/// <summary>
		/// Resource bucket folder names, ordered deterministically so repeated builds produce a byte
		/// identical res.xml.
		/// </summary>
		List<string> GetResourceFolders(string contentsDirectory)
		{
			var folders = new SortedSet<string>(StringComparer.Ordinal);

			if (ProcessedImages?.Length > 0)
			{
				foreach (var item in ProcessedImages)
				{
					var path = item.GetMetadata("FullPath");
					if (string.IsNullOrEmpty(path))
						path = item.ItemSpec;

					var directory = Path.GetDirectoryName(path);
					if (string.IsNullOrEmpty(directory))
						continue;

					// Only consider images that actually live under res/contents.
					var parent = Path.GetFileName(Path.GetDirectoryName(directory) ?? string.Empty);
					if (!string.Equals(parent, "contents", StringComparison.OrdinalIgnoreCase))
						continue;

					folders.Add(Path.GetFileName(directory));
				}
			}
			else if (Directory.Exists(contentsDirectory))
			{
				foreach (var subDir in new DirectoryInfo(contentsDirectory).GetDirectories())
					folders.Add(subDir.Name);
			}

			return folders.ToList();
		}
	}
}
