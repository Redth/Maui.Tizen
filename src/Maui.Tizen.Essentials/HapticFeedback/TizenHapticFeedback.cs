using System;
using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices;
using TizenFeedback = Tizen.System.Feedback;
using TizenFeedbackType = Tizen.System.FeedbackType;

namespace Microsoft.Maui.Platforms.Tizen.Essentials
{
	/// <summary>
	/// Tizen implementation of <see cref="IHapticFeedback"/>, backed by <c>Tizen.System.Feedback</c>.
	/// </summary>
	public sealed class TizenHapticFeedback : IHapticFeedback
	{
		/// <inheritdoc/>
		public bool IsSupported
		{
			get
			{
				try
				{
					var feedback = new TizenFeedback();
					return feedback.IsSupportedPattern(TizenFeedbackType.Vibration, "Tap");
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"TizenHapticFeedback.IsSupported failed: {ex.Message}");
					return false;
				}
			}
		}

		/// <inheritdoc/>
		public void Perform(HapticFeedbackType type)
		{
			TizenPermissions.EnsureDeclared<Permissions.Vibrate>();

			var pattern = ConvertType(type);
			var feedback = new TizenFeedback();

			if (!feedback.IsSupportedPattern(TizenFeedbackType.Vibration, pattern))
			{
				throw TizenEssentialsSupport.NotSupported(
					$"{nameof(IHapticFeedback)}.{nameof(Perform)}({type})",
					$"The Tizen feedback pattern '{pattern}' is not supported by this device.");
			}

			feedback.Play(TizenFeedbackType.Vibration, pattern);
		}

		internal static string ConvertType(HapticFeedbackType type) =>
			type switch
			{
				HapticFeedbackType.LongPress => "Hold",
				_ => "Tap",
			};
	}
}
