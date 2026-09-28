using System.Runtime.InteropServices;
using System.Text;

namespace CaptionTranslator.Speech
{
    /// <summary>Decides whether a caption speaker is the user, so their own lines are not read back to them.</summary>
    public static class SpeakerMatcher
    {
        private const int nameDisplay = 3;

        /// <summary>
        /// True if the caption speaker is the given name. Case and extra spaces are ignored, and Teams additions
        /// such as "(You)", "(Guest)" or "(Company)" after the name still match.
        /// </summary>
        public static bool IsSameSpeaker(string speaker, string? myName)
        {
            string me = Normalize(myName);
            string other = Normalize(speaker);
            if (me.Length == 0 || other.Length == 0)
                return false;

            if (other == me)
                return true;

            // "Name (You)" / "Name (Guest)": the caption name starts with my name followed by a bracket.
            return other.StartsWith(me + " (", StringComparison.Ordinal) || me.StartsWith(other + " (", StringComparison.Ordinal);
        }

        /// <summary>The Windows display name of the signed-in user (e.g. "Muster Anna"), used to prefill "My name in Teams".</summary>
        public static string GetWindowsDisplayName()
        {
            try
            {
                int size = 256;
                StringBuilder builder = new StringBuilder(size);
                if (GetUserNameEx(nameDisplay, builder, ref size) && builder.Length > 0)
                    return builder.ToString().Trim();
            }
            catch (Exception exception) when (exception is DllNotFoundException || exception is EntryPointNotFoundException)
            {
                // Not available on this system; the user can type the name.
                Log.Info("Windows display name not available: " + exception.Message);
            }

            return string.Empty;
        }

        private static string Normalize(string? name)
            => string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetUserNameEx(int nameFormat, StringBuilder userName, ref int size);
    }
}
