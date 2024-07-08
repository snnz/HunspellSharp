using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace HunspellSharp
{
  using static Utils;

  /// <summary>
  /// Implements Hunspell checker
  /// </summary>
  public partial class Hunspell : IDisposable
  {
    StringHelper helper = new StringHelper();

    const string HZIP_EXTENSION = ".hz";
    const string SPELL_XML = "<?xml?>";

    const int MAXSHARPS = 5;
    const int MAXWORDLEN = 100;
    
    const long TIMELIMIT_GLOBAL = 1000 / 4;

    ThreadLocal<Context> context = new ThreadLocal<Context>(() => new Context());
    Context Context => context.Value;

    /// <summary>
    /// Initializes a new instance of the <see cref="Hunspell" /> for the specific streams.
    /// </summary>
    /// <param name="aff">Affix stream</param>
    /// <param name="dic">Dictionary stream</param>
    public Hunspell(Stream aff, Stream dic)
    {
      LoadAff(aff is HzipStream hzAff ? (FileMgr)hzAff.CreateHunzip() : new PlainFile(aff));
      LoadDic(dic is HzipStream hzDic ? (FileMgr)hzDic.CreateHunzip() : new PlainFile(dic));

      InitializeSuggestMgr();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Hunspell" /> for the specified affix and dictionary file paths.
    /// </summary>
    /// <param name="aff">Affix file path</param>
    /// <param name="dic">Dictionary file path</param>
    /// <param name="key">Optional hzip key</param>
    public Hunspell(string aff, string dic, byte[] key = null)
    {
      try
      {
        using (var stream = new FileStream(aff, FileMode.Open, FileAccess.Read, FileShare.Read))
          LoadAff(new PlainFile(stream));
      }
      catch (FileNotFoundException e)
      {
        try
        {
          using (var stream = new FileStream(aff + HZIP_EXTENSION, FileMode.Open, FileAccess.Read, FileShare.Read))
            LoadAff(new Hunzip(stream, key));
        }
        catch (FileNotFoundException)
        {
          throw e;
        }
      }

      try
      {
        using (var stream = new FileStream(dic, FileMode.Open, FileAccess.Read, FileShare.Read))
          LoadDic(new PlainFile(stream));
      }
      catch (FileNotFoundException e)
      {
        try
        {
          using (var stream = new FileStream(dic + HZIP_EXTENSION, FileMode.Open, FileAccess.Read, FileShare.Read))
            LoadDic(new Hunzip(stream, key));
        }
        catch (FileNotFoundException)
        {
          throw e;
        }
      }

      InitializeSuggestMgr();
    }

    /// <summary>
    /// Loads extra dictionary.
    /// </summary>
    /// <param name="dic">Dictionary stream</param>
    /// <returns><code>true</code> on success, <code>false</code> if there were format errors in non-strict mode
    /// In strict mode (<code>StrictFormat == true</code>) it throws an exception.
    /// It may also throw standard I/O exceptions.</returns>
    public bool AddDic(Stream dic)
    {
      return load_tables(dic is HzipStream hzDic ? (FileMgr)hzDic.CreateHunzip() : new PlainFile(dic));
    }


    /// <summary>
    /// Loads extra dictionary.
    /// </summary>
    /// <param name="dic">Dictionary file path</param>
    /// <param name="key">Optional hzip key</param>
    /// <returns><code>true</code> on success, <code>false</code> if there were format errors in non-strict mode
    /// In strict mode (<code>StrictFormat == true</code>) it throws an exception.
    /// It may also throw standard I/O exceptions.</returns>
    public bool AddDic(string dic, byte[] key = null)
    {
      try
      {
        using (var stream = new FileStream(dic, FileMode.Open, FileAccess.Read, FileShare.Read))
          return load_tables(new PlainFile(stream));
      }
      catch (FileNotFoundException e)
      {
        try
        {
          using (var stream = new FileStream(dic + HZIP_EXTENSION, FileMode.Open, FileAccess.Read, FileShare.Read))
            return load_tables(new Hunzip(stream, key));
        }
        catch (FileNotFoundException)
        {
          throw e;
        }
      }
    }

    // make a copy of src at destination while removing all leading
    // blanks and removing any trailing periods after recording
    // their presence with the abbreviation flag
    // also since already going through character by character,
    // set the capitalization type
    // return the length of the "cleaned" (and UTF-8 encoded) word

    string cleanword2(Context ctx,
                      string src,
                      out CapType pcaptype,
                      out int pabbrev)
    {
      // remove IGNORE characters from the string
      src = remove_ignored_chars(src, ctx);

      int q = 0, nl = src.Length;

      // first skip over any leading blanks
      while (nl > 0 && src[q] == ' ')
      {
        ++q;
        nl--;
      }

      // now strip off any trailing periods (recording their presence)
      pabbrev = 0;

      while ((nl > 0) && (src[q + nl - 1] == '.'))
      {
        nl--;
        pabbrev++;
      }

      // if no characters are left it can't be capitalized
      if (nl <= 0)
      {
        pcaptype = CapType.NOCAP;
        return string.Empty;
      }

      if (nl < src.Length) src = src.Substring(q, nl);
      pcaptype = get_captype(src, textinfo);
      return src;
    }

    string cleanword(string src,
                     out CapType pcaptype,
                     out int pabbrev)
    {
      int q = 0, nl = src.Length;

      // first skip over any leading blanks
      while (nl > 0 && src[q] == ' ')
      {
        ++q;
        nl--;
      }

      // now strip off any trailing periods (recording their presence)
      pabbrev = 0;

      while ((nl > 0) && (src[q + nl - 1] == '.'))
      {
        nl--;
        pabbrev++;
      }

      // if no characters are left it can't be capitalized
      if (nl <= 0)
      {
        pcaptype = CapType.NOCAP;
        return string.Empty;
      }

      // now determine the capitalization type of the first nl letters
      int ncap = 0;
      int nneutral = 0;

      for (int i = q; i < nl; ++i)
      {
        if (char.IsUpper(src, i))
          ncap++;
        if (textinfo.ToUpper(src[i]) == textinfo.ToLower(src[i]))
          nneutral++;
      }
      // remember to terminate the destination string
      bool firstcap = char.IsUpper(src, q);

      // now finally set the captype
      if (ncap == 0)
      {
        pcaptype = CapType.NOCAP;
      }
      else if ((ncap == 1) && firstcap)
      {
        pcaptype = CapType.INITCAP;
      }
      else if ((ncap == nl) || ((ncap + nneutral) == nl))
      {
        pcaptype = CapType.ALLCAP;
      }
      else if ((ncap > 1) && firstcap)
      {
        pcaptype = CapType.HUHINITCAP;
      }
      else
      {
        pcaptype = CapType.HUHCAP;
      }

      return nl < src.Length ? src.Substring(q, nl) : src;
    }

    // recursive search for right ss - sharp s permutations
    hentry spellsharps(string word,
                       int n_pos,
                       int n,
                       int repnum,
                       ref SPELL? info,
                       ref string root)
    {
      int pos = word.IndexOf("ss", n_pos);
      if (pos >= 0 && (n < MAXSHARPS))
      {
        hentry h = spellsharps(word.Substring(0, pos) + "\x00DF" + word.Substring(pos + 2), pos + 1, n + 1, repnum + 1, ref info, ref root);
        if (h != null)
          return h;
        return spellsharps(word, pos + 2, n + 1, repnum, ref info, ref root);
      }
      else if (repnum > 0)
      {
        return checkword(word, ref info, ref root);
      }
      return null;
    }

    bool is_keepcase(hentry rv)
    {
      return rv.astr != null && keepcase != 0 &&
             TESTAFF(rv.astr, keepcase);
    }

    /* insert a word to the beginning of the suggestion array */
    void insert_sug(List<string> slst, string word)
    {
      slst.Insert(0, word);
    }

    bool spell(string word, List<string> candidate_stack)
    {
      SPELL? info = null;
      string root = null;
      return spell(word, candidate_stack, ref info, ref root);
    }

    bool spell(string word, List<string> candidate_stack, ref SPELL? info)
    {
      string root = null;
      return spell(word, candidate_stack, ref info, ref root);
    }


    bool spell(string word, List<string> candidate_stack, ref SPELL? info, ref string root)
    {
      // something very broken if spell ends up calling itself with the same word
      if (candidate_stack.IndexOf(word) >= 0)
        return false;

      candidate_stack.Add(word);
      bool r = spell_internal(word, candidate_stack, ref info, ref root);
      candidate_stack.RemoveAt(candidate_stack.Count - 1);

      if (r && root != null)
      {
        // output conversion
        var rl = get_oconvtable();
        if (rl != null) root = rl.conv(root);
      }
      return r;
    }

    const int NBEGIN = 0, NNUM = 1, NSEP = 2;

    bool spell_internal(string word, List<string> candidate_stack, ref SPELL? info_, ref string root)
    {
      hentry rv = null;

      SPELL? info2 = 0;
      ref SPELL? info = ref info2;
      if (info_ != null)
      {
        info_ = 0;
        info = ref info_;
      }

      // Hunspell supports XML input of the simplified API (see manual)
      if (word == SPELL_XML)
        return true;
      if (word.Length >= MAXWORDLEN)
        return false;

      var ctx = Context;

      // input conversion
      var rl = get_iconvtable();
      if (rl != null) word = rl.conv(word);

      word = cleanword2(ctx, word, out var captype, out var abbv);
      var wl = word.Length;

#if FUZZING_BUILD_MODE_UNSAFE_FOR_PRODUCTION
    if (wl > 32768)
      return false;
#endif

      if (wl == 0)
        return true;
      if (root != null)
        root = string.Empty;

      // allow numbers with dots, dashes and commas (but forbid double separators:
      // "..", "--" etc.)
      int nstate = NBEGIN;
      int i;

      for (i = 0; (i < wl); i++) {
        if ((word[i] <= '9') && (word[i] >= '0')) {
          nstate = NNUM;
        } else if ((word[i] == ',') || (word[i] == '.') || (word[i] == '-')) {
          if ((nstate == NSEP) || (i == 0))
            break;
          nstate = NSEP;
        } else
          break;
      }
      if ((i == wl) && (nstate == NNUM))
        return true;

      switch (captype)
      {
        case CapType.HUHCAP:
        /* FALLTHROUGH */
        case CapType.HUHINITCAP:
          info |= SPELL.ORIGCAP;
          goto case CapType.NOCAP;
        /* FALLTHROUGH */
        case CapType.NOCAP:
          rv = checkword(word, ref info, ref root);
          if (abbv != 0 && rv == null)
          {
            rv = checkword(word + '.', ref info, ref root);
          }
          break;
        case CapType.ALLCAP:
          {
            info |= SPELL.ORIGCAP;
            rv = checkword(word, ref info, ref root);
            if (rv != null)
              break;
            if (abbv != 0)
            {
              rv = checkword(word + '.', ref info, ref root);
              if (rv != null)
                break;
            }
            // Spec. prefix handling for Catalan, French, Italian:
            // prefixes separated by apostrophe (SANT'ELIA -> Sant'+Elia).
            int apos = word.IndexOf('\'');
            if (apos >= 0)
            {
              word = textinfo.ToLower(word);
              //conversion may result in string with different len to pre-mkallsmall2
              //so re-scan
              if (apos >= 0 && apos < word.Length - 1)
              {
                string part1 = word.Substring(0, apos + 1), part2 = word.Substring(apos + 1);
                part2 = ctx.mkinitcap(part2, textinfo);
                word = part1 + part2;
                rv = checkword(word, ref info, ref root);
                if (rv != null)
                  break;
                word = ctx.mkinitcap(word, textinfo);
                rv = checkword(word, ref info, ref root);
                if (rv != null)
                  break;
              }
            }
            if (checksharps && word.IndexOf("SS") >= 0)
            {
              word = textinfo.ToLower(word);
              rv = spellsharps(word, 0, 0, 0, ref info, ref root);
              var u8buffer = word;
              if (rv == null)
              {
                word = ctx.mkinitcap(word, textinfo);
                rv = spellsharps(word, 0, 0, 0, ref info, ref root);
              }
              if (abbv != 0 && rv == null)
              {
                rv = spellsharps(u8buffer + '.', 0, 0, 0, ref info, ref root);
                if (rv == null)
                {
                  rv = spellsharps(word + '.', 0, 0, 0, ref info, ref root);
                }
              }
              if (rv != null)
                break;
            }
          }
          goto case CapType.INITCAP;
        /* FALLTHROUGH */
        case CapType.INITCAP:
          {
            // handle special capitalization of dotted I
            info |= SPELL.ORIGCAP;
            if (captype == CapType.ALLCAP)
            {
              word = ctx.mkinitcap(textinfo.ToLower(word), textinfo);
            }
            if (captype == CapType.INITCAP)
              info |= SPELL.INITCAP;
            rv = checkword(word, ref info, ref root);
            if (captype == CapType.INITCAP)
              info &= ~SPELL.INITCAP;
            // forbid bad capitalization
            // (for example, ijs -> Ijs instead of IJs in Dutch)
            // use explicit forms in dic: Ijs/F (F = FORBIDDENWORD flag)
            if ((info & SPELL.FORBIDDEN) != 0)
            {
              rv = null;
              break;
            }
            if (rv != null && is_keepcase(rv) && (captype == CapType.ALLCAP))
              rv = null;
            if (rv != null)
              break;

            word = textinfo.ToLower(word);
            var u8buffer = word;
            word = ctx.mkinitcap(word, textinfo);

            rv = checkword(u8buffer, ref info, ref root);
            if (abbv != 0 && rv == null)
            {
              u8buffer += '.';
              rv = checkword(u8buffer, ref info, ref root);
              if (rv == null)
              {
                u8buffer = word + '.';
                if (captype == CapType.INITCAP)
                  info |= SPELL.INITCAP;
                rv = checkword(u8buffer, ref info, ref root);
                if (captype == CapType.INITCAP)
                  info &= ~SPELL.INITCAP;
                if (rv != null && is_keepcase(rv) && (captype == CapType.ALLCAP))
                  rv = null;
                break;
              }
            }
            if (rv != null && is_keepcase(rv) &&
                ((captype == CapType.ALLCAP) ||
                 // if CHECKSHARPS: KEEPCASE words with \xDF  are allowed
                 // in INITCAP form, too.
                 !(checksharps &&
                   u8buffer.IndexOf('\x00DF') >= 0)))
              rv = null;
            break;
          }
      }

      if (rv != null)
      {
        if (warn != 0 && rv.astr != null &&
            TESTAFF(rv.astr, warn))
        {
          info |= SPELL.WARN;
          if (forbidwarn)
            return false;
          return true;
        }
        return true;
      }

      // recursive breaking at break points
      if (breaktable.Count > 0 && (info & SPELL.FORBIDDEN) == 0)
      {

        int nbr = 0;
        wl = word.Length;

        // calculate break points for recursion limit
        foreach (var j in breaktable)
        {
          int pos = 0;
          while ((pos = word.IndexOf(j, pos)) >= 0)
          {
            ++nbr;
            pos += j.Length;
          }
        }
        if (nbr >= 10)
          return false;

        // check boundary patterns (^begin and end$)
        foreach (var j in breaktable)
        {
          int plen = j.Length;
          if (plen == 1 || plen > wl)
            continue;

          if (j[0] == '^' &&
              string.CompareOrdinal(word, 0, j, 1, plen - 1) == 0 && spell(word.Substring(plen - 1), candidate_stack))
          {
            info |= SPELL.COMPOUND;
            return true;
          }

          if (j[plen - 1] == '$' &&
              string.CompareOrdinal(word, wl - plen + 1, j, 0, plen - 1) == 0 &&
              spell(word.Remove(wl - plen + 1), candidate_stack))
          {
            info |= SPELL.COMPOUND;
            return true;
          }
        }

        // other patterns
        foreach (var j in breaktable)
        {
          int plen = j.Length;
          int found = word.IndexOf(j);
          if ((found > 0) && (found < wl - plen))
          {
            int found2 = word.IndexOf(j, found + 1);
            // try to break at the second occurance
            // to recognize dictionary words with breaktable
            if (found2 > 0 && (found2 < wl - plen))
              found = found2;
            if (!spell(word.Substring(found + plen), candidate_stack))
              continue;
            // examine 2 sides of the break point
            if (spell(word.Remove(found), candidate_stack))
            {
              info |= SPELL.COMPOUND;
              return true;
            }

            // LANG.hu: spec. dash rule
            if (langnum == LANG.hu && j == "-" &&
                spell(word.Remove(found + 1), candidate_stack))
            {
              info |= SPELL.COMPOUND;
              return true;  // check the first part with dash
            }
            // end of LANG specific region
          }
        }

        // other patterns (break at first break point)
        foreach (var j in breaktable)
        {
          int plen = j.Length, found = word.IndexOf(j);
          if ((found > 0) && (found < wl - plen))
          {
            if (!spell(word.Substring(found + plen), candidate_stack))
              continue;
            // examine 2 sides of the break point
            if (spell(word.Remove(found), candidate_stack))
            {
              info |= SPELL.COMPOUND;
              return true;
            }

            // LANG.hu: spec. dash rule
            if (langnum == LANG.hu && j == "-" &&
                spell(word.Remove(found + 1), candidate_stack))
            {
              info |= SPELL.COMPOUND;
              return true;  // check the first part with dash
            }
            // end of LANG specific region
          }
        }
      }

      return false;
    }

    hentry checkword(string word, ref SPELL? info, ref string root)
    {
      var ctx = Context;

      // remove IGNORE characters from the string
      word = remove_ignored_chars(word, ctx);

      if (word.Length == 0)
        return null;

      // word reversing wrapper for complex prefixes
      if (complexprefixes)
      {
        word = ctx.reverseword(word);
      }

      int len = word.Length;

      // look word in hash table
      hentry he = lookup(word);

      // check forbidden and onlyincompound words
      if (he != null && he.astr != null &&
          TESTAFF(he.astr, forbiddenword))
      {
        if (info != null) info |= SPELL.FORBIDDEN;
        // LANG_hu section: set dash information for suggestions
        if (langnum == LANG.hu)
        {
          if (compoundflag != 0 &&
              TESTAFF(he.astr, compoundflag))
          {
            if (info != null) info |= SPELL.COMPOUND;
          }
        }
        return null;
      }

      // he = next not needaffix, onlyincompound homonym or onlyupcase word
      while (he != null && he.astr != null &&
              ((needaffix != 0 &&
                TESTAFF(he.astr, needaffix)) ||
              (onlyincompound != 0 &&
                TESTAFF(he.astr, onlyincompound)) ||
              (info != null && (info & SPELL.INITCAP) != 0 &&
                TESTAFF(he.astr, ONLYUPCASEFLAG))))
        he = he.next_homonym;

      // check with affixes
      if (he == null)
      {
        // try stripping off affixes
        he = affix_check(word, 0, len, 0);

        // check compound restriction and onlyupcase
        if (he != null && he.astr != null &&
            ((onlyincompound != 0 &&
              TESTAFF(he.astr, onlyincompound)) ||
             (info != null && (info & SPELL.INITCAP) != 0 &&
              TESTAFF(he.astr, ONLYUPCASEFLAG))))
        {
          he = null;
        }

        if (he != null)
        {
          if (he.astr != null &&
              TESTAFF(he.astr, forbiddenword))
          {
            if (info != null) info |= SPELL.FORBIDDEN;
            return null;
          }
          if (root != null)
          {
            root = he.word;
            if (complexprefixes)
            {
              root = ctx.reverseword(root);
            }
          }
          // try check compound word
        }
        else if (get_compound())
        {
          // first allow only 2 words in the compound
          SPELL? setinfo = SPELL.COMPOUND_2;
          if (info != null)
            setinfo |= info;
          he = compound_check(word, 0, 0, 0, null, false, false, setinfo);
          if (info != null)
            info = setinfo & ~SPELL.COMPOUND_2;
          // if not 2-word compoud word, try with 3 or more words
          // (only if original info didn't forbid it)
          if (he == null && info != null && (info & SPELL.COMPOUND_2) == 0)
          {
            info &= ~SPELL.COMPOUND_2;
            he = compound_check(word, 0, 0, 0, null, false, false, info);
            // accept the compound with 3 or more words only if it is
            // - not a dictionary word with a typo and
            // - not two words written separately,
            // - or if it's an arbitrary number accepted by compound rules (e.g. 999%)
            if (he != null && !char.IsDigit(word[0]))
            {
              bool onlycompoundsug = false;
              if (suggest(ctx, new List<string>(), word, ref onlycompoundsug, /*test_simplesug=*/true))
                he = null;
            }
          }

          // LANG_hu section: `moving rule' with last dash
          if (he == null && (langnum == LANG.hu) && (word[len - 1] == '-'))
          {
            he = compound_check(word.Substring(0, len - 1), -5, 0, 0, null, true, false, info);
          }
          // end of LANG specific region
          if (he != null)
          {
            if (root != null)
            {
              root = he.word;
              if (complexprefixes)
              {
                root = ctx.reverseword(root);
              }
            }
            if (info != null)
              info |= SPELL.COMPOUND;
          }
        }
      }

      return he;
    }

#if FUZZING_BUILD_MODE_UNSAFE_FOR_PRODUCTION
    const int MAX_CANDIDATE_STACK_DEPTH = 512;
#else
    const int MAX_CANDIDATE_STACK_DEPTH = 2048;
#endif

    List<string> suggest(string word, List<string> suggest_candidate_stack)
    {

      if (suggest_candidate_stack.Count > MAX_CANDIDATE_STACK_DEPTH || // apply a fairly arbitrary depth limit
                                                                       // something very broken if suggest ends up calling itself with the same word
          suggest_candidate_stack.IndexOf(word) >= 0)
      {
        return new List<string>();
      }

      var ctx = Context;

      bool capwords = false;
      var spell_candidate_stack = new List<string>();
      suggest_candidate_stack.Add(word);
      var slst = suggest_internal(ctx, word, spell_candidate_stack, suggest_candidate_stack,
                                               ref capwords, out var abbv, out var captype);
      suggest_candidate_stack.RemoveAt(suggest_candidate_stack.Count - 1);
      // word reversing wrapper for complex prefixes
      if (complexprefixes)
        for (int j = slst.Count - 1; j >=0; --j)
          slst[j] = ctx.reverseword(slst[j]);

      // capitalize
      if (capwords)
        for (int j = 0; j < slst.Count; ++j)
          slst[j] = ctx.mkinitcap(slst[j], textinfo);

      // expand suggestions with dot(s)
      if (abbv != 0 && sugswithdots && word.Length >= abbv)
      {
        for (int j = 0; j < slst.Count; ++j)
        {
          slst[j] = slst[j] + word.Substring(word.Length - abbv);
        }
      }

      // remove bad capitalized and forbidden forms
      if (keepcase != 0 || forbiddenword != 0)
      {
        switch (captype)
        {
          case CapType.INITCAP:
          case CapType.ALLCAP:
            {
              int l = 0;
              for (int j = 0; j < slst.Count; ++j)
              {
                if (slst[j].IndexOf(' ') < 0 && !spell(slst[j], spell_candidate_stack))
                {
                  var s = textinfo.ToLower(slst[j]);
                  if (spell(s, spell_candidate_stack))
                  {
                    slst[l] = s;
                    ++l;
                  }
                  else
                  {
                    s = ctx.mkinitcap(s, textinfo);
                    if (spell(s, spell_candidate_stack))
                    {
                      slst[l] = s;
                      ++l;
                    }
                  }
                }
                else
                {
                  slst[l] = slst[j];
                  ++l;
                }
              }
              slst.RemoveRange(l, slst.Count - l);
            }
            break;
        }
      }

      // remove duplications
      {
        int l = 0;
        for (int j = 0; j < slst.Count; ++j)
        {
          slst[l] = slst[j];
          for (int k = 0; k < l; ++k)
          {
            if (slst[k] == slst[j])
            {
              --l;
              break;
            }
          }
          ++l;
        }
        slst.RemoveRange(l, slst.Count - l);
      }

      // output conversion
      var rl = get_oconvtable();
      if (rl != null)
      {
        for (int i = 0; i < slst.Count; ++i)
        {
          slst[i] = rl.conv(slst[i]);
        }
      }
      return slst;
    }

    /// <summary>
    /// Searchs for suggestions.
    /// </summary>
    /// <param name="word">The [bad] word</param>
    /// <returns>The list of suggestions</returns>
    public List<string> Suggest(string word)
    {
      return suggest(word, new List<string>());
    }

    List<string> suggest_internal(Context ctx,
                                  string word,
                                  List<string> spell_candidate_stack,
                                  List<string> suggest_candidate_stack,
                                  ref bool capwords, out int abbv, out CapType captype)
    {
      captype = CapType.NOCAP;
      abbv = 0;
      capwords = false;

      var slst = new List<string>();

      bool onlycmpdsug = false;

      // process XML input of the simplified API (see manual)
      if (string.CompareOrdinal(word, 0, SPELL_XML, 0, SPELL_XML.Length - 2) == 0)
      {
        return spellml(word);
      }
      if (word.Length >= MAXWORDLEN)
        return slst;

      // input conversion
      var rl = get_iconvtable();
      if (rl != null) word = rl.conv(word);

      var scw = cleanword2(ctx, word, out captype, out abbv);
      var wl = scw.Length;

      if (wl == 0)
        return slst;

#if FUZZING_BUILD_MODE_UNSAFE_FOR_PRODUCTION
        if (wl > 32768)
          return slst;
#endif

      bool good = false;

      // initialize in every suggestion call
      var timer = Stopwatch.StartNew();

      // check capitalized form for FORCEUCASE
      if (captype == CapType.NOCAP && forceucase != 0)
      {
        SPELL? info = SPELL.ORIGCAP;
        string root = null;
        if (checkword(scw, ref info, ref root) != null)
        {
          slst.Add(ctx.mkinitcap(scw, textinfo));
          return slst;
        }
      }

      switch (captype)
      {
        case CapType.NOCAP:
          {
            good |= suggest(ctx, slst, scw, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            if (abbv != 0)
            {
              good |= suggest(ctx, slst, scw + ".", ref onlycmpdsug);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
            }
            break;
          }

        case CapType.INITCAP:
          {
            capwords = true;
            good |= suggest(ctx, slst, scw, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            good |= suggest(ctx, slst, textinfo.ToLower(scw), ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            break;
          }
        case CapType.HUHINITCAP:
          capwords = true;
          goto case CapType.HUHCAP;
          /* FALLTHROUGH */
        case CapType.HUHCAP:
          {
            good |= suggest(ctx, slst, scw, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            // something.The -> something. The
            int dot_pos = scw.IndexOf('.');
            if (dot_pos >= 0)
            {
              string postdot = scw.Substring(dot_pos + 1);
              if (get_captype(postdot, textinfo) == CapType.INITCAP)
              {
                insert_sug(slst, scw.Insert(dot_pos + 1, " "));
              }
            }

            string wspace;

            if (captype == CapType.HUHINITCAP)
            {
              // TheOpenOffice.org -> The OpenOffice.org
              good |= suggest(ctx, slst, ctx.mkinitsmall(scw, textinfo), ref onlycmpdsug);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
            }
            wspace = textinfo.ToLower(scw);
            if (spell(wspace, spell_candidate_stack))
              insert_sug(slst, wspace);
            int prevns = slst.Count;
            good |= suggest(ctx, slst, wspace, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            if (captype == CapType.HUHINITCAP)
            {
              wspace = ctx.mkinitcap(wspace, textinfo);
              if (spell(wspace, spell_candidate_stack))
                insert_sug(slst, wspace);
              good |= suggest(ctx, slst, wspace, ref onlycmpdsug);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
            }
            // aNew -> "a New" (instead of "a new")
            for (int j = prevns; j < slst.Count; ++j)
            {
              int space = slst[j].IndexOf(' ');
              if (space >= 0)
              {
                int slen = slst[j].Length - space - 1;
                // different case after space (need capitalisation)
                if ((slen < wl) && string.CompareOrdinal(scw, wl - slen, slst[j], space + 1, slen) != 0)
                {
                  var s = slst[j].Substring(0, space + 1) + ctx.mkinitcap(slst[j].Substring(space + 1), textinfo);
                  // set as first suggestion
                  slst.RemoveAt(j);
                  slst.Insert(0, s);
                }
              }
            }
            break;
          }

        case CapType.ALLCAP:
          {
            string wspace = textinfo.ToLower(scw);
            good |= suggest(ctx, slst, wspace, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            if (keepcase != 0 && spell(wspace, spell_candidate_stack))
              insert_sug(slst, wspace);
            wspace = ctx.mkinitcap(wspace, textinfo);
            good |= suggest(ctx, slst, wspace, ref onlycmpdsug);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            for (int j = 0; j < slst.Count; ++j)
            {
              slst[j] = textinfo.ToUpper(slst[j]);
              if (checksharps)
              {
                slst[j] = slst[j].Replace("\x00DF", "SS");
              }
            }
            break;
          }
      }

      // LANG_hu section: replace '-' with ' ' in Hungarian
      if (langnum == LANG.hu)
      {
        for (int j = 0; j < slst.Count; ++j)
        {
          int pos = slst[j].IndexOf('-');
          if (pos >= 0)
          {
            SPELL? info = 0;
            string root = null;
            var w = slst[j].Remove(pos, 1);
            spell(w, spell_candidate_stack, ref info, ref root);
            if ((info & SPELL.COMPOUND) != 0 && (info & SPELL.FORBIDDEN) != 0)
            {
              slst[j] = w.Insert(pos, " ");
            }
          }
        }
      }

      // END OF LANG_hu section
      // try ngram approach since found nothing good suggestion
      if (!good && (slst.Count == 0 || onlycmpdsug) && (maxngramsugs != 0))
      {

        switch (captype)
        {
          case CapType.NOCAP:
            {
              ngsuggest(ctx, slst, scw, CapType.NOCAP);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
              break;
            }
          case CapType.HUHINITCAP:
            capwords = true;
            goto case CapType.HUHCAP;
            /* FALLTHROUGH */
          case CapType.HUHCAP:
            {
              ngsuggest(ctx, slst, textinfo.ToLower(scw), CapType.HUHCAP);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
              break;
            }
          case CapType.INITCAP:
            {
              capwords = true;
              ngsuggest(ctx, slst, textinfo.ToLower(scw), CapType.INITCAP);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
              break;
            }
          case CapType.ALLCAP:
            {
              int oldns = slst.Count;
              ngsuggest(ctx, slst, textinfo.ToLower(scw), CapType.ALLCAP);
              if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
                return slst;
              for (int j = oldns; j < slst.Count; ++j)
              {
                slst[j] = textinfo.ToUpper(slst[j]);
              }
              break;
            }
        }

      }

      // try dash suggestion (Afo-American -> Afro-American)
      // Note: LibreOffice was modified to treat dashes as word
      // characters to check "scot-free" etc. word forms, but
      // we need to handle suggestions for "Afo-American", etc.,
      // while "Afro-American" is missing from the dictionary.
      // TODO avoid possible overgeneration
      int dash_pos = scw.IndexOf('-');
      if (dash_pos >= 0)
      {
        bool nodashsug = true;
        for (int j = 0; j < slst.Count && nodashsug; ++j)
        {
          if (slst[j].IndexOf('-') >= 0)
            nodashsug = false;
        }

        int prev_pos = 0;
        bool last = false;

        while (!good && nodashsug && !last)
        {
          if (dash_pos == scw.Length)
            last = true;
          string chunk = scw.Substring(prev_pos, dash_pos - prev_pos);
          if (chunk != word && !spell(chunk, spell_candidate_stack))
          {
            List<string> nlst = suggest(chunk, suggest_candidate_stack);
            if (timer.ElapsedMilliseconds > TIMELIMIT_GLOBAL)
              return slst;
            for (var j = nlst.Count - 1; j >= 0; --j)
            {
              string wspace = scw.Substring(0, prev_pos);
              wspace += nlst[j];
              if (!last)
              {
                wspace += "-";
                wspace += scw.Substring(dash_pos + 1);
              }
              SPELL? info = 0;
              if (forbiddenword != 0)
              {
                string root = null;
                checkword(wspace, ref info, ref root);
              }
              if ((info & SPELL.FORBIDDEN) == 0)
                insert_sug(slst, wspace);
            }
            nodashsug = false;
          }
          if (!last)
          {
            prev_pos = dash_pos + 1;
            dash_pos = scw.IndexOf('-', prev_pos);
          }
          if (dash_pos < 0)
            dash_pos = scw.Length;
        }
      }
      return slst;
    }

    /// <summary>
    /// Gets the encoding of the dictionary.
    /// </summary>
    public Encoding DicEncoding => encoding;

    /// <summary>
    /// Gets stems from a morphological analysis.
    /// </summary>
    /// <param name="desc">Morphological analysis data</param>
    /// <returns>A list of stems</returns>
    public List<string> Stem(List<string> desc)
    {
      if (desc.Count == 0)
        return new List<string>();

      var ctx = Context;
      var result = ctx.PopStringBuilder();
      var result2 = ctx.PopStringBuilder();
      foreach (var i in desc)
      {
        result.Clear();

        // add compound word parts (except the last one)
        int s = 0, part = i.IndexOf(MORPH.PART);
        if (part >= 0)
        {
          int nextpart = i.IndexOf(MORPH.PART, part + 1);
          while (nextpart >= 0)
          {
            copy_field(result, i, part, MORPH.PART);
            part = nextpart;
            nextpart = i.IndexOf(MORPH.PART, part + 1);
          }
          s = part;
        }

        var pl = i.Substring(s).Replace(" | ", MSEP.ALT_as_string).Split(MSEP.ALT);
        for (int ki = 0; ki < pl.Length; ++ki)
        {
          var k = pl[ki];
          // add derivational suffixes
          if (k.Contains(MORPH.DERI_SFX))
          {
            // remove inflectional suffixes
            int @is = k.IndexOf(MORPH.INFL_SFX);
            if (@is >= 0)
              k = k.Remove(@is);
            var singlepl = new List<string>();
            singlepl.Add(k);
            string sg = suggest_gen(ctx, singlepl, k);
            if (sg.Length > 0)
            {
              var gen = sg.Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries);
              for (int j = 0; j < gen.Length; ++j)
              {
                result2.Append(MSEP.REC);
                result2.Append(result);
                result2.Append(gen[j]);
              }
            }
          }
          else
          {
            result2.Append(MSEP.REC);
            result2.Append(result);
            copy_field(result2, k, 0, MORPH.SURF_PFX);
            copy_field(result2, k, 0, MORPH.STEM);
          }
        }
      }

      ctx.PushStringBuilder(result);
      return uniqlist(new List<string>(ctx.ToStringPushStringBuilder(result2).Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries)));
    }

    /// <summary>
    /// Stemmer function
    /// </summary>
    /// <param name="word">The input word</param>
    /// <returns>A list of stems</returns>
    public List<string> Stem(string word)
    {
      return Stem(Analyze(word));
    }

    /// <summary>
    /// Gets extra word characters defined in affix file for tokenization.
    /// </summary>
    public char[] Wordchars => wordchars;

    /// <summary>
    /// Gets affix and dictionary file version string.
    /// </summary>
    public string Version => version;

    void cat_result(StringBuilder result, string st)
    {
      if (!string.IsNullOrEmpty(st))
      {
        if (result.Length > 0) result.Append('\n');
        result.Append(st);
      }
    }

    /// <summary>
    /// Morphological analysis of the word
    /// </summary>
    /// <param name="word">The word to analyze</param>
    /// <returns>Morphological data</returns>
    public List<string> Analyze(string word)
    {
      List<string> slst = analyze_internal(word);
      // output conversion
      var rl = get_oconvtable();
      if (rl != null)
      {
        for (int i = 0; i < slst.Count; ++i)
        {
          slst[i] = rl.conv(slst[i]);
        }
      }
      return slst;
    }

    List<string> analyze_internal(string word)
    {
      var slst = new List<string>();
      if (word.Length >= MAXWORDLEN)
        return slst;

      var ctx = Context;

      // input conversion
      var rl = get_iconvtable();
      if (rl != null) word = rl.conv(word);
      var scw = cleanword2(ctx, word, out var captype, out var abbv);
      var wl = scw.Length;

      if (wl == 0)
      {
        if (abbv != 0)
        {
          scw = new string('.', abbv);
          abbv = 0;
        }
        else
          return slst;
      }

      var candidate_stack = new List<string>();
      var result = ctx.PopStringBuilder();
      try
      {
        int n = 0;
        // test numbers
        // LANG_hu section: set dash information for suggestions
        if (langnum == LANG.hu)
        {
          int n2 = 0;
          int n3 = 0;

          while ((n < wl) && (((scw[n] <= '9') && (scw[n] >= '0')) ||
                              (((scw[n] == '.') || (scw[n] == ',')) && (n > 0))))
          {
            n++;
            if ((scw[n] == '.') || (scw[n] == ','))
            {
              if (((n2 == 0) && (n > 3)) ||
                  ((n2 > 0) && ((scw[n - 1] == '.') || (scw[n - 1] == ','))))
                break;
              n2++;
              n3 = n;
            }
          }

          if ((n == wl) && (n3 > 0) && (n - n3 > 3))
            return slst;
          if ((n == wl) || ((n > 0) && ((scw[n] == '%') || (scw[n] == '\xB0'))))
          {
            SPELL? info = null;
            string root = null;
            if (checkword(scw.Substring(n), ref info, ref root) != null)
            {
              result.Append(scw);
              result.Length = n - 1;
              if (n == wl)
                cat_result(result, suggest_morph(ctx, scw.Substring(n - 1)));
              else
              {
                cat_result(result, suggest_morph(ctx, scw.Substring(n - 1, 1)));
                result.Append('+');  // XXX SPEC. MORPHCODE
                cat_result(result, suggest_morph(ctx, scw.Substring(n)));
              }
              return new List<string>(result.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
            }
          }
        }
        // END OF LANG_hu section

        switch (captype)
        {
          case CapType.HUHCAP:
          case CapType.HUHINITCAP:
          case CapType.NOCAP:
            {
              cat_result(result, suggest_morph(ctx, scw));
              if (abbv != 0)
              {
                cat_result(result, suggest_morph(ctx, scw + '.'));
              }
              break;
            }
          case CapType.INITCAP:
            {
              var u8buffer = textinfo.ToLower(scw);
              scw = ctx.mkinitcap(scw, textinfo);
              cat_result(result, suggest_morph(ctx, u8buffer));
              cat_result(result, suggest_morph(ctx, scw));
              if (abbv != 0)
              {
                cat_result(result, suggest_morph(ctx, u8buffer + '.'));
                cat_result(result, suggest_morph(ctx, scw + '.'));
              }
              break;
            }
          case CapType.ALLCAP:
            {
              cat_result(result, suggest_morph(ctx, scw));
              if (abbv != 0)
              {
                cat_result(result, suggest_morph(ctx, scw + '.'));
              }
              var u8buffer = textinfo.ToLower(scw);
              scw = ctx.mkinitcap(u8buffer, textinfo);

              cat_result(result, suggest_morph(ctx, u8buffer));
              cat_result(result, suggest_morph(ctx, scw));
              if (abbv != 0)
              {
                cat_result(result, suggest_morph(ctx, u8buffer + '.'));
                cat_result(result, suggest_morph(ctx, scw + '.'));
              }
              break;
            }
        }

        if (result.Length > 0)
        {
          // word reversing wrapper for complex prefixes
          if (complexprefixes)
            for (int i = 0, j = result.Length - 1; i < j; ++i, --j)
            {
              var t = result[i]; result[i] = result[j]; result[j] = t;
            }

          return new List<string>(result.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
        }

        // compound word with dash (HU) I18n
        // LANG_hu section: set dash information for suggestions

        int dash_pos = langnum == LANG.hu ? scw.IndexOf('-') : -1;
        if (dash_pos > 0)
        {
          bool nresult = false;

          string part1 = scw.Substring(0, dash_pos), part2 = scw.Substring(dash_pos + 1);

          // examine 2 sides of the dash
          if (part2.Length == 0)
          {  // base word ending with dash
            if (spell(part1, candidate_stack))
            {
              string p = suggest_morph(ctx, part1);
              if (p.Length > 0)
              {
                return new List<string>(p.Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
              }
            }
          }
          else if (part2.Length == 1 && part2[0] == 'e')
          {  // XXX (HU) -e hat.
            if (spell(part1, candidate_stack) && (spell("-e", candidate_stack)))
            {
              string st = suggest_morph(ctx, part1);
              if (st.Length > 0)
              {
                result.Append(st);
              }
              result.Append('+');  // XXX spec. separator in MORPHCODE
              st = suggest_morph(ctx, "-e");
              if (st.Length > 0)
              {
                result.Append(st);
              }
              return new List<string>(result.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
            }
          }
          else
          {
            // first word ending with dash: word- XXX ???
            nresult = spell(part1 + " ", candidate_stack);
            if (nresult && spell(part2, candidate_stack) &&
                ((part2.Length > 1) || ((part2[0] > '0') && (part2[0] < '9'))))
            {
              string st = suggest_morph(ctx, part1);
              if (st.Length > 0)
              {
                result.Append(st);
                result.Append('+');  // XXX spec. separator in MORPHCODE
              }
              st = suggest_morph(ctx, part2);
              if (st.Length > 0)
              {
                result.Append(st);
              }
              return new List<string>(result.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
            }
          }
          // affixed number in correct word
          if (nresult && (dash_pos > 0) &&
              (((scw[dash_pos - 1] <= '9') && (scw[dash_pos - 1] >= '0')) ||
               (scw[dash_pos - 1] == '.')))
          {
            n = 1;
            if (scw[dash_pos - n] == '.')
              n++;
            // search first not a number character to left from dash
            while ((dash_pos >= n) && ((scw[dash_pos - n] == '0') || (n < 3)) &&
                   (n < 6))
            {
              n++;
            }
            if (dash_pos < n)
              n--;
            // numbers: valami1000000-hoz
            // examine 100000-hoz, 10000-hoz 1000-hoz, 10-hoz,
            // 56-hoz, 6-hoz
            for (; n >= 1; n--)
            {
              if (scw[dash_pos - n] < '0' || scw[dash_pos - n] > '9')
              {
                continue;
              }
              string chunk = scw.Substring(dash_pos - n);
              SPELL? info = null;
              string root = null;
              if (checkword(chunk, ref info, ref root) != null)
              {
                result.Append(chunk);
                string st = suggest_morph(ctx, chunk);
                if (st.Length > 0)
                {
                  result.Append(st);
                }
                return new List<string>(result.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));
              }
            }
          }
        }
        return slst;
      }
      finally
      {
        ctx.PushStringBuilder(result);
      }
    }

    /// <summary>
    /// Generation by morphological description(s)
    /// </summary>
    /// <param name="word">The input word</param>
    /// <param name="pl">Morphological description; it may depend on dictionary</param>
    /// <returns>A list of generated words</returns>
    public List<string> Generate(string word, List<string> pl)
    {
      var slst = new List<string>();
      if (pl == null || pl.Count == 0)
        return slst;
      List<string> pl2 = Analyze(word);
      cleanword(word, out var captype, out var abbv);

      var ctx = Context;
      var result = ctx.PopStringBuilder();

      for (int i = 0; i < pl.Count; ++i)
      {
        cat_result(result, suggest_gen(ctx, pl2, pl[i]));
      }

      if (result.Length > 0)
      {
        var result_ = result.ToString();

        // allcap
        if (captype == CapType.ALLCAP)
          result_ = textinfo.ToUpper(result_);

        // line split
        slst = new List<string>(result_.Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries));

        // capitalize
        if (captype == CapType.INITCAP || captype == CapType.HUHINITCAP)
        {
          for (int i = 0; i < slst.Count; ++i)
          {
            slst[i] = ctx.mkinitcap(slst[i], textinfo);
          }
        }

        // temporary filtering of prefix related errors (eg.
        // generate("undrinkable", "eats") --> "undrinkables" and "*undrinks")
        int j = 0;
        for (int i = 0; i < slst.Count; ++i)
        {
          if (spell(slst[i], new List<string>()))
          {
            if (j < i) slst[j] = slst[i];
            ++j;
          }
        }
        if (j < slst.Count) slst.RemoveRange(j, slst.Count - j);
      }
      ctx.PushStringBuilder(result);
      return slst;
    }

    /// <summary>
    /// Morphological generation by example(s)
    /// </summary>
    /// <param name="word">The input word</param>
    /// <param name="pattern">The example</param>
    /// <returns>A list of generated words</returns>
    public List<string> Generate(string word, string pattern)
    {
      List<string> pl = Analyze(pattern);
      List<string> slst = Generate(word, pl);
      uniqlist(slst);
      return slst;
    }

    // minimal XML parser functions
    string get_xml_par(string par, int pos)
    {
      if (pos < 0)
        return string.Empty;
      char end = par[pos];
      if (end == '>')
        end = '<';
      else if (end != '\'' && end != '"')
        return string.Empty;  // bad XML
      var dest = new StringBuilder();
      for (pos++; pos < par.Length && par[pos] != end; ++pos)
      {
        dest.Append(par[pos]);
      }
      dest.Replace("&lt;", "<").Replace("&amp;", "&");
      return dest.ToString();
    }

    /// <summary>
    /// Gets language number of the dictionary.
    /// </summary>
    public LANG LangNum => langnum;

    /// <summary>
    /// Converts the word according to the ICONV table specified in the affix file.
    /// </summary>
    /// <param name="word">The input word</param>
    /// <returns>The output word</returns>
    public string InputConv(string word)
    {
      var rl = get_iconvtable();
      return rl != null ? rl.conv(word) : word;
    }

    // return the beginning of the element (attr == null) or the attribute
    int get_xml_pos(string s, int pos, string attr)
    {
      if (pos < 0)
        return -1;

      int endpos = s.IndexOf('>', pos);
      if (attr == null)
        return endpos;
      while (true)
      {
        pos = s.IndexOf(attr, pos);
        if (pos < 0 || pos >= endpos)
          return -1;
        if (s[pos - 1] == ' ' || s[pos - 1] == '\n')
          break;
        pos += attr.Length;
      }
      return pos + attr.Length;
    }

    bool check_xml_par(string q, int pos,
                                    string attr,
                                    string value)
    {
      string cw = get_xml_par(q, get_xml_pos(q, pos, attr));
      return cw == value;
    }

    List<string> get_xml_list(string list, int pos, string tag)
    {
      var slst = new List<string>();
      if (pos < 0)
        return slst;
      while (true)
      {
        pos = list.IndexOf(tag, pos);
        if (pos < 0)
          break;
        string cw = get_xml_par(list, pos + tag.Length - 1);
        if (cw.Length == 0)
        {
          break;
        }
        slst.Add(cw);
        ++pos;
      }
      return slst;
    }

    List<string> spellml(string in_word)
    {
      var slst = new List<string>();

      int qpos = in_word.IndexOf("<query");
      if (qpos < 0)
        return slst;  // bad XML input

      int q2pos = in_word.IndexOf('>', qpos);
      if (q2pos < 0)
        return slst;  // bad XML input

      q2pos = in_word.IndexOf("<word", q2pos);
      if (q2pos < 0)
        return slst;  // bad XML input

      if (check_xml_par(in_word, qpos, "type=", "analyze"))
      {
        string cw = get_xml_par(in_word, in_word.IndexOf('>', q2pos));
        if (cw.Length > 0)
          slst = Analyze(cw);
        if (slst.Count == 0)
          return slst;
        // convert the result to <code><a>ana1</a><a>ana2</a></code> format
        var ctx = Context;
        var r = ctx.PopStringBuilder();
        r.Append("<code>");
        foreach (var entry in slst)
        {
          r.Append("<a>");

          int i = r.Length;
          r.Append(entry);
          r.Replace("\t", " ", i, r.Length);
          r.Replace("&", "&amp;", i, r.Length);
          r.Replace("<", "&lt;", i, r.Length);

          r.Append("</a>");
        }
        r.Append("</code>");
        slst.Clear();
        slst.Add(ctx.ToStringPushStringBuilder(r));
        return slst;
      }
      else if (check_xml_par(in_word, qpos, "type=", "stem"))
      {
        string cw = get_xml_par(in_word, in_word.IndexOf('>', q2pos));
        if (cw.Length > 0)
          return Stem(cw);
      }
      else if (check_xml_par(in_word, qpos, "type=", "generate"))
      {
        string cw = get_xml_par(in_word, in_word.IndexOf('>', q2pos));
        if (cw.Length == 0)
          return slst;
        int q3pos = in_word.IndexOf("<word", q2pos + 1);
        if (q3pos >= 0)
        {
          string cw2 = get_xml_par(in_word, in_word.IndexOf('>', q3pos));
          if (cw2.Length > 0)
          {
            return Generate(cw, cw2);
          }
        }
        else
        {
          q2pos = in_word.IndexOf("<code", q2pos + 1);
          if (q2pos >= 0)
          {
            List<string> slst2 = get_xml_list(in_word, in_word.IndexOf('>', q2pos), "<a>");
            if (slst2.Count > 0)
            {
              slst = Generate(cw, slst2);
              uniqlist(slst);
              return slst;
            }
          }
        }
      }
      else if (check_xml_par(in_word, qpos, "type=", "add"))
      {
        string cw = get_xml_par(in_word, in_word.IndexOf('>', q2pos));
        if (cw.Length == 0)
          return slst;
        int q3pos = in_word.IndexOf("<word", q2pos + 1);
        if (q3pos >= 0)
        {
          string cw2 = get_xml_par(in_word, in_word.IndexOf('>', q3pos));
          if (cw2.Length > 0)
          {
            AddWithAffix(cw, cw2);
          }
          else
          {
            Add(cw);
          }
        }
        else
        {
          Add(cw);
        }
      }
      return slst;
    }

    /// <summary>
    /// Suggests words from suffix rules.
    /// </summary>
    /// <param name="root_word">The word</param>
    /// <returns>A list of suggestions</returns>
    public List<string> SuffixSuggest(string root_word)
    {
      var slst = new List<string>();

      var word = remove_ignored_chars(root_word, Context);

      if (word.Length == 0)
        return slst;

      var he = lookup(word);
      if (he != null)
      {
        get_suffix_words(he.astr, root_word, slst);
      }
      return slst;
    }

    /// <summary>
    /// Checks the spelling of the word.
    /// </summary>
    /// <param name="word">The word to check</param>
    /// <param name="info">Optional information bit array, fields:
    ///   SPELL.COMPOUND  = a compound word
    ///   SPELL.FORBIDDEN = an explicit forbidden word
    /// </param>
    /// <param name="root">Optional root (stem), when input is a word with affix(es)</param>
    /// <returns><code>true</code> if the word is correct, <code>false</code> if the word is not found in dictionary.</returns>
    public bool Spell(string word, out SPELL? info, out string root)
    {
      info = 0;
      root = string.Empty;
      return spell(word, new List<string>(), ref info, ref root);
    }

    /// <summary>
    /// Checks the spelling of the word.
    /// </summary>
    /// <param name="word">The word to check</param>
    /// <param name="info">Optional information bit array, fields:
    ///   SPELL.COMPOUND  = a compound word
    ///   SPELL.FORBIDDEN = an explicit forbidden word
    /// </param>
    /// <returns><code>true</code> if the word is correct, <code>false</code> if the word is not found in dictionary.</returns>
    public bool Spell(string word, out SPELL? info)
    {
      info = 0;
      return spell(word, new List<string>(), ref info);
    }

    /// <summary>
    /// Checks the spelling of the word.
    /// </summary>
    /// <param name="word">The word to check</param>
    /// <returns><code>true</code> if the word is correct, <code>false</code> if the word is not found in dictionary.</returns>
    public bool Spell(string word)
    {
      return spell(word, new List<string>());
    }

    /// <summary>
    /// 
    /// </summary>
    public static bool StrictFormat { get; set; } = true;

    /// <summary>
    /// Sets custom warning handler.
    /// </summary>
    /// <param name="handler">New handler</param>
    public static void SetWarningHandler(IHunspellWarningHandler handler)
    {
      warningHandler = handler;
    }

    /// <summary>
    /// Releases the resources used by this <see cref="Hunspell" /> instance.
    /// </summary>
    public void Dispose()
    {
      context.Dispose();
    }
  }
}
