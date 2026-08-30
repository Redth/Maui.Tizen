using System;
using Microsoft.Maui.Devices.Sensors;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// MIME types used by the Tizen <c>AppControl</c> based pickers and launchers.
	/// </summary>
	/// <remarks>
	/// dotnet/maui keeps its equivalent <c>FileMimeTypes</c> constants internal, so a standalone
	/// platform backend cannot reference them.
	/// </remarks>
	public static class TizenFileMimeTypes
	{
		/// <summary>Matches any file type.</summary>
		public const string All = "*/*";

		/// <summary>Matches any image file type.</summary>
		public const string ImageAll = "image/*";

		/// <summary>Matches PNG images.</summary>
		public const string ImagePng = "image/png";

		/// <summary>Matches JPEG images.</summary>
		public const string ImageJpg = "image/jpeg";

		/// <summary>Matches any video file type.</summary>
		public const string VideoAll = "video/*";

		/// <summary>Matches PDF documents.</summary>
		public const string Pdf = "application/pdf";
	}

	/// <summary>
	/// Placemark helpers used by the Tizen map integration.
	/// </summary>
	public static class TizenPlacemarkExtensions
	{
		/// <summary>
		/// Builds the URL-escaped, single-line address used for <c>geo:0,0?q=</c> map queries.
		/// </summary>
		/// <param name="placemark">The placemark to format.</param>
		/// <returns>The escaped address.</returns>
		/// <remarks>
		/// Behavioural port of the internal <c>PlacemarkExtensions.GetEscapedAddress</c> helper in
		/// dotnet/maui, which is not accessible from a standalone platform backend.
		/// </remarks>
		public static string GetEscapedAddress(this Placemark placemark)
		{
			ArgumentNullException.ThrowIfNull(placemark);

			var address =
				$"{placemark.Thoroughfare} {placemark.Locality} {placemark.AdminArea} {placemark.PostalCode} {placemark.CountryName}";

			return Uri.EscapeDataString(address);
		}
	}
}
