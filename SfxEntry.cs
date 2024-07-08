using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  using static Utils;

  class SfxEntry : AffEntry
  {
    private string rappnd;

    private SfxEntry next;
    private SfxEntry nexteq;
    private SfxEntry nextne;
    private SfxEntry flgnxt;

    public string add(Hunspell pmyMgr, string word)
    {
      var len = word.Length;
      /* make sure all conditions match */
      if ((len > strip.Length || (len == 0 && pmyMgr.get_fullstrip())) &&
          (len >= numconds) && test_condition(word) &&
          (strip.Length == 0 ||
           (len >= strip.Length && string.CompareOrdinal(word, len - strip.Length, strip, 0, strip.Length) == 0)))
      {
        /* we have a match so add suffix */
        if (strip.Length > 0) word = word.Remove(len - strip.Length);
        return word + appnd;
      }
      return null;
    }

    public bool test_condition(IEnumerable<char> s)
    {
      int st = s.Length();
      int pos = -1;  // group with pos input position
      bool neg = false;        // complementer
      bool ingroup = false;    // character in the group
      if (numconds == 0)
        return true;
      int p = 0;
      st--;
      int i = 1;
      while (true)
      {
        switch (conds[p])
        {
          case '[':
            ++p;
            pos = st;
            break;
          case '^':
            ++p;
            neg = true;
            break;
          case ']':
            if (!neg && !ingroup)
              return false;
            i++;
            // skip the next character
            if (!ingroup)
            {
              st--;
            }
            pos = -1;
            neg = false;
            ingroup = false;
            ++p;
            if (st < 0 && p < conds.Length)
              return false;  // word <= condition
            break;
          case '.':
            if (pos < 0)
            {
              // dots are not metacharacters in groups: [.]
              ++p;
              // skip the next character
              if (--st < 0)
              {  // word <= condition
                if (p < conds.Length)
                  return false;
                else
                  return true;
              }
              break;
            }
            goto default;
          /* FALLTHROUGH */
          default:
            {
              if (s.At(st) == conds[p])
              {
                ++p;
                if (pos >= 0)
                {
                  if (neg)
                    return false;
                  else if (i == numconds)
                    return true;
                  ingroup = true;
                  while (p < conds.Length && conds[p] != ']' && ++p < conds.Length)
                  {
                  }
                  //			if (p && *p != ']') p = nextchar(p);
                  st--;
                }
                if (pos < 0)
                {
                  i++;
                  st--;
                }
                if (st < 0 && p < conds.Length && conds[p] != ']')
                  return false;      // word <= condition
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

    public hentry checkword(Hunspell pmyMgr, 
                            IEnumerable<char> word,
                            int start,
                            int len,
                            ae optflags,
                            PfxEntry ppfx,
                            ushort cclass,
                            ushort needflag,
                            ushort badflag)
    {
      hentry he;  // hash entry pointer
      PfxEntry ep = ppfx;

      // if this suffix is being cross checked with a prefix
      // but it does not support cross products skip it

      if (((optflags & ae.XPRODUCT) != 0) && ((opts & ae.XPRODUCT) == 0))
        return null;

      // upon entry suffix is 0 length or already matches the end of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword
                                     // the second condition is not enough for UTF-8 strings
                                     // it checked in test_condition()

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing suffix and adding
        // back any characters that would have been stripped or
        // or null terminating the shorter string

        var stripl = strip?.Length ?? 0;
        var tmpstring = new char[tmpl + stripl];
        word.CopyTo(start, tmpstring, 0, tmpl);
        if (stripl > 0)
        {
          strip.CopyTo(0, tmpstring, tmpl, stripl);
        }

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then check if resulting
        // root word in the dictionary

        if (test_condition(tmpstring))
        {
#if SZOSZABLYA_POSSIBLE_ROOTS
          fprintf(stdout, "%s %s %c\n", word.c_str() + start, tmpword, aflag);
#endif
          if ((he = pmyMgr.lookup(tmpstring)) != null)
          {
            do
            {
              // check conditional suffix (enabled by prefix)
              if ((TESTAFF(he.astr, aflag) ||
                   (ep != null && ep.getCont() != null &&
                    TESTAFF(ep.getCont(), aflag))) &&
                  (((optflags & ae.XPRODUCT) == 0) ||
                   (ep != null && TESTAFF(he.astr, ep.getFlag())) ||
                   // enabled by prefix
                   (contclass != null &&
                    (ep != null && TESTAFF(contclass, ep.getFlag())))) &&
                  // handle cont. class
                  ((cclass == 0) ||
                   ((contclass != null) && TESTAFF(contclass, cclass))) &&
                  // check only in compound homonyms (bad flags)
                  (badflag == 0 || !TESTAFF(he.astr, badflag)) &&
                  // handle required flag
                  ((needflag == 0) ||
                   (TESTAFF(he.astr, needflag) ||
                    (contclass != null && TESTAFF(contclass, needflag)))))
                return he;
              he = he.next_homonym;  // check homonyms
            } while (he != null);
          }
        }
      }
      return null;
    }

    public hentry check_twosfx(Hunspell pmyMgr, 
                               IEnumerable<char> word,
                               int start,
                               int len,
                               ae optflags,
                               PfxEntry ppfx,
                               ushort needflag = 0)
    {
      // if this suffix is being cross checked with a prefix
      // but it does not support cross products skip it

      if ((optflags & ae.XPRODUCT) != 0 && (opts & ae.XPRODUCT) == 0)
        return null;

      // upon entry suffix is 0 length or already matches the end of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing suffix and adding
        // back any characters that would have been stripped or
        // or null terminating the shorter string

        var tmpword = new char[tmpl + strip.Length];
        word.CopyTo(start, tmpword, 0, tmpl);
        strip.CopyTo(0, tmpword, tmpl, strip.Length);

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then recall suffix_check

        if (test_condition(tmpword))
        {
          hentry he;  // hash entry pointer
          if (ppfx != null)
          {
            // handle conditional suffix
            if ((contclass !=  null) && TESTAFF(contclass, ppfx.getFlag()))
              he = pmyMgr.suffix_check(tmpword, 0, tmpl, 0, null,
                                        aflag, needflag, IN_CPD.NOT);
            else
              he = pmyMgr.suffix_check(tmpword, 0, tmpl, optflags, ppfx,
                                        aflag, needflag, IN_CPD.NOT);
          }
          else
          {
            he = pmyMgr.suffix_check(tmpword, 0, tmpl, 0, null,
                                      aflag, needflag, IN_CPD.NOT);
          }
          if (he != null)
            return he;
        }
      }
      return null;
    }

    public void check_twosfx_morph(Hunspell pmyMgr,
                                   StringBuilder result,
                                   string word,
                                   int start,
                                   int len,
                                   ae optflags,
                                   PfxEntry ppfx,
                                   ushort needflag = 0)
    {
      // if this suffix is being cross checked with a prefix
      // but it does not support cross products skip it

      if ((optflags & ae.XPRODUCT) != 0 && (opts & ae.XPRODUCT) == 0)
        return;

      // upon entry suffix is 0 length or already matches the end of the word.
      // So if the remaining root word has positive length
      // and if there are enough chars in root word and added back strip chars
      // to meet the number of characters conditions, then test it

      int tmpl = len - appnd.Length; // length of tmpword

      if ((tmpl > 0 || (tmpl == 0 && pmyMgr.get_fullstrip())) &&
          (tmpl + strip.Length >= numconds))
      {
        // generate new root word by removing suffix and adding
        // back any characters that would have been stripped or
        // or null terminating the shorter string

        string tmpword = word.Substring(start, tmpl) + strip;

        // now make sure all of the conditions on characters
        // are met.  Please see the appendix at the end of
        // this file for more info on exactly what is being
        // tested

        // if all conditions are met then recall suffix_check

        if (test_condition(tmpword))
        {
          if (ppfx != null)
          {
            // handle conditional suffix
            if ((contclass != null) && TESTAFF(contclass, ppfx.getFlag()))
            {
              var prelen = result.Length;
              pmyMgr.suffix_check_morph(result, tmpword, 0, tmpl, 0, null, aflag,
                                                          needflag);
              if (result.Length > prelen)
              {
                result.TrimRec();
                if (ppfx.getMorph() != null)
                {
                  result.Insert(prelen, ppfx.getMorph()).Append(MSEP.FLD);
                }
              }
            }
            else
            {
              pmyMgr.suffix_check_morph(result, tmpword, 0, tmpl, optflags, ppfx, aflag, needflag);
              result.TrimRec();
            }
          }
          else
          {
            pmyMgr.suffix_check_morph(result, tmpword, 0, tmpl, 0, null, aflag, needflag);
            result.TrimRec();
          }
        }
      }
    }


    // get next homonym with same affix
    public hentry get_next_homonym(hentry he,
                                   ae optflags,
                                   PfxEntry ppfx,
                                   ushort cclass,
                                   ushort needflag)
    {
      PfxEntry ep = ppfx;
      ushort eFlag = ep != null ? ep.getFlag() : (ushort)0;

      while (he.next_homonym != null)
      {
        he = he.next_homonym;
        if ((TESTAFF(he.astr, aflag) ||
             (ep != null && ep.getCont() != null &&
              TESTAFF(ep.getCont(), aflag))) &&
            ((optflags & ae.XPRODUCT) == 0 || TESTAFF(he.astr, eFlag) ||
             // handle conditional suffix
             ((contclass != null) && TESTAFF(contclass, eFlag))) &&
            // handle cont. class
            ((cclass == 0) ||
             ((contclass != null) && TESTAFF(contclass, cclass))) &&
            // handle required flag
            ((needflag == 0) ||
             (TESTAFF(he.astr, needflag) ||
              ((contclass != null) && TESTAFF(contclass, needflag)))))
          return he;
      }
      return null;
    }

    public ushort getFlag() { return aflag; }
    public string getKey() { return rappnd; }
    public string getMorph() { return morphcode; }

    public ushort[] getCont() { return contclass; }
    public string getAffix() { return appnd; }

    public int getKeyLen() { return appnd.Length; }

    public SfxEntry getNext() { return next; }
    public SfxEntry getNextNE() { return nextne; }
    public SfxEntry getNextEQ() { return nexteq; }
    public SfxEntry getFlgNxt() { return flgnxt; }

    public void setNext(SfxEntry ptr) { next = ptr; }
    public void setNextNE(SfxEntry ptr) { nextne = ptr; }
    public void setNextEQ(SfxEntry ptr) { nexteq = ptr; }
    public void setFlgNxt(SfxEntry ptr) { flgnxt = ptr; }
    public void initReverseWord(StringHelper ctx)
    {
      rappnd = ctx.reverseword(appnd);
    }
  }
}
