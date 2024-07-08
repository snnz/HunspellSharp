using System;
using System.Text;

namespace HunspellSharp.Encodings
{
  class Iso8859_10
  {
    public static Encoding Instance => Iso8859_10_Encoding.Instance;

    class Iso8859_10_Encoding : Encoding
    {
      static Iso8859_10_Encoding() { }

      public override int CodePage => 28600;
      public override string EncodingName => "ISO-8859-10";
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
        '\x0104', '\x0112', '\x0122', '\x012A', '\x0128', '\x0136', '\x00A7', '\x013B', '\x0110', '\x0160',
        '\x0166', '\x017D', '\x00AD', '\x016A', '\x014A', '\x00B0', '\x0105', '\x0113', '\x0123', '\x012B',
        '\x0129', '\x0137', '\x00B7', '\x013C', '\x0111', '\x0161', '\x0167', '\x017E', '\x2015', '\x016B',
        '\x014B', '\x0100', '\x00C1', '\x00C2', '\x00C3', '\x00C4', '\x00C5', '\x00C6', '\x012E', '\x010C',
        '\x00C9', '\x0118', '\x00CB', '\x0116', '\x00CD', '\x00CE', '\x00CF', '\x00D0', '\x0145', '\x014C',
        '\x00D3', '\x00D4', '\x00D5', '\x00D6', '\x0168', '\x00D8', '\x0172', '\x00DA', '\x00DB', '\x00DC',
        '\x00DD', '\x00DE', '\x00DF', '\x0101', '\x00E1', '\x00E2', '\x00E3', '\x00E4', '\x00E5', '\x00E6',
        '\x012F', '\x010D', '\x00E9', '\x0119', '\x00EB', '\x0117', '\x00ED', '\x00EE', '\x00EF', '\x00F0',
        '\x0146', '\x014D', '\x00F3', '\x00F4', '\x00F5', '\x00F6', '\x0169', '\x00F8', '\x0173', '\x00FA',
        '\x00FB', '\x00FC', '\x00FD', '\x00FE', '\x0138'
      };

      internal static readonly Encoding Instance = new Iso8859_10_Encoding();
    }
  }
}
