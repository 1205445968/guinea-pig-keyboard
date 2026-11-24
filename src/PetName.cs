using System;
using System.Globalization;
using System.Text;

namespace WheekKeys
{
    internal static class PetName
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static bool TryNormalize(string input, out string name)
        {
            name = null;
            if (input == null) return false;
            string value = input.Trim();
            int scalars = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (Char.IsLowSurrogate(c)) return false;
                if (Char.IsHighSurrogate(c) && (i + 1 >= value.Length || !Char.IsLowSurrogate(value[i + 1]))) return false;
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, i);
                if (category == UnicodeCategory.Control || category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator || c == '\u061c' || c == '\u200e' || c == '\u200f' ||
                    (c >= '\u202a' && c <= '\u202e') || (c >= '\u2066' && c <= '\u2069')) return false;
                if (++scalars > 16) return false;
                if (Char.IsHighSurrogate(c)) i++;
            }
            try { if (Utf8.GetByteCount(value) > 64) return false; }
            catch (EncoderFallbackException) { return false; }
            name = value;
            return true;
        }

        public static string Encode(string name)
        {
            return Convert.ToBase64String(Utf8.GetBytes(name));
        }

        public static bool TryDecode(string encoded, out string name)
        {
            name = null;
            if (encoded.Length > 88) return false;
            try
            {
                byte[] bytes = Convert.FromBase64String(encoded);
                if (bytes.Length > 64 || Convert.ToBase64String(bytes) != encoded) return false;
                string decoded = Utf8.GetString(bytes);
                string normalized;
                if (!TryNormalize(decoded, out normalized) || normalized != decoded) return false;
                name = decoded;
                return true;
            }
            catch (FormatException) { return false; }
            catch (DecoderFallbackException) { return false; }
        }
    }
}
