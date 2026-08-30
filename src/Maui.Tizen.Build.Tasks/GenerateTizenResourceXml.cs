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

		/// <summary>
		/// True when this run actually replaced the file on disk, false when the generated content
		/// was byte identical to what was already there.
		/// </summary>
		/// <remarks>
		/// Reported so a caller can tell "nothing to do" apart from "did not run", and so the
		/// no-replace behaviour is observable rather than only inferable from a timestamp.
		/// </remarks>
		[Output]
		public bool ResourceXmlChanged { get; set; }

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

			// Rendered to a temporary file and compared, rather than saved straight over the
			// destination.
			//
			// res.xml is an input to TPK packaging and to everything downstream of it, so
			// rewriting a byte identical file on every build re-stamps the whole chain. The
			// generating target is incremental, which is the primary defence; this is the second
			// one, and it covers the builds where the target legitimately runs (a new bucket, a
			// deleted output, a task assembly upgrade) but produces exactly what was already
			// there.
			//
			// The temporary file is written with the same XmlDocument.Save call the destination
			// used to receive, so the bytes that land on disk are unchanged from before - the
			// comparison cannot drift from the writer.
			var temporary = destination + ".tmp";
			doc.Save(temporary);

			try
			{
				if (File.Exists(destination) && FilesAreIdentical(temporary, destination))
				{
					ResourceXmlChanged = false;
					Log.LogMessage(MessageImportance.Low, $"res.xml in '{outputResourceDir}' is already up to date; leaving it untouched.");
				}
				else
				{
					File.Copy(temporary, destination, overwrite: true);
					ResourceXmlChanged = true;
					Log.LogMessage(MessageImportance.Low, $"res.xml has been saved to '{outputResourceDir}'.");
				}
			}
			finally
			{
				try
				{
					File.Delete(temporary);
				}
				catch (IOException)
				{
					// Leaving a stray .tmp behind must never fail the build.
				}
			}

			GeneratedResourceXml = new TaskItem(destination);
		}

		static bool FilesAreIdentical(string first, string second)
		{
			var firstInfo = new FileInfo(first);
			var secondInfo = new FileInfo(second);

			if (firstInfo.Length != secondInfo.Length)
				return false;

			using var a = File.OpenRead(first);
			using var b = File.OpenRead(second);

			var bufferA = new byte[8192];
			var bufferB = new byte[8192];

			while (true)
			{
				var readA = a.Read(bufferA, 0, bufferA.Length);
				var readB = b.Read(bufferB, 0, bufferB.Length);

				if (readA != readB)
					return false;

				if (readA == 0)
					return true;

				for (var i = 0; i < readA; i++)
				{
					if (bufferA[i] != bufferB[i])
						return false;
				}
			}
		}

		/// <summary>
		/// Resource bucket folder names, ordered deterministically so repeated builds produce a byte
		/// identical res.xml.
		/// </summary>
		/// <remarks>
		/// Shared with <see cref="ComputeTizenResourceLayout"/> through
		/// <see cref="TizenResourceBuckets"/>, so the state the targets record for the up-to-date
		/// check and the state this generator actually consumes cannot disagree.
		/// </remarks>
		List<string> GetResourceFolders(string contentsDirectory)
		{
			var folders = ProcessedImages?.Length > 0
				? TizenResourceBuckets.FromProcessedImages(ProcessedImages)
				: TizenResourceBuckets.FromContentsDirectory(contentsDirectory);

			return folders.ToList();
		}
	}
}
