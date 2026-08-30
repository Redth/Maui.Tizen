#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Maui.Tizen.Build.Tasks
{
	/// <summary>
	/// Rewrites the user authored <c>tizen-manifest.xml</c> with the single project identity
	/// (application id, version, title) and the generated icon / splash resources.
	/// </summary>
	/// <remarks>
	/// Ported from <c>GenerateTizenManifest</c> in dotnet/maui. The upstream task discovered splash
	/// entries through a static field mutated by the splash task in the same MSBuild node, which
	/// breaks whenever either target is skipped for being up to date. This port takes the splash
	/// entries explicitly, either as items or through the map file persisted by
	/// <see cref="GenerateTizenSplashScreens"/>.
	/// </remarks>
	public class GenerateTizenManifest : Task
	{
		const string ApplicationIdPlaceholder = "maui-application-id-placeholder";
		const string LabelPlaceholder = "maui-application-title-placeholder";
		const string ManifestVersionPlaceholder = "0.0.0";
		const string AppIconPlaceholder = "maui-appicon-placeholder";
		const string TizenManifestFileName = "tizen-manifest.xml";
		const string IconDefaultDpiType = "xhdpi";
		const string IconImageExtension = ".png";
		const string UiApplicationName = "ui-application";
		const string PackageName = "package";
		const string AppidName = "appid";
		const string VersionName = "version";
		const string LabelName = "label";
		const string IconName = "icon";
		const string SplashScreensName = "splash-screens";
		const string SplashScreenName = "splash-screen";
		const string DpiName = "dpi";

		[Required]
		public string IntermediateOutputPath { get; set; } = null!;

		[Required]
		public string TizenManifestFile { get; set; } = TizenManifestFileName;

		public string GeneratedFilename { get; set; } = TizenManifestFileName;

		public string? ApplicationId { get; set; }

		public string? ApplicationDisplayVersion { get; set; }

		public string? ApplicationVersion { get; set; }

		public string? ApplicationTitle { get; set; }

		public ITaskItem[]? AppIcon { get; set; }

		public ITaskItem[]? SplashScreen { get; set; }

		/// <summary>
		/// Splash entries with <c>Resolution</c> and <c>Orientation</c> metadata, as produced by
		/// <see cref="GenerateTizenSplashScreens"/>.
		/// </summary>
		public ITaskItem[]? SplashScreenEntries { get; set; }

		/// <summary>Fallback source for splash entries on incremental builds.</summary>
		public string? SplashScreenMapFile { get; set; }

		[Output]
		public ITaskItem GeneratedTizenManifest { get; set; } = null!;

		public override bool Execute()
		{
			try
			{
				Directory.CreateDirectory(IntermediateOutputPath);

				var sourceManifest = Path.IsPathRooted(TizenManifestFile)
					? TizenManifestFile
					: Path.Combine(Environment.CurrentDirectory, TizenManifestFile);

				if (!File.Exists(sourceManifest))
				{
					Log.LogError($"The Tizen manifest '{sourceManifest}' could not be found. Add a 'Platforms/Tizen/tizen-manifest.xml' file or set the 'TizenManifestFile' property.");
					return false;
				}

				var targetFilename = Path.Combine(IntermediateOutputPath, GeneratedFilename);

				var manifest = XDocument.Load(sourceManifest);

				UpdateManifest(manifest);

				manifest.Save(targetFilename);

				GeneratedTizenManifest = new TaskItem(targetFilename);
			}
			catch (Exception ex)
			{
				Log.LogErrorFromException(ex);
			}

			return !Log.HasLoggedErrors;
		}

		void UpdateManifest(XDocument tizenManifest)
		{
			var xmlns = tizenManifest.Root!.GetDefaultNamespace();
			var manifest = tizenManifest.Root;
			var uiApplication = manifest.Element(xmlns + UiApplicationName);

			if (uiApplication == null)
			{
				Log.LogWarning($"The Tizen manifest does not contain a '{UiApplicationName}' element; no single project values were applied.");
				return;
			}

			UpdateSharedManifest(xmlns, manifest);
			UpdateSharedResources(xmlns, manifest);
		}

		void UpdateSharedManifest(XNamespace xmlns, XElement manifest)
		{
			var uiApplication = manifest.Element(xmlns + UiApplicationName)!;

			if (!string.IsNullOrEmpty(ApplicationId))
			{
				UpdateElementAttribute(manifest, PackageName, ApplicationId, ApplicationIdPlaceholder);
				UpdateElementAttribute(uiApplication, AppidName, ApplicationId, ApplicationIdPlaceholder);
			}

			if (!string.IsNullOrEmpty(ApplicationDisplayVersion))
			{
				if (TryMergeVersionNumbers(ApplicationDisplayVersion, out var finalVersion))
					UpdateElementAttribute(manifest, VersionName, finalVersion, ManifestVersionPlaceholder);
				else
					Log.LogWarning($"ApplicationDisplayVersion '{ApplicationDisplayVersion}' was not a valid version for Tizen");
			}

			if (!string.IsNullOrEmpty(ApplicationTitle))
			{
				var label = uiApplication.Element(xmlns + LabelName);
				if (label == null)
				{
					label = new XElement(xmlns + LabelName);
					uiApplication.AddFirst(label);
				}

				UpdateElementValue(label, ApplicationTitle, LabelPlaceholder);
			}
		}

		void UpdateSharedResources(XNamespace xmlns, XElement manifestElement)
		{
			var uiApplicationElement = manifestElement.Element(xmlns + UiApplicationName)!;
			var appIconInfo = AppIcon?.Length > 0 ? TizenImageInfo.Parse(AppIcon[0]) : null;

			if (appIconInfo != null)
			{
				var iconElements = uiApplicationElement.Elements(xmlns + IconName);
				var iconPlaceholderElements = iconElements.Where(d => d.Value == AppIconPlaceholder).ToList();

				foreach (var icon in iconPlaceholderElements)
				{
					var dpiAttribute = icon.Attribute(DpiName);
					if (dpiAttribute == null)
					{
						var defaultDpi = TizenDpiPath.AppIcon.FirstOrDefault(n => n.Path.EndsWith(IconDefaultDpiType, StringComparison.Ordinal));
						icon.Value = IconDefaultDpiType + "/" + appIconInfo.OutputName + defaultDpi?.FileSuffix + IconImageExtension;
					}
					else
					{
						// Note: the upstream implementation concatenated the suffix without the
						// separating '.', producing "appiconxhigh.png" while the Resizetizer writes
						// "appicon.xhigh.png" (the suffix comes from the DpiPath scale suffix
						// ".high" / ".xhigh"). This port emits the name that actually exists.
						var dpiValue = dpiAttribute.Value;
						var fileSuffix = dpiValue == IconDefaultDpiType ? "xhigh" : "high";
						icon.Value = dpiValue + "/" + appIconInfo.OutputName + "." + fileSuffix + IconImageExtension;
					}
				}
			}

			var splashEntries = GetSplashEntries();
			if (SplashScreen?.Length > 0 && splashEntries.Count > 0)
			{
				var splashscreensElement = uiApplicationElement.Element(xmlns + SplashScreensName);
				if (splashscreensElement == null)
				{
					splashscreensElement = new XElement(xmlns + SplashScreensName);
					uiApplicationElement.Add(splashscreensElement);
				}

				foreach (var entry in splashEntries)
				{
					var existing = splashscreensElement.Elements(xmlns + SplashScreenName).Where(d =>
						d.Attribute("type")?.Value == "img"
						&& d.Attribute(DpiName)?.Value == entry.Resolution
						&& d.Attribute("orientation")?.Value == entry.Orientation
						&& d.Attribute("indicator-display")?.Value == "false");

					if (existing.Any())
						continue;

					var splashscreenElement = new XElement(xmlns + SplashScreenName);
					splashscreenElement.SetAttributeValue("src", entry.Source);
					splashscreenElement.SetAttributeValue("type", "img");
					splashscreenElement.SetAttributeValue(DpiName, entry.Resolution);
					splashscreenElement.SetAttributeValue("orientation", entry.Orientation);
					splashscreenElement.SetAttributeValue("indicator-display", "false");
					splashscreensElement.Add(splashscreenElement);
				}
			}
		}

		IReadOnlyList<(string Resolution, string Orientation, string Source)> GetSplashEntries()
		{
			if (SplashScreenEntries?.Length > 0)
			{
				return SplashScreenEntries
					.Select(i => (i.GetMetadata("Resolution"), i.GetMetadata("Orientation"), i.ItemSpec.Replace('\\', '/')))
					.Where(e => !string.IsNullOrEmpty(e.Item1) && !string.IsNullOrEmpty(e.Item2))
					.ToList();
			}

			return GenerateTizenSplashScreens.ReadMap(SplashScreenMapFile);
		}

		static void UpdateElementAttribute(XElement element, XName attrName, string? value, string? placeholder)
		{
			var attr = element.Attribute(attrName);
			if (attr == null || string.IsNullOrEmpty(attr.Value) || attr.Value == placeholder)
				element.SetAttributeValue(attrName, value);
		}

		static void UpdateElementValue(XElement element, string? value, string? placeholder)
		{
			if (string.IsNullOrEmpty(element.Value) || element.Value == placeholder)
				element.Value = value;
		}

		public static bool TryMergeVersionNumbers(string? displayVersion, out string? finalVersion)
		{
			displayVersion = displayVersion?.Trim();
			finalVersion = null;

			var parts = displayVersion?.Split('.') ?? Array.Empty<string>();
			if (parts.Length > 3)
				return false;

			var v = new int[3];
			for (var i = 0; i < 3 && i < parts.Length; i++)
			{
				if (!int.TryParse(parts[i], out var parsed))
					return false;

				v[i] = parsed;
			}

			if (!VerifyTizenVersion(v[0], v[1], v[2]))
				return false;

			finalVersion = $"{v[0]:0}.{v[1]:0}.{v[2]:0}";
			return true;
		}

		static bool VerifyTizenVersion(int x, int y, int z)
			=> !(x < 0 || x > 255 || y < 0 || y > 255 || z < 0 || z > 65535);
	}
}
