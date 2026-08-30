using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Platforms.Tizen.Essentials;
using Microsoft.Maui.Storage;
using Xunit;

namespace Maui.Tizen.Essentials.Tests;

public class TizenFileResultBlockerTests
{
	[Fact]
	public async Task NeutralFileResultCannotOpenAnExistingTizenPath()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, "file-result-blocker");
		Directory.CreateDirectory(directory);
		var path = Path.Combine(directory, "picked.txt");
		await File.WriteAllTextAsync(path, "picked", TestContext.Current.CancellationToken);

		try
		{
			var result = new FileResult(path, "text/plain");

			Assert.Equal(path, result.FullPath);
			Assert.Equal("text/plain", result.ContentType);
			var exception = await Record.ExceptionAsync(() => result.OpenReadAsync());

			Assert.NotNull(exception);
			Assert.Equal("NotImplementedInReferenceAssemblyException", exception.GetType().Name);
		}
		finally
		{
			File.Delete(path);
			Directory.Delete(directory);
		}
	}

	[Fact]
	public void FileBaseWrapperFlowsAreBlockedButExplicitPathFallbackWorks()
	{
		var result = new FileResult("/data/media/photo.png", "image/png");

		Assert.Equal(
			"NotImplementedInReferenceAssemblyException",
			Assert.ThrowsAny<Exception>(() => new ShareFile(result)).GetType().Name);
		Assert.Equal(
			"NotImplementedInReferenceAssemblyException",
			Assert.ThrowsAny<Exception>(() => new EmailAttachment(result)).GetType().Name);
		Assert.Equal(
			"NotImplementedInReferenceAssemblyException",
			Assert.ThrowsAny<Exception>(() => new OpenFileRequest("Photo", result)).GetType().Name);

		var share = TizenShare.CreateFilePayload([new ShareFile(result.FullPath, "image/png")]);
		var email = TizenEmail.CreateAttachmentPayload([new EmailAttachment(result.FullPath, "image/png")]);
		var launch = TizenLauncher.CreateOpenFilePayload(
			new OpenFileRequest("Photo", new ReadOnlyFile(result.FullPath, "image/png")));

		Assert.Equal(["/data/media/photo.png"], share.Paths);
		Assert.Equal(["/data/media/photo.png"], email.Paths);
		Assert.Equal("image/png", share.Mime);
		Assert.Equal("image/png", email.Mime);
		Assert.Equal("image/png", launch.Mime);
		Assert.Equal("file:///data/media/photo.png", launch.Uri);

		var pathOnlyLaunch = TizenLauncher.CreateOpenFilePayload(
			new OpenFileRequest
			{
				Title = "Document",
				File = new ReadOnlyFile("/data/media/document.pdf"),
			});
		Assert.Equal("application/pdf", pathOnlyLaunch.Mime);
	}
}
