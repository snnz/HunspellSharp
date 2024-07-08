using System;
using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  class RepList
  {
    private List<replentry> dat;

    public RepList(int n)
    {
      dat = new List<replentry>(Math.Min(n, 16384));
    }

    public int find(string word, int atstart = 0)
    {
      int p1 = 0;
      int p2 = dat.Count - 1;
      int ret = -1;
      while (p1 <= p2)
      {
        int m = (p1 + p2) >> 1;
        int c = string.Compare(word, atstart, dat[m].pattern, 0, dat[m].pattern.Length, StringComparison.Ordinal);
        if (c < 0)
          p2 = m - 1;
        else if (c > 0)
          p1 = m + 1;
        else
        {      // scan in the right half for a longer match
          ret = m;
          p1 = m + 1;
        }
      }
      return ret;
    }

    public string replace(int wordlen, int ind, bool atstart)
    {
      int type = atstart ? 1 : 0;
      if (wordlen == dat[ind].pattern.Length)
        type = atstart ? 3 : 2;
      while (type != 0 && string.IsNullOrEmpty(dat[ind].outstrings[type]))
        type = (type == 2 && !atstart) ? 0 : type - 1;
      return dat[ind].outstrings[type];
    }

    public int add(string pat1, string pat2)
    {
      if (pat1.Length == 0 || pat2.Length == 0)
      {
        return 1;
      }
      // analyse word context
      int type = 0;
      if (pat1[0] == '_')
      {
        pat1 = pat1.Remove(0, 1);
        type = 1;
      }
      if (pat1.Length != 0 && pat1[pat1.Length - 1] == '_')
      {
        type = type + 2;
        pat1 = pat1.Remove(pat1.Length - 1);
      }
      pat1 = pat1.Replace("_", " ");

      // find existing entry
      int m = find(pat1);
      if (m >= 0 && dat[m].pattern == pat1)
      {
        // since already used
        dat[m].outstrings[type] = pat2.Replace("_", " ");
        return 0;
      }

      // make a new entry if none exists
      var r = new replentry(pat1, pat2.Replace("_", " "), type);
      dat.Add(r);
      // sort to the right place in the list
      int i;
      for (i = dat.Count - 1; i > 0; --i)
      {
        if (string.Compare(r.pattern, dat[i - 1].pattern, StringComparison.Ordinal) < 0)
        {
          dat[i] = dat[i - 1];
        }
        else
          break;
      }
      dat[i] = r;
      return 0;
    }

    public string conv(string word)
    {
      StringBuilder dest = null;
      int i0 = 0, wordlen = word.Length;

      for (int i = 0; i < wordlen; ++i)
      {
        int n = find(word, i);
        if (n < 0) continue;

        var l = replace(wordlen - i, n, i == 0);
        if (string.IsNullOrEmpty(l)) continue;

        if (dest == null) dest = new StringBuilder();
        if (i0 < i) dest.Append(word, i0, i - i0);

        dest.Append(l);

        if (dat[n].pattern.Length != 0)
        {
          i += dat[n].pattern.Length - 1;
        }
        i0 = i + 1;
      }

      if (dest == null) return word;
      if (i0 < wordlen) dest.Append(word, i0, wordlen - i0);
      return dest.ToString();
    }

    public bool check_against_breaktable(List<string> breaktable)
    {
      foreach (var i in dat)
      {
        foreach (var outstring in i.outstrings)
          if (!string.IsNullOrEmpty(outstring))
          {
            foreach (var str in breaktable)
            {
              if (outstring.IndexOf(str) >= 0)
              {
                return false;
              }
            }
          }
      }

      return true;
    }
  }
}
