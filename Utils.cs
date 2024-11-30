using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HunspellSharp
{
  static class Utils
  {
    public const int DEFAULTFLAGS = 65510;
    public const int FORBIDDENWORD = 65510;
    public const int ONLYUPCASEFLAG = 65511;

    static char[] seps = new char[] { ' ', '\t' };

    public static string[] SplitIn2(string line)
    {
      return line.Split(seps, 2, StringSplitOptions.RemoveEmptyEntries);
    }

    public static int atoi(string a)
    {
      return int.TryParse(a, out var i) ? i : 0;
    }

    public static int atoi(Bytes a)
    {
      try
      {
        return int.Parse(a.String(Encoding.ASCII));
      }
      catch
      {
        return 0;
      }
    }

    /* get type of capitalization */
    public static CapType get_captype(string word, TextInfo textinfo)
    {
      int n = word.Length;
      if (n == 0) return CapType.NOCAP;

      // now determine the capitalization type of the first nl letters
      bool hasLower = false, hasNoCaps = true;
      char c;
      for (int i = n - 1; i > 0; --i) {
        if (char.IsUpper(c = word[i]))
        {
          hasNoCaps = false;
          if (hasLower) break;
        }
        else if (!hasLower && ('a' <= c && c <= 'z' || char.IsLower(c) && textinfo.ToUpper(c) != c))
          hasLower = true;
      }

      // now finally set the captype
      return char.IsUpper(c = word[0]) ? (hasNoCaps ? CapType.INITCAP : hasLower ? CapType.HUHINITCAP : CapType.ALLCAP) :
                                         (hasNoCaps ? CapType.NOCAP : (hasLower || 'a' <= c && c <= 'z' || char.IsLower(c) && textinfo.ToUpper(c) != c) ? CapType.HUHCAP : CapType.ALLCAP);
    }

    public static void GetLanguageAndTextInfo(string lang, out LANG l, ref TextInfo textinfo)
    {
      int lcid;

      switch (lang)
      {
        case "ar": lcid = 0x401; l = LANG.ar; break;
        case "az_AZ": // for back-compatibility
        case "az": lcid = 0x42C; l = LANG.az; break;
        case "bg": lcid = 0x402; l = LANG.bg; break;
        case "ca": lcid = 0x403; l = LANG.ca; break;
        case "crh": lcid = -1; l = LANG.crh; break;
        case "cs": lcid = 0x405; l = LANG.cs; break;
        case "da": lcid = 0x406; l = LANG.da; break;
        case "de": lcid = 0x407; l = LANG.de; break;
        case "el": lcid = 0x408; l = LANG.el; break;
        case "en": lcid = 0x409; l = LANG.en; break;
        case "es": lcid = 0x40A; l = LANG.es; break;
        case "eu": lcid = 0x42D; l = LANG.eu; break;
        case "gl": lcid = 0x456; l = LANG.gl; break;
        case "fr": lcid = 0x40C; l = LANG.fr; break;
        case "hr": lcid = 0x41A; l = LANG.hr; break;
        case "hu_HU": // for back-compatibility
        case "hu": lcid = 0x40E; l = LANG.hu; break;
        case "it": lcid = 0x410; l = LANG.it; break;
        case "la": lcid = 0x476; l = LANG.la; break;
        case "lv": lcid = 0x426; l = LANG.lv; break;
        case "nl": lcid = 0x413; l = LANG.nl; break;
        case "pl": lcid = 0x415; l = LANG.pl; break;
        case "pt": lcid = 0x816; l = LANG.pt; break;
        case "sv": lcid = 0x424; l = LANG.sv; break;
        case "tr_TR": // for back-compatibility
        case "tr": lcid = 0x41F; l = LANG.tr; break;
        case "ru": lcid = 0x419; l = LANG.ru; break;
        case "uk": lcid = 0x422; l = LANG.uk; break;
        default: lcid = -1; l = LANG.xx; break;
      }

      try
      {
        var ci = lcid > 0 ? CultureInfo.GetCultureInfo(lcid) : CultureInfo.GetCultureInfo(lang.Replace('_', '-'));
        if (ci != CultureInfo.InvariantCulture) textinfo = ci.TextInfo;
      }
      catch
      {
      }
    }

    // uniq line in place
    public static string line_uniq(string text)
    {
      var lines = text.Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries);
      if (lines.Length == 0) return string.Empty;

      int n = 1;
      for (int i = 1; i < lines.Length; ++i)
      {
        if (Array.IndexOf(lines, lines[i], 0, n) < 0)
        {
          if (n < i) lines[n] = lines[i];
          ++n;
        }
      }

      return n < lines.Length ? string.Join(MSEP.REC_as_string, lines, 0, n) : text;
    }

    // uniq and boundary for compound analysis: "1\n\2\n\1" -> " ( \1 | \2 ) "
    public static string line_uniq_app(string text)
    {
      if (text.IndexOf(MSEP.REC) < 0) return text;

      var lines = text.Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries);
      switch (lines.Length)
      {
        case 0:
          return string.Empty;
        
        case 1:
          return lines[0];

        default:
          return " ( " + string.Join(" | ", lines) + " ) ";
      }
    }


    static char[] copy_field_stop_chars = new char[] { ' ', '\t', '\n' };

    public static int fieldlen(string r, int start)
    {
      int i = r.IndexOfAny(copy_field_stop_chars, start);
      return (i >= 0 ? i : r.Length) - start;
    }

    public static string copy_field(string morph, int pos, string var)
    {
      if (string.IsNullOrEmpty(morph) ||
          (pos = morph.IndexOf(var, pos)) < 0)
      {
        return null;
      }

      int end = morph.IndexOfAny(copy_field_stop_chars, pos += MORPH.TAG_LEN);
      if (end < 0) end = morph.Length;
      return morph.Substring(pos, end - pos);
    }

    public static void copy_field(StringBuilder dest, string morph, int pos, string var)
    {
      if (string.IsNullOrEmpty(morph) ||
          (pos = morph.IndexOf(var, pos)) < 0)
      {
        return;
      }

      int end = morph.IndexOfAny(copy_field_stop_chars, pos += MORPH.TAG_LEN);
      if (end < 0) end = morph.Length;
      dest.Append(morph, pos, end - pos);
    }

    public static List<string> uniqlist(List<string> list)
    {
      int n = list.Count;
      if (n < 2)
        return list;

      int j = 1;
      for (int i = 1; i < n; ++i)
      {
        if (list.IndexOf(list[i], 0, j) < 0)
        {
          if (j < i) list[j] = list[i];
          ++j;
        }
      }

      if (j < n) list.RemoveRange(j, n - j);
      return list;
    }

    public static void SortRemoveDuplicates(ref ushort[] a)
    {
      int n = a.Length;
      if (n > 1)
      {
        Array.Sort(a);
        var prev = a[0];
        int j;
        for (int i = j = 1; i < n; ++i)
        {
          var v = a[i];
          if (v != prev)
          {
            if (j < i) a[j] = v;
            ++j;
            prev = v;
          }
        }
        if (j < n) Array.Resize(ref a, j);
      }
    }

    public static bool TESTAFF(ushort[] a, ushort b)
    {
      if (a == null) return false;
      if (a.Length < 5)
      {
        for (int i = a.Length - 1; i >= 0; --i)
          if (a[i] == b) return true; else if (a[i] < b) break;
        return false;
      }
      int lo = 0, hi = a.Length;
      while (lo < hi)
      {
        int i = (lo + hi) / 2;
        if (a[i] == b) return true;
        if (a[i] < b)
          lo = i + 1;
        else
          hi = i;
      }
      return false;
    }

    public static void Heapify<V>(int[] keys, V[] values)
    {
      int key = keys[0], child = keys[2] < keys[1] ? 2 : 1;
      if (keys[child] >= key) return;
      int i = 0, n = keys.Length;
      var value = values[0];
      do
      {
        keys[i] = keys[child];
        values[i] = values[child];
        i = child;
        child = 2 * i + 1;
        if (child >= n) break;
        if (child + 1 < n && keys[child + 1] < keys[child]) ++child;
      } while (keys[child] < key);
      keys[i] = key;
      values[i] = value;
    }

    public static readonly char[] EmptyCharArray = new char[0];
    public static readonly char[] Dot = new char[] { '.' };

    public static IHunspellWarningHandler warningHandler;

    static string FormatMessage(string message, object[] args)
    {
      int n = args.Length;
      if (n > 0 && args[n - 1] is FileMgr file)
        --n;
      else
        file = null;
      if (n > 0) message = string.Format(message, args);
      if (file == null) return message;
      var name = file.Name;
      if (name != null) return $"{name}({file.getlinenum()}): {message}";
      return string.Format(Properties.Resources.MessageWithLineNumber, file.getlinenum(), message);
    }

    public static void HUNSPELL_WARNING(bool error, string format, params object[] args)
    {
      var message = FormatMessage(format, args);
      if (error && Hunspell.StrictFormat) throw new HunspellException(message);
      if (warningHandler == null || !warningHandler.HandleWarning(message))
        System.Diagnostics.Debug.WriteLine(message);
    }
  }
}
