using System;
using System.IO;

namespace HunspellSharp
{
  using static Utils;

  class Hunzip : FileMgr
  {
    struct bit
    {
      public byte c0, c1;
      public int v0, v1;
    };

    const int BUFSIZE = 65536;
    const int BASEBITREC = 5000;

    int bufsiz, lastbit, inc, inbits, outc;
    bit[] dec = new bit[0];                // code table
    byte[] @in = new byte[BUFSIZE];        // input buffer
    byte[] @out = new byte[BUFSIZE + 1];   // Huffman-decoded buffer
    byte[] line = new byte[BUFSIZE + 50];  // decoded line
    int linelen;
    byte[] linebuf = new byte[BUFSIZE];
    bool firstLine = true;

    public Hunzip(Stream stream, byte[] key) : base(stream)
    {
      @in[0] = @out[0] = 0;
      bufsiz = getcode(key) ? getbuf() : -1;
    }

    bool getcode(byte[] key)
    {
      byte[] c = new byte[2];
      int i, j, n, allocatedbit = BASEBITREC;
      int enc = 0;

      // read magic number
      if (fin.Read(@in, 0, 3) != 3 ||
          !(@in[0] == 0x68 && @in[1] == 0x7A && (@in[2] == 0x30 || @in[2] == 0x31)))
      {
        HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
        return false;
      }

      // check encryption
      if (@in[2] == 0x31)
      {
        byte cs;
        if (key == null)
        {
          HUNSPELL_WARNING(true, Properties.Resources.BadPassword, this);
          return false;
        }
        if (fin.Read(c, 0, 1) != 1)
        {
          HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
          return false;
        }
        for (cs = 0; enc < key.Length; enc++)
          cs ^= key[enc];
        if (cs != c[0])
        {
          HUNSPELL_WARNING(true, Properties.Resources.BadPassword, this);
          return false;
        }
        enc = 0;
      }
      else
        key = null;

      // read record count
      if (fin.Read(c, 0, 2) != 2)
      {
        HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
        return false;
      }

      if (key != null)
      {
        c[0] ^= key[enc];
        if (++enc >= key.Length)
          enc = 0;
        c[1] ^= key[enc];
      }

      n = (c[0] << 8) + c[1];
      Array.Resize(ref dec, BASEBITREC);
      dec[0].v0 = 0;
      dec[0].v1 = 0;

      // read codes
      for (i = 0; i < n; i++)
      {
        byte[] l = new byte[1];
        if (fin.Read(c, 0, 2) != 2)
        {
          HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
          return false;
        }

        if (key != null)
        {
          if (++enc >= key.Length)
            enc = 0;
          c[0] ^= key[enc];
          if (++enc >= key.Length)
            enc = 0;
          c[1] ^= key[enc];
        }

        if (fin.Read(l, 0, 1) != 1)
        {
          HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
          return false;
        }

        if (key != null)
        {
          if (++enc >= key.Length)
            enc = 0;
          l[0] ^= key[enc];
        }

        var nl = (l[0] >> 3) + 1;
        if (fin.Read(@in, 0, nl) != nl)
        {
          HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
          return false;
        }

        if (key != null)
          for (j = 0; j < nl; j++)
          {
            if (++enc >= key.Length)
              enc = 0;
            @in[j] ^= key[enc];
          }

        int p = 0;
        for (j = 0; j < l[0]; j++)
        {
          bool b = (@in[(j >> 3)] & (1 << (7 - (j & 7)))) != 0;
          int oldp = p;
          p = b ? dec[p].v1 : dec[p].v0;
          if (p == 0)
          {
            lastbit++;
            if (lastbit == allocatedbit)
            {
              allocatedbit += BASEBITREC;
              Array.Resize(ref dec, allocatedbit);
            }
            dec[lastbit].v0 = 0;
            dec[lastbit].v1 = 0;
            if (b) dec[oldp].v1 = lastbit; else dec[oldp].v0 = lastbit;
            p = lastbit;
          }
        }
        dec[p].c0 = c[0];
        dec[p].c1 = c[1];
      }

      return true;
    }

    int getbuf()
    {
      int p = 0;
      int o = 0;
      do
      {
        if (inc == 0)
        {
          inbits = fin.Read(@in, 0, BUFSIZE) << 3;
        }
        for (; inc < inbits; inc++)
        {
          bool b = (@in[inc >> 3] & (1 << (7 - (inc & 7)))) != 0;
          int oldp = p;
          p = b ? dec[p].v1 : dec[p].v0;
          if (p == 0)
          {
            if (oldp == lastbit)
            {
              try
              {
                fin.Close();
              }
              finally
              {
                fin = null;
              }
              // add last odd byte
              if (dec[lastbit].c0 != 0)
                @out[o++] = dec[lastbit].c1;
              return o;
            }
            @out[o++] = dec[oldp].c0;
            @out[o++] = dec[oldp].c1;
            if (o == BUFSIZE)
              return o;
            p = b ? dec[p].v1 : dec[p].v0;
          }
        }

        inc = 0;
      } while (inbits == BUFSIZE * 8);
      
      HUNSPELL_WARNING(true, Properties.Resources.NotHzipFormat, this);
      return -1;
    }

    protected override bool ReadLine(out Bytes result)
    {
      if (bufsiz == -1)
      {
        result = default;
        return false;
      }

      int l = 0, left = 0, right = 0;
      bool eol = false;

      while (l < bufsiz && !eol)
      {
        switch (linebuf[l++] = @out[outc]) {
          case 9: // '\t'
            break;
          case 31:
            {  // escape
              if (++outc == bufsiz)
              {
                bufsiz = getbuf();
                outc = 0;
              }
              linebuf[l - 1] = @out[outc];
              break;
            }
          case 32: // ' '
            break;

          default:
            if (@out[outc] < 47) {
              if (@out[outc] > 32) {
                right = @out[outc] - 31;
                if (++outc == bufsiz)
                {
                  bufsiz = getbuf();
                  outc = 0;
                }
              }
              if (@out[outc] == 30)
                left = 9;
              else
                left = @out[outc];
              linebuf[l - 1] = 10; // '\n'
              eol = true;
            }
            break;
        }
        if (++outc == bufsiz)
        {
          outc = 0;
          bufsiz = fin != null ? getbuf() : -1;
        }
      }
      if (right != 0)
      {
        Array.Copy(line, linelen - right - 1, linebuf, l - 1, right + 1);
        l += right;
      }
      Array.Copy(linebuf, 0, line, left, l);
      linelen = left + l;

      int i = 0;
      if (firstLine)
      {
        if (linelen >= 3 && line[0] == 0xEF && line[1] == 0xBB && line[2] == 0xBF) i = 3;
        firstLine = false;
      }
      int n = linelen - i;
      if (n > 0 && line[linelen - 1] == 10) // '\n'
      {
        if (--n > 0 && line[linelen - 2] == 13) --n; // '\r'
      }
      result = new Bytes(line, i, n);
      return true;
    }
  }
}
