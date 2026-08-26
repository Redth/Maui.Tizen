using System;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.Maui.Platforms.Tizen.BlazorWebView.Tests
{
	/// <summary>
	/// Verifies the static asset file provider. The Tizen resource directory is a constructor argument
	/// rather than a call into <c>Tizen.Applications.Application.Current</c>, so the real provider can be
	/// exercised against a temporary directory on the host.
	/// </summary>
	public sealed class TizenAssetFileProviderTests : IDisposable
	{
		private readonly string _resourceDirectory;

		public TizenAssetFileProviderTests()
		{
			_resourceDirectory = Path.Combine(Path.GetTempPath(), "maui-tizen-bwv-" + Guid.NewGuid().ToString("n"));
			Directory.CreateDirectory(Path.Combine(_resourceDirectory, "wwwroot", "css"));
			File.WriteAllText(Path.Combine(_resourceDirectory, "wwwroot", "index.html"), "<html>hello</html>", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			File.WriteAllText(Path.Combine(_resourceDirectory, "wwwroot", "css", "app.css"), "body{}", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}

		public void Dispose()
		{
			if (Directory.Exists(_resourceDirectory))
			{
				Directory.Delete(_resourceDirectory, recursive: true);
			}
		}

		private TizenAssetFileProvider CreateProvider(string contentRoot = "wwwroot")
			=> new(_resourceDirectory, contentRoot);

		[Fact]
		public void RootsContentUnderTheTizenResourceDirectory()
		{
			var provider = CreateProvider();

			Assert.Equal(Path.Combine(_resourceDirectory, "wwwroot"), provider.RootDirectory);
		}

		[Fact]
		public void ResolvesAnExistingFile()
		{
			var provider = CreateProvider();

			var file = provider.GetFileInfo("index.html");

			Assert.True(file.Exists);
			Assert.Equal("index.html", file.Name);
			Assert.Equal(18, file.Length);
			Assert.False(file.IsDirectory);
		}

		[Fact]
		public void ResolvesRootedSubpathsHandedOutByWebViewManager()
		{
			// WebViewManager asks for "/index.html"; Path.Combine would otherwise treat that as absolute
			// and silently discard the Tizen resource root.
			var provider = CreateProvider();

			Assert.True(provider.GetFileInfo("/index.html").Exists);
			Assert.True(provider.GetFileInfo("/css/app.css").Exists);
		}

		[Fact]
		public void ReadsFileContent()
		{
			var provider = CreateProvider();

			using var stream = provider.GetFileInfo("index.html").CreateReadStream();
			using var reader = new StreamReader(stream);

			Assert.Equal("<html>hello</html>", reader.ReadToEnd());
		}

		[Fact]
		public void ReportsMissingFilesWithoutThrowing()
		{
			var provider = CreateProvider();

			var file = provider.GetFileInfo("does-not-exist.html");

			Assert.False(file.Exists);
			Assert.Equal(-1, file.Length);
		}

		[Fact]
		public void UsesAStableLastModifiedValue()
		{
			// Tizen resource files are read-only application assets; a stable epoch value keeps the
			// generated ETag/Last-Modified handling deterministic.
			var provider = CreateProvider();

			Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(0), provider.GetFileInfo("index.html").LastModified);
		}

		[Fact]
		public void DoesNotEnumerateDirectories()
		{
			// Directory enumeration is never used by BlazorWebView or WebViewManager.
			var provider = CreateProvider();

			var contents = provider.GetDirectoryContents("css");

			Assert.False(contents.Exists);
			Assert.Empty(contents);
		}

		[Fact]
		public void WatchReturnsANullChangeToken()
		{
			var provider = CreateProvider();

			var token = provider.Watch("**/*");

			Assert.Same(NullChangeToken.Singleton, token);
			Assert.False(token.HasChanged);
			Assert.False(token.ActiveChangeCallbacks);
		}

		[Fact]
		public void SupportsAnEmptyContentRoot()
		{
			var provider = CreateProvider(contentRoot: string.Empty);

			Assert.Equal(_resourceDirectory, provider.RootDirectory.TrimEnd(Path.DirectorySeparatorChar));
			Assert.True(provider.GetFileInfo("wwwroot/index.html").Exists);
		}

		[Fact]
		public void RejectsANullResourceDirectory()
		{
			Assert.Throws<ArgumentNullException>(() => new TizenAssetFileProvider(null!, "wwwroot"));
		}

		[Fact]
		public void ImplementsTheFileProviderContract()
		{
			Assert.IsAssignableFrom<IFileProvider>(CreateProvider());
		}
	}
}
