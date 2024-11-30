using System;
using System.Collections.Generic;

namespace HunspellSharp
{
  using static Utils;

  partial class Hunspell
  {
    // approx. number  of user defined words
    const int USERWORD = 1000;

    // morphological description of a dictionary item can contain
    // arbitrary number "ph:" (MORPH_PHON) fields to store typical
    // phonetic or other misspellings of that word.
    // ratio of lines/lines with "ph:" in the dic file: 1/MORPH_PHON_RATIO
    const int MORPH_PHON_RATIO = 500;

    hentry[] tableptr;
    int wordcount;

    void LoadDic(FileMgr dic)
    {
      if (!load_tables(dic))
        tableptr = new hentry[1];
    }

    // lookup a root word in the hashtable
    internal hentry lookup(string word, int start = 0, int length = -1)
    {
      if (length < 0) length = word.Length - start;
      var hv = hash(word, start, length);
      var dp = tableptr[hv % (uint)tableptr.Length];
      if (dp == null)
        return null;
      for (; dp != null; dp = dp.next)
      {
        if (hv == dp.hash && length == dp.word.Length && string.CompareOrdinal(word, start, dp.word, 0, length) == 0)
          return dp;
      }
      return null;
    }

    internal hentry lookup(char[] word, int start = 0, int length = -1)
    {
      if (length < 0) length = word.Length - start;
      var hv = hash(word, start, length);
      var dp = tableptr[hv % (uint)tableptr.Length];
      if (dp == null)
        return null;
      for (; dp != null; dp = dp.next)
      {
        if (hv != dp.hash || length != dp.word.Length) continue;
        for (int i = 0; i < length; ++i)
          if (word[start + i] != dp.word[i]) goto next;
        return dp;
        next:;
      }
      return null;
    }

    // add a word to the hash table (private)
    bool add_word(string in_word,
                  ushort[] aff,
                  string desc,
                  bool onlyupcase,
                  CapType captype,
                  FileMgr file = null)
    {
      if (aff != null && aff.Length > short.MaxValue)
      {
        HUNSPELL_WARNING(true, Properties.Resources.TooLargeAffix, aff.Length, file);
        return false;
      }

      string word = remove_ignored_chars(in_word);

      if (complexprefixes)
      {
        word = helper.reverseword(word);

        if (desc != null && !is_aliasm())
        {
          desc = helper.reverseword(desc);
        }
      }

      if (word.Length > short.MaxValue)
      {
        HUNSPELL_WARNING(true, "Word length {0} is over max limit", word.Length, file);
        return false;
      }

      bool upcasehomonym = false;

      var hv = hash(word);

      // variable-length hash record with word and optional fields
      var hp = new hentry(word, aff, (captype == CapType.INITCAP) ? H_OPT.INITCAP : 0, hv);

      // store the description string or its pointer
      if (desc != null)
      {
        if (is_aliasm())
        {
          hp.data = get_aliasm(atoi(desc));
        }
        else
        {
          hp.data = desc;
        }
        if (hp.Contains(MORPH.PHON))
        {
          hp.var |= H_OPT.PHON;
          // store ph: fields (pronounciation, misspellings, old orthography etc.)
          // of a morphological description in reptable to use in REP replacements.
          int predicted = tableptr.Length / MORPH_PHON_RATIO;
          if (reptable.Capacity < predicted)
            reptable.Capacity = predicted;
          string fields = hp.data;
          foreach (var piece in fields.Split(' ', '\t'))
          {
            if (piece.StartsWith(MORPH.PHON)) {
              string ph = piece.Substring(MORPH.PHON.Length);
              if (ph.Length != 0)
              {
                int strippatt;
                string wordpart;
                // dictionary based REP replacement, separated by "->"
                // for example "pretty ph:prity ph:priti->pretti" to handle
                // both prity -> pretty and pritier -> prettiest suggestions.
                if (((strippatt = ph.IndexOf("->")) > 0) && (strippatt < ph.Length - 2)) {
                  wordpart = ph.Substring(strippatt + 2);
                  ph = ph.Remove(strippatt);
                }
                else
                  wordpart = in_word;
                // when the ph: field ends with the character *,
                // strip last character of the pattern and the replacement
                // to match in REP suggestions also at character changes,
                // for example, "pretty ph:prity*" results "prit->prett"
                // REP replacement instead of "prity->pretty", to get
                // prity->pretty and pritiest.prettiest suggestions.
                if (ph[ph.Length - 1] == '*')
                {
                  strippatt = 1;
                  int stripword = 0;
                  ++strippatt;
                  ++stripword;
                  if (ph.Length > strippatt && wordpart.Length > stripword)
                  {
                    ph = ph.Remove(ph.Length - strippatt, strippatt);
                    wordpart = wordpart.Remove(wordpart.Length - stripword, stripword);
                  }
                }
                // capitalize lowercase pattern for capitalized words to support
                // good suggestions also for capitalized misspellings, eg.
                // Wednesday ph:wendsay
                // results wendsay -> Wednesday and Wendsay -> Wednesday, too.
                if (captype == CapType.INITCAP)
                {
                  if (get_captype(ph, textinfo) == CapType.NOCAP)
                  {
                    var ph_capitalized = helper.mkinitcap(ph, textinfo);

                    if (ph_capitalized.Length > 0)
                    {
                      // add also lowercase word in the case of German or
                      // Hungarian to support lowercase suggestions lowercased by
                      // compound word generation or derivational suffixes
                      // (for example by adjectival suffix "-i" of geographical
                      // names in Hungarian:
                      // Massachusetts ph:messzecsuzec
                      // messzecsuzeci -> massachusettsi (adjective)
                      // For lowercasing by conditional PFX rules, see
                      // tests/germancompounding test example or the
                      // Hungarian dictionary.)
                      if (langnum == LANG.de || langnum == LANG.hu)
                      {
                        reptable.Add(new replentry(ph, textinfo.ToLower(wordpart)));
                      }
                      reptable.Add(new replentry(ph_capitalized, wordpart));
                    }
                  }
                }
                reptable.Add(new replentry(ph, wordpart));
              }
            }
          }
        }
      }

      var i = hv % (uint)tableptr.Length;
      var dp = tableptr[i];
      if (dp == null)
      {
        tableptr[i] = hp;
        return true;
      }
      while (dp.next != null)
      {
        if (dp.next_homonym == null && hv == dp.hash && word == dp.word)
        {
          // remove hidden onlyupcase homonym
          if (!onlyupcase)
          {
            if (TESTAFF(dp.astr, ONLYUPCASEFLAG))
            {
              dp.astr = hp.astr;
              return true;
            }
            else
            {
              dp.next_homonym = hp;
            }
          }
          else
          {
            upcasehomonym = true;
          }
        }
        dp = dp.next;
      }
      if (hv == dp.hash && word == dp.word)
      {
        // remove hidden onlyupcase homonym
        if (!onlyupcase)
        {
          if (TESTAFF(dp.astr, ONLYUPCASEFLAG))
          {
            dp.astr = hp.astr;
            return true;
          }
          else
          {
            dp.next_homonym = hp;
          }
        }
        else
        {
          upcasehomonym = true;
        }
      }
      if (!upcasehomonym)
      {
        dp.next = hp;
      }
      return true;
    }


    bool add_hidden_capitalized_word(string word,
                                     ushort[] flags,
                                     string dp,
                                     CapType captype,
                                     FileMgr file = null)
    {
      var flagslen = flags != null ? flags.Length : 0;

      // add inner capitalized forms to handle the following allcap forms:
      // Mixed caps: OpenOffice.org -> OPENOFFICE.ORG
      // Allcaps with suffixes: CIA's -> CIA'S
      if (((captype == CapType.HUHCAP) || (captype == CapType.HUHINITCAP) ||
           ((captype == CapType.ALLCAP) && (flagslen != 0))) &&
          !((flagslen != 0) && TESTAFF(flags, forbiddenword)))
      {
        int i = flagslen > 0 ? Array.BinarySearch(flags, (ushort)ONLYUPCASEFLAG) : ~0;
        if (i < 0)
        {
          i = ~i;
          var flags2 = new ushort[flagslen + 1];
          if (i > 0) Array.Copy(flags, 0, flags2, 0, i);
          flags2[i] = ONLYUPCASEFLAG;
          if (i < flagslen) Array.Copy(flags, i, flags2, i + 1, flagslen - i);
          flags = flags2;
        }

        var new_word = helper.mkinitcap(textinfo.ToLower(word), textinfo);
        return add_word(new_word, flags, dp, true, CapType.INITCAP, file);
      }
      return true;
    }

    /// <summary>
    /// Removes word from the run-time dictionary.
    /// </summary>
    /// <param name="word">The word to remove from the dictionary</param>
    public void Remove(string word)
    {
      hentry dp = lookup(word);
      while (dp != null)
      {
        if (!TESTAFF(dp.astr, forbiddenword))
        {
          if (dp.astr == null)
            dp.astr = new ushort[1];
          else
            Array.Resize(ref dp.astr, dp.astr.Length + 1);

          dp.astr[dp.astr.Length - 1] = forbiddenword;
          Array.Sort(dp.astr);
        }
        dp = dp.next_homonym;
      }
    }

    /* remove forbidden flag to add a personal word to the hash */
    void remove_forbidden_flag(string word)
    {
      hentry dp = lookup(word);
      if (dp == null)
        return;
      while (dp != null)
      {
        if (TESTAFF(dp.astr, forbiddenword))
          dp.astr = null;  // XXX forbidden words of personal dic.
        dp = dp.next_homonym;
      }
    }

    /// <summary>
    /// Adds word to the run-time dictionary.
    /// </summary>
    /// <param name="word">The word to be added to the dictionary.</param>
    public void Add(string word)
    {
      if (word == null) throw new ArgumentNullException(nameof(word));
      remove_forbidden_flag(word);
      var captype = get_captype(word, textinfo);
      add_word(word, null, null, false, captype);
      add_hidden_capitalized_word(word, null, null, captype);
    }

    /// <summary>
    /// Adds word to the run-time dictionary.
    /// </summary>
    /// <param name="word">The word to be added to the dictionary.</param>
    /// <param name="flags">Affix flags</param>
    /// <param name="desc">Morphological data</param>
    public void AddWithFlags(string word, byte[] flags, string desc)
    {
      remove_forbidden_flag(word);
      ushort[] df = decode_flags(new Bytes(flags), null);
      var captype = get_captype(word, textinfo);
      add_word(word, df, desc, false, captype);
      add_hidden_capitalized_word(word, df, desc, captype);
    }

    /// <summary>
    /// Adds word to the run-time dictionary with affix flags of the example (a dictionary word).
    /// Hunspell will recognize affixed forms of the new word, too.
    /// </summary>
    /// <param name="word">The word to be added to the dictionary.</param>
    /// <param name="example">Sample word existing in the dictionary.</param>
    /// <returns><code>true</code> if the word is added; <code>false</code> if the sample word is not recognized.</returns>
    public bool AddWithAffix(string word, string example)
    {
      // detect captype and modify word length for UTF-8 encoding
      hentry dp = lookup(example);
      remove_forbidden_flag(word);
      if (dp != null && dp.astr != null)
      {
        var captype = get_captype(word, textinfo);
        if (is_aliasf())
        {
          add_word(word, dp.astr, null, false, captype);
        }
        else
        {
          var flags = new ushort[dp.astr.Length];
          Array.Copy(dp.astr, flags, dp.astr.Length);
          add_word(word, flags, null, false, captype);
        }
        add_hidden_capitalized_word(word, dp.astr, null, captype);
        return true;
      }
      return false;
    }

    // walk the hash table entry by entry
    IEnumerable<hentry> walk_hashtable()
    {
      for (int col = 0; col < tableptr.Length; ++col)
        for (var hp = tableptr[col]; hp != null; hp = hp.next)
          yield return hp;
    }

    // load a munched word list and build a hash table on the fly
    bool load_tables(FileMgr dic)
    {
      // first read the first line of file to get hash table size
      if (!dic.getline(out var ts))
      {
        HUNSPELL_WARNING(false, Properties.Resources.EmptyDic, dic);
        return true;
      }

      int expected;
      using (var part = ts.Split())
        expected = part.MoveNext() ? atoi(part.Current) : 0;

      const int nExtra = 5 + USERWORD;
      int tablesize = expected + (tableptr == null ? nExtra : tableptr.Length);

      if (expected <= 0 || tablesize >= int.MaxValue - 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidInitialWordCount, dic);
        return false;
      }

      if ((tablesize & 1) == 0)
        tablesize++;

      // allocate the hash table
      if (tableptr == null)
        tableptr = new hentry[tablesize];
      else if ((expected + wordcount) * 100 > tableptr.Length * 101)
        Resize(tablesize);

      // loop through all words on much list and add to hash
      // table and create word and affix strings

      int nLineCount = 0;
      while (dic.getline(out ts))
      {
        ++nLineCount;

        // split each line into word and morphological description
        int dp_pos = 0;
        while ((dp_pos = ts.IndexOf(':', dp_pos)) >= 0)
        {
          if ((dp_pos > 3) && (ts[dp_pos - 3] == ' ' || ts[dp_pos - 3] == '\t'))
          {
            for (dp_pos -= 3; dp_pos > 0 && (ts[dp_pos - 1] == ' ' || ts[dp_pos - 1] == '\t'); --dp_pos)
              ;
            if (dp_pos == 0)
            {  // missing word
              dp_pos = -1;
            }
            else
            {
              ++dp_pos;
            }
            break;
          }
          ++dp_pos;
        }

        // tabulator is the old morphological field separator
        int dp2_pos = ts.IndexOf('\t');
        if (dp2_pos >= 0 && (dp_pos < 0 || dp2_pos < dp_pos))
        {
          dp_pos = dp2_pos + 1;
        }

        string dp = null;
        if (dp_pos >= 0)
        {
          dp = ts.Substring(dp_pos, encoding);
          ts.Remove(dp_pos - 1);
        }

        // split each line into word and affix char strings
        // "\/" signs slash in words (not affix separator)
        // "/" at beginning of the line is word character (not affix separator)
        int ap_pos = ts.IndexOf('/');
        while (ap_pos >= 0)
        {
          if (ap_pos == 0)
          {
            ++ap_pos;
            continue;
          }
          else if (ts[ap_pos - 1] != '\\')
            break;
          // replace "\/" with "/"
          ts.Remove(ap_pos - 1, 1);
          ap_pos = ts.IndexOf('/', ap_pos);
        }

        ushort[] flags;
        if (ap_pos >= 0 && ap_pos != ts.Length)
        {
          var ap = ts.Substring(ap_pos + 1);
          ts.Remove(ap_pos);
          if (is_aliasf())
          {
            flags = get_aliasf(atoi(ap), dic);
          }
          else
          {
            flags = decode_flags(ap, dic);
            SortRemoveDuplicates(ref flags);
          }
        }
        else
        {
          flags = null;
        }

        var word = ts.String(encoding);
        var captype = get_captype(word, textinfo);
        // add the word and its index plus its capitalized form optionally
        if (!add_word(word, flags, dp, false, captype, dic) ||
            !add_hidden_capitalized_word(word, flags, dp, captype, dic)) return false;
      }

      // reject ludicrous tablesizes
      if (expected > 8192 && expected > nLineCount * 10)
      {
        HUNSPELL_WARNING(true, Properties.Resources.TooLargeInitialWordCount, expected, nLineCount);
        return false;
      }

      wordcount += nLineCount;

      return true;
    }

    // the hash function is a simple load and rotate
    // algorithm borrowed

    uint hash(string word, int i = 0, int length = -1)
    {
      int end = length < 0 ? word.Length : (i + length);
      uint hash1 = 0x15051505, hash2 = hash1;
      for (; i < end; ++i)
      {
        hash1 = (((hash1 << 5) + hash1) + (hash1 >> 27)) ^ word[i];
        if (++i == end) break;
        hash2 = (((hash2 << 5) + hash2) + (hash2 >> 27)) ^ word[i];
      }
      return hash1 + hash2 * 0x5d588b65;
    }

    uint hash(char[] word, int i = 0, int length = -1)
    {
      int end = length < 0 ? word.Length : (i + length);
      uint hash1 = 0x15051505, hash2 = hash1;
      for (; i < end; ++i)
      {
        hash1 = (((hash1 << 5) + hash1) + (hash1 >> 27)) ^ word[i];
        if (++i == end) break;
        hash2 = (((hash2 << 5) + hash2) + (hash2 >> 27)) ^ word[i];
      }
      return hash1 + hash2 * 0x5d588b65;
    }

    void Resize(int newSize)
    {
      hentry top = null;
      for (int i = 0; i < tableptr.Length; ++i)
        for (var hp = tableptr[i]; hp != null;)
        {
          var next = hp.next;
          hp.next = top;
          top = hp;
          hp = next;
        }

      tableptr = new hentry[newSize];

      while (top != null)
      {
        var next = top.next;
        var i = top.hash % (uint)newSize;
        top.next = tableptr[i];
        tableptr[i] = top;
        top = next;
      }
    }
  }
}
