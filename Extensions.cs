using System;
using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  static class Extensions
  {
    public static bool StartsWith(this string s1, char[] s2)
    {
      if (s1.Length < s2.Length) return false;
      for (int i = s2.Length - 1; i >= 0; --i)
        if (s1[i] != s2[i]) return false;
      return true;
    }

    public static bool EndsWith(this string s1, char[] s2)
    {
      if (s1.Length < s2.Length) return false;
      for (int i = s1.Length - 1, j = s2.Length - 1; j >= 0; --i, --j)
        if (s1[i] != s2[j]) return false;
      return true;
    }

    public static int Length(this IEnumerable<char> chars)
    {
      switch (chars)
      {
        case string s: return s.Length;
        case char[] s: return s.Length;
        default: throw new NotSupportedException();
      }
    }

    public static char At(this IEnumerable<char> chars, int index)
    {
      switch (chars)
      {
        case string s: return s[index];
        case char[] s: return s[index];
        default: throw new NotSupportedException();
      }
    }

    public static void CopyTo(this IEnumerable<char> src, int i1, char[] dst, int i2, int len)
    {
      switch (src)
      {
        case string s: s.CopyTo(i1, dst, i2, len); break;
        case char[] s: Array.Copy(s, i1, dst, i2, len); break;
        default: throw new NotSupportedException();
      }
    }

    public static int IndexOf(this IEnumerable<char> chars, string value, int start)
    {
      switch (chars)
      {
        case string s: return s.IndexOf(value, start, s.Length - start);
        case char[] s:
          for (;; ++start)
          {
            int len = s.Length - start - value.Length - 1;
            if (len < 1 || (start = Array.IndexOf(s, value[0], start, len)) < 0) return -1;
            for (int i = 1; i < value.Length; ++i)
              if (s[i + start] != value[i]) goto next;
            return start;
            next:;
          }
        default: throw new NotSupportedException();
      }
    }

    public static bool IsDot(this char[] s) { return s.Length == 1 && s[0] == '.'; }

    public static StringBuilder TrimRec(this StringBuilder sb)
    {
      int i = sb.Length;
      while (--i > 0 && sb[i] == MSEP.REC) ;
      if (++i < sb.Length) sb.Length = i;
      return sb;
    }
  }
}
