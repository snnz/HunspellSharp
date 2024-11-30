using System;
using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  struct Bytes
  {
    byte[] Data;
    int Offset;
    public int Length { get; private set; }

    public Bytes(byte[] data, int offset, int length)
    {
      Data = data;
      Offset = offset;
      Length = length;
    }

    public Bytes(byte[] data)
    {
      Data = data;
      Offset = 0;
      Length = data.Length;
    }

    public byte this[int i] => Data[Offset + i];

    public bool Equals(string value)
    {
      if (Length != value.Length) return false;
      for (int i = 0; i < value.Length; ++i)
        if (Data[Offset + i] != (byte)value[i]) return false;
      return true;
    }

    public bool Contains(string value)
    {
      int last = Offset + Length - value.Length;
      for (int i = Offset; i <= last && (i = Array.IndexOf(Data, (byte)value[0], i, Length - (i - Offset))) >= 0; ++i)
      {
        for (int j = 1; j < value.Length; ++j)
          if (Data[i + j] != value[j]) goto next;
        return true;
      next:;
      }
      return false;
    }

    public int IndexOf(char c)
    {
      var i = Array.IndexOf(Data, (byte)c, Offset, Length);
      return i < 0 ? -1 : (i - Offset);
    }

    public int IndexOf(char c, int start)
    {
      var i = Array.IndexOf(Data, (byte)c, Offset + start, Length - start);
      return i < 0 ? -1 : (i - Offset);
    }

    public char[] Chars(Encoding encoding) => encoding.GetChars(Data, Offset, Length);

    public string String(Encoding encoding) => encoding.GetString(Data, Offset, Length);
    public string Substring(int start, Encoding encoding) => encoding.GetString(Data, Offset + start, Length - start);

    public Bytes Substring(int start) => new Bytes(Data, Offset + start, Length - start);
    public Bytes Substring(int start, int count) => new Bytes(Data, Offset + start, count);

    public void Remove(int start)
    {
      Length = start;
    }

    public void Remove(int start, int count)
    {
      Length -= count;
      Array.Copy(Data, Offset + start + count, Data, Offset + start, Length - start);
    }

    public Bytes ExpandToEndOf(Bytes line)
    {
      return new Bytes(Data, Offset, line.Offset + line.Length - Offset);
    }

    public int IndexIn(Bytes other)
    {
      return Offset - other.Offset;
    }

    public IEnumerable<byte> ToEnumerable()
    {
      for (int i = Offset, end = i + Length; i < end; ++i)
        yield return Data[i];
    }

    public IEnumerator<Bytes> Split()
    {
      for (int i = Offset, j, end = i + Length; ; i = j + 1)
      {
        for (; ; ++i)
        {
          if (i >= end) yield break;
          var c = Data[i];
          if (c != ' ' && c != '\t') break;
        }

        for (j = i + 1; j < end; ++j)
        {
          var c = Data[j];
          if (c == ' ' || c == '\t') break;
        }

        yield return new Bytes(Data, i, j - i);
      }
    }
  }
}
