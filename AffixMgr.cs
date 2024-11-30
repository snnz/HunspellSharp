using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace HunspellSharp
{
  using static Utils;

  partial class Hunspell
  {
    const int SETSIZE = 1 << 8;
    const int CONTSIZE = 1 << 16;
    const int MINCPDLEN = 3;
    const long TIMELIMIT = 1000 / 20;

    const byte dupSFX = (1 << 0);
    const byte dupPFX = (1 << 1);

    PfxEntry pStart0;
    SfxEntry sStart0;
    Dictionary<char, PfxEntry> pStart;
    Dictionary<char, SfxEntry> sStart;
    PfxEntry[] pFlag;
    SfxEntry[] sFlag;
    string keystring;
    string trystring;
    Encoding encoding;
    bool complexprefixes;
    ushort compoundflag;          // permits word in compound forms
    ushort compoundbegin;         // may be first word in compound forms
    ushort compoundmiddle;        // may be middle word in compound forms
    ushort compoundend;           // may be last word in compound forms
    ushort compoundroot;          // compound word signing flag
    ushort compoundforbidflag;    // compound fordidden flag for suffixed word
    ushort compoundpermitflag;    // compound permitting flag for suffixed word
    bool compoundmoresuffixes;    // allow more suffixes within compound words
    bool checkcompounddup;        // forbid double words in compounds
    bool checkcompoundrep;        // forbid bad compounds (may be non-compound word with a REP substitution)
    bool checkcompoundcase;       // forbid upper and lowercase combinations at word bounds
    bool checkcompoundtriple;     // forbid compounds with triple letters
    bool simplifiedtriple;        // allow simplified triple letters in compounds (Schiff+fahrt -> Schiffahrt)
    ushort forbiddenword;         // forbidden word signing flag
    ushort nosuggest;             // don't suggest words signed with NOSUGGEST flag
    ushort nongramsuggest;
    ushort needaffix;             // forbidden root, allowed only with suffixes
    int cpdmin;
    RepList iconvtable;
    RepList oconvtable;
    List<mapentry> maptable;
    bool parsedbreaktable;
    List<string> breaktable;
    bool parsedcheckcpd;
    List<patentry> checkcpdtable;
    bool simplifiedcpd;           // allow simplified compound forms (see 3rd field of CHECKCOMPOUNDPATTERN)
    bool parseddefcpd;
    List<ushort[]> defcpdtable;
    phonetable phone;
    int maxngramsugs;
    int maxcpdsugs;
    int maxdiff;
    bool onlymaxdiff;
    bool nosplitsugs;
    bool sugswithdots;
    int cpdwordmax;
    int cpdmaxsyllable;           // default 0: unlimited syllablecount in compound words
    char[] cpdvowels;             // vowels (for calculating of Hungarian compounding limit,
    string cpdsyllablenum;        // syllable count incrementing flag
//    bool checknum;              // checking numbers, and word with numbers
    char[] wordchars;             // letters + spec. word characters
    char[] ignorechars;           // letters + spec. word characters
    string version;               // affix and dictionary file version string
    LANG langnum;
    TextInfo textinfo;
    // LEMMA_PRESENT: not put root into the morphological output. Lemma presents
    // in morhological description in dictionary file. It's often combined with
    // PSEUDOROOT.
    ushort lemma_present;
    ushort circumfix;
    ushort onlyincompound;
    ushort keepcase;
    ushort forceucase;
    ushort warn;
    bool forbidwarn;
    ushort substandard;
    bool checksharps;
    bool fullstrip;

    bool havecontclass;           // flags of possible continuing classes (double affix)
    bool[] contclasses;           // flags of possible continuing classes (twofold affix)

    enum FlagMode { CHAR, LONG, NUM, UNI };

    FlagMode flag_mode;
    List<ushort[]> aliasf;     // flag vector `compression' with aliases
    List<string> aliasm;       // morphological desciption `compression' with aliases
                               // reptable created from REP table of aff file and from "ph:" fields
                               // of the dic file. It contains phonetic and other common misspellings
                               // (letters, letter groups and words) for better suggestions
    List<replentry> reptable;

    void LoadAff(FileMgr afflst)
    {
      pStart = new Dictionary<char, PfxEntry>();
      sStart = new Dictionary<char, SfxEntry>();
      pFlag = new PfxEntry[SETSIZE];
      sFlag = new SfxEntry[SETSIZE];
      breaktable = new List<string>();
      checkcpdtable = new List<patentry>();
      defcpdtable = new List<ushort[]>();
      contclasses = new bool[CONTSIZE];
      encoding = Encoding.GetEncoding(28591);
      langnum = LANG.xx;
      textinfo = CultureInfo.InvariantCulture.TextInfo;

      forbiddenword = FORBIDDENWORD;
      cpdwordmax = -1;        // default: unlimited wordcount in compound words
      cpdmin = -1;            // undefined
      maxngramsugs = -1;      // undefined
      maxdiff = -1;           // undefined
      maxcpdsugs = -1;        // undefined

      aliasf = new List<ushort[]>();
      aliasm = new List<string>();
      reptable = new List<replentry>();

      // Load affix data from aff file
      parse_file(afflst);

      // convert affix trees to sorted list
      process_pfx_tree_to_list();
      process_sfx_tree_to_list();

      // affix trees are sorted now

      // now we can speed up performance greatly taking advantage of the
      // relationship between the affixes and the idea of "subsets".

      // View each prefix as a potential leading subset of another and view
      // each suffix (reversed) as a potential trailing subset of another.

      // To illustrate this relationship if we know the prefix "ab" is found in the
      // word to examine, only prefixes that "ab" is a leading subset of need be
      // examined.
      // Furthermore is "ab" is not present then none of the prefixes that "ab" is
      // is a subset need be examined.
      // The same argument goes for suffix string that are reversed.

      // Then to top this off why not examine the first char of the word to quickly
      // limit the set of prefixes to examine (i.e. the prefixes to examine must
      // be leading supersets of the first character of the word (if they exist)

      // To take advantage of this "subset" relationship, we need to add two links
      // from entry.  One to take next if the current prefix is found (call it
      // nexteq)
      // and one to take next if the current prefix is not found (call it nextne).

      // Since we have built ordered lists, all that remains is to properly
      // initialize
      // the nextne and nexteq pointers that relate them

      process_pfx_order();
      process_sfx_order();

      // default BREAK definition
      if (!parsedbreaktable) {
        breaktable.Add("-");
        breaktable.Add("^-");
        breaktable.Add("-$");
        parsedbreaktable = true;
      }

    #if FUZZING_BUILD_MODE_UNSAFE_FOR_PRODUCTION
      // not entirely sure this is invalid, so only for fuzzing for now
      if (iconvtable && !iconvtable.check_against_breaktable(breaktable)) {
          delete iconvtable;
          iconvtable = nullptr;
      }
    #endif

      if (cpdmin == -1)
        cpdmin = MINCPDLEN;
    }

    // read in aff file and build up prefix and suffix entry objects
    bool parse_file(FileMgr afflst)
    {
      // checking flag duplication
      byte[] dupflags = null;
      string enc = null, lang = null;

      // step one is to parse the affix file building up the internal
      // affix data structures

      // read in each line ignoring any that do not
      // start with a known line type indicator
      while (afflst.getline(out var line))
        using (var parts = line.Split())
          if (parts.MoveNext())
          {
            var keyword = parts.Current;

            /* parse in the keyboard string */
            if (keyword.Equals("KEY"))
            {
              if (!parse_string(parts, ref keystring, afflst)) return false;
            }

            /* parse in the try string */
            else if (keyword.Equals("TRY"))
              parse_string(parts, ref trystring, afflst);

            /* parse in the name of the character set used by the .dic and .aff */
            else if (keyword.Equals("SET"))
            {
              if (!parse_string(parts, ref enc, afflst)) return false;
              SetEncoding(enc, afflst);
            }

            /* parse COMPLEXPREFIXES for agglutinative languages with right-to-left
             * writing system */
            else if (keyword.Equals("COMPLEXPREFIXES"))
              complexprefixes = true;

            /* parse in the flag used by the controlled compound words */
            else if (keyword.Equals("COMPOUNDFLAG"))
            {
              if (!parse_flag(parts, ref compoundflag, afflst)) return false;
            }

            /* parse in the flag used by compound words */
            else if (keyword.Equals("COMPOUNDBEGIN"))
            {
              if (complexprefixes)
              {
                if (!parse_flag(parts, ref compoundend, afflst)) return false;
              }
              else
              {
                if (!parse_flag(parts, ref compoundbegin, afflst)) return false;
              }
            }

            /* parse in the flag used by compound words */
            else if (keyword.Equals("COMPOUNDMIDDLE"))
            {
              if (!parse_flag(parts, ref compoundmiddle, afflst)) return false;
            }

            /* parse in the flag used by compound words */
            else if (keyword.Equals("COMPOUNDEND"))
            {
              if (complexprefixes)
              {
                if (!parse_flag(parts, ref compoundbegin, afflst)) return false;
              }
              else
              {
                if (!parse_flag(parts, ref compoundend, afflst)) return false;
              }
            }

            /* parse in the data used by compound_check() method */
            else if (keyword.Equals("COMPOUNDWORDMAX"))
            {
              if (!parse_num(parts, ref cpdwordmax, afflst)) return false;
            }

            /* parse in the flag sign compounds in dictionary */
            else if (keyword.Equals("COMPOUNDROOT"))
            {
              if (!parse_flag(parts, ref compoundroot, afflst)) return false;
            }

            /* parse in the flag used by compound_check() method */
            else if (keyword.Equals("COMPOUNDPERMITFLAG"))
            {
              if (!parse_flag(parts, ref compoundpermitflag, afflst)) return false;
            }

            /* parse in the flag used by compound_check() method */
            else if (keyword.Equals("COMPOUNDFORBIDFLAG"))
            {
              if (!parse_flag(parts, ref compoundforbidflag, afflst)) return false;
            }

            else if (keyword.Equals("COMPOUNDMORESUFFIXES"))
              compoundmoresuffixes = true;

            else if (keyword.Equals("CHECKCOMPOUNDDUP"))
              checkcompounddup = true;

            else if (keyword.Equals("CHECKCOMPOUNDREP"))
              checkcompoundrep = true;

            else if (keyword.Equals("CHECKCOMPOUNDTRIPLE"))
              checkcompoundtriple = true;

            else if (keyword.Equals("SIMPLIFIEDTRIPLE"))
              simplifiedtriple = true;

            else if (keyword.Equals("CHECKCOMPOUNDCASE"))
              checkcompoundcase = true;

            else if (keyword.Equals("NOSUGGEST"))
            {
              if (!parse_flag(parts, ref nosuggest, afflst)) return false;
            }

            else if (keyword.Equals("NONGRAMSUGGEST"))
            {
              if (!parse_flag(parts, ref nongramsuggest, afflst)) return false;
            }

            else if (keyword.Equals("FLAG"))
            {
              if (flag_mode != FlagMode.CHAR) HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, "FLAG", afflst);

              if (parts.MoveNext())
              {
                keyword = parts.Current;
                if (keyword.Contains("long"))
                  flag_mode = FlagMode.LONG;
                if (keyword.Contains("num"))
                  flag_mode = FlagMode.NUM;
                if (keyword.Contains("UTF-8"))
                  flag_mode = FlagMode.UNI;
              }

              if (flag_mode == FlagMode.CHAR) HUNSPELL_WARNING(true, Properties.Resources.InvalidFlagMode, afflst);
            }

            /* parse in the flag used by forbidden words */
            else if (keyword.Equals("FORBIDDENWORD"))
            {
              if (!parse_flag(parts, ref forbiddenword, afflst)) return false;
            }

            /* parse in the flag used by forbidden words (is deprecated) */
            else if (keyword.Equals("LEMMA_PRESENT"))
            {
              if (!parse_flag(parts, ref lemma_present, afflst)) return false;
            }

            /* parse in the flag used by circumfixes */
            else if (keyword.Equals("CIRCUMFIX"))
            {
              if (!parse_flag(parts, ref circumfix, afflst)) return false;
            }

            /* parse in the flag used by fogemorphemes */
            else if (keyword.Equals("ONLYINCOMPOUND"))
            {
              if (!parse_flag(parts, ref onlyincompound, afflst)) return false;
            }

            /* parse in the flag used by `needaffixs' (is deprecated) */
            else if (keyword.Equals("PSEUDOROOT"))
            {
              if (!parse_flag(parts, ref needaffix, afflst)) return false;
            }

            /* parse in the flag used by `needaffixs' */
            else if (keyword.Equals("NEEDAFFIX"))
            {
              if (!parse_flag(parts, ref needaffix, afflst)) return false;
            }

            /* parse in the minimal length for words in compounds */
            else if (keyword.Equals("COMPOUNDMIN"))
            {
              if (!parse_num(parts, ref cpdmin, afflst)) return false;
              if (cpdmin < 1) cpdmin = 1;
            }

            /* parse in the max. words and syllables in compounds */
            else if (keyword.Equals("COMPOUNDSYLLABLE"))
            {
              if (!parse_cpdsyllable(parts, afflst)) return false;
            }

            /* parse in the flag used by compound_check() method */
            else if (keyword.Equals("SYLLABLENUM"))
            {
              if (!parse_string(parts, ref cpdsyllablenum, afflst)) return false;
            }

            /* parse in the flag used by the controlled compound words */
            else if (keyword.Equals("CHECKNUM"))
            {
              // checknum = true;
            }

            /* parse in the extra word characters */
            else if (keyword.Equals("WORDCHARS"))
            {
              if (!parse_array(parts, ref wordchars, afflst)) return false;
            }

            /* parse in the ignored characters (for example, Arabic optional diacretics
             * charachters */
            else if (keyword.Equals("IGNORE"))
            {
              if (!parse_array(parts, ref ignorechars, afflst)) return false;
            }

            /* parse in the input conversion table */
            else if (keyword.Equals("ICONV"))
            {
              if (!parse_convtable(parts, ref iconvtable, "ICONV", afflst)) return false;
            }

            /* parse in the output conversion table */
            else if (keyword.Equals("OCONV"))
            {
              if (!parse_convtable(parts, ref oconvtable, "OCONV", afflst)) return false;
            }

            /* parse in the phonetic translation table */
            else if (keyword.Equals("PHONE"))
            {
              if (!parse_phonetable(parts, afflst)) return false;
            }

            /* parse in the checkcompoundpattern table */
            else if (keyword.Equals("CHECKCOMPOUNDPATTERN"))
            {
              if (!parse_checkcpdtable(parts, afflst)) return false;
            }

            /* parse in the defcompound table */
            else if (keyword.Equals("COMPOUNDRULE"))
            {
              if (!parse_defcpdtable(parts, afflst)) return false;
            }

            /* parse in the related character map table */
            else if (keyword.Equals("MAP"))
            {
              if (!parse_maptable(parts, afflst)) return false;
            }

            /* parse in the word breakpoints table */
            else if (keyword.Equals("BREAK"))
            {
              if (!parse_breaktable(parts, afflst)) return false;
            }

            /* parse in the language for language specific codes */
            else if (keyword.Equals("LANG"))
            {
              if (!parse_string(parts, ref lang, afflst)) return false;
              GetLanguageAndTextInfo(lang, out langnum, ref textinfo);
            }

            else if (keyword.Equals("VERSION"))
            {
              if (parts.MoveNext())
                version = parts.Current.ExpandToEndOf(line).String(encoding);
            }

            else if (keyword.Equals("MAXNGRAMSUGS"))
            {
              if (!parse_num(parts, ref maxngramsugs, afflst)) return false;
            }

            else if (keyword.Equals("ONLYMAXDIFF"))
              onlymaxdiff = true;

            else if (keyword.Equals("MAXDIFF"))
            {
              if (!parse_num(parts, ref maxdiff, afflst)) return false;
            }

            else if (keyword.Equals("MAXCPDSUGS"))
            {
              if (!parse_num(parts, ref maxcpdsugs, afflst)) return false;
            }

            else if (keyword.Equals("NOSPLITSUGS"))
              nosplitsugs = true;

            else if (keyword.Equals("FULLSTRIP"))
              fullstrip = true;

            else if (keyword.Equals("SUGSWITHDOTS"))
              sugswithdots = true;

            /* parse in the flag used by forbidden words */
            else if (keyword.Equals("KEEPCASE"))
            {
              if (!parse_flag(parts, ref keepcase, afflst)) return false;
            }

            /* parse in the flag used by `forceucase' */
            else if (keyword.Equals("FORCEUCASE"))
            {
              if (!parse_flag(parts, ref forceucase, afflst)) return false;
            }

            /* parse in the flag used by `warn' */
            else if (keyword.Equals("WARN"))
            {
              if (!parse_flag(parts, ref warn, afflst)) return false;
            }

            else if (keyword.Equals("FORBIDWARN"))
              forbidwarn = true;

            /* parse in the flag used by the affix generator */
            else if (keyword.Equals("SUBSTANDARD"))
            {
              if (!parse_flag(parts, ref substandard, afflst)) return false;
            }

            else if (keyword.Equals("CHECKSHARPS"))
              checksharps = true;

            /* parse in the typical fault correcting table */
            else if (keyword.Equals("REP"))
            {
              if (!parse_reptable(parts, afflst)) return false;
            }

            else if (keyword.Equals("AF"))
            {
              if (!parse_aliasf(parts, afflst)) return false;
            }

            else if (keyword.Equals("AM"))
            {
              if (!parse_aliasm(parts, afflst)) return false;
            }

            /* parse this affix: P - prefix, S - suffix */
            else if (keyword.Equals("PFX") || keyword.Equals("SFX"))
            {
              if (dupflags == null) dupflags = new byte[CONTSIZE];
              if (!parse_affix(parts, (keyword[0] == (byte)'P') != complexprefixes ? 'P' : 'S', afflst, dupflags))
                return false;
            }
          }
      return true;
    }

    // we want to be able to quickly access prefix information
    // both by prefix flag, and sorted by prefix string itself
    // so we need to set up two indexes

    void build_pfxtree(PfxEntry pfxptr)
    {
      PfxEntry ptr;
      PfxEntry pptr;
      PfxEntry ep = pfxptr;

      // get the right starting points
      string key = ep.getKey();
      var flg = (byte)(ep.getFlag() & 0x00FF);

      // first index by flag which must exist
      ptr = pFlag[flg];
      ep.setFlgNxt(ptr);
      pFlag[flg] = ep;

      // handle the special case of null affix string
      if (key.Length == 0) {
        // always inset them at head of list at element 0
        ptr = pStart0;
        ep.setNext(ptr);
        pStart0 = ep;
        return;
      }

      // now handle the normal case
      ep.setNextEQ(null);
      ep.setNextNE(null);

      char sp = key[0];

      // handle the first insert
      if (!pStart.TryGetValue(sp, out ptr))
      {
        pStart.Add(sp, ep);
        return;
      }

      // otherwise use binary tree insertion so that a sorted
      // list can easily be generated later
      pptr = null;
      for (;;) {
        pptr = ptr;
        if (string.CompareOrdinal(ep.getKey(), ptr.getKey()) <= 0) {
          ptr = ptr.getNextEQ();
          if (ptr == null) {
            pptr.setNextEQ(ep);
            break;
          }
        } else {
          ptr = ptr.getNextNE();
          if (ptr == null) {
            pptr.setNextNE(ep);
            break;
          }
        }
      }
    }

    // we want to be able to quickly access suffix information
    // both by suffix flag, and sorted by the reverse of the
    // suffix string itself; so we need to set up two indexes
    void build_sfxtree(SfxEntry sfxptr)
    {
      sfxptr.initReverseWord(helper);

      SfxEntry ptr;
      SfxEntry pptr;
      SfxEntry ep = sfxptr;

      /* get the right starting point */
      string key = ep.getKey();
      var flg = (byte)(ep.getFlag() & 0x00FF);

      // first index by flag which must exist
      ptr = sFlag[flg];
      ep.setFlgNxt(ptr);
      sFlag[flg] = ep;

      // next index by affix string

      // handle the special case of null affix string
      if (key.Length == 0) {
        // always inset them at head of list at element 0
        ptr = sStart0;
        ep.setNext(ptr);
        sStart0 = ep;
        return;
      }

      // now handle the normal case
      ep.setNextEQ(null);
      ep.setNextNE(null);

      char sp = key[0];

      // handle the first insert
      if (!sStart.TryGetValue(sp, out ptr))
      {
        sStart.Add(sp, ep);
        return;
      }

      // otherwise use binary tree insertion so that a sorted
      // list can easily be generated later
      pptr = null;
      for (;;) {
        pptr = ptr;
        if (string.CompareOrdinal(ep.getKey(), ptr.getKey()) <= 0) {
          ptr = ptr.getNextEQ();
          if (ptr == null) {
            pptr.setNextEQ(ep);
            break;
          }
        } else {
          ptr = ptr.getNextNE();
          if (ptr == null) {
            pptr.setNextNE(ep);
            break;
          }
        }
      }
    }

    // convert from binary tree to sorted list
    void process_pfx_tree_to_list()
    {
      foreach (var i in pStart.Keys.ToArray())
        pStart[i] = process_pfx_in_order(pStart[i], null);
    }

    PfxEntry process_pfx_in_order(PfxEntry ptr, PfxEntry nptr)
    {
      if (ptr != null)
      {
        nptr = process_pfx_in_order(ptr.getNextNE(), nptr);
        ptr.setNext(nptr);
        nptr = process_pfx_in_order(ptr.getNextEQ(), ptr);
      }
      return nptr;
    }

    // convert from binary tree to sorted list
    void process_sfx_tree_to_list()
    {
      foreach (var i in sStart.Keys.ToArray())
        sStart[i] = process_sfx_in_order(sStart[i], null);
    }

    SfxEntry process_sfx_in_order(SfxEntry ptr, SfxEntry nptr)
    {
      if (ptr != null)
      {
        nptr = process_sfx_in_order(ptr.getNextNE(), nptr);
        ptr.setNext(nptr);
        nptr = process_sfx_in_order(ptr.getNextEQ(), ptr);
      }
      return nptr;
    }

    // reinitialize the PfxEntry links NextEQ and NextNE to speed searching
    // using the idea of leading subsets this time
    int process_pfx_order()
    {
      // loop through each prefix list starting point
      foreach (var kv in pStart)
      {
        var ptr = kv.Value;

        // look through the remainder of the list
        //  and find next entry with affix that
        // the current one is not a subset of
        // mark that as destination for NextNE
        // use next in list that you are a subset
        // of as NextEQ

        for (; ptr != null; ptr = ptr.getNext())
        {
          PfxEntry nptr = ptr.getNext();
          for (; nptr != null; nptr = nptr.getNext())
          {
            if (!isSubset(ptr.getKey(), nptr.getKey()))
              break;
          }
          ptr.setNextNE(nptr);
          ptr.setNextEQ(null);
          if (ptr.getNext() != null &&
              isSubset(ptr.getKey(), ptr.getNext().getKey()))
            ptr.setNextEQ(ptr.getNext());
        }

        // now clean up by adding smart search termination strings:
        // if you are already a superset of the previous prefix
        // but not a subset of the next, search can end here
        // so set NextNE properly

        ptr = kv.Value;
        for (; ptr != null; ptr = ptr.getNext())
        {
          PfxEntry nptr = ptr.getNext();
          PfxEntry mptr = null;
          for (; nptr != null; nptr = nptr.getNext())
          {
            if (!isSubset(ptr.getKey(), nptr.getKey()))
              break;
            mptr = nptr;
          }
          if (mptr != null)
            mptr.setNextNE(null);
        }
      }
      return 0;
    }

    // initialize the SfxEntry links NextEQ and NextNE to speed searching
    // using the idea of leading subsets this time
    int process_sfx_order()
    {
      // loop through each prefix list starting point
      foreach (var kv in sStart)
      {
        var ptr = kv.Value;

        // look through the remainder of the list
        //  and find next entry with affix that
        // the current one is not a subset of
        // mark that as destination for NextNE
        // use next in list that you are a subset
        // of as NextEQ

        for (; ptr != null; ptr = ptr.getNext())
        {
          SfxEntry nptr = ptr.getNext();
          for (; nptr != null; nptr = nptr.getNext())
          {
            if (!isSubset(ptr.getKey(), nptr.getKey()))
              break;
          }
          ptr.setNextNE(nptr);
          ptr.setNextEQ(null);
          if (ptr.getNext() != null &&
              isSubset(ptr.getKey(), ptr.getNext().getKey()))
            ptr.setNextEQ(ptr.getNext());
        }

        // now clean up by adding smart search termination strings:
        // if you are already a superset of the previous suffix
        // but not a subset of the next, search can end here
        // so set NextNE properly

        ptr = kv.Value;
        for (; ptr != null; ptr = ptr.getNext())
        {
          SfxEntry nptr = ptr.getNext();
          SfxEntry mptr = null;
          for (; nptr != null; nptr = nptr.getNext())
          {
            if (!isSubset(ptr.getKey(), nptr.getKey()))
              break;
            mptr = nptr;
          }
          if (mptr != null)
            mptr.setNextNE(null);
        }
      }
      return 0;
    }

    // add flags to the result for dictionary debugging
    void debugflag(StringBuilder result, ushort flag) {
      result.Append(MSEP.FLD);
      result.Append(MORPH.FLAG);
      encode_flag(flag, result);
    }

    // calculate the character length of the condition
    int condlen(char[] s) {
      int l = 0;
      bool group = false;
      int st = 0, end = s.Length;
      while (st != end) {
        if (s[st] == '[') {
          group = true;
          l++;
        } else if (s[st] == ']')
          group = false;
        else if (!group)
          l++;
        ++st;
      }
      return l;
    }

    void encodeit(AffEntry entry, char[] cs) {
      if (cs.Length != 1 || cs[0] != '.') {
        entry.numconds = (byte)condlen(cs);
        entry.conds = cs;
      } else {
        entry.numconds = 0;
        entry.conds = EmptyCharArray;
      }
    }

    // return 1 if s1 is a leading subset of s2 (dots are for infixes)
    bool isSubset(string s1, string s2, int start = 0) {
      int i1, i2;
      for (i1 = 0, i2 = start; i1 < s1.Length && i2 < s2.Length; ++i1, ++i2)
        if (s1[i1] != s2[i2] && s1[i1] != '.') break;
      return i1 == s1.Length;
    }

    bool isSubset(string s1, IEnumerable<char> s2_, int start = 0)
    {
      switch (s2_)
      {
        case string s: return isSubset(s1, s, start);
        case char[] s2:
          int i1, i2;
          for (i1 = 0, i2 = start; i1 < s1.Length && i2 < s2.Length; ++i1, ++i2)
            if (s1[i1] != s2[i2] && s1[i1] != '.') break;
          return i1 == s1.Length;
        default: throw new NotSupportedException();
      }
    }

    // check word for prefixes
    hentry prefix_check(IEnumerable<char> word,
                               int start,
                               int len,
                               IN_CPD in_compound,
                               ushort needflag = 0)
    {
      hentry rv = null;

      var ctx = Context;
      ctx.pfx = null;
      ctx.sfxappnd = null;
      ctx.sfxextra = 0;

      // first handle the special case of 0 length prefixes
      for (PfxEntry pe = pStart0; pe != null; pe = pe.getNext())
      {
        if (
            // fogemorpheme
            ((in_compound != IN_CPD.NOT) ||
             !(pe.getCont() != null &&
               onlyincompound != 0 && TESTAFF(pe.getCont(), onlyincompound))) &&
            // permit prefixes in compounds
            ((in_compound != IN_CPD.END) ||
             (pe.getCont() != null &&
              compoundpermitflag != 0 && TESTAFF(pe.getCont(), compoundpermitflag))))
        {
          // check prefix
          rv = pe.checkword(this, word, start, len, in_compound, needflag);
          if (rv != null)
          {
            ctx.pfx = pe;  // BUG: pfx not stateless
            return rv;
          }
        }
      }

      // now handle the general case
      char sp = word.At(start);
      if (pStart.TryGetValue(sp, out var pptr))
        do
        {
          if (isSubset(pptr.getKey(), word, start))
          {
            if (
                // fogemorpheme
                ((in_compound != IN_CPD.NOT) ||
                 !(pptr.getCont() != null &&
                   onlyincompound != 0 && TESTAFF(pptr.getCont(), onlyincompound))) &&
                // permit prefixes in compounds
                ((in_compound != IN_CPD.END) ||
                 (pptr.getCont() != null && compoundpermitflag != 0 && TESTAFF(pptr.getCont(), compoundpermitflag))))
            {
              // check prefix
              rv = pptr.checkword(this, word, start, len, in_compound, needflag);
              if (rv != null)
              {
                ctx.pfx = pptr;  // BUG: pfx not stateless
                return rv;
              }
            }
            pptr = pptr.getNextEQ();
          }
          else
          {
            pptr = pptr.getNextNE();
          }
        } while (pptr != null);

      return null;
    }

    // check word for prefixes and two-level suffixes
    hentry prefix_check_twosfx(IEnumerable<char> word,
                               int start,
                               int len,
                               IN_CPD in_compound,
                               ushort needflag = 0)
    { 
      hentry rv = null;

      var ctx = Context;
      ctx.pfx = null;
      ctx.sfxappnd = null;
      ctx.sfxextra = 0;

      // first handle the special case of 0 length prefixes
      for (var pe = pStart0; pe != null; pe = pe.getNext())
      {
        rv = pe.check_twosfx(this, word, start, len, in_compound, needflag);
        if (rv != null)
          return rv;
      }

      // now handle the general case
      char sp = word.At(start);
      if (pStart.TryGetValue(sp, out var pptr))
        do
        {
          if (isSubset(pptr.getKey(), word, start))
          {
            rv = pptr.check_twosfx(this, word, start, len, in_compound, needflag);
            if (rv != null)
            {
              ctx.pfx = pptr;
              return rv;
            }
            pptr = pptr.getNextEQ();
          }
          else
          {
            pptr = pptr.getNextNE();
          }
        } while (pptr != null);

      return null;
    }

    // check word for prefixes and morph
    void prefix_check_morph(StringBuilder result,
                                   string word,
                                    int start,
                                    int len,
                                    IN_CPD in_compound,
                                    ushort needflag = 0)
    {
      var ctx = Context;
      ctx.pfx = null;
      ctx.sfxappnd = null;
      ctx.sfxextra = 0;

      // first handle the special case of 0 length prefixes
      for (var pe = pStart0; pe != null; pe = pe.getNext())
      {
        pe.check_morph(this, result, word, start, len, in_compound, needflag);
      }

      // now handle the general case
      char sp = word[start];
      if (pStart.TryGetValue(sp, out var pptr))
        do
        {
          if (isSubset(pptr.getKey(), word, start))
          {
            if (in_compound != IN_CPD.NOT ||
                !(pptr.getCont() != null && onlyincompound != 0 && TESTAFF(pptr.getCont(), onlyincompound)))
            {
              var prelen = result.Length;
              pptr.check_morph(this, result, word, start, len, in_compound, needflag);
              if (result.Length > prelen)
              {
                // fogemorpheme
                ctx.pfx = pptr;
              }
            }
            pptr = pptr.getNextEQ();
          }
          else
          {
            pptr = pptr.getNextNE();
          }
        } while (pptr != null);
    }

    // check word for prefixes and morph and two-level suffixes
    void prefix_check_twosfx_morph(StringBuilder result,
                                   string word,
                                   int start,
                                   int len,
                                   IN_CPD in_compound,
                                   ushort needflag = 0) {
      var ctx = Context;
      ctx.pfx = null;
      ctx.sfxappnd = null;
      ctx.sfxextra = 0;

      // first handle the special case of 0 length prefixes
      for (var pe = pStart0; pe != null; pe = pe.getNext())
      {
        pe.check_twosfx_morph(this, result, word, start, len, in_compound, needflag);
      }

      // now handle the general case
      char sp = word[start];
      if (pStart.TryGetValue(sp, out var pptr))
        do
        {
          if (isSubset(pptr.getKey(), word, start))
          {
            int prelen = result.Length;
            pptr.check_twosfx_morph(this, result, word, start, len, in_compound, needflag);
            if (result.Length > prelen)
            {
              ctx.pfx = pptr;
            }
            pptr = pptr.getNextEQ();
          }
          else
          {
            pptr = pptr.getNextNE();
          }
        } while (pptr != null);
    }

    // Is word a non-compound with a REP substitution (see checkcompoundrep)?
    bool cpdrep_check(Context ctx, IEnumerable<char> word, int wl)
    {
      wl = Math.Min(wl, word.Length());
      if ((wl < 2) || reptable.Count == 0)
        return false;

      char[] candidate = null;
      try
      {
        foreach (var i in reptable)
        {
          // use only available mid patterns
          if (!string.IsNullOrEmpty(i.outstrings[0]))
          {
            int r = 0;
            int lenp = i.pattern.Length;
            // search every occurence of the pattern in the word
            while ((r = word.IndexOf(i.pattern, r)) >= 0)
            {
              var len = wl - lenp + i.outstrings[0].Length;
              if (candidate == null)
                candidate = ctx.PopBuffer(len);
              else if (candidate.Length < len)
                candidate = new char[len];
              word.CopyTo(0, candidate, 0, r);
              i.outstrings[0].CopyTo(0, candidate, r, i.outstrings[0].Length);
              word.CopyTo(r + lenp, candidate, r + i.outstrings[0].Length, wl - r - lenp);
              if (candidate_check(candidate, len))
                return true;
              ++r;  // search for the next letter
            }
          }
        }
      }
      finally
      {
        if (candidate != null) ctx.PushBuffer(candidate);
      }
      return false;
    }

    // forbid compound words, if they are in the dictionary as a
    // word pair separated by space
    bool cpdwordpair_check(Context ctx, IEnumerable<char> word, int wl)
    {
      wl = Math.Min(wl, word.Length());
      if (wl > 2) {
        var candidate = ctx.PopBuffer(wl + 1);
        try
        {
          word.CopyTo(0, candidate, 1, wl);
          for (int i = 1; i < wl - 1; i++)
          {
            candidate[i - 1] = candidate[i];
            candidate[i] = ' ';
            if (candidate_check(candidate, wl + 1))
              return true;
          }
        }
        finally
        {
          ctx.PushBuffer(candidate);
        }
      }

      return false;
    }

    // forbid compoundings when there are special patterns at word bound
    bool cpdpat_check(string word,
                      int pos,
                      hentry r1,
                      hentry r2,
                      bool affixed)
    {
      foreach (var i in checkcpdtable) {
        int len;
        if (isSubset(i.pattern2, word, pos) &&
            (r1 == null || i.cond == 0 ||
             (TESTAFF(r1.astr, i.cond))) &&
            (r2 == null || i.cond2 == 0 ||
             (TESTAFF(r2.astr, i.cond2))) &&
            // zero length pattern => only TESTAFF
            // zero pattern (0/flag) => unmodified stem (zero affixes allowed)
            (string.IsNullOrEmpty(i.pattern) ||
             ((i.pattern[0] == '0' && r1.word.Length <= pos &&
               string.CompareOrdinal(word, pos - r1.word.Length, r1.word, 0, r1.word.Length) == 0) ||
              (i.pattern[0] != '0' &&
               ((len = i.pattern.Length) != 0) && len <= pos &&
               string.CompareOrdinal(word, pos - len, i.pattern, 0, len) == 0)))) {
          return true;
        }
      }
      return false;
    }

    // forbid compounding with neighbouring upper and lower case characters at word
    // bounds
    bool cpdcase_check(string word, int pos)
    {
      char a = word[pos - 1], b = word[pos];
      return (char.IsUpper(a) || char.IsUpper(b)) && (a != '-') && (b != '-');
    }

    // check compound patterns
    bool defcpd_check(Context ctx,
                      ref hentry[] words,
                      int wnum,
                      hentry rv,
                      hentry[] def,
                      bool all)
    {
      bool w = false;

      if (words == null) {
        w = true;
        words = def;
      }

      if (words == null) {
        return false;
      }

      var btinfo = ctx.btinfo;
      if (btinfo == null) btinfo = ctx.btinfo = new metachar_data[4];

      short bt = 0;

      words[wnum] = rv;

      // has the last word COMPOUNDRULE flag?
      if (rv.astr == null) {
        words[wnum] = null;
        if (w)
          words = null;
        return false;
      }
      bool ok = false;
      foreach (var i in defcpdtable) {
        foreach (var j in i) {
          if (j != '*' && j != '?' &&
              TESTAFF(rv.astr, j)) {
            ok = true;
            break;
          }
        }
      }
      if (!ok) {
        words[wnum] = null;
        if (w)
          words = null;
        return false;
      }

      foreach (var i in defcpdtable) {
        int pp = 0;  // pattern position
        short wp = 0;  // "words" position
        bool ok2 = true;
        ok = true;
        do {
          while ((pp < i.Length) && (wp <= wnum)) {
            if (((pp + 1) < i.Length) &&
                ((i[pp + 1] == '*') ||
                 (i[pp + 1] == '?'))) {
              int wend = (i[pp + 1] == '?') ? wp : wnum;
              ok2 = true;
              pp += 2;
              btinfo[bt].btpp = (short)pp;
              btinfo[bt].btwp = wp;
              while (wp <= wend) {
                if (words[wp] == null ||
                    words[wp].astr == null ||
                    !TESTAFF(words[wp].astr, i[pp - 2])) {
                  ok2 = false;
                  break;
                }
                wp++;
              }
              if (wp <= wnum)
                ok2 = false;
              btinfo[bt].btnum = wp - btinfo[bt].btwp;
              if (btinfo[bt].btnum > 0) {
                ++bt;
                if (btinfo.Length <= bt)
                {
                  Array.Resize(ref btinfo, bt + 1);
                  ctx.btinfo = btinfo;
                }
              }
              if (ok2)
                break;
            } else {
              ok2 = true;
              if (words[wp] == null || words[wp].astr == null ||
			      !TESTAFF(words[wp].astr, i[pp])) {
                ok = false;
                break;
              }
              pp++;
              wp++;
              if ((i.Length == pp) && !(wp > wnum))
                ok = false;
            }
          }
          if (ok && ok2) {
            int r = pp;
            while ((i.Length > r) && ((r + 1) < i.Length) &&
                   ((i[r + 1] == '*') ||
                    (i[r + 1] == '?')))
              r += 2;
            if (i.Length <= r)
              return true;
          }
          // backtrack
          if (bt != 0)
            do {
              ok = true;
              btinfo[bt - 1].btnum--;
              pp = btinfo[bt - 1].btpp;
              wp = (short)(btinfo[bt - 1].btwp + btinfo[bt - 1].btnum);
            } while ((btinfo[bt - 1].btnum < 0) && --bt != 0);
        } while (bt != 0);

        if (ok && ok2 && (!all || (i.Length <= pp)))
          return true;

        // check zero ending
        while (ok && ok2 && (i.Length > pp) &&
               ((pp + 1) < i.Length) &&
               ((i[pp + 1] == '*') ||
                (i[pp + 1] == '?')))
          pp += 2;
        if (ok && ok2 && (i.Length <= pp))
          return true;
      }
      words[wnum] = null;
      if (w)
        words = null;
      return false;
    }

    bool candidate_check(char[] word, int wl)
    {
      hentry rv = lookup(word, 0, wl);
      if (rv != null)
        return true;

      //  rv = prefix_check(word,0,len,1);
      //  if (rv) return 1;

      rv = affix_check(word, 0, wl);
      if (rv != null)
        return true;
      return false;
    }

    // calculate number of syllable for compound-checking
    int get_syllable(string word, int i, int len)
    {
      if (cpdmaxsyllable == 0)
        return 0;

      if (len < 0) len = word.Length - i;

      int num = 0;
      for (; i < len; ++i)
        if (Array.BinarySearch(cpdvowels, word[i]) >= 0) ++num;
      return num;
    }
    int get_syllable(string word, int i = 0) => get_syllable(word, i, -1);

    int get_syllable(char[] word, int i, int len)
    {
      if (cpdmaxsyllable == 0)
        return 0;

      if (len < 0) len = word.Length - i;

      int num = 0;
      for (; i < len; ++i)
        if (Array.BinarySearch(cpdvowels, word[i]) >= 0) ++num;
      return num;
    }
    int get_syllable(char[] word, int i = 0) => get_syllable(word, i, -1);

    void setcminmax(out int cmin, out int cmax, string word)
    {
      cmin = cpdmin;
      cmax = word.Length - cpdmin + 1;
    }

    void setcminmax(out int cmin, out int cmax, int len)
    {
      cmin = cpdmin;
      cmax = len - cpdmin + 1;
    }

    // check if compound word is correctly spelled
    // hu_mov_rule = spec. Hungarian rule (XXX)
    hentry compound_check(string word,
                          int wordnum,
                          int numsyllable,
                          int wnum,
                          hentry[] words,
                          bool hu_mov_rule,
                          bool is_sug,
                          SPELL? info)
    {
      int oldnumsyllable, oldnumsyllable2, oldwordnum, oldwordnum2;
      hentry rv, rv_first;
      char[] st;
      int stsize;
      char ch = '\0';
      bool affixed, striple = false, checkedstriple = false;
      int cmin, cmax;
      int soldi = 0, oldcmin = 0, oldcmax = 0, oldlen = 0;
      hentry[] oldwords = words;
      int scpd = 0, len = word.Length;

      bool checked_prefix;

      var ctx = Context;
      var rwords = ctx.GetCompoundCheckBuffer();
      int maxwordnum = rwords.Length;

      // add a time limit to handle possible
      // combinatorical explosion of the overlapping words

      var timer = ctx.compoundCheckTimer;
      if (wordnum == 0)
      {
        // set the start time
        timer.Restart();
      }
      else if (timer.ElapsedMilliseconds > TIMELIMIT)
        timer.IsExpired = true;

      setcminmax(out cmin, out cmax, word);

      st = ctx.PopBuffer(len);
      try
      {
        word.CopyTo(0, st, 0, len);
        stsize = len;

        for (int i = cmin; i < cmax; ++i)
        {
          words = oldwords;
          int onlycpdrule = words != null ? 1 : 0;

          do
          {  // onlycpdrule loop

            oldnumsyllable = numsyllable;
            oldwordnum = wordnum;
            checked_prefix = false;

            do
            {  // simplified checkcompoundpattern loop

              if (timer.IsExpired)
                return null;

              if (scpd > 0)
              {
                for (; scpd <= checkcpdtable.Count &&
                       (string.IsNullOrEmpty(checkcpdtable[scpd - 1].pattern3) ||
                        i >= word.Length ||
                        string.CompareOrdinal(word, i, checkcpdtable[scpd - 1].pattern3, 0, checkcpdtable[scpd - 1].pattern3.Length) != 0);
                     scpd++)
                  ;

                if (scpd > checkcpdtable.Count)
                  break;  // break simplified checkcompoundpattern loop

                int newlen = checkcpdtable[scpd - 1].pattern.Length + checkcpdtable[scpd - 1].pattern2.Length + word.Length - checkcpdtable[scpd - 1].pattern3.Length;
                if (newlen > st.Length) Array.Resize(ref st, newlen);

                checkcpdtable[scpd - 1].pattern.CopyTo(0, st, i, checkcpdtable[scpd - 1].pattern.Length);
                soldi = i;
                i += checkcpdtable[scpd - 1].pattern.Length;
                checkcpdtable[scpd - 1].pattern2.CopyTo(0, st, i, checkcpdtable[scpd - 1].pattern2.Length);
                word.CopyTo(soldi + checkcpdtable[scpd - 1].pattern3.Length, st, i + checkcpdtable[scpd - 1].pattern2.Length, word.Length - soldi - checkcpdtable[scpd - 1].pattern3.Length);
                if (newlen > stsize) stsize = newlen;

                oldlen = len;
                len += checkcpdtable[scpd - 1].pattern.Length +
                       checkcpdtable[scpd - 1].pattern2.Length -
                       checkcpdtable[scpd - 1].pattern3.Length;
                oldcmin = cmin;
                oldcmax = cmax;
                setcminmax(out cmin, out cmax, len);

                cmax = len - cpdmin + 1;
              }

              if (i >= stsize)
                return null;

              ch = st[i];
              st[i] = '\0';

              ctx.sfx = null;
              ctx.pfx = null;

              // FIRST WORD

              affixed = true;
              rv = lookup(st, 0, i);  // perhaps without prefix

              // forbid dictionary stems with COMPOUNDFORBIDFLAG in
              // compound words, overriding the effect of COMPOUNDPERMITFLAG
              if (rv != null && compoundforbidflag != 0 &&
                      TESTAFF(rv.astr, compoundforbidflag) && !hu_mov_rule)
              {
                bool would_continue = onlycpdrule == 0 && simplifiedcpd;
                if (scpd == 0 && would_continue)
                {
                  // given the while conditions that continue jumps to, this situation
                  // never ends
                  HUNSPELL_WARNING(false, Properties.Resources.InfiniteLoop);
                  break;
                }

                if (scpd > 0 && would_continue)
                {
                  // under these conditions we loop again, but the assumption above
                  // appears to be that cmin and cmax are the original values they
                  // had in the outside loop
                  cmin = oldcmin;
                  cmax = oldcmax;
                }
                continue;
              }

              // search homonym with compound flag
              while (rv != null && !hu_mov_rule &&
                     ((needaffix != 0 && TESTAFF(rv.astr, needaffix)) ||
                      !((compoundflag != 0 && words == null && onlycpdrule == 0 &&
                         TESTAFF(rv.astr, compoundflag)) ||
                        (compoundbegin != 0 && wordnum == 0 && onlycpdrule == 0 &&
                         TESTAFF(rv.astr, compoundbegin)) ||
                        (compoundmiddle != 0 && wordnum != 0 && words == null && onlycpdrule == 0 &&
                         TESTAFF(rv.astr, compoundmiddle)) ||
                        (defcpdtable.Count > 0 && onlycpdrule != 0 &&
                         ((words == null && wordnum == 0 &&
                           defcpd_check(ctx, ref words, wnum, rv, rwords, false)) ||
                          (words != null &&
                           defcpd_check(ctx, ref words, wnum, rv, rwords, false))))) ||
                      (scpd != 0 && checkcpdtable[scpd - 1].cond != 0 &&
                       !TESTAFF(rv.astr, checkcpdtable[scpd - 1].cond))))
              {
                rv = rv.next_homonym;
              }

              if (rv != null)
                affixed = false;

              if (rv == null)
              {
                if (onlycpdrule != 0)
                  break;
                if (compoundflag != 0 &&
                    (rv = prefix_check(st, 0, i,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                        compoundflag)) == null)
                {
                  if (((rv = suffix_check(
                            st, 0, i, 0, null, 0, compoundflag,
                            hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                       (compoundmoresuffixes &&
                        (rv = suffix_check_twosfx(st, 0, i, 0, null, compoundflag)) != null)) &&
                      !hu_mov_rule && ctx.sfx.getCont() != null &&
                      ((compoundforbidflag != 0 &&
                        TESTAFF(ctx.sfx.getCont(), compoundforbidflag)) ||
                       (compoundend != 0 &&
                        TESTAFF(ctx.sfx.getCont(), compoundend))))
                  {
                    rv = null;
                  }
                }

                if (rv != null ||
                    (((wordnum == 0) && compoundbegin != 0 &&
                      ((rv = suffix_check(
                            st, 0, i, 0, null, 0, compoundbegin,
                            hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                       (compoundmoresuffixes &&
                        (rv = suffix_check_twosfx(
                             st, 0, i, 0, null,
                             compoundbegin)) != null) ||  // twofold suffixes + compound
                       (rv = prefix_check(st, 0, i,
                                          hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                          compoundbegin)) != null)) ||
                     ((wordnum > 0) && compoundmiddle != 0 &&
                      ((rv = suffix_check(
                            st, 0, i, 0, null, 0, compoundmiddle,
                            hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                       (compoundmoresuffixes &&
                        (rv = suffix_check_twosfx(
                             st, 0, i, 0, null,
                             compoundmiddle)) != null) ||  // twofold suffixes + compound
                       (rv = prefix_check(st, 0, i,
                                          hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                          compoundmiddle)) != null))))
                  checked_prefix = true;
                // else check forbiddenwords and needaffix
              }
              else if (rv.astr != null && (TESTAFF(rv.astr, forbiddenword) ||
                                      needaffix != 0 && TESTAFF(rv.astr, needaffix) ||
                                      TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
                                      (is_sug && nosuggest != 0 &&
                                       TESTAFF(rv.astr, nosuggest))))
              {
                // continue;
                st[i] = ch;
                break;
              }

              // check non_compound flag in suffix and prefix
              if (rv != null && !hu_mov_rule &&
                  ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                    compoundforbidflag != 0 && TESTAFF(ctx.pfx.getCont(), compoundforbidflag)) ||
                   (ctx.sfx != null && ctx.sfx.getCont() != null &&
                    compoundforbidflag != 0 && TESTAFF(ctx.sfx.getCont(), compoundforbidflag))))
              {
                rv = null;
              }

              // check compoundend flag in suffix and prefix
              if (rv != null && !checked_prefix && compoundend != 0 && !hu_mov_rule &&
                  ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                    TESTAFF(ctx.pfx.getCont(), compoundend)) ||
                   (ctx.sfx != null && ctx.sfx.getCont() != null &&
                    TESTAFF(ctx.sfx.getCont(), compoundend))))
              {
                rv = null;
              }

              // check compoundmiddle flag in suffix and prefix
              if (rv != null && !checked_prefix && (wordnum == 0) && compoundmiddle != 0 &&
                  !hu_mov_rule &&
                  ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                    TESTAFF(ctx.pfx.getCont(), compoundmiddle)) ||
                   (ctx.sfx != null && ctx.sfx.getCont() != null &&
                    TESTAFF(ctx.sfx.getCont(), compoundmiddle))))
              {
                rv = null;
              }

              // check forbiddenwords
              if (rv != null && rv.astr != null &&
                  (TESTAFF(rv.astr, forbiddenword) ||
                   TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
                   (is_sug && nosuggest != 0 && TESTAFF(rv.astr, nosuggest))))
              {
                return null;
              }

              // increment word number, if the second root has a compoundroot flag
              if (rv != null && compoundroot != 0 &&
                  TESTAFF(rv.astr, compoundroot))
              {
                wordnum++;
              }

              // first word is acceptable in compound words?
              if ((rv != null &&
                   (checked_prefix || (words != null && words[wnum] != null) ||
                    (compoundflag != 0 && TESTAFF(rv.astr, compoundflag)) ||
                    ((oldwordnum == 0) && compoundbegin != 0 &&
                     TESTAFF(rv.astr, compoundbegin)) ||
                    ((oldwordnum > 0) && compoundmiddle != 0 &&
                     TESTAFF(rv.astr, compoundmiddle))

                    // LANG_hu section: spec. Hungarian rule
                    || ((langnum == LANG.hu) && hu_mov_rule &&
                        (TESTAFF(
                             rv.astr, 'F') ||  // XXX hardwired Hungarian dictionary codes
                         TESTAFF(rv.astr, 'G') ||
                         TESTAFF(rv.astr, 'H')))
                    // END of LANG_hu section
                    ) &&
                   (
                       // test CHECKCOMPOUNDPATTERN conditions
                       scpd == 0 || checkcpdtable[scpd - 1].cond == 0 ||
                       TESTAFF(rv.astr, checkcpdtable[scpd - 1].cond)) &&
                   !((checkcompoundtriple && scpd == 0 &&
                      words == null && i < word.Length && // test triple letters
                      (word[i - 1] == word[i]) &&
                      (((i > 1) && (word[i - 1] == word[i - 2])) ||
                       ((i + 1 < word.Length && word[i - 1] == word[i + 1]))  // may be word[i+1] == '\0'
                       )) ||
                     (checkcompoundcase && scpd == 0 && words == null && i < word.Length &&
                      cpdcase_check(word, i))))
                  // LANG_hu section: spec. Hungarian rule
                  || (rv == null && (langnum == LANG.hu) && hu_mov_rule &&
                      (rv = affix_check(st, 0, i)) != null &&
                      (ctx.sfx != null && ctx.sfx.getCont() != null &&
                       (  // XXX hardwired Hungarian dic. codes
                           TESTAFF(ctx.sfx.getCont(), (ushort)'x') ||
                           TESTAFF(ctx.sfx.getCont(), (ushort)'%')))))
              {  // first word is ok condition

                // LANG_hu section: spec. Hungarian rule
                if (langnum == LANG.hu)
                {
                  // calculate syllable number of the word
                  numsyllable += get_syllable(st, 0, i);
                  // + 1 word, if syllable number of the prefix > 1 (hungarian
                  // convention)
                  if (ctx.pfx != null && get_syllable(ctx.pfx.getKey()) > 1)
                    wordnum++;
                }
                // END of LANG_hu section

                // NEXT WORD(S)
                rv_first = rv;
                st[i] = ch;

                do
                {  // striple loop

                  // check simplifiedtriple
                  if (simplifiedtriple)
                  {
                    if (striple)
                    {
                      checkedstriple = true;
                      i--;  // check "fahrt" instead of "ahrt" in "Schiffahrt"
                    }
                    else if (i > 2 && i <= word.Length && word[i - 1] == word[i - 2])
                      striple = true;
                  }

                  rv = lookup(st, i, stsize - i);  // perhaps without prefix

                  // search homonym with compound flag
                  while (rv != null &&
                         ((needaffix != 0 && TESTAFF(rv.astr, needaffix)) ||
                          !((compoundflag != 0 && words == null &&
                             TESTAFF(rv.astr, compoundflag)) ||
                            (compoundend != 0 && words == null &&
                             TESTAFF(rv.astr, compoundend)) ||
                            (defcpdtable.Count > 0 && words != null &&
                             defcpd_check(ctx, ref words, wnum + 1, rv, null, true))) ||
                          (scpd != 0 && checkcpdtable[scpd - 1].cond2 != 0 &&
                           !TESTAFF(rv.astr, checkcpdtable[scpd - 1].cond2))))
                  {
                    rv = rv.next_homonym;
                  }

                  // check FORCEUCASE
                  if (rv != null && forceucase != 0 &&
                      TESTAFF(rv.astr, forceucase) &&
                      info != null && (info & SPELL.ORIGCAP) == 0)
                    rv = null;

                  if (rv != null && words != null && words[wnum + 1] != null)
                    return rv_first;

                  oldnumsyllable2 = numsyllable;
                  oldwordnum2 = wordnum;

                  // LANG_hu section: spec. Hungarian rule, XXX hardwired dictionary
                  // code
                  if (rv != null && (langnum == LANG.hu) &&
                      (TESTAFF(rv.astr, 'I')) &&
                      !(TESTAFF(rv.astr, 'J')))
                  {
                    numsyllable--;
                  }
                  // END of LANG_hu section

                  // increment word number, if the second root has a compoundroot flag
                  if (rv != null && compoundroot != 0 &&
                      (TESTAFF(rv.astr, compoundroot)))
                  {
                    wordnum++;
                  }

                  // check forbiddenwords
                  if (rv != null && rv.astr != null &&
                      (TESTAFF(rv.astr, forbiddenword) ||
                       TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
                       (is_sug && nosuggest != 0 &&
                        TESTAFF(rv.astr, nosuggest))))
                    return null;

                  // second word is acceptable, as a root?
                  // hungarian conventions: compounding is acceptable,
                  // when compound forms consist of 2 words, or if more,
                  // then the syllable number of root words must be 6, or lesser.

                  if (rv != null &&
                      ((compoundflag != 0 && TESTAFF(rv.astr, compoundflag)) ||
                       (compoundend != 0 && TESTAFF(rv.astr, compoundend))) &&
                      (((cpdwordmax == -1) || (wordnum + 1 < cpdwordmax)) ||
                       ((cpdmaxsyllable != 0) &&
                        (numsyllable + get_syllable(rv.word) <=
                         cpdmaxsyllable))) &&
                      (
                          // test CHECKCOMPOUNDPATTERN
                          checkcpdtable.Count == 0 || scpd != 0 ||
                          (i < word.Length && !cpdpat_check(word, i, rv_first, rv, false))) &&
                      ((!checkcompounddup || (rv != rv_first)))
                      // test CHECKCOMPOUNDPATTERN conditions
                      &&
                      (scpd == 0 || checkcpdtable[scpd - 1].cond2 == 0 ||
                       TESTAFF(rv.astr, checkcpdtable[scpd - 1].cond2)))
                  {
                    // forbid compound word, if it is a non-compound word with typical
                    // fault
                    if ((checkcompoundrep && cpdrep_check(ctx, word, len)) ||
                            cpdwordpair_check(ctx, word, len))
                      return null;
                    return rv_first;
                  }

                  numsyllable = oldnumsyllable2;
                  wordnum = oldwordnum2;

                  // perhaps second word has prefix or/and suffix
                  ctx.sfx = null;
                  ctx.sfxflag = 0;
                  rv = (compoundflag != 0 && onlycpdrule == 0 && i < word.Length)
                           ? affix_check(word, i, word.Length - i, compoundflag,
                                         IN_CPD.END)
                           : null;
                  if (rv == null && compoundend != 0 && onlycpdrule == 0)
                  {
                    ctx.sfx = null;
                    ctx.pfx = null;
                    if (i < word.Length)
                      rv = affix_check(word, i, word.Length - i, compoundend, IN_CPD.END);
                  }

                  if (rv == null && defcpdtable.Count > 0 && words != null)
                  {
                    if (i < word.Length)
                      rv = affix_check(word, i, word.Length - i, 0, IN_CPD.END);
                    if (rv != null && defcpd_check(ctx, ref words, wnum + 1, rv, null, true))
                      return rv_first;
                    rv = null;
                  }

                  // test CHECKCOMPOUNDPATTERN conditions (allowed forms)
                  if (rv != null &&
                      !(scpd == 0 || checkcpdtable[scpd - 1].cond2 == 0 ||
                        TESTAFF(rv.astr, checkcpdtable[scpd - 1].cond2)))
                    rv = null;

                  // test CHECKCOMPOUNDPATTERN conditions (forbidden compounds)
                  if (rv != null && checkcpdtable.Count > 0 && scpd == 0 &&
                      cpdpat_check(word, i, rv_first, rv, affixed))
                    rv = null;

                  // check non_compound flag in suffix and prefix
                  if (rv != null && ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                                compoundforbidflag != 0 && TESTAFF(ctx.pfx.getCont(), compoundforbidflag)) ||
                               (ctx.sfx != null && ctx.sfx.getCont() != null &&
                                compoundforbidflag != 0 && TESTAFF(ctx.sfx.getCont(), compoundforbidflag))))
                  {
                    rv = null;
                  }

                  // check FORCEUCASE
                  if (rv != null && forceucase != 0 &&
                      TESTAFF(rv.astr, forceucase) &&
                      !(info != null && (info & SPELL.ORIGCAP) != 0))
                    rv = null;

                  // check forbiddenwords
                  if (rv != null && rv.astr != null &&
                      (TESTAFF(rv.astr, forbiddenword) ||
                       TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
                       (is_sug && nosuggest != 0 &&
                        TESTAFF(rv.astr, nosuggest))))
                    return null;

                  // calculate syllable number of prefix.
                  // hungarian convention: when syllable number of prefix is more,
                  // than 1, the prefix+word counts as two words.

                  if (langnum == LANG.hu)
                  {
                    if (i < word.Length)
                    {
                      // calculate syllable number of the word
                      numsyllable += get_syllable(word, i);
                    }

                    // - affix syllable num.
                    // XXX only second suffix (inflections, not derivations)
                    if (ctx.sfxappnd != null)
                    {
                      int n = ctx.sfxappnd.Length;
                      var tmp = ctx.PeekBuffer(n);
                      ctx.sfxappnd.CopyTo(0, tmp, 0, n);
                      Array.Reverse(tmp, 0, n);
                      numsyllable -= get_syllable(tmp, 0, n) + ctx.sfxextra;
                    }
                    else
                    {
                      numsyllable -= ctx.sfxextra;
                    }

                    // + 1 word, if syllable number of the prefix > 1 (hungarian
                    // convention)
                    if (ctx.pfx != null && (get_syllable(ctx.pfx.getKey()) > 1))
                      wordnum++;

                    // increment syllable num, if last word has a SYLLABLENUM flag
                    // and the suffix is beginning `s'

                    if (!string.IsNullOrEmpty(cpdsyllablenum))
                    {
                      switch (ctx.sfxflag)
                      {
                        case 'c':
                          {
                            numsyllable += 2;
                            break;
                          }
                        case 'J':
                          {
                            numsyllable += 1;
                            break;
                          }
                        case 'I':
                          {
                            if (rv != null && TESTAFF(rv.astr, 'J'))
                              numsyllable += 1;
                            break;
                          }
                      }
                    }
                  }

                  // increment word number, if the second word has a compoundroot flag
                  if (rv != null && compoundroot != 0 &&
                      (TESTAFF(rv.astr, compoundroot)))
                  {
                    wordnum++;
                  }
                  // second word is acceptable, as a word with prefix or/and suffix?
                  // hungarian conventions: compounding is acceptable,
                  // when compound forms consist 2 word, otherwise
                  // the syllable number of root words is 6, or lesser.
                  if (rv != null &&
                      (((cpdwordmax == -1) || (wordnum + 1 < cpdwordmax)) ||
                       ((cpdmaxsyllable != 0) && (numsyllable <= cpdmaxsyllable))) &&
                      ((!checkcompounddup || (rv != rv_first))))
                  {
                    // forbid compound word, if it is a non-compound word with typical
                    // fault
                    if ((checkcompoundrep && cpdrep_check(ctx, word, len)) ||
                            cpdwordpair_check(ctx, word, len))
                      return null;
                    return rv_first;
                  }

                  numsyllable = oldnumsyllable2;
                  wordnum = oldwordnum2;

                  // perhaps second word is a compound word (recursive call)
                  // (only if SPELL_COMPOUND_2 is not set and maxwordnum is not exceeded)
                  if (info == null || (info & SPELL.COMPOUND_2) == 0 && wordnum + 2 < maxwordnum)
                  {
                    rv = compound_check(new string(st, i, len - i), wordnum + 1,
                                        numsyllable, wnum + 1, words, false,
                                        is_sug, info);

                    if (rv != null && checkcpdtable.Count > 0 && i < word.Length &&
                        ((scpd == 0 &&
                          cpdpat_check(word, i, rv_first, rv, affixed)) ||
                         (scpd != 0 &&
                          !cpdpat_check(word, i, rv_first, rv, affixed))))
                      rv = null;
                  }
                  else
                  {
                    rv = null;
                  }
                  if (rv != null)
                  {
                    // forbid compound word, if it is a non-compound word with typical
                    // fault, or a dictionary word pair

                    if (cpdwordpair_check(ctx, word, len))
                      return null;

                    if (checkcompoundrep || forbiddenword != 0)
                    {

                      if (checkcompoundrep && cpdrep_check(ctx, word, len))
                        return null;

                      // check first part
                      if (i < word.Length && string.CompareOrdinal(word, i, rv.word, 0, rv.word.Length) == 0)
                      {
                        if ((checkcompoundrep && cpdrep_check(ctx, st, i + rv.word.Length)) ||
                            cpdwordpair_check(ctx, st, i + rv.word.Length))
                        {
                          continue;
                        }

                        if (forbiddenword != 0)
                        {
                          hentry rv2 = lookup(word);
                          if (rv2 == null && len <= word.Length)
                            rv2 = affix_check(word, 0, len);
                          if (rv2 != null && rv2.astr != null &&
                              TESTAFF(rv2.astr, forbiddenword))
                          {
                            int l = i + rv.word.Length;
                            if (l <= len && l <= rv2.word.Length)
                              while (--l >= 0)
                                if (rv2.word[l] != st[l]) break;
                            if (l < 0) return null;
                          }
                        }
                      }
                    }
                    return rv_first;
                  }
                } while (striple && !checkedstriple);  // end of striple loop

                if (checkedstriple)
                {
                  i++;
                  checkedstriple = false;
                  striple = false;
                }

              }  // first word is ok condition

              if (soldi != 0)
              {
                i = soldi;
                soldi = 0;
                len = oldlen;
                cmin = oldcmin;
                cmax = oldcmax;
              }
              scpd++;

            } while (onlycpdrule == 0 && simplifiedcpd &&
                     scpd <= checkcpdtable.Count);  // end of simplifiedcpd loop

            scpd = 0;
            wordnum = oldwordnum;
            numsyllable = oldnumsyllable;

            if (soldi != 0)
            {
              i = soldi;
              word.CopyTo(0, st, 0, stsize = word.Length);  // XXX add more optim.
              soldi = 0;
              len = oldlen;
              cmin = oldcmin;
              cmax = oldcmax;
            }
            else
              st[i] = ch;

          } while (defcpdtable.Count > 0 && oldwordnum == 0 &&
                   onlycpdrule++ < 1);  // end of onlycpd loop
        }
      }
      finally
      {
        ctx.PushBuffer(st);
      }
      return null;
    }

    // check if compound word is correctly spelled
    // hu_mov_rule = spec. Hungarian rule (XXX)
    void compound_check_morph(string word,
                              int wordnum,
                              int numsyllable,
                              int wnum,
                              hentry[] words,
                              bool hu_mov_rule,
                              StringBuilder result,
                              StringBuilder partresult)
    {
      var ctx = Context;

      int oldnumsyllable, oldnumsyllable2, oldwordnum, oldwordnum2;
      hentry rv, rv_first;
      string st;
      StringBuilder presult = ctx.PopStringBuilder(), p = null;
      bool affixed;
      bool checked_prefix, ok = false;
      int cmin, cmax;
      hentry[] oldwords = words;

      var rwords = ctx.GetCompoundCheckBuffer();
      int maxwordnum = rwords.Length;

      // add a time limit to handle possible
      // combinatorical explosion of the overlapping words

      var timer = ctx.compoundCheckTimer;
      if (wordnum == 0) {
        // set the start time
        timer.Restart();
      }
      else if (timer.ElapsedMilliseconds > TIMELIMIT)
        timer.IsExpired = true;

      try
      {
        setcminmax(out cmin, out cmax, word);

        st = word;

        for (int i = cmin; i < cmax; ++i)
        {
          words = oldwords;
          int onlycpdrule = words != null ? 1 : 0;

          do
          {  // onlycpdrule loop

            if (timer.IsExpired)
              return;

            oldnumsyllable = numsyllable;
            oldwordnum = wordnum;
            checked_prefix = false;

            ctx.sfx = null;

            // FIRST WORD

            affixed = true;

            presult.Length = 0;
            if (partresult != null)
              presult.Append(partresult);

            rv = lookup(st, 0, i);  // perhaps without prefix

            // forbid dictionary stems with COMPOUNDFORBIDFLAG in
            // compound words, overriding the effect of COMPOUNDPERMITFLAG
            if (rv != null && compoundforbidflag != 0 &&
                    TESTAFF(rv.astr, compoundforbidflag) && !hu_mov_rule)
              continue;

            // search homonym with compound flag
            while (rv != null && !hu_mov_rule &&
                   ((needaffix != 0 && TESTAFF(rv.astr, needaffix)) ||
                    !((compoundflag != 0 && words == null && onlycpdrule == 0 &&
                       TESTAFF(rv.astr, compoundflag)) ||
                      (compoundbegin != 0 && wordnum == 0 && onlycpdrule == 0 &&
                       TESTAFF(rv.astr, compoundbegin)) ||
                      (compoundmiddle != 0 && wordnum != 0 && words == null && onlycpdrule == 0 &&
                       TESTAFF(rv.astr, compoundmiddle)) ||
                      (defcpdtable.Count > 0 && onlycpdrule != 0 &&
                       ((words == null && wordnum == 0 &&
                         defcpd_check(ctx, ref words, wnum, rv, rwords, false)) ||
                        (words != null &&
                         defcpd_check(ctx, ref words, wnum, rv, rwords, false)))))))
            {
              rv = rv.next_homonym;
            }


            if (rv != null)
              affixed = false;

            if (rv != null)
            {
              presult.Append(MSEP.FLD);
              presult.Append(MORPH.PART);
              presult.Append(st, 0, i);
              if (!rv.Contains(MORPH.STEM))
              {
                presult.Append(MSEP.FLD);
                presult.Append(MORPH.STEM);
                presult.Append(st, 0, i);
              }
              if (rv.data != null)
              {
                presult.Append(MSEP.FLD);
                presult.Append(rv.data);
              }
            }

            if (rv == null)
            {
              if (compoundflag != 0 &&
                  (rv =
                        prefix_check(st, 0, i, hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                     compoundflag)) == null)
              {
                if (((rv = suffix_check(st, 0, i, 0, null, 0,
                                        compoundflag,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                     (compoundmoresuffixes &&
                      (rv = suffix_check_twosfx(st, 0, i, 0, null, compoundflag)) != null)) &&
                    !hu_mov_rule && ctx.sfx.getCont() != null &&
                    ((compoundforbidflag != 0 &&
                      TESTAFF(ctx.sfx.getCont(), compoundforbidflag)) ||
                     (compoundend != 0 &&
                      TESTAFF(ctx.sfx.getCont(), compoundend))))
                {
                  rv = null;
                }
              }

              if (rv != null ||
                  (((wordnum == 0) && compoundbegin != 0 &&
                    ((rv = suffix_check(st, 0, i, 0, null, 0,
                                        compoundbegin,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                     (compoundmoresuffixes &&
                      (rv = suffix_check_twosfx(
                           st, 0, i, 0, null,
                           compoundbegin)) != null) ||  // twofold suffix+compound
                     (rv = prefix_check(st, 0, i,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                        compoundbegin)) != null)) ||
                   ((wordnum > 0) && compoundmiddle != 0 &&
                    ((rv = suffix_check(st, 0, i, 0, null, 0,
                                        compoundmiddle,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN)) != null ||
                     (compoundmoresuffixes &&
                      (rv = suffix_check_twosfx(
                           st, 0, i, 0, null,
                           compoundmiddle)) != null) ||  // twofold suffix+compound
                     (rv = prefix_check(st, 0, i,
                                        hu_mov_rule ? IN_CPD.OTHER : IN_CPD.BEGIN,
                                        compoundmiddle)) != null))))
              {
                if (p == null) p = ctx.PopStringBuilder(); else p.Length = 0;
                if (compoundflag != 0)
                  affix_check_morph(p, st, 0, i, compoundflag);
                if (p.Length == 0)
                {
                  if ((wordnum == 0) && compoundbegin != 0)
                  {
                    affix_check_morph(p, st, 0, i, compoundbegin);
                  }
                  else if ((wordnum > 0) && compoundmiddle != 0)
                  {
                    affix_check_morph(p, st, 0, i, compoundmiddle);
                  }
                }
                if (p.Length > 0)
                {
                  presult.Append(MSEP.FLD);
                  presult.Append(MORPH.PART);
                  presult.Append(st, 0, i);
                  presult.Append(line_uniq_app(p.ToString()));
                }
                checked_prefix = true;
              }
              // else check forbiddenwords
            }
            else if (rv.astr != null && (TESTAFF(rv.astr, forbiddenword) ||
                                    TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
                                    needaffix != 0 && TESTAFF(rv.astr, needaffix)))
            {
              continue;
            }

            // check non_compound flag in suffix and prefix
            if (rv != null && !hu_mov_rule &&
                ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                  compoundforbidflag != 0 && TESTAFF(ctx.pfx.getCont(), compoundforbidflag)) ||
                 (ctx.sfx != null && ctx.sfx.getCont() != null &&
                  compoundforbidflag != 0 && TESTAFF(ctx.sfx.getCont(), compoundforbidflag))))
            {
              continue;
            }

            // check compoundend flag in suffix and prefix
            if (rv != null && !checked_prefix && compoundend != 0 && !hu_mov_rule &&
                ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                  TESTAFF(ctx.pfx.getCont(), compoundend)) ||
                 (ctx.sfx != null && ctx.sfx.getCont() != null &&
                  TESTAFF(ctx.sfx.getCont(), compoundend))))
            {
              continue;
            }

            // check compoundmiddle flag in suffix and prefix
            if (rv != null && !checked_prefix && (wordnum == 0) && compoundmiddle != 0 &&
                !hu_mov_rule &&
                ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                  TESTAFF(ctx.pfx.getCont(), compoundmiddle)) ||
                 (ctx.sfx != null && ctx.sfx.getCont() != null &&
                  TESTAFF(ctx.sfx.getCont(), compoundmiddle))))
            {
              rv = null;
            }

            // check forbiddenwords
            if (rv != null && rv.astr != null && (TESTAFF(rv.astr, forbiddenword) ||
                                       TESTAFF(rv.astr, ONLYUPCASEFLAG)))
              continue;

            // increment word number, if the second root has a compoundroot flag
            if (rv != null && compoundroot != 0 &&
                (TESTAFF(rv.astr, compoundroot)))
            {
              wordnum++;
            }

            // first word is acceptable in compound words?
            if ((rv != null &&
                 (checked_prefix || (words != null && words[wnum] != null) ||
                  (compoundflag != 0 && TESTAFF(rv.astr, compoundflag)) ||
                  ((oldwordnum == 0) && compoundbegin != 0 &&
                   TESTAFF(rv.astr, compoundbegin)) ||
                  ((oldwordnum > 0) && compoundmiddle != 0 &&
                   TESTAFF(rv.astr, compoundmiddle))
                  // LANG_hu section: spec. Hungarian rule
                  || ((langnum == LANG.hu) &&  // hu_mov_rule
                      hu_mov_rule && (TESTAFF(rv.astr, 'F') ||
                                      TESTAFF(rv.astr, 'G') ||
                                      TESTAFF(rv.astr, 'H')))
                  // END of LANG_hu section
                  ) &&
                 !((checkcompoundtriple && words == null &&  // test triple letters
                    (word[i - 1] == word[i]) &&
                    (((i > 1) && (word[i - 1] == word[i - 2])) ||
                     ((i + 1 < word.Length && word[i - 1] == word[i + 1]))  // may be word[i+1] == '\0'
                     )) ||
                   (
                       // test CHECKCOMPOUNDPATTERN
                       checkcpdtable.Count > 0 && words == null &&
                       cpdpat_check(word, i, rv, null, affixed)) ||
                   (checkcompoundcase && words == null && cpdcase_check(word, i))))
                // LANG_hu section: spec. Hungarian rule
                ||
                (rv == null && (langnum == LANG.hu) && hu_mov_rule &&
                 (rv = affix_check(st, 0, i)) != null &&
                 (ctx.sfx != null && ctx.sfx.getCont() != null &&
                  (TESTAFF(ctx.sfx.getCont(), 'x') ||
                   TESTAFF(ctx.sfx.getCont(), '%'))))
                // END of LANG_hu section
                )
            {
              // LANG_hu section: spec. Hungarian rule
              if (langnum == LANG.hu)
              {
                // calculate syllable number of the word
                numsyllable += get_syllable(st, 0, i);

                // + 1 word, if syllable number of the prefix > 1 (hungarian
                // convention)
                if (ctx.pfx != null && (get_syllable(ctx.pfx.getKey()) > 1))
                  wordnum++;
              }
              // END of LANG_hu section

              // NEXT WORD(S)
              rv_first = rv;
              rv = lookup(word, i);  // perhaps without prefix

              // search homonym with compound flag
              while (rv != null && ((needaffix != 0 && TESTAFF(rv.astr, needaffix)) ||
                              !((compoundflag != 0 && words == null &&
                                 TESTAFF(rv.astr, compoundflag)) ||
                                (compoundend != 0 && words == null &&
                                 TESTAFF(rv.astr, compoundend)) ||
                                (defcpdtable.Count > 0 && words != null &&
                                 defcpd_check(ctx, ref words, wnum + 1, rv, null, true)))))
              {
                rv = rv.next_homonym;
              }

              if (rv != null && words != null && words[wnum + 1] != null)
              {
                result.Append(presult);
                result.Append(MSEP.FLD);
                result.Append(MORPH.PART);
                result.Append(word, i, word.Length - i);
                if (complexprefixes && rv.data != null)
                  result.Append(rv.data);
                if (!rv.Contains(MORPH.STEM))
                {
                  result.Append(MSEP.FLD);
                  result.Append(MORPH.STEM);
                  result.Append(rv.word);
                }
                // store the pointer of the hash entry
                if (!complexprefixes && rv.data != null)
                {
                  result.Append(MSEP.FLD);
                  result.Append(rv.data);
                }
                result.Append(MSEP.REC);
                return;
              }

              oldnumsyllable2 = numsyllable;
              oldwordnum2 = wordnum;

              // LANG_hu section: spec. Hungarian rule
              if (rv != null && (langnum == LANG.hu) &&
                  (TESTAFF(rv.astr, 'I')) &&
                  !(TESTAFF(rv.astr, 'J')))
              {
                numsyllable--;
              }
              // END of LANG_hu section
              // increment word number, if the second root has a compoundroot flag
              if (rv != null && compoundroot != 0 &&
                  (TESTAFF(rv.astr, compoundroot)))
              {
                wordnum++;
              }

              // check forbiddenwords
              if (rv != null && rv.astr != null &&
                  (TESTAFF(rv.astr, forbiddenword) ||
                   TESTAFF(rv.astr, ONLYUPCASEFLAG)))
              {
                continue;
              }

              // second word is acceptable, as a root?
              // hungarian conventions: compounding is acceptable,
              // when compound forms consist of 2 words, or if more,
              // then the syllable number of root words must be 6, or lesser.
              if (rv != null &&
                  ((compoundflag != 0 && TESTAFF(rv.astr, compoundflag)) ||
                   (compoundend != 0 && TESTAFF(rv.astr, compoundend))) &&
                  (((cpdwordmax == -1) || (wordnum + 1 < cpdwordmax)) ||
                   ((cpdmaxsyllable != 0) &&
                    (numsyllable + get_syllable(rv.word) <=
                     cpdmaxsyllable))) &&
                  ((!checkcompounddup || (rv != rv_first))))
              {
                // bad compound word
                result.Append(presult);
                result.Append(MSEP.FLD);
                result.Append(MORPH.PART);
                result.Append(word, i, word.Length - i);

                if (rv.data != null)
                {
                  if (complexprefixes)
                    result.Append(rv.data);
                  if (!rv.Contains(MORPH.STEM))
                  {
                    result.Append(MSEP.FLD);
                    result.Append(MORPH.STEM);
                    result.Append(rv.word);
                  }
                  // store the pointer of the hash entry
                  if (!complexprefixes)
                  {
                    result.Append(MSEP.FLD);
                    result.Append(rv.data);
                  }
                }
                result.Append(MSEP.REC);
                ok = true;
              }

              numsyllable = oldnumsyllable2;
              wordnum = oldwordnum2;

              // perhaps second word has prefix or/and suffix
              ctx.sfx = null;
              ctx.sfxflag = 0;

              if (compoundflag != 0 && onlycpdrule == 0)
                rv = affix_check(word, i, word.Length - i, compoundflag);
              else
                rv = null;

              if (rv == null && compoundend != 0 && onlycpdrule == 0)
              {
                ctx.sfx = null;
                ctx.pfx = null;
                rv = affix_check(word, i, word.Length - i, compoundend);
              }

              if (rv == null && defcpdtable.Count > 0 && words != null)
              {
                rv = affix_check(word, i, word.Length - i, 0, IN_CPD.END);
                if (rv != null && words != null && defcpd_check(ctx, ref words, wnum + 1, rv, null, true))
                {
                  if (p == null) p = ctx.PopStringBuilder(); else p.Length = 0;
                  if (compoundflag != 0)
                    affix_check_morph(p, word, i, word.Length - i, compoundflag);
                  if (p.Length == 0 && compoundend != 0)
                  {
                    affix_check_morph(p, word, i, word.Length - i, compoundend);
                  }
                  result.Append(presult);
                  if (p.Length > 0)
                  {
                    result.Append(MSEP.FLD);
                    result.Append(MORPH.PART);
                    result.Append(word, i, word.Length - i);
                    result.Append(line_uniq_app(p.ToString()));
                  }
                  result.Append(MSEP.REC);
                  ok = true;
                }
              }

              // check non_compound flag in suffix and prefix
              if (rv != null &&
                  ((ctx.pfx != null && ctx.pfx.getCont() != null &&
                    compoundforbidflag != 0 && TESTAFF(ctx.pfx.getCont(), compoundforbidflag)) ||
                   (ctx.sfx != null && ctx.sfx.getCont() != null &&
                    compoundforbidflag != 0 && TESTAFF(ctx.sfx.getCont(), compoundforbidflag))))
              {
                rv = null;
              }

              // check forbiddenwords
              if (rv != null && rv.astr != null &&
                  (TESTAFF(rv.astr, forbiddenword) ||
                   TESTAFF(rv.astr, ONLYUPCASEFLAG)) &&
                  (!(needaffix != 0 && TESTAFF(rv.astr, needaffix))))
              {
                continue;
              }

              if (langnum == LANG.hu)
              {
                // calculate syllable number of the word
                numsyllable += get_syllable(word, i);

                // - affix syllable num.
                // XXX only second suffix (inflections, not derivations)
                if (ctx.sfxappnd != null)
                {
                  numsyllable -= get_syllable(ctx.reverseword(ctx.sfxappnd)) + ctx.sfxextra;
                }
                else
                {
                  numsyllable -= ctx.sfxextra;
                }

                // + 1 word, if syllable number of the prefix > 1 (hungarian
                // convention)
                if (ctx.pfx != null && get_syllable(ctx.pfx.getKey()) > 1)
                  wordnum++;

                // increment syllable num, if last word has a SYLLABLENUM flag
                // and the suffix is beginning `s'

                if (!string.IsNullOrEmpty(cpdsyllablenum))
                {
                  switch (ctx.sfxflag)
                  {
                    case 'c':
                      {
                        numsyllable += 2;
                        break;
                      }
                    case 'J':
                      {
                        numsyllable += 1;
                        break;
                      }
                    case 'I':
                      {
                        if (rv != null && TESTAFF(rv.astr, 'J'))
                          numsyllable += 1;
                        break;
                      }
                  }
                }
              }

              // increment word number, if the second word has a compoundroot flag
              if (rv != null && compoundroot != 0 &&
                  TESTAFF(rv.astr, compoundroot))
              {
                wordnum++;
              }
              // second word is acceptable, as a word with prefix or/and suffix?
              // hungarian conventions: compounding is acceptable,
              // when compound forms consist 2 word, otherwise
              // the syllable number of root words is 6, or lesser.
              if (rv != null &&
                  (((cpdwordmax == -1) || (wordnum + 1 < cpdwordmax)) ||
                   ((cpdmaxsyllable != 0) && (numsyllable <= cpdmaxsyllable))) &&
                  ((!checkcompounddup || (rv != rv_first))))
              {
                if (p == null) p = ctx.PopStringBuilder(); else p.Length = 0;
                if (compoundflag != 0)
                  affix_check_morph(p, word, i, word.Length - i, compoundflag);
                if (p.Length == 0 && compoundend != 0)
                {
                  affix_check_morph(p, word, i, word.Length - i, compoundend);
                }
                result.Append(presult);
                if (p.Length > 0)
                {
                  result.Append(MSEP.FLD);
                  result.Append(MORPH.PART);
                  result.Append(word, i, word.Length - i);
                  result.Append(MSEP.FLD);
                  result.Append(line_uniq_app(p.ToString()));
                }
                result.Append(MSEP.REC);
                ok = true;
              }

              numsyllable = oldnumsyllable2;
              wordnum = oldwordnum2;

              // perhaps second word is a compound word (recursive call)
              if ((wordnum + 2 < maxwordnum) && !ok)
              {
                compound_check_morph(word.Substring(i), wordnum + 1,
                                     numsyllable, wnum + 1, words, false,
                                     result, presult);
              }
              else
              {
                rv = null;
              }
            }
            wordnum = oldwordnum;
            numsyllable = oldnumsyllable;

          } while (defcpdtable.Count > 0 && oldwordnum == 0 &&
                   onlycpdrule++ < 1);  // end of onlycpd loop
        }
      }
      finally
      {
        if (p != null) ctx.PushStringBuilder(p);
        ctx.PushStringBuilder(presult);
      }
    }


    bool isRevSubset(string s1, string s2,
                                     int end_of_s2,
                                     int len) {
      int i1 = 0;
      while (len > 0 && i1 < s1.Length && (s1[i1] == s2[end_of_s2] || s1[i1] == '.')) {
        i1++;
        end_of_s2--;
        len--;
      }
      return i1 == s1.Length;
    }

    bool isRevSubset(string s1, IEnumerable<char> s2_,
                                     int end_of_s2,
                                     int len)
    {
      switch (s2_)
      {
        case string s: return isRevSubset(s1, s, end_of_s2, len);
        case char[] s2:
          int i1 = 0;
          while (len > 0 && i1 < s1.Length && (s1[i1] == s2[end_of_s2] || s1[i1] == '.'))
          {
            i1++;
            end_of_s2--;
            len--;
          }
          return i1 == s1.Length;
        default: throw new NotSupportedException();
      }
    }

    // check word for suffixes
    internal hentry suffix_check(IEnumerable<char> word,
                               int start,
                               int len,
                               ae sfxopts,
                               PfxEntry ppfx,
                               ushort cclass = 0,
                               ushort needflag = 0,
                               IN_CPD in_compound = IN_CPD.NOT)
    {
      var ctx = Context;

      hentry rv = null;
      PfxEntry ep = ppfx;

      // first handle the special case of 0 length suffixes
      for (var se = sStart0; se != null; se = se.getNext())
      {
        if (cclass == 0 || se.getCont() != null)
        {
          // suffixes are not allowed in beginning of compounds
          if ((in_compound != IN_CPD.BEGIN ||  // && !cclass
                                               // except when signed with compoundpermitflag flag
               (se.getCont() != null && compoundpermitflag != 0 &&
                TESTAFF(se.getCont(), compoundpermitflag))) &&
              (circumfix == 0 ||
               // no circumfix flag in prefix and suffix
               ((ppfx == null || ep.getCont() == null ||
                 !TESTAFF(ep.getCont(), circumfix)) &&
                (se.getCont() == null ||
                 !(TESTAFF(se.getCont(), circumfix)))) ||
               // circumfix flag in prefix AND suffix
               ((ppfx != null && ep.getCont() != null &&
                 TESTAFF(ep.getCont(), circumfix)) &&
                (se.getCont() != null &&
                 (TESTAFF(se.getCont(), circumfix))))) &&
              // fogemorpheme
              (in_compound != IN_CPD.NOT ||
               !(se.getCont() != null &&
                 (onlyincompound != 0 && TESTAFF(se.getCont(), onlyincompound)))) &&
              // needaffix on prefix or first suffix
              (cclass != 0 ||
               !(se.getCont() != null &&
                 needaffix != 0 && TESTAFF(se.getCont(), needaffix)) ||
               (ppfx != null &&
                !(ep.getCont() != null &&
                  needaffix != 0 && TESTAFF(ep.getCont(), needaffix)))))
          {
            rv = se.checkword(this, word, start, len, sfxopts, ppfx,
                               cclass, needflag,
                               (in_compound != IN_CPD.NOT ? (ushort)0 : onlyincompound));
            if (rv != null)
            {
              ctx.sfx = se;  // BUG: sfx not stateless
              return rv;
            }
          }
        }
      }

      // now handle the general case
      if (len == 0)
        return null;  // FULLSTRIP

      char sp = word.At(start + len - 1);
      if (sStart.TryGetValue(sp, out var sptr))
        do
        {
          if (isRevSubset(sptr.getKey(), word, start + len - 1, len))
          {
            // suffixes are not allowed in beginning of compounds
            if ((in_compound != IN_CPD.BEGIN ||  // && !cclass
                                                 // except when signed with compoundpermitflag flag
                 (sptr.getCont() != null && compoundpermitflag != 0 &&
                  TESTAFF(sptr.getCont(), compoundpermitflag))) &&
                (circumfix == 0 ||
                 // no circumfix flag in prefix and suffix
                 ((ppfx == null || ep.getCont() == null ||
                   !TESTAFF(ep.getCont(), circumfix)) &&
                  (sptr.getCont() == null ||
                   !(TESTAFF(sptr.getCont(), circumfix)))) ||
                 // circumfix flag in prefix AND suffix
                 ((ppfx != null && ep.getCont() != null &&
                   TESTAFF(ep.getCont(), circumfix)) &&
                  (sptr.getCont() != null &&
                   (TESTAFF(sptr.getCont(), circumfix))))) &&
                // fogemorpheme
                (in_compound != IN_CPD.NOT ||
                 !(sptr.getCont() != null && onlyincompound != 0 && TESTAFF(sptr.getCont(), onlyincompound))) &&
                // needaffix on prefix or first suffix
                (cclass != 0 ||
                 !(sptr.getCont() != null &&
                   needaffix != 0 && TESTAFF(sptr.getCont(), needaffix)) ||
                 (ppfx != null &&
                  !(ep.getCont() != null &&
                    needaffix != 0 && TESTAFF(ep.getCont(), needaffix)))))
              if (in_compound != IN_CPD.END || ppfx != null ||
                  !(sptr.getCont() != null &&
                    onlyincompound != 0 && TESTAFF(sptr.getCont(), onlyincompound)))
              {
                rv = sptr.checkword(this, word, start, len, sfxopts, ppfx,
                                     cclass, needflag,
                                     (in_compound != IN_CPD.NOT ? (ushort)0 : onlyincompound));
                if (rv != null)
                {
                  ctx.sfx = sptr;                 // BUG: sfx not stateless
                  ctx.sfxflag = sptr.getFlag();  // BUG: sfxflag not stateless
                  if (sptr.getCont() == null)
                    ctx.sfxappnd = sptr.getKey();  // BUG: sfxappnd not stateless
                                               // LANG_hu section: spec. Hungarian rule
                  else if (langnum == LANG.hu && sptr.getKeyLen() != 0 &&
                           sptr.getKey()[0] == 'i' && sptr.getKey()[1] != 'y' &&
                           sptr.getKey()[1] != 't')
                  {
                    ctx.sfxextra = 1;
                  }
                  // END of LANG_hu section
                  return rv;
                }
              }
            sptr = sptr.getNextEQ();
          }
          else
          {
            sptr = sptr.getNextNE();
          }
        } while (sptr != null);

      return null;
    }

    // check word for two-level suffixes
    internal hentry suffix_check_twosfx(IEnumerable<char> word,
                                        int start,
                                        int len,
                                        ae sfxopts,
                                        PfxEntry ppfx,
                                        ushort needflag = 0) {
      var ctx = Context;

      hentry rv = null;

      // first handle the special case of 0 length suffixes
      for (var se = sStart0; se != null; se = se.getNext())
      {
        if (contclasses[se.getFlag()])
        {
          rv = se.check_twosfx(this, word, start, len, sfxopts, ppfx, needflag);
          if (rv != null)
            return rv;
        }
      }

      // now handle the general case
      if (len == 0)
        return null;  // FULLSTRIP

      char sp = word.At(start + len - 1);

      if (sStart.TryGetValue(sp, out var sptr))
        do
        {
          if (isRevSubset(sptr.getKey(), word, start + len - 1, len))
          {
            if (contclasses[sptr.getFlag()])
            {
              rv = sptr.check_twosfx(this, word, start, len, sfxopts, ppfx, needflag);
              if (rv != null)
              {
                ctx.sfxflag = sptr.getFlag();  // BUG: sfxflag not stateless
                if (sptr.getCont() == null)
                  ctx.sfxappnd = sptr.getKey();  // BUG: sfxappnd not stateless
                return rv;
              }
            }
            sptr = sptr.getNextEQ();
          }
          else
          {
            sptr = sptr.getNextNE();
          }
        } while (sptr != null);

      return null;
    }

    // check word for two-level suffixes and morph
    internal void suffix_check_twosfx_morph(StringBuilder result,
                                          string word,
                                          int start,
                                          int len,
                                          ae sfxopts,
                                          PfxEntry ppfx,
                                          ushort needflag = 0)
    {
      // now handle the general case
      if (len == 0)
        return;  // FULLSTRIP

      var ctx = Context;
      StringBuilder st = null;

      // first handle the special case of 0 length suffixes
      for (var se = sStart0; se != null; se = se.getNext())
      {
        if (contclasses[se.getFlag()])
        {
          if (st == null) st = ctx.PopStringBuilder(); else st.Length = 0;
          se.check_twosfx_morph(this, st, word, start, len, sfxopts, ppfx, needflag);
          if (st != null && st.Length > 0)
          {
            if (ppfx != null)
            {
              if (ppfx.getMorph() != null)
              {
                result.Append(ppfx.getMorph());
                result.Append(MSEP.FLD);
              }
              else
                debugflag(result, ppfx.getFlag());
            }
            result.Append(st);
            if (se.getMorph() != null)
            {
              result.Append(MSEP.FLD);
              result.Append(se.getMorph());
            }
            else
              debugflag(result, se.getFlag());
            result.Append(MSEP.REC);
          }
        }
      }

      char sp = word[start + len - 1];
      if (sStart.TryGetValue(sp, out var sptr))
        do
        {
          if (isRevSubset(sptr.getKey(), word, start + len - 1, len))
          {
            if (contclasses[sptr.getFlag()])
            {
              if (st == null) st = ctx.PopStringBuilder(); else st.Length = 0;
              sptr.check_twosfx_morph(this, st, word, start, len, sfxopts, ppfx, needflag);
              if (st != null && st.Length > 0)
              {
                ctx.sfxflag = sptr.getFlag();  // BUG: sfxflag not stateless
                if (sptr.getCont() == null)
                  ctx.sfxappnd = sptr.getKey();  // BUG: sfxappnd not stateless
                result.Append(st);

                if (sptr.getMorph() != null)
                {
                  result.Append(MSEP.FLD);
                  result.Append(sptr.getMorph());
                }
                else
                  debugflag(result, sptr.getFlag());
                result.Append(MSEP.REC);
              }
            }
            sptr = sptr.getNextEQ();
          }
          else
          {
            sptr = sptr.getNextNE();
          }
        } while (sptr != null);

      if (st != null) ctx.PushStringBuilder(st);
    }

    internal void suffix_check_morph(StringBuilder result,
                                   string word,
                                   int start,
                                   int len,
                                   ae sfxopts,
                                   PfxEntry ppfx,
                                   ushort cclass = 0,
                                   ushort needflag = 0,
                                   IN_CPD in_compound = IN_CPD.NOT)
    {
      var prelen = result.Length;
      hentry rv = null;

      PfxEntry ep = ppfx;

      // first handle the special case of 0 length suffixes
      for (var se = sStart0; se != null; se = se.getNext())
      {
        if (cclass == 0 || se.getCont() != null)
        {
          // suffixes are not allowed in beginning of compounds
          if (((in_compound != IN_CPD.BEGIN ||  // && !cclass
                                                // except when signed with compoundpermitflag flag
                (se.getCont() != null && compoundpermitflag != 0 &&
                 TESTAFF(se.getCont(), compoundpermitflag))) &&
               (circumfix == 0 ||
                // no circumfix flag in prefix and suffix
                ((ppfx == null || ep.getCont() == null ||
                  !TESTAFF(ep.getCont(), circumfix)) &&
                 (se.getCont() == null ||
                  !(TESTAFF(se.getCont(), circumfix)))) ||
                // circumfix flag in prefix AND suffix
                ((ppfx != null && ep.getCont() != null &&
                  TESTAFF(ep.getCont(), circumfix)) &&
                 (se.getCont() != null &&
                  (TESTAFF(se.getCont(), circumfix))))) &&
               // fogemorpheme
               (in_compound != IN_CPD.NOT ||
                !((se.getCont() != null &&
                   (onlyincompound != 0 && TESTAFF(se.getCont(), onlyincompound))))) &&
               // needaffix on prefix or first suffix
               (cclass != 0 ||
                !(se.getCont() != null &&
                  needaffix != 0 && TESTAFF(se.getCont(), needaffix)) ||
                (ppfx != null &&
                 !(ep.getCont() != null &&
                   needaffix != 0 && TESTAFF(ep.getCont(), needaffix))))))
            rv = se.checkword(this, word, start, len, sfxopts, ppfx, cclass,
                               needflag, 0);
          while (rv != null)
          {
            if (ppfx != null)
            {
              if (ppfx.getMorph() != null)
              {
                result.Append(ppfx.getMorph());
                result.Append(MSEP.FLD);
              }
              else
                debugflag(result, ppfx.getFlag());
            }
            if (complexprefixes && rv.data != null)
              result.Append(rv.data);
            if (!rv.Contains(MORPH.STEM))
            {
              result.Append(MSEP.FLD);
              result.Append(MORPH.STEM);
              result.Append(rv.word);
            }

            if (!complexprefixes && rv.data != null)
            {
              result.Append(MSEP.FLD);
              result.Append(rv.data);
            }
            if (se.getMorph() != null)
            {
              result.Append(MSEP.FLD);
              result.Append(se.getMorph());
            }
            else
              debugflag(result, se.getFlag());
            result.Append(MSEP.REC);
            rv = se.get_next_homonym(rv, sfxopts, ppfx, cclass, needflag);
          }
        }
      }

      // now handle the general case
      if (len == 0)
      {
        result.Length = prelen;
        return;  // FULLSTRIP
      }


      char sp = word[start + len - 1];
      if (sStart.TryGetValue(sp, out var sptr))
        do
        {
          if (isRevSubset(sptr.getKey(), word, start + len - 1, len))
          {
            // suffixes are not allowed in beginning of compounds
            if (((in_compound != IN_CPD.BEGIN ||  // && !cclass
                                                      // except when signed with compoundpermitflag flag
                  (sptr.getCont() != null && compoundpermitflag != 0 &&
                   TESTAFF(sptr.getCont(), compoundpermitflag))) &&
                 (circumfix == 0 ||
                  // no circumfix flag in prefix and suffix
                  ((ppfx == null || ep.getCont() == null ||
                    !TESTAFF(ep.getCont(), circumfix)) &&
                   (sptr.getCont() == null ||
                    !(TESTAFF(sptr.getCont(), circumfix)))) ||
                  // circumfix flag in prefix AND suffix
                  ((ppfx != null && ep.getCont() != null &&
                    TESTAFF(ep.getCont(), circumfix)) &&
                   (sptr.getCont() != null &&
                    (TESTAFF(sptr.getCont(), circumfix))))) &&
                 // fogemorpheme
                 (in_compound != IN_CPD.NOT ||
                  !((sptr.getCont() != null && (onlyincompound != 0 && TESTAFF(sptr.getCont(), onlyincompound))))) &&
                 // needaffix on first suffix
                 (cclass != 0 ||
                  !(sptr.getCont() != null &&
                    needaffix != 0 && TESTAFF(sptr.getCont(), needaffix)))))
              rv = sptr.checkword(this, word, start, len, sfxopts, ppfx, cclass,
                                   needflag, 0);
            while (rv != null)
            {
              if (ppfx != null)
              {
                if (ppfx.getMorph() != null)
                {
                  result.Append(ppfx.getMorph());
                  result.Append(MSEP.FLD);
                }
                else
                  debugflag(result, ppfx.getFlag());
              }
              if (complexprefixes && rv.data != null)
                result.Append(rv.data);
              if (!rv.Contains(MORPH.STEM))
              {
                result.Append(MSEP.FLD);
                result.Append(MORPH.STEM);
                result.Append(rv.word);
              }

              if (!complexprefixes && rv.data != null)
              {
                result.Append(MSEP.FLD);
                result.Append(rv.data);
              }

              if (sptr.getMorph() != null)
              {
                result.Append(MSEP.FLD);
                result.Append(sptr.getMorph());
              }
              else
                debugflag(result, sptr.getFlag());
              result.Append(MSEP.REC);
              rv = sptr.get_next_homonym(rv, sfxopts, ppfx, cclass, needflag);
            }
            sptr = sptr.getNextEQ();
          }
          else
          {
            sptr = sptr.getNextNE();
          }
        } while (sptr != null);
    }

    // check if word with affixes is correctly spelled
    hentry affix_check(IEnumerable<char> word,
                              int start,
                              int len,
                              ushort needflag = 0,
                              IN_CPD in_compound = IN_CPD.NOT) {

      // check all prefixes (also crossed with suffixes if allowed)
      hentry rv = prefix_check(word, start, len, in_compound, needflag);
      if (rv != null)
        return rv;

      // if still not found check all suffixes
      rv = suffix_check(word, start, len, 0, null, 0, needflag, in_compound);

      if (havecontclass) {
        var ctx = Context;
        ctx.sfx = null;
        ctx.pfx = null;

        if (rv != null)
          return rv;
        // if still not found check all two-level suffixes
        rv = suffix_check_twosfx(word, start, len, 0, null, needflag);

        if (rv != null)
          return rv;
        // if still not found check all two-level suffixes
        rv = prefix_check_twosfx(word, start, len, IN_CPD.NOT, needflag);
      }

      return rv;
    }

    // check if word with affixes is correctly spelled
    void affix_check_morph(StringBuilder result,
                           string word,
                           int start,
                           int len,
                           ushort needflag = 0,
                           IN_CPD in_compound = IN_CPD.NOT)
    {
      // check all prefixes (also crossed with suffixes if allowed)
      prefix_check_morph(result, word, start, len, in_compound);

      // if still not found check all suffixes
      suffix_check_morph(result, word, start, len, 0, null, 0, needflag, in_compound);

      if (havecontclass) {
        var ctx = Context;
        ctx.sfx = null;
        ctx.pfx = null;
        // if still not found check all two-level suffixes
        suffix_check_twosfx_morph(result, word, start, len, 0, null, needflag);

        // if still not found check all two-level suffixes
        prefix_check_twosfx_morph(result, word, start, len, IN_CPD.NOT, needflag);
      }
    }

    // morphcmp(): compare MORPH_DERI_SFX, MORPH_INFL_SFX and MORPH_TERM_SFX fields
    // in the first line of the inputs
    // return 0, if inputs equal
    // return 1, if inputs may equal with a secondary suffix
    // otherwise return -1
    static int morphcmp(string s, string t)
    {
      bool se = false, te = false;
      int si, sl, olds;
      int ti, tl, oldt;
      if (s == null || t == null)
        return 1;
      olds = 0;
      sl = s.IndexOf('\n');
      si = s.IndexOf(MORPH.DERI_SFX);
      if (si < 0 || (sl >= 0 && sl < si))
        si = s.IndexOf(MORPH.INFL_SFX);
      if (si < 0 || (sl >= 0 && sl < si)) {
        si = s.IndexOf(MORPH.TERM_SFX);
        olds = -1;
      }
      oldt = 0;
      tl = t.IndexOf('\n');
      ti = t.IndexOf(MORPH.DERI_SFX);
      if (ti < 0 || (tl >= 0 && tl < ti))
        ti = t.IndexOf(MORPH.INFL_SFX);
      if (ti < 0 || (tl >= 0 && tl < ti)) {
        ti = t.IndexOf(MORPH.TERM_SFX);
        oldt = -1;
      }

      while (si >= 0 && ti >= 0 && (sl < 0 || sl > si) && (tl < 0 || tl >= ti)) {
        si += MORPH.TAG_LEN;
        ti += MORPH.TAG_LEN;
        se = false;
        te = false;
        while (!se && !te && s[si] == t[ti]) {
          if (++si >= s.Length)
            se = true;
          else
            switch (s[si])
            {
              case ' ':
              case '\n':
              case '\t':
                se = true;
                break;
            }

          if (++ti >= t.Length)
            te = true;
          else
            switch (t[ti])
            {
              case ' ':
              case '\n':
              case '\t':
                te = true;
                break;
            }
        }
        if (!se || !te) {
          // not terminal suffix difference
          if (olds >= 0)
            return -1;
          return 1;
        }
        olds = si;
        si = s.IndexOf(MORPH.DERI_SFX, si);
        if (si < 0 || (sl >= 0 && sl < si))
          si = s.IndexOf(MORPH.INFL_SFX, olds);
        if (si < 0 || (sl >= 0 && sl < si)) {
          si = s.IndexOf(MORPH.TERM_SFX, olds);
          olds = -1;
        }
        oldt = ti;
        ti = t.IndexOf(MORPH.DERI_SFX, ti);
        if (ti < 0 || (tl >= 0 && tl < ti))
          ti = t.IndexOf(MORPH.INFL_SFX, oldt);
        if (ti < 0 || (tl >= 0 && tl < ti)) {
          ti = t.IndexOf(MORPH.TERM_SFX, oldt);
          oldt = -1;
        }
      }
      if (si < 0 && ti < 0 && se && te)
        return 0;
      return 1;
    }

    string morphgen(string ts,
                    ushort[] ap,
                    string morph,
                    string targetmorph,
                    int level) {
      // handle suffixes
      if (morph == null || ap == null)
        return string.Empty;

      // check substandard flag
      if (substandard != 0 && TESTAFF(ap, substandard))
        return string.Empty;

      if (morphcmp(morph, targetmorph) == 0)
        return ts;

      int stemmorphcatpos;
      string mymorph;

      // use input suffix fields, if exist
      if (morph.IndexOf(MORPH.INFL_SFX) >= 0 || morph.IndexOf(MORPH.DERI_SFX) >= 0) {
        mymorph = morph + MSEP.FLD;
        stemmorphcatpos = mymorph.Length;
      } else {
        mymorph = null;
        stemmorphcatpos = -1;
      }

      for (int i = 0; i < ap.Length; i++) {
        var c = (byte)(ap[i] & 0x00FF);
        var sptr = sFlag[c];
        if (sptr != null)
          do {
            if (sptr.getFlag() == ap[i] && sptr.getMorph() != null &&
                ((sptr.getCont() == null) ||
                 // don't generate forms with substandard affixes
                 !(substandard != 0 && TESTAFF(sptr.getCont(), substandard)))) {
              string stemmorph;
              if (stemmorphcatpos != -1) {
                if (stemmorphcatpos < mymorph.Length) mymorph = mymorph.Remove(stemmorphcatpos);
                mymorph += sptr.getMorph();
                stemmorph = mymorph;
              } else {
                stemmorph = sptr.getMorph();
              }

              int cmp = morphcmp(stemmorph, targetmorph);

              if (cmp == 0) {
                string newword = sptr.add(this, ts);
                if (newword != null) {
                  hentry check = lookup(newword);  // XXX extra dic
                  if (check == null || check.astr == null ||
                      !(TESTAFF(check.astr, forbiddenword) ||
                        TESTAFF(check.astr, ONLYUPCASEFLAG))) {
                    return newword;
                  }
                }
              }

              // recursive call for secondary suffixes
              if ((level == 0) && (cmp == 1) && (sptr.getCont() != null) &&
                  !(substandard != 0 && TESTAFF(sptr.getCont(), substandard))) {
                string newword = sptr.add(this, ts);
                if (newword != null) {
                  string newword2 =
                      morphgen(newword, sptr.getCont(), stemmorph, targetmorph, 1);

                  if (newword2.Length > 0) {
                    return newword2;
                  }
                }
              }
            }
            sptr = sptr.getFlgNxt();
          } while (sptr != null);
      }
      return string.Empty;
    }

    int expand_rootword(Context ctx,
                        guessword[] wlst,
                        string ts,
                        ushort[] ap,
                        string bad,
                        string phon)
    {
      int nh = 0, al = ap != null ? ap.Length : 0;
      // first add root word to list
      if ((nh < wlst.Length) &&
          !(al != 0 && ((needaffix != 0 && TESTAFF(ap, needaffix)) ||
                   (onlyincompound != 0 && TESTAFF(ap, onlyincompound))))) {
        wlst[nh].word = ts;
        wlst[nh].allow = false;
        wlst[nh].orig = null;
        nh++;
        // add special phonetic version
        if (phon != null && (nh < wlst.Length)) {
          wlst[nh].word = phon;
          wlst[nh].allow = false;
          wlst[nh].orig = ts;
          nh++;
        }
      }

      // handle suffixes
      for (int i = 0; i < al; i++) {
        var c = (byte)(ap[i] & 0x00FF);
        var sptr = sFlag[c];
        if (sptr != null)
          do
          {
            if ((sptr.getFlag() == ap[i]) &&
                (sptr.getKeyLen() == 0 ||
                 (bad.Length > sptr.getKeyLen() &&
                  string.CompareOrdinal(sptr.getAffix(), 0, bad,  bad.Length - sptr.getKeyLen(), sptr.getKeyLen()) == 0)) &&
                // check needaffix flag
                !(sptr.getCont() != null &&
                  ((needaffix != 0 &&
                    TESTAFF(sptr.getCont(), needaffix)) ||
                   (circumfix != 0 &&
                    TESTAFF(sptr.getCont(), circumfix)) ||
                   (onlyincompound != 0 &&
                    TESTAFF(sptr.getCont(), onlyincompound))))) {
              string newword = sptr.add(this, ts);
              if (newword != null) {
                if (nh < wlst.Length) {
                  wlst[nh].word = newword;
                  wlst[nh].allow = sptr.allowCross();
                  wlst[nh].orig = null;
                  nh++;
                  // add special phonetic version
                  if (phon != null && (nh < wlst.Length)) {
                    wlst[nh].word = phon + ctx.reverseword(sptr.getKey());
                    wlst[nh].allow = false;
                    wlst[nh].orig = newword;
                    nh++;
                  }
                }
              }
            }
            sptr = sptr.getFlgNxt();
          } while (sptr != null);
      }

      int n = nh;

      // handle cross products of prefixes and suffixes
      if (al > 0)
        for (int j = 1; j < n; j++)
          if (wlst[j].allow) {
            for (int k = 0; k < al; k++) {
              var c = (byte)(ap[k] & 0x00FF);
              PfxEntry cptr = pFlag[c];
              while (cptr != null) {
                if ((cptr.getFlag() == ap[k]) && cptr.allowCross() &&
                    (cptr.getKeyLen() == 0 ||
                     ((bad.Length > cptr.getKeyLen()) &&
                      (string.CompareOrdinal(cptr.getKey(), 0, bad, 0, cptr.getKeyLen()) == 0)))) {
                  string newword = cptr.add(this, wlst[j].word);
                  if (!string.IsNullOrEmpty(newword)) {
                    if (nh < wlst.Length) {
                      wlst[nh].word = newword;
                      wlst[nh].allow = cptr.allowCross();
                      wlst[nh].orig = null;
                      nh++;
                    }
                  }
                }
                cptr = cptr.getFlgNxt();
              }
            }
          }

      // now handle pure prefixes
      for (int m = 0; m < al; m++) {
        var c = (byte)(ap[m] & 0x00FF);
        var ptr = pFlag[c];
        if (ptr != null)
          do
          {
            if ((ptr.getFlag() == ap[m]) &&
                (ptr.getKeyLen() == 0 ||
                 ((bad.Length > ptr.getKeyLen()) &&
                  (string.CompareOrdinal(ptr.getKey(), 0, bad, 0, ptr.getKeyLen()) == 0))) &&
                // check needaffix flag
                !(ptr.getCont() != null &&
                  ((needaffix != 0 &&
                    TESTAFF(ptr.getCont(), needaffix)) ||
                   (circumfix != 0 &&
                    TESTAFF(ptr.getCont(), circumfix)) ||
                   (onlyincompound != 0 &&
                    TESTAFF(ptr.getCont(), onlyincompound)))))
            {
              string newword = ptr.add(this, ts);
              if (!string.IsNullOrEmpty(newword))
              {
                if (nh < wlst.Length)
                {
                  wlst[nh].word = newword;
                  wlst[nh].allow = ptr.allowCross();
                  wlst[nh].orig = null;
                  nh++;
                }
              }
            }
            ptr = ptr.getFlgNxt();
          } while (ptr != null);
      }

      return nh;
    }

    // return iconv table
    RepList get_iconvtable() {
      return iconvtable;
    }

    // return oconv table
    RepList get_oconvtable() {
      return oconvtable;
    }

    // return FULLSTRIP option
    internal bool get_fullstrip() {
      return fullstrip;
    }

    ushort decode_flag(Bytes f, FileMgr file)
    {
      ushort s = 0;
      int i;
      switch (flag_mode)
      {
        case FlagMode.LONG:
          if (f.Length >= 2)
            s = (ushort)((f[0] << 8) | f[1]);
          break;
        case FlagMode.NUM:
          i = atoi(f);
          if (i >= DEFAULTFLAGS)
          {
            HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, i, DEFAULTFLAGS - 1, file);
            i = 0;
          }
          s = (ushort)i;
          break;
        case FlagMode.UNI:
          {
            var chars = f.Chars(Encoding.UTF8);
            if (chars.Length > 0) s = chars[0];
            break;
          }
        default:
          if (f.Length > 0)
            s = f[0];
          break;
      }
      if (s == 0) HUNSPELL_WARNING(true, Properties.Resources.ZeroFlag, file);
      return s;
    }

    ushort[] decode_flags(Bytes flags, FileMgr file)
    {
      if (flags.Length == 0) return null;
      int len;
      ushort[] result;
      switch (flag_mode)
      {
        case FlagMode.LONG:
          {  // two-character flags (1x2yZz -> 1x 2y Zz)
            len = flags.Length;
            if ((len & 1) == 1) HUNSPELL_WARNING(true, Properties.Resources.BadFlagvector, file);
            len >>= 1;
            result = new ushort[len];
            for (int i = 0; i < len; i++)
            {
              ushort flag = (ushort)((flags[i << 1] << 8) | flags[(i << 1) | 1]);

              if (flag >= DEFAULTFLAGS)
              {
                HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, flag, DEFAULTFLAGS - 1, file);
                flag = 0;
              }

              result[i] = flag;
            }
            break;
          }
        case FlagMode.NUM:
          {  // decimal numbers separated by comma (4521,23,233 -> 4521
             // 23 233)
            len = 1 + flags.ToEnumerable().Count(c => c == ',');
            result = new ushort[len];
            int dest = 0;
            int src = 0;
            for (int p = 0; p < flags.Length; ++p)
            {
              if (flags[p] == ',')
              {
                int i = atoi(flags.Substring(src, p - src));
                if (i >= DEFAULTFLAGS || i < 0)
                {
                  HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, i, DEFAULTFLAGS - 1, file);
                  i = 0;
                }

                result[dest] = (ushort)i;
                if (result[dest] == 0) HUNSPELL_WARNING(true, Properties.Resources.ZeroFlag, file);
                src = p + 1;
                dest++;
              }
            }
            {
              int i = atoi(flags.Substring(src));
              if (i >= DEFAULTFLAGS || i < 0)
              {
                HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, i, DEFAULTFLAGS - 1, file);
                i = 0;
              }

              result[dest] = (ushort)i;
              if (result[dest] == 0) HUNSPELL_WARNING(true, Properties.Resources.ZeroFlag, file);
            }
            break;
          }
        case FlagMode.UNI:
          {  // UTF-8 characters
            var chars = flags.Chars(Encoding.UTF8);
            result = new ushort[chars.Length];
            for (int i = 0; i < chars.Length; i++) result[i] = chars[i];
            break;
          }

        default:
          {  // Ispell's one-character flags (erfg -> e r f g)
            result = new ushort[flags.Length];
            for (int i = 0; i < flags.Length; i++) result[i] = flags[i];
            break;
          }
      }
      return result;
    }

    bool decode_flags(List<ushort> result, Bytes flags, FileMgr af)
    {
      if (flags.Length == 0)
      {
        return false;
      }
      switch (flag_mode)
      {
        case FlagMode.LONG:
          {  // two-character flags (1x2yZz -> 1x 2y Zz)
            int len = flags.Length;
            if ((len & 1) == 1) HUNSPELL_WARNING(true, Properties.Resources.BadFlagvector, af);
            len >>= 1;
            result.Capacity = result.Count + len;
            for (int i = 0; i < len; ++i)
            {
              ushort flag = (ushort)((flags[i << 1] << 8) | flags[(i << 1) | 1]);

              if (flag >= DEFAULTFLAGS)
              {
                HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, flag, DEFAULTFLAGS - 1, af);
                flag = 0;
              }

              result.Add(flag);
            }
            break;
          }
        case FlagMode.NUM:
          {  // decimal numbers separated by comma (4521,23,233 -> 4521
             // 23 233)
            int src = 0;
            for (int p = 0; p < flags.Length; ++p)
            {
              if (flags[p] == ',')
              {
                int i = atoi(flags.Substring(src, p - src));
                if (i >= DEFAULTFLAGS)
                {
                  HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, i, DEFAULTFLAGS - 1, af);
                  i = 0;
                }
                result.Add((ushort)i);
                if ((ushort)i == 0) HUNSPELL_WARNING(true, Properties.Resources.ZeroFlag, af);
                src = p + 1;
              }
            }
            {
              int i = atoi(flags.Substring(src));
              if (i >= DEFAULTFLAGS)
              {
                HUNSPELL_WARNING(true, Properties.Resources.TooLargeFlag, i, DEFAULTFLAGS - 1, af);
                i = 0;
              }

              result.Add((ushort)i);
              if ((ushort)i == 0) HUNSPELL_WARNING(true, Properties.Resources.ZeroFlag, af);
            }
            break;
          }
        case FlagMode.UNI:
          {  // UTF-8 characters
            var chars = flags.Chars(Encoding.UTF8);
            result.Capacity = result.Count + chars.Length;
            for (int i = 0; i < chars.Length; i++) result.Add(chars[i]);
            break;
          }
        default:
          {  // Ispell's one-character flags (erfg -> e r f g)
            result.Capacity = result.Count + flags.Length;
            for (int i = 0; i < flags.Length; i++) result.Add(flags[i]);
            break;
          }
      }
      return true;
    }

    internal void encode_flag(ushort f, StringBuilder sb) {
      if (f == 0)
        sb.Append("(NULL)");
      else if (flag_mode == FlagMode.LONG)
        sb.Append((char)(f >> 8)).Append((char)(f - ((f >> 8) << 8)));
      else if (flag_mode == FlagMode.NUM)
        sb.Append(f);
      else
        sb.Append((char)f);
    }

    string encode_flag(ushort f)
    {
      var sb = new StringBuilder();
      encode_flag(f, sb);
      return sb.ToString();
    }

    // return the keyboard string for suggestions
    string get_key_string() {
      if (string.IsNullOrEmpty(keystring))
        keystring = "qwertyuiop|asdfghjkl|zxcvbnm";
      return keystring;
    }

    // is there compounding?
    bool get_compound() {
      return compoundflag != 0 || compoundbegin != 0 || defcpdtable.Count > 0;
    }

    // return the forbidden words flag modify flag
    internal ushort get_needaffix() {
      return needaffix;
    }


    bool parse_string(IEnumerator<Bytes> parts, ref string dest, FileMgr af)
    {
      if (dest != null && dest.Length != 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      dest = parts.Current.String(encoding);
      return true;
    }

    bool parse_array(IEnumerator<Bytes> parts, ref char[] dest, FileMgr af)
    {
      if (dest != null && dest.Length != 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      dest = parts.Current.Chars(encoding);
      Array.Sort(dest);
      return true;
    }

    /* parse flag */
    bool parse_flag(IEnumerator<Bytes> parts, ref ushort dest, FileMgr af)
    {
      if (dest != 0 && !(dest >= DEFAULTFLAGS))
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      dest = decode_flag(parts.Current, af);
      return true;
    }

    /* parse num */
    bool parse_num(IEnumerator<Bytes> parts, ref int dest, FileMgr af)
    {
      if (dest != -1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      dest = atoi(parts.Current);
      return true;
    }

    /* parse in the max syllablecount of compound words and  */
    bool parse_cpdsyllable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingCompoundsyllableInfo, af);
        return false;
      }

      cpdmaxsyllable = atoi(parts.Current);
      if (parts.MoveNext())
      {
        cpdvowels = parts.Current.Chars(encoding);
        Array.Sort(cpdvowels);
      }
      else
      {
        cpdvowels = "AEIOUaeiou".ToCharArray();
      }
      return true;
    }

    bool parse_convtable(IEnumerator<Bytes> parts,
                                 ref RepList rl,
                                 string keyword,
                                 FileMgr af)
    {
      if (rl != null)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numrl = atoi(parts.Current);
      if (numrl < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      rl = new RepList(numrl);

      /* now parse the num lines to read in the remainder of the table */
      for (int j = 0; j < numrl; j++)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        string pattern = null;
        string pattern2 = null;

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals(keyword))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            return false;
          }

          if (parts.MoveNext())
          {
            pattern = parts.Current.String(encoding);

            if (parts.MoveNext())
            {
              pattern2 = parts.Current.String(encoding);
            }
          }
        }

        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(pattern2))
        {
          HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
          return false;
        }

        rl.add(pattern, pattern2);
      }
      return true;
    }

    /* parse in the typical fault correcting table */
    bool parse_phonetable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (phone != null)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int num = atoi(parts.Current);
      if (num < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      phonetable new_phone = new phonetable();

      /* now parse the phone.num lines to read in the remainder of the table */
      for (int j = 0; j < num; ++j)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        int old_size = new_phone.Count;

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("PHONE"))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            return false;
          }

          if (parts.MoveNext())
          {
            new_phone.Add(parts.Current.String(encoding));

            if (parts.MoveNext())
            {
              new_phone.Add(parts.Current.String(encoding).Replace("_", ""));
            }
          }
        }

        if (new_phone.Count != old_size + 2)
        {
          HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
          return false;
        }
      }

      new_phone.init_hash();
      phone = new_phone;
      return true;
    }

    /* parse in the checkcompoundpattern table */
    bool parse_checkcpdtable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (parsedcheckcpd)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      parsedcheckcpd = true;

      int numcheckcpd = atoi(parts.Current);
      if (numcheckcpd < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      checkcpdtable.Capacity = Math.Min(numcheckcpd, 16384);

      /* now parse the numcheckcpd lines to read in the remainder of the table */
      for (int j = 0; j < numcheckcpd; ++j)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("CHECKCOMPOUNDPATTERN"))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            checkcpdtable.Clear();
            return false;
          }

          if (parts.MoveNext())
          {
            var entry = new patentry();

            int slash_pos = parts.Current.IndexOf('/');
            if (slash_pos < 0)
              entry.pattern = parts.Current.String(encoding);
            else
            {
              entry.pattern = parts.Current.Substring(0, slash_pos).String(encoding);
              entry.cond = decode_flag(parts.Current.Substring(slash_pos + 1), af);
            }

            if (parts.MoveNext())
            {
              slash_pos = parts.Current.IndexOf('/');
              if (slash_pos < 0)
                entry.pattern2 = parts.Current.String(encoding);
              else
              {
                entry.pattern2 = parts.Current.Substring(0, slash_pos).String(encoding);
                entry.cond2 = decode_flag(parts.Current.Substring(slash_pos + 1), af);
              }

              if (parts.MoveNext())
              {
                entry.pattern3 = parts.Current.String(encoding);
                simplifiedcpd = true;
              }
              else
              {
                entry.pattern3 = string.Empty;
              }
            }
            else
            {
              entry.pattern2 = entry.pattern3 = string.Empty;
            }

            checkcpdtable.Add(entry);
          }
        }
      }
      return true;
    }

    /* parse in the compound rule table */
    bool parse_defcpdtable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (parseddefcpd)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      parseddefcpd = true;

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numdefcpd = atoi(parts.Current);
      if (numdefcpd < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      defcpdtable.Capacity = Math.Min(numdefcpd, 16384);

      /* now parse the numdefcpd lines to read in the remainder of the table */
      for (int j = 0; j < numdefcpd; ++j)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("COMPOUNDRULE"))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            numdefcpd = 0;
            return false;
          }

          if (parts.MoveNext())
          {
            var p = parts.Current;
            if (p.IndexOf('(') >= 0)
            {
              var flags = new List<ushort>();
              int i = 0;
              for (int k = 0; k < p.Length; ++k)
              {
                switch ((char)p[k])
                {
                  case '(':
                    var parpos = p.IndexOf(')', k);
                    if (parpos >= 0)
                    {
                      if (i < k) flags.AddRange(decode_flags(p.Substring(i, k - i), af));
                      flags.AddRange(decode_flags(p.Substring(k + 1, parpos - k - 1), af));
                      i = (k = parpos) + 1;
                    }
                    break;

                  case '*':
                  case '?':
                    if (i < k) flags.AddRange(decode_flags(p.Substring(i, k - i), af));
                    i = k + 1;
                    flags.Add(p[k]);
                    break;
                }
              }
              if (i < p.Length) flags.AddRange(decode_flags(p.Substring(i), af));
              defcpdtable.Add(flags.ToArray());
            }
            else
            {
              defcpdtable.Add(decode_flags(p, af));
            }
          }
          else
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            return false;
          }
        }
      }
      return true;
    }

    /* parse in the character map table */
    bool parse_maptable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (maptable != null)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int nummap = atoi(parts.Current);
      if (nummap < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      maptable = new List<mapentry>(Math.Min(nummap, 16384));

      /* now parse the nummap lines to read in the remainder of the table */
      for (int j = 0; j < nummap; ++j)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        var entry = new mapentry();

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("MAP"))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            nummap = 0;
            return false;
          }

          if (parts.MoveNext())
          {
            var s = parts.Current.String(encoding);
            for (var k = 0; k < s.Length; ++k)
            {
              int chb = k, che = k + 1;
              if (s[k] == '(')
              {
                var parpos = s.IndexOf(')', k);
                if (parpos >= 0)
                {
                  chb = k + 1;
                  che = parpos;
                  k = parpos;
                }
              }

              if (chb == che) HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);

              entry.Add(s.Substring(chb, che - chb));
            }
          }
        }

        if (entry.Count == 0)
        {
          HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
          return false;
        }

        maptable.Add(entry);
      }
      return true;
    }

    /* parse in the word breakpoint table */
    bool parse_breaktable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (parsedbreaktable)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      parsedbreaktable = true;

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numbreak = numbreak = atoi(parts.Current);
      if (numbreak < 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }
      if (numbreak == 0) return true;

      breaktable.Capacity = Math.Min(numbreak, 16384);

      /* now parse the numbreak lines to read in the remainder of the table */
      for (int j = 0; j < numbreak; ++j)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        var entry = new mapentry();

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("BREAK"))
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            numbreak = 0;
            return false;
          }

          if (parts.MoveNext()) breaktable.Add(parts.Current.String(encoding));
        }
      }

      if (breaktable.Count != numbreak)
      {
        HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
        return false;
      }

      return true;
    }


    /* parse in the ALIAS table */
    bool parse_aliasf(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (aliasf.Count != 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        aliasf.Clear();
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numaliasf = atoi(parts.Current);
      if (numaliasf < 1)
      {
        aliasf.Clear();
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      aliasf.Capacity = Math.Min(numaliasf, 16384);

      /* now parse the numaliasf lines to read in the remainder of the table */
      for (int j = 0; j < numaliasf; ++j)
      {
        if (!af.getline(out var line))
        {
          aliasf.Clear();
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        using (parts = line.Split())
        {
          ushort[] alias;
          if (!parts.MoveNext() || !parts.Current.Equals("AF") ||
              !parts.MoveNext() || (alias = decode_flags(parts.Current, af)) == null)
          {
            aliasf.Clear();
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            return false;
          }

          SortRemoveDuplicates(ref alias);
          aliasf.Add(alias);
        }
      }
      return true;
    }

    bool is_aliasf()
    {
      return aliasf.Count != 0;
    }

    ushort[] get_aliasf(int index, FileMgr file)
    {
      if (index > 0 && index <= aliasf.Count) return aliasf[index - 1];
      HUNSPELL_WARNING(true, Properties.Resources.InvalidFlagAliasIndex, index);
      return null;
    }

    /* parse morph alias definitions */
    bool parse_aliasm(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (aliasm.Count != 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numaliasm = atoi(parts.Current);
      if (numaliasm < 1)
      {
        aliasm.Clear();
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      aliasm.Capacity = Math.Min(numaliasm, 16384);

      /* now parse the numaliasm lines to read in the remainder of the table */
      for (int j = 0; j < numaliasm; ++j)
      {
        if (!af.getline(out var line))
        {
          aliasm.Clear();
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        using (parts = line.Split())
        {
          string alias;
          if (!parts.MoveNext() || !parts.Current.Equals("AM") ||
              !parts.MoveNext() || (alias = parts.Current.ExpandToEndOf(line).String(encoding)) == null)
          {
            aliasm.Clear();
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            return false;
          }

          if (complexprefixes) alias = helper.reverseword(alias);
          aliasm.Add(alias);
        }
      }
      return true;
    }

    bool is_aliasm()
    {
      return aliasm.Count != 0;
    }

    string get_aliasm(int index)
    {
      if (index > 0 && index <= aliasm.Count) return aliasm[index - 1];
      HUNSPELL_WARNING(true, Properties.Resources.InvalidMorphAliasIndex, index);
      return null;
    }

    /* parse in the typical fault correcting table */
    bool parse_reptable(IEnumerator<Bytes> parts, FileMgr af)
    {
      if (reptable.Count != 0)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MultipleDefinitions, parts.Current.String(encoding), af);
        return false;
      }

      if (!parts.MoveNext())
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      int numrep = atoi(parts.Current);
      if (numrep < 1)
      {
        HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
        return false;
      }

      reptable.Capacity = Math.Min(numrep, 16384);

      /* now parse the numrep lines to read in the remainder of the table */
      for (int j = 0; j < numrep; ++j)
      {
        if (!af.getline(out var line))
        {
          reptable.Clear();
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        using (parts = line.Split())
        {
          if (!parts.MoveNext() || !parts.Current.Equals("REP") || !parts.MoveNext())
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            reptable.Clear();
            return false;
          }

          int type = 0;
          var pattern = parts.Current.String(encoding);
          if (pattern[0] == '^')
          {
            type = 1;
            pattern = pattern.Remove(0, 1);
          }
          pattern = pattern.Replace('_', ' ');
          if (pattern.Length > 0 && pattern[pattern.Length - 1] == '$')
          {
            type += 2;
            pattern = pattern.Remove(pattern.Length - 1);
          }

          if (!parts.MoveNext())
          {
            HUNSPELL_WARNING(true, Properties.Resources.TableCorrupted, af);
            reptable.Clear();
            return false;
          }

          reptable.Add(new replentry(pattern, parts.Current.String(encoding).Replace('_', ' '), type));
        }
      }
      return true;
    }

    void reverse_word_condition(char[] s)
    {
      if (s.Length == 0)
          return;

      Array.Reverse(s);
      bool neg = false;
      for (var k = s.Length - 1; k >= 0; --k) {
        switch (s[k]) {
          case '[': {
            if (neg)
              s[k + 1] = '[';
            else
              s[k] = ']';
            break;
          }
          case ']': {
            s[k] = '[';
            if (neg)
              s[k + 1] = '^';
            neg = false;
            break;
          }
          case '^': {
            if (s[k + 1] == ']')
              neg = true;
            else if (neg)
              s[k + 1] = s[k];
            break;
          }
          default: {
            if (neg)
              s[k + 1] = s[k];
            break;
          }
        }
      }
    }

    class entries_container : List<AffEntry> {
      char m_at;

      public entries_container(char at)
      { 
        m_at = at;
      }
      public void initialize(int numents, ae opts, ushort aflag)
      {
        Capacity = Math.Min(numents, 16384);

        AffEntry entry;

        if (m_at == 'P') {
          entry = new PfxEntry();
        } else {
          entry = new SfxEntry();
        }

        entry.opts = opts;
        entry.aflag = aflag;
        Add(entry);
      }

      public AffEntry add_entry(ae opts) {
        AffEntry ret;
        if (m_at == 'P') {
          ret = new PfxEntry();
        } else {
          ret = new SfxEntry();
        }
        Add(ret);
        ret.opts = this[0].opts & opts;
        return ret;
      }

      public AffEntry first_entry() {
        return Count == 0 ? null : this[0];
      }
    };

    bool parse_affix(IEnumerator<Bytes> parts,
                             char at,
                             FileMgr af,
                             byte[] dupflags)
    {
      int numents = 0;  // number of AffEntry structures to parse

      ushort aflag = 0;  // affix char identifier

      ae opts = 0;
      var affentries = new entries_container(at);

    // checking lines with bad syntax
    #if DEBUG
      int basefieldnum = 0;
    #endif

      // piece 2 - is affix char
      if (parts.MoveNext())
      {
        aflag = decode_flag(parts.Current, af);
        if (((at == 'S') && (dupflags[aflag] & dupSFX) != 0) ||
            ((at == 'P') && (dupflags[aflag] & dupPFX) != 0)) 
        {
          HUNSPELL_WARNING(true, Properties.Resources.MultipleAffixFlag, af);
        }
        dupflags[aflag] += (at == 'S') ? dupSFX : dupPFX;

        // piece 3 - is cross product indicator
        if (parts.MoveNext())
        {
          if (parts.Current[0] == 'Y')
            opts = ae.XPRODUCT;

          // piece 4 - is number of affentries
          if (parts.MoveNext())
          {
            numents = atoi(parts.Current);
            if (numents <= 0 || int.MaxValue / 200 < numents)
            {
              HUNSPELL_WARNING(true, Properties.Resources.InvalidEntriesNumber, af);
              return false;
            }

            if (is_aliasf())
              opts |= ae.ALIASF;
            if (is_aliasm())
              opts |= ae.ALIASM;
            affentries.initialize(numents, opts, aflag);
          }
        }
      }

      AffEntry entry = affentries.first_entry();

      // check to make sure we parsed enough pieces
      if (entry == null)
      {
        HUNSPELL_WARNING(true, Properties.Resources.MissingData, af);
        return false;
      }

      // now parse numents affentries for this affix
      for (int ent = 0; ent < numents; ++ent)
      {
        if (!af.getline(out var line))
        {
          HUNSPELL_WARNING(true, Properties.Resources.UnexpectedEOF, af);
          return false;
        }

        int np = 0;

        // split line into pieces
        using (parts = line.Split())
        {
          if (parts.MoveNext())
          {
            // piece 1 - is type
            np++;
            if (ent != 0)
              entry = affentries.add_entry(ae.XPRODUCT | ae.ALIASF | ae.ALIASM);

            // piece 2 - is affix char
            if (parts.MoveNext())
            {
              np++;
              if (decode_flag(parts.Current, af) != aflag)
              {
                HUNSPELL_WARNING(true, Properties.Resources.AffixCorrupted, encode_flag(aflag), af);
                return false;
              }

              if (ent != 0)
              {
                AffEntry start_entry = affentries.first_entry();
                entry.aflag = start_entry.aflag;
              }

              // piece 3 - is string to strip or 0 for null
              if (parts.MoveNext())
              {
                np++;
                entry.strip = parts.Current.String(encoding);
                if (complexprefixes)
                {
                  entry.strip = helper.reverseword(entry.strip);
                }
                if (entry.strip == "0")
                {
                  entry.strip = string.Empty;
                }

                // piece 4 - is affix string or 0 for null
                if (parts.MoveNext())
                {
                  entry.morphcode = null;
                  entry.contclass = null;
                  np++;
                  int dash = parts.Current.IndexOf('/');
                  if (dash >= 0)
                  {
                    entry.appnd = remove_ignored_chars(parts.Current.Substring(0, dash).String(encoding));
                    var dash_str = parts.Current.Substring(dash + 1);

                    if (complexprefixes)
                    {
                      entry.appnd = helper.reverseword(entry.appnd);
                    }

                    if (is_aliasf())
                    {
                      entry.contclass = get_aliasf(atoi(dash_str), af);
                    }
                    else
                    {
                      entry.contclass = decode_flags(dash_str, af);
                      if (entry.contclass != null) SortRemoveDuplicates(ref entry.contclass);
                    }

                    if (entry.contclass != null)
                    {
                      havecontclass = true;
                      for (ushort _i = 0; _i < entry.contclass.Length; _i++)
                      {
                        contclasses[entry.contclass[_i]] = true;
                      }
                    }
                  }
                  else
                  {
                    entry.appnd = remove_ignored_chars(parts.Current.String(encoding));

                    if (complexprefixes)
                    {
                      entry.appnd = helper.reverseword(entry.appnd);
                    }
                  }

                  if (entry.appnd == "0")
                  {
                    entry.appnd = string.Empty;
                  }

                  // piece 5 - is the conditions descriptions
                  if (parts.MoveNext())
                  {
                    var chunk = parts.Current.Chars(encoding);
                    np++;
                    if (complexprefixes)
                    {
                      reverse_word_condition(chunk);
                    }
                    if (entry.strip.Length > 0 && !chunk.IsDot() &&
                        redundant_condition(at, entry.strip, chunk, af))
                      chunk = Dot;
                    if (at == 'S')
                    {
                      reverse_word_condition(chunk);
                    }
                    encodeit(entry, chunk);

                    if (parts.MoveNext())
                    {
                      if (is_aliasm())
                      {
                        int index = atoi(parts.Current);
                        entry.morphcode = get_aliasm(index);
                      }
                      else if (parts.Current[0] != '#')
                      {
                        var chunk2 = parts.Current.String(encoding);
                        if (complexprefixes)
                        {  // XXX - fix me for morph. gen.
                          chunk2 = helper.reverseword(chunk2);
                        }
                        // add the remaining of the line
                        var pos = parts.Current.IndexIn(line) + parts.Current.Length;
                        if (pos < line.Length)
                        {
                          chunk2 += line.Substring(pos, encoding);
                        }
                        entry.morphcode = chunk2;
                      }
                    }
                  }
                }
              }
            }
          }
        }

        // check to make sure we parsed enough pieces
        if (np < 4)
        {
          HUNSPELL_WARNING(true, Properties.Resources.AffixCorrupted, encode_flag(aflag), af);
          return false;
        }

#if DEBUG
        // detect unnecessary fields, excepting comments
        if (basefieldnum != 0) {
          int fieldnum =
              entry.morphcode == null ? 5 : 6;
          if (fieldnum != basefieldnum)
            HUNSPELL_WARNING(false, Properties.Resources.InvalidFieldsNumber, af);
        } else {
          basefieldnum =
              entry.morphcode == null ? 5 : 6;
        }
#endif
      }

      // now create SfxEntry or PfxEntry objects and use links to
      // build an ordered (sorted by affix string) list
      foreach (var affentry in affentries) {
        if (at == 'P') {
          build_pfxtree((PfxEntry)affentry);
        } else {
          build_sfxtree((SfxEntry)affentry);
        }
      }

      return true;
    }

    bool redundant_condition(char ft,
                             string strip,
                             char[] cond,
                             FileMgr file)
    {
      int stripl = strip.Length, condl = cond.Length, i, j;
      bool neg, @in;
      if (ft == 'P') {  // prefix
        if (strip.StartsWith(cond))
          return true;
        else {
          for (i = 0, j = 0; (i < stripl) && (j < condl); i++, j++) {
            if (cond[j] != '[') {
              if (cond[j] != strip[i]) {
                HUNSPELL_WARNING(false, Properties.Resources.IncompatibleCondition, file);
                return false;
              }
            } else {
              neg = (cond[j + 1] == '^');
              @in = false;
              do {
                j++;
                if (strip[i] == cond[j])
                  @in = true;
              } while ((j < (condl - 1)) && (cond[j] != ']'));
              if (j == (condl - 1) && (cond[j] != ']'))
              {
                HUNSPELL_WARNING(false, Properties.Resources.IncompleteCondition, cond, file);
                return false;
              }
              if ((!neg && !@in) || (neg && @in)) {
                HUNSPELL_WARNING(false, Properties.Resources.IncompatibleCondition, file);
                return false;
              }
            }
          }
          if (j >= condl)
            return true;
        }
      } else {  // suffix
        if ((stripl >= condl) && strip.EndsWith(cond))
          return true;
        else {
          for (i = stripl - 1, j = condl - 1; (i >= 0) && (j >= 0); i--, j--) {
            if (cond[j] != ']') {
              if (cond[j] != strip[i]) {
                HUNSPELL_WARNING(false, Properties.Resources.IncompatibleCondition, file);
                return false;
              }
            } else if (j > 0) {
              @in = false;
              do {
                j--;
                if (strip[i] == cond[j])
                  @in = true;
              } while ((j > 0) && (cond[j] != '['));
              if ((j == 0) && (cond[j] != '['))
              {
                HUNSPELL_WARNING(false, Properties.Resources.IncompleteCondition, cond, file);
                return false;
              }
              neg = (cond[j + 1] == '^');
              if ((!neg && !@in) || (neg && @in)) {
                HUNSPELL_WARNING(false, Properties.Resources.IncompatibleCondition, file);
                return false;
              }
            }
          }
          if (j < 0)
            return true;
        }
      }
      return false;
    }

    List<string> get_suffix_words(ushort[] suff, string root_word, List<string> slst)
    {
      if (suff != null)
        foreach (var v in sStart.Values) {
          var ptr = v;
          while (ptr != null) {
            for (int i = 0; i < suff.Length; i++) {
              if (suff[i] == ptr.getFlag()) {
                string nw = root_word + ptr.getAffix();
                hentry ht = ptr.checkword(this, nw, 0, nw.Length, 0, null, 0, 0, 0);
                if (ht != null) {
                  slst.Add(nw);
                }
              }
            }
            ptr = ptr.getNext();
          }
        }
      return slst;
    }

    // make a copy of src at dest while removing all characters
    // specified in IGNORE rule
    string remove_ignored_chars(string word, StringHelper ctx = null)
    {
      if (ignorechars == null) return word;

      int i = word.IndexOfAny(ignorechars);
      if (i < 0) return word;

      if (ctx == null) ctx = helper;
      var sb = ctx.PopStringBuilder();
      int j = 0;
      do
      {
        if (i > j) sb.Append(word, j, i - j);
        j = i + 1;
      } while (j < word.Length && (i = word.IndexOfAny(ignorechars, j)) >= 0);
      if (j < word.Length) sb.Append(word, j, word.Length - j);
      return ctx.ToStringPushStringBuilder(sb);
    }


    static Regex rxNotAZ09 = new Regex("[^A-Z0-9]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    void SetEncoding(string id, FileMgr af)
    {
      if (id == "UTF-8")
      {
        encoding = Encoding.UTF8;
        return;
      }

      int cp;
      switch (rxNotAZ09.Replace(id, "").ToLowerInvariant())
      {
        case "iso88591": cp = 28591; break;
        case "iso88592": cp = 28592; break;
        case "iso88593": cp = 28593; break;
        case "iso88594": cp = 28594; break;
        case "iso88595": cp = 28595; break;
        case "iso88596": cp = 28596; break;
        case "iso88597": cp = 28597; break;
        case "iso88598": cp = 28598; break;
        case "iso88599": cp = 28599; break;
        case "iso885910": cp = 28600; break;
        case "tis620":
        case "tis6202533":
        case "iso885911": cp = 874; break;
        case "iso885913": cp = 28603; break;
        case "iso885914": cp = 28604; break;
        case "iso885915": cp = 28605; break;
        case "koi8r": cp = 20866; break;
        case "koi8u": cp = 21866; break;
        case "cp1251":
        case "microsoftcp1251": cp = 1251; break;
        case "xisciias":
        case "isciidevanagari": cp = 57006; break;
        default: cp = 0; break;
      }

      if (cp > 0)
        try
        {
          encoding = Encoding.GetEncoding(cp);
          if (cp == 28599 && textinfo == CultureInfo.InvariantCulture.TextInfo) textinfo = CultureInfo.GetCultureInfo(0x41F).TextInfo;
          return;
        }
        catch
        {
          switch (cp)
          {
            case 28600: encoding = Encodings.Iso8859_10.Instance; break;
            case 28604: encoding = Encodings.Iso8859_14.Instance; break;
            default: HUNSPELL_WARNING(true, Properties.Resources.UnknownEncodingError, id, af); break;
          }
        }
      else
        HUNSPELL_WARNING(false, Properties.Resources.UnknownEncodingWarning, id, af);
    }
  }
}
