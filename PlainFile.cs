using System;
using System.IO;

namespace HunspellSharp
{
  class PlainFile : FileMgr
  {
    byte[] buf = new byte[65536];
    int pos, end;
    bool eof;

    public PlainFile(Stream stream) : base(stream)
    {
      end = fin.Read(buf, 0, buf.Length);
      if (end >= 3 && buf[0] == 0xEF && buf[1] == 0xBB && buf[2] == 0xBF) pos = 3;
    }

    protected override bool ReadLine(out Bytes line)
    {
      if (pos >= end)
      {
        if (!eof) 
        {
          end = fin.Read(buf, pos = 0, buf.Length);
          if (end == 0) eof = true;
        }
        if (eof)
        {
          line = default;
          return false;
        }
      }

      for (; ;)
      {
        int i = Array.IndexOf<byte>(buf, 10 /* \n */, pos, end - pos);
        if (i >= 0)
        {
          int n = i - pos;
          if (n > 0 && buf[i - 1] == 13 /* \r */) --n;
          line = new Bytes(buf, pos, n);
          pos = i + 1;
          return true;
        }

        if (eof)
        {
          line = new Bytes(buf, pos, end - pos);
          pos = end;
          return true;
        }
        
        if (pos > 0)
        {
          Array.Copy(buf, pos, buf, 0, end - pos);
          end -= pos; pos = 0;
        }

        if (end < buf.Length)
        {
          int n = fin.Read(buf, end, buf.Length - end);
          if (n == 0) eof = true; else end += n;
        }
        else
        {
          line = new Bytes(buf, 0, buf.Length);
          pos = end;
          return true;
        }
      }
    }
  }
}
