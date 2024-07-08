using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  using static Utils;

  class PfxEntry : AffEntry
  {
    private PfxEntry next;
    private PfxEntry nexteq;
    private PfxEntry nextne;
    private PfxEntry flgnxt;

    public string add(Hunspell pmyMgr, string word)
    {
      var len = word.Length;
      if ((len > strip.Length || (len == 0 && pmyMgr.get_fullstrip())) &&
          (len >= numconds) && test_condition(word) &&
          (string.IsNullOrEmpty(strip) ||
          (len >= strip.Length && string.CompareOrdinal(word, 0, strip, 0, strip.Length) == 0)))
      {
        /* we have a match so add prefix */
        return appnd + word.Substring(strip.Length);
      }
      return null;
    }

    public bool test_condition(IEnumerable<char> s)
    {
      int st = 0, len = s.Length();
      int pos = -1;  // group with pos input position
      bool neg = false;        // complementer
      bool ingroup = false;    // character in the group
      if (numconds == 0)
        return true;
      int p = 0;
      while (true)
      {
        switch (conds[p])
        {
          case '[':
            {
              neg = false;
              ingroup = false;
              ++p;
              pos = st;
              break;
            }
          case '^':
            {
              ++p;
              neg = true;
              break;
            }
          case ']':
            {
              if (neg == ingroup)
                return false;
              pos = -1;
              ++p;
              // skip the next character
              if (!ingroup && st < len)
              {
                ++st;
              }
              if (st == len && p < conds.Length)
                return false;  // word <= condition
              break;
            }

          case '.':
            if (pos < 0)
            {  // dots are not metacharacters in groups: [.]
              ++p;
              // skip the next character
              ++st;
              if (st == len && p < conds.Length)
                return false;  // word <= condition
              break;
            }
            goto default;

          /* FALLTHROUGH */
          default:
            {
              if (st < len && s.At(st) == conds[p])
              {
                ++st;
                p++;
                if (pos >= 0)
                {
                  ingroup = true;
                  while (p < conds.Length && conds[p] != ']' && ++p < conds.Length)
                  {
                  }
                }
              }
              else if (pos >= 0)
              {  // group
                ++p;
              }
              else
                return false;
              break;
            }
        }
        if (p >= conds.Length)
          return true;
      }
    }

    public bool allowCross() { return (opts & ae.XPRODUCT) != 0; }

    // check if this prefix entry matches
    public hentry checkword(Hunspell pmyMgr,
                            IEnumerable<char> word,
                            int start,
                            int len,
                            IN_CPD in_compound,
                            ushort needflag = 0)
    {
      hentry he;  // hash entry of root word or NULL

      // on entry prefix is 0 length or already matches the beginning of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword

      if (tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) {
        // generate new root word by removing prefix and adding
        // back any characters that would have been stripped

        var tmpword = new char[strip.Length + tmpl];
        strip.CopyTo(0, tmpword, 0, strip.Length);
        word.CopyTo(start + appnd.Length, tmpword, strip.Length, tmpl);

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then check if resulting
        // root word in the dictionary

        if (test_condition(tmpword)) {
          tmpl += strip.Length;
          if ((he = pmyMgr.lookup(tmpword)) != null) {
            do {
              if (TESTAFF(he.astr, aflag) &&
                  // forbid single prefixes with needaffix flag
                  !TESTAFF(contclass, pmyMgr.get_needaffix()) &&
                  // needflag
                  (needflag == 0 || TESTAFF(he.astr, needflag) ||
                   (contclass != null && TESTAFF(contclass, needflag))))
                return he;
              he = he.next_homonym;  // check homonyms
            } while (he != null);
          }

          // prefix matched but no root word was found
          // if aeXPRODUCT is allowed, try again but now
          // ross checked combined with a suffix

          // if ((opts & aeXPRODUCT) && in_compound) {
          if ((opts & ae.XPRODUCT) != 0) {
            he = pmyMgr.suffix_check(tmpword, 0, tmpl, ae.XPRODUCT, this,
                                      0, needflag, in_compound);
            if (he != null)
              return he;
          }
        }
      }
      return null;
    }

    // check if this prefix entry matches
    public hentry check_twosfx(Hunspell pmyMgr,
                               IEnumerable<char> word,
                               int start,
                               int len,
                               IN_CPD in_compound,
                               ushort needflag = 0)
    {
      // on entry prefix is 0 length or already matches the beginning of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing prefix and adding
        // back any characters that would have been stripped

        var tmpword = new char[strip.Length + tmpl];
        strip.CopyTo(0, tmpword, 0, strip.Length);
        word.CopyTo(start + appnd.Length, tmpword, strip.Length, tmpl);

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then check if resulting
        // root word in the dictionary

        if (test_condition(tmpword))
        {
          tmpl += strip.Length;

          // prefix matched but no root word was found
          // if aeXPRODUCT is allowed, try again but now
          // cross checked combined with a suffix

          if ((opts & ae.XPRODUCT) != 0 && (in_compound != IN_CPD.BEGIN))
          {
            // hash entry of root word or NULL
            hentry he = pmyMgr.suffix_check_twosfx(tmpword, 0, tmpl, ae.XPRODUCT, this,
                                                            needflag);
            if (he != null)
              return he;
          }
        }
      }
      return null;
    }

    // check if this prefix entry matches
    public void check_twosfx_morph(Hunspell pmyMgr,
                                   StringBuilder result,
                                   string word,
                                   int start,
                                   int len,
                                   IN_CPD in_compound,
                                   ushort needflag = 0)
    {
      // on entry prefix is 0 length or already matches the beginning of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it
      int tmpl = len - appnd.Length; // length of tmpword

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing prefix and adding
        // back any characters that would have been stripped

        string tmpword = strip + word.Substring(start + appnd.Length, tmpl);

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then check if resulting
        // root word in the dictionary

        if (test_condition(tmpword))
        {
          tmpl += strip.Length;

          // prefix matched but no root word was found
          // if aeXPRODUCT is allowed, try again but now
          // ross checked combined with a suffix

          if ((opts & ae.XPRODUCT) != 0 && (in_compound != IN_CPD.BEGIN))
          {
            pmyMgr.suffix_check_twosfx_morph(result, tmpword, 0, tmpl,
                                                       ae.XPRODUCT,
                                                       this, needflag);
            return;
          }
        }
      }
    }

    // check if this prefix entry matches
    public void check_morph(Hunspell pmyMgr,
                            StringBuilder result,
                            string word,
                            int start,
                            int len,
                            IN_CPD in_compound,
                            ushort needflag = 0)
    {
      // on entry prefix is 0 length or already matches the beginning of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing prefix and adding
        // back any characters that would have been stripped

        string tmpword = strip + word.Substring(start + appnd.Length, tmpl);

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then check if resulting
        // root word in the dictionary

        if (test_condition(tmpword))
        {
          tmpl += strip.Length;
          hentry he;  // hash entry of root word or NULL
          if ((he = pmyMgr.lookup(tmpword)) != null)
          {
            do
            {
              if (TESTAFF(he.astr, aflag) &&
                  // forbid single prefixes with needaffix flag
                  !TESTAFF(contclass, pmyMgr.get_needaffix()) &&
                  // needflag
                  (needflag == 0 || TESTAFF(he.astr, needflag) ||
                   (contclass != null && TESTAFF(contclass, needflag))))
              {
                if (morphcode != null)
                {
                  result.Append(MSEP.FLD);
                  result.Append(morphcode);
                }
                else
                  result.Append(getKey());
                if (!he.Contains(MORPH.STEM))
                {
                  result.Append(MSEP.FLD);
                  result.Append(MORPH.STEM);
                  result.Append(he.word);
                }
                // store the pointer of the hash entry
                if (he.data != null)
                {
                  result.Append(MSEP.FLD);
                  result.Append(he.data);
                }
                else
                {
                  // return with debug information
                  result.Append(MSEP.FLD);
                  result.Append(MORPH.FLAG);
                  pmyMgr.encode_flag(getFlag(), result);
                }
                result.Append(MSEP.REC);
              }
              he = he.next_homonym;
            } while (he != null);
          }

          // prefix matched but no root word was found
          // if aeXPRODUCT is allowed, try again but now
          // ross checked combined with a suffix

          if ((opts & ae.XPRODUCT) != 0 && (in_compound != IN_CPD.BEGIN))
          {
            pmyMgr.suffix_check_morph(result, tmpword, 0, tmpl, ae.XPRODUCT, this, 0, needflag);
          }
        }
      }
    }

    public ushort getFlag() { return aflag; }
    public string getKey() { return appnd; }

    public int getKeyLen() { return appnd.Length; }

    public string getMorph() { return morphcode; }

    public ushort[] getCont() { return contclass; }
    public int  getContLen() { return contclass.Length; }

    public PfxEntry getNext() { return next; }
    public PfxEntry getNextNE() { return nextne; }
    public PfxEntry getNextEQ() { return nexteq; }
    public PfxEntry getFlgNxt() { return flgnxt; }

    public void setNext(PfxEntry ptr) { next = ptr; }
    public void setNextNE(PfxEntry ptr) { nextne = ptr; }
    public void setNextEQ(PfxEntry ptr) { nexteq = ptr; }
    public void setFlgNxt(PfxEntry ptr) { flgnxt = ptr; }
  }
}
