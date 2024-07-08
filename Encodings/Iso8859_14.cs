using System;
using System.Text;

namespace HunspellSharp.Encodings
{
  class Iso8859_14
  {
    public static Encoding Instance => Iso8859_14_Encoding.Instance;

    class Iso8859_14_Encoding : Encoding
    {
      static Iso8859_14_Encoding() { }

      public override int CodePage => 28604;
      public override string EncodingName => "ISO-8859-14";
      public override bool IsSingleByte => true;

      public override int GetMaxByteCount(int charCount) => charCount;

      public override int GetByteCount(char[] chars, int index, int count) => count;

      public override int GetBytes(char[] chars, int charIndex, int charCount, byte[] bytes, int byteIndex)
      {
        for (int i, n = charCount; n-- > 0;)
        {
          var c = chars[charIndex++];
          bytes[byteIndex++] = c < '\xA1' ? (byte)c : (i = Array.IndexOf(aboveA0, c)) < 0 ? (byte)0x3F : (byte)(i + 0xA1);
        }
        return charCount;
      }

      public override int GetMaxCharCount(int byteCount) => byteCount;

      public override int GetCharCount(byte[] bytes, int index, int count) => count;

      public override int GetChars(byte[] bytes, int byteIndex, int byteCount, char[] chars, int charIndex)
      {
        for (int n = byteCount; n-- > 0;)
        {
          var b = bytes[byteIndex++];
          chars[charIndex++] = b < 0xA1 ? (char)b : aboveA0[b - 0xA1];
        }
        return byteCount;
      }

      static readonly char[] aboveA0 =
      {
        '\x1E02', '\x1E03', '\x00A3', '\x010A', '\x010B', '\x1E0A', '\x00A7', '\x1E80', '\x00A9', '\x1E82',
        '\x1E0B', '\x1EF2', '\x00AD', '\x00AE', '\x0178', '\x1E1E', '\x1E1F', '\x0120', '\x0121', '\x1E40',
        '\x1E41', '\x00B6', '\x1E56', '\x1E81', '\x1E57', '\x1E83', '\x1E60', '\x1EF3', '\x1E84', '\x1E85',
        '\x1E61', '\x00C0', '\x00C1', '\x00C2', '\x00C3', '\x00C4', '\x00C5', '\x00C6', '\x00C7', '\x00C8',
        '\x00C9', '\x00CA', '\x00CB', '\x00CC', '\x00CD', '\x00CE', '\x00CF', '\x0174', '\x00D1', '\x00D2',
        '\x00D3', '\x00D4', '\x00D5', '\x00D6', '\x1E6A', '\x00D8', '\x00D9', '\x00DA', '\x00DB', '\x00DC',
        '\x00DD', '\x0176', '\x00DF', '\x00E0', '\x00E1', '\x00E2', '\x00E3', '\x00E4', '\x00E5', '\x00E6',
        '\x00E7', '\x00E8', '\x00E9', '\x00EA', '\x00EB', '\x00EC', '\x00ED', '\x00EE', '\x00EF', '\x0175',
        '\x00F1', '\x00F2', '\x00F3', '\x00F4', '\x00F5', '\x00F6', '\x1E6B', '\x00F8', '\x00F9', '\x00FA',
        '\x00FB', '\x00FC', '\x00FD', '\x0177', '\x00FF'
      };

      internal static readonly Encoding Instance = new Iso8859_14_Encoding();
    }
  }
}
