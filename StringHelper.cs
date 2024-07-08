using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HunspellSharp
{
  class StringHelper
  {
    const int MinBufSize = 25, MaxCachedBufSize = 100, MaxBuffersCount = 100, MaxStringBuildersCount = 30;
    Stack<char[]> buffers = new Stack<char[]>();
    Stack<StringBuilder> stringBuilders = new Stack<StringBuilder>();

    public char[] PopBuffer(int size)
    {
      if (buffers.Count == 0 || size > MaxCachedBufSize) return new char[Math.Max(size, MinBufSize)];
      var buf = buffers.Pop();
      return buf.Length < size ? new char[Math.Max(size, MinBufSize)] : buf;
    }

    public void PushBuffer(char[] buffer)
    {
      if (buffers.Count < MaxBuffersCount && buffer.Length <= MaxCachedBufSize) buffers.Push(buffer);
    }

    public char[] PeekBuffer(int size)
    {
      char[] buf;
      if (buffers.Count == 0)
      {
        buf = new char[Math.Max(size, MinBufSize)];
        if (size < MaxCachedBufSize) buffers.Push(buf);
      }
      else if ((buf = buffers.Peek()).Length < size)
      {
        buf = new char[Math.Max(size, MinBufSize)];
        if (size < MaxCachedBufSize) { buffers.Pop(); buffers.Push(buf); }
      }
      return buf;
    }

    public string reverseword(string word)
    {
      int n = word.Length;
      if (n < 2) return word;
      var buf = PeekBuffer(n);
      word.CopyTo(0, buf, 0, n);
      Array.Reverse(buf, 0, n);
      return new string(buf, 0, n);
    }

    public string mkinitcap(string s, TextInfo textinfo)
    {
      int n = s.Length;
      if (n == 0) return s;
      var c = textinfo.ToUpper(s[0]);
      if (c == s[0]) return s;
      if (n == 1) return new string(c, 1);
      var buf = PeekBuffer(n);
      buf[0] = c;
      s.CopyTo(1, buf, 1, n - 1);
      return new string(buf, 0, n);
    }

    public string mkinitsmall(string s, TextInfo textinfo)
    {
      int n = s.Length;
      if (n == 0) return s;
      var c = textinfo.ToLower(s[0]);
      if (c == s[0]) return s;
      if (n == 1) return new string(c, 1);
      var buf = PeekBuffer(n);
      buf[0] = c;
      s.CopyTo(1, buf, 1, n - 1);
      return new string(buf, 0, n);
    }


    public StringBuilder PopStringBuilder()
    {
      if (stringBuilders.Count == 0) return new StringBuilder();
      var sb = stringBuilders.Pop();
      sb.Length = 0;
      return sb;
    }

    public void PushStringBuilder(StringBuilder sb)
    {
      if (stringBuilders.Count < MaxStringBuildersCount) stringBuilders.Push(sb);
    }

    public string ToStringPushStringBuilder(StringBuilder sb)
    {
      var s = sb.ToString();
      if (stringBuilders.Count < MaxStringBuildersCount) stringBuilders.Push(sb);
      return s;
    }
  }
}
