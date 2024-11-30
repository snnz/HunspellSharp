using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace HunspellSharp
{
  using static Utils;

  partial class Hunspell
  {
    string ckey;
    int ckeyl;
    string ctry;
    int ctryl;
    bool lang_with_dash_usage;

    const int maxSug = 15;
    internal const int MAX_ROOTS = 100;
    internal const int MAX_WORDS = 100;
    internal const int MAX_GUESS = 200;
    const int MAXNGRAMSUGS = 4;
    const int MAXPHONSUGS = 2;
    const int MAXCOMPOUNDSUGS = 3;
    const int MAX_CHAR_DISTANCE = 4;

    const int NGRAM_LONGER_WORSE = (1 << 0);
    const int NGRAM_ANY_MISMATCH = (1 << 1);
    const int NGRAM_LOWERING = (1 << 2);
    const int NGRAM_WEIGHTED = (1 << 3);

    const long TIMELIMIT_SUGGESTION = 1000 / 10;

    void InitializeSuggestMgr()
    {
      // register affix manager and check in string of chars to
      // try when building candidate suggestions

      if (maxngramsugs < 0) maxngramsugs = MAXNGRAMSUGS;
      if (maxcpdsugs < 0) maxcpdsugs = MAXCOMPOUNDSUGS;

      ckey = get_key_string();

      if (!string.IsNullOrEmpty(ckey))
      {
        ckeyl = ckey.Length;
      }

      ctry = trystring;
      if (!string.IsNullOrEmpty(ctry))
      {
        ctryl = ctry.Length;

        // language with possible dash usage
        // (latin letters or dash in TRY characters)
        lang_with_dash_usage = ctry.IndexOf('-') >= 0 || ctry.IndexOf('a') >= 0;
      }
    }

    void testsug(List<string> wlst,
                 string candidate,
                 int cpdsuggest,
                 CountdownTimer timer,
                 ref SPELL info)
    {
      if (wlst.Count == maxSug)
        return;

      if (wlst.IndexOf(candidate) < 0)
      {
        int result = checkword(candidate, cpdsuggest, timer);
        if (result != 0) {
          // compound word in the dictionary
          if (cpdsuggest == 0 && result >= 2)
            info |= SPELL.COMPOUND;
          wlst.Add(candidate);
        }
      }
    }

    /* generate suggestions for a misspelled word
     *    pass in address of array of char * pointers
     * onlycompoundsug: probably bad suggestions (need for ngram sugs, too)
     * return value: true, if there is a good suggestion
     * (REP, ph: or a dictionary word pair)
     */
    bool suggest(Context ctx, List<string> slst, string word, ref bool onlycompoundsug,
      // for testing compound words formed from 3 or more words:
      // if test_simplesug == true, suggest() doesn't suggest compound words,
      // and it returns with true at the first suggestion found
      bool test_simplesug = false)
    {
      bool nocompoundtwowords = false; // no second or third loops, see below
      int nsugorig = slst.Count, oldSug = 0;
      bool good_suggestion = false;

      // word reversing wrapper for complex prefixes
      if (complexprefixes)
      {
        word = ctx.reverseword(word);
      }

      var timer = new Stopwatch();

      // three loops:
      // - the first without compounding,
      // - the second one with 2-word compounding,
      // - the third one with 3-or-more-word compounding
      // Run second and third loops only if:
      // - no ~good suggestion in the first loop
      // - not for testing compound words with 3 or more words (test_simplesug == false)
      SPELL info = 0;
      for (int cpdsuggest = 0; cpdsuggest < 3 && !nocompoundtwowords; cpdsuggest++)
      {
        // initialize both in non-compound and compound cycles
        timer.Restart();

        // limit compound suggestion
        if (cpdsuggest > 0)
          oldSug = slst.Count;

        // suggestions for an uppercase word (html -> HTML)
        if (slst.Count < maxSug)
        {
          int i = slst.Count;
          capchars(slst, word, cpdsuggest, ref info);
          if (slst.Count > i)
            good_suggestion = true;
        }

        // perhaps we made a typical fault of spelling
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          int i = slst.Count;
          replchars(ctx, slst, word, cpdsuggest, ref info);
          if (slst.Count > i)
          {
            good_suggestion = true;
            if ((info & SPELL.BEST_SUG) != 0)
              return true;
          }
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // perhaps we made chose the wrong char from a related set
        if ((slst.Count < maxSug) &&
            (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          mapchars(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // only suggest compound words when no other ~good suggestion
        if ((cpdsuggest == 0) && (slst.Count > nsugorig))
          nocompoundtwowords = true;

        // did we swap the order of chars by mistake
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          swapchar(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we swap the order of non adjacent chars by mistake
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          longswapchar(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we just hit the wrong key in place of a good char (case and keyboard)
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          badcharkey(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we add a char that should not be there
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          extrachar(slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we forgot a char
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          forgotchar(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we move a char
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          movechar(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we just hit the wrong key in place of a good char
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          badchar(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // did we double two characters
        if ((slst.Count < maxSug) && (cpdsuggest == 0 || (slst.Count < oldSug + maxcpdsugs)))
        {
          doubletwochars(ctx, slst, word, cpdsuggest, ref info);
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;
        if (test_simplesug && slst.Count > 0)
          return true;

        // perhaps we forgot to hit space and two words ran together
        // (dictionary word pairs have top priority here, so
        // we always suggest them, in despite of nosplitsugs, and
        // drop compound word and other suggestions)
        if (cpdsuggest == 0 || (!nosplitsugs && slst.Count < oldSug + maxcpdsugs))
        {
          good_suggestion = twowords(ctx, slst, word, cpdsuggest, good_suggestion, ref info);

          if ((info & SPELL.BEST_SUG) != 0)
            return true;
        }
        if (timer.ElapsedMilliseconds > TIMELIMIT_SUGGESTION)
          return good_suggestion;

        // testing returns after the first loop
        if (test_simplesug)
          return slst.Count > 0;

        // don't need third loop, if the second loop was successful or
        // the first loop found a dictionary-based compound word
        // (we don't need more, likely worse and false 3-or-more-word compound words)
        if (cpdsuggest == 1 && (slst.Count > oldSug || (info & SPELL.COMPOUND) != 0))
          nocompoundtwowords = true;

      }  // repeating ``for'' statement compounding support

      if (!nocompoundtwowords && slst.Count > 0)
        onlycompoundsug = true;

      return good_suggestion;
    }

    // suggestions for an uppercase word (html -> HTML)
    void capchars(List<string> wlst,
                  string word,
                  int cpdsuggest, ref SPELL info)
    {
      testsug(wlst, textinfo.ToUpper(word), cpdsuggest, null, ref info);
    }

    // suggestions for when chose the wrong char out of a related set
    int mapchars(Context ctx,
                 List<string> wlst,
                 string word,
                 int cpdsuggest, ref SPELL info)
    {

      if (word.Length < 2)
        return wlst.Count;

      if (maptable == null)
        return wlst.Count;

      var timer = ctx.suggestInnerTimer;
      timer.Restart();
      var candidate = string.Empty;
      return map_related(word, ref candidate, 0, wlst, cpdsuggest,
                         maptable, timer, 0, ref info);
    }

    int map_related(string word,
                    ref string candidate,
                    int wn,
                    List<string> wlst,
                    int cpdsuggest,
                    List<mapentry> maptable,
                    CountdownTimer timer,
                    int depth, ref SPELL info)
    {
      if (word.Length == wn)
      {
        if (!wlst.Contains(candidate) && checkword(candidate, cpdsuggest, timer) != 0)
        {
          if (wlst.Count < maxSug)
          {
            wlst.Add(candidate);
          }
        }
        return wlst.Count;
      }

      if (depth > 0x3F00)
      {
        if (timer != null) timer.IsExpired = true;
        return wlst.Count;
      }

      bool in_map = false;
      for (int j = 0; j < maptable.Count; ++j)
      {
        for (int k = 0; k < maptable[j].Count; ++k)
        {
          int len = maptable[j][k].Length;
          if (len > 0 && string.CompareOrdinal(word, wn, maptable[j][k], 0, len) == 0)
          {
            in_map = true;
            var candidate_ = candidate;
            for (int l = 0; l < maptable[j].Count; ++l)
            {
              candidate = candidate_ + maptable[j][l];
              map_related(word, ref candidate, wn + len, wlst,
                               cpdsuggest, maptable, timer, depth + 1, ref info);
              if (timer != null && timer.IsExpired)
                return wlst.Count;
            }
          }
        }
      }
      if (!in_map)
      {
        candidate += word[wn];
        map_related(word, ref candidate, wn + 1, wlst, cpdsuggest,
                    maptable, timer, depth + 1, ref info);
      }
      return wlst.Count;
    }

    // suggestions for a typical fault of spelling, that
    // differs with more, than 1 letter from the right form.
    int replchars(Context ctx,
                  List<string> wlst,
                  string word,
                  int cpdsuggest, ref SPELL info)
    {
      int wl = word.Length;
      if (wl < 2)
        return wlst.Count;

      var sb = ctx.PopStringBuilder();
      for (int i = 0; i < reptable.Count; ++i)
      {
        var entry = reptable[i];
        int r = 0;
        // search every occurence of the pattern in the word
        while ((r = word.IndexOf(entry.pattern, r)) >= 0) {
          int type = (r == 0) ? 1 : 0;
          if (r + entry.pattern.Length == word.Length)
            type += 2;
          while (type != 0 && string.IsNullOrEmpty(entry.outstrings[type]))
            type = (type == 2 && r != 0) ? 0 : type - 1;
          if (string.IsNullOrEmpty(entry.outstrings[type])) {
            ++r;
            continue;
          }

          var candidate = sb.Clear().Append(word, 0, r).Append(entry.outstrings[type]).Append(word, r + entry.pattern.Length, wl - (r + entry.pattern.Length)).ToString();
          int sp = candidate.IndexOf(' ');
          int oldns = wlst.Count;
          testsug(wlst, candidate, cpdsuggest, null, ref info);
          if (oldns < wlst.Count)
          {
            // REP suggestions are the best, don't search other type of suggestions
            info |= SPELL.BEST_SUG;
          }

          // check REP suggestions with space
          if (sp >= 0) {
            int prev = 0;
            do
            {
              string prev_chunk = candidate.Substring(prev, sp - prev);
              if (checkword(prev_chunk, 0, null) != 0)
              {
                oldns = wlst.Count;
                string post_chunk = candidate.Substring(sp + 1);
                testsug(wlst, post_chunk, cpdsuggest, null, ref info);
                if (oldns < wlst.Count)
                {
                  wlst[wlst.Count - 1] = candidate;
                }
              }
              prev = sp + 1;
              sp = candidate.IndexOf(' ', prev);
            } while (sp >= 0);
          }
          r++;  // search for the next letter
        }
      }
      ctx.PushStringBuilder(sb);
      return wlst.Count;
    }

    // perhaps we doubled two characters
    // (for example vacation -> vacacation)
    // The recognized pattern with regex back-references:
    // "(.)(.)\1\2\1" or "..(.)(.)\1\2"

    void doubletwochars(Context ctx,
                        List<string> wlst,
                        string word,
                        int cpdsuggest, ref SPELL info)
    {
      int wl = word.Length;
      if (wl < 5) return;

      var buf = ctx.PopBuffer(wl - 2);
      int state = 0;
      for (int i = 2; i < wl; ++i)
      {
        if (word[i] == word[i - 2])
        {
          state++;
          if (state == 3 || (state == 2 && i >= 4))
          {
            word.CopyTo(0, buf, 0, i - 1);
            word.CopyTo(i + 1, buf, i - 1, wl - (i + 1));
            testsug(wlst, new string(buf, 0, wl - 2), cpdsuggest, null, ref info);
            state = 0;
          }
        } else
        {
          state = 0;
        }
      }
      ctx.PushBuffer(buf);
    }

    // error is wrong char in place of correct one (case and keyboard related
    // version)
    void badcharkey(Context ctx,
                    List<string> wlst,
                    string word,
                    int cpdsuggest, ref SPELL info)
    {
      int wl = word.Length;
      var candidate = ctx.PopBuffer(wl);
      word.CopyTo(0, candidate, 0, wl);

      // swap out each char one by one and try uppercase and neighbor
      // keyboard chars in its place to see if that makes a good word
      for (int i = 0; i < wl; ++i)
      {
        char tmpc = candidate[i];
        // check with uppercase letters
        candidate[i] = char.ToUpper(tmpc);
        if (tmpc != candidate[i])
        {
          testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
          candidate[i] = tmpc;
        }
        // check neighbor characters in keyboard string
        if (ckeyl == 0)
          continue;
        int loc = 0;
        while ((loc < ckeyl) && ckey[loc] != tmpc)
          ++loc;
        while (loc < ckeyl)
        {
          if ((loc > 0) && ckey[loc - 1] != '|')
          {
            candidate[i] = ckey[loc - 1];
            testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
          }
          if (((loc + 1) < ckeyl) && (ckey[loc + 1] != '|'))
          {
            candidate[i] = ckey[loc + 1];
            testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
          }
          do
          {
            loc++;
          } while ((loc < ckeyl) && ckey[loc] != tmpc);
        }
        candidate[i] = tmpc;
      }

      ctx.PushBuffer(candidate);
    }

    // error is wrong char in place of correct one
    void badchar(Context ctx,
                 List<string> wlst,
                 string word,
                 int cpdsuggest, ref SPELL info)
    {
      int wl = word.Length;
      var candidate = ctx.PopBuffer(wl);
      word.CopyTo(0, candidate, 0, wl);
      try
      {
        var timer = ctx.suggestInnerTimer;
        timer.Restart();
        // swap out each char one by one and try all the tryme
        // chars in its place to see if that makes a good word
        for (int j = 0; j < ctryl; ++j)
        {
          for (int aI = wl - 1; aI >= 0; --aI)
          {
            char tmpc = candidate[aI];
            if (ctry[j] == tmpc)
              continue;
            candidate[aI] = ctry[j];
            testsug(wlst, new string(candidate, 0, wl), cpdsuggest, timer, ref info);
            if (timer.IsExpired) return;
            candidate[aI] = tmpc;
          }
        }
      }
      finally
      {
        ctx.PushBuffer(candidate);
      }
    }

    // error is word has an extra letter it does not need
    void extrachar(List<string> wlst,
                   string word,
                   int cpdsuggest, ref SPELL info)
    {
      if (word.Length < 2) return;

      // try omitting one char of word at a time
      for (int i = word.Length - 1; i >= 0; --i)
      {
        testsug(wlst, word.Remove(i, 1), cpdsuggest, null, ref info);
      }
    }

    // error is missing a letter it needs
    void forgotchar(Context ctx,
                    List<string> wlst,
                    string word,
                    int cpdsuggest, ref SPELL info)
    {
      var timer = ctx.suggestInnerTimer;
      timer.Restart();

      int n = word.Length;
      var candidate = ctx.PopBuffer(n + 1);
      try
      {
        word.CopyTo(0, candidate, 0, n);

        // try inserting a tryme character before every letter (and the null
        // terminator)
        for (int k = 0; k < ctryl; ++k)
        {
          for (int i = n; ; --i)
          {
            candidate[i] = ctry[k];
            testsug(wlst, new string(candidate, 0, n + 1), cpdsuggest, timer, ref info);
            if (timer.IsExpired) return;
            if (i == 0) break;
            candidate[i] = candidate[i - 1];
          }
          if (++k >= ctryl) break;
          for (int i = 0; ; ++i)
          {
            candidate[i] = ctry[k];
            testsug(wlst, new string(candidate, 0, n + 1), cpdsuggest, timer, ref info);
            if (timer.IsExpired) return;
            if (i == n) break;
            candidate[i] = candidate[i + 1];
          }
        }
      }
      finally
      {
        ctx.PushBuffer(candidate);
      }
    }

    /* error is should have been two words
     * return value is true, if there is a dictionary word pair,
     * or there was already a good suggestion before calling
     * this function.
     */
    bool twowords(Context ctx,
                  List<string> wlst,
                  string word,
                  int cpdsuggest,
                  bool good, ref SPELL info)
    {
      int c2, wl = word.Length;
      if (wl < 3)
        return false;

      bool cwrd, forbidden = langnum == LANG.hu && check_forbidden(word);

      var candidate = ctx.PopBuffer(wl + 1);
      word.CopyTo(0, candidate, 1, wl);

      // split the string into two pieces after every char
      // if both pieces are good words make them a suggestion
      for (int p = 1; p < wl; p++)
      {
        candidate[p - 1] = candidate[p];

        // Suggest only word pairs, if they are listed in the dictionary.
        // For example, adding "a lot" to the English dic file will
        // result only "alot" -> "a lot" suggestion instead of
        // "alto, slot, alt, lot, allot, aloft, aloe, clot, plot, blot, a lot".
        // Note: using "ph:alot" keeps the other suggestions:
        // a lot ph:alot
        // alot -> a lot, alto, slot...
        if (cpdsuggest == 0)
        {
          candidate[p] = ' ';

          var candidate_ = new string(candidate, 0, wl + 1);
          if (checkword(candidate_, cpdsuggest, null) != 0)
          {
            // best solution
            info |= SPELL.BEST_SUG;

            // remove not word pair suggestions
            if (!good)
            {
              good = true;
              wlst.Clear();
            }

            wlst.Insert(0, candidate_);
          }
        }

        // word pairs with dash?
        if (lang_with_dash_usage && cpdsuggest == 0)
        {
          candidate[p] = '-';

          var candidate_ = new string(candidate, 0, wl + 1);
          if (checkword(candidate_, cpdsuggest, null) != 0)
          {
            // best solution
            info |= SPELL.BEST_SUG;

            // remove not word pair suggestions
            if (!good)
            {
              good = true;
              wlst.Clear();
            }
            wlst.Insert(0, candidate_);
          }
        }

        if (wlst.Count < maxSug && !nosplitsugs && !good)
        {
          int c1 = checkword(new string(candidate, 0, p), cpdsuggest, null);
          if (c1 != 0)
          {
            c2 = checkword(new string(candidate, p + 1, wl - p), cpdsuggest, null);
            if (c2 != 0)
            {
              // spec. Hungarian code (TODO need a better compound word support)
              if ((langnum == LANG.hu) && !forbidden &&
                  // if 3 repeating letter, use - instead of space
                  (((candidate[p - 1] == candidate[p + 1]) &&
                  ((p > 1 && (candidate[p - 1] == candidate[p - 2])) || (candidate[p - 1] == candidate[p + 2]))) ||
                  // or multiple compounding, with more, than 6 syllables
                  ((c1 == 3) && (c2 >= 2))))
                candidate[p] = '-';
              else
                candidate[p] = ' ';

              var candidate_ = new string(candidate, 0, wl + 1);
              cwrd = wlst.IndexOf(candidate_) < 0;

              if (cwrd && (wlst.Count < maxSug))
                wlst.Add(candidate_);

              // add two word suggestion with dash, depending on the language
              // Note that cwrd doesn't modified for REP twoword sugg.
              if (!nosplitsugs && lang_with_dash_usage &&
                  wl - p > 1 && p > 1)
              {
                candidate[p] = '-';
                candidate_ = new string(candidate, 0, wl + 1);
                if (wlst.Contains(candidate_)) cwrd = false;

                if ((wlst.Count < maxSug) && cwrd)
                  wlst.Add(candidate_);
              }
            }
          }
        }
      }
      ctx.PushBuffer(candidate);
      return good;
    }

    // error is adjacent letter were swapped
    void swapchar(Context ctx,
                  List<string> wlst,
                  string word,
                  int cpdsuggest, ref SPELL info)
    {
      int wl = word.Length;
      if (wl < 2) return;

      var candidate = ctx.PopBuffer(wl);
      word.CopyTo(0, candidate, 0, wl);

      // try swapping adjacent chars one by one
      for (int i = 0; i < wl - 1; ++i)
      {
        char t = candidate[i]; candidate[i] = candidate[i + 1]; candidate[i + 1] = t;
        testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
        candidate[i + 1] = candidate[i]; candidate[i] = t;
      }

      // try double swaps for short words
      // ahev -> have, owudl -> would
      if (wl == 4 || wl == 5)
      {
        candidate[0] = word[1];
        candidate[1] = word[0];
        candidate[2] = word[2];
        candidate[wl - 2] = word[wl - 1];
        candidate[wl - 1] = word[wl - 2];
        testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
        if (wl == 5)
        {
          candidate[0] = word[0];
          candidate[1] = word[2];
          candidate[2] = word[1];
          testsug(wlst, new string(candidate, 0, wl), cpdsuggest, null, ref info);
        }
      }

      ctx.PushBuffer(candidate);
    }

    // error is not adjacent letter were swapped
    void longswapchar(Context ctx,
                      List<string> wlst,
                      string word,
                      int cpdsuggest, ref SPELL info)
    {
      int n = word.Length;
      var candidate = ctx.PopBuffer(n);
      word.CopyTo(0, candidate, 0, n);

      // try swapping not adjacent chars one by one
      for (var p = 0; p < n; ++p)
      {
        for (var q = 0; q < n; ++q)
        {
          int distance = Math.Abs(q - p);
          if (distance > 1 && distance <= MAX_CHAR_DISTANCE)
          {
            var t = candidate[p]; candidate[p] = candidate[q]; candidate[q] = t;
            testsug(wlst, new string(candidate, 0, n), cpdsuggest, null, ref info);
            candidate[q] = candidate[p]; candidate[p] = t;
          }
        }
      }

      ctx.PushBuffer(candidate);
    }

    // error is a letter was moved
    void movechar(Context ctx,
                 List<string> wlst,
                 string word,
                 int cpdsuggest, ref SPELL info)
    {
      int n = word.Length;
      if (n < 2) return;

      var candidate = ctx.PopBuffer(n);

      // try moving a char
      for (var p = 0; p < n - 1; ++p)
      {
        word.CopyTo(0, candidate, 0, n);
        for (var q = p + 1; q < n && q - p <= MAX_CHAR_DISTANCE; ++q)
        {
          var t = candidate[q]; candidate[q] = candidate[q - 1]; candidate[q - 1] = t;
          if (q - p < 2)
            continue;  // omit swap char
          testsug(wlst, new string(candidate, 0, n), cpdsuggest, null, ref info);
        }
      }

      for (var p = n - 1; p >= 1; --p)
      {
        word.CopyTo(0, candidate, 0, n);
        for (var q = p - 1; q >= 0 && p - q <= MAX_CHAR_DISTANCE; --q)
        {
          var t = candidate[q]; candidate[q] = candidate[q + 1]; candidate[q + 1] = t;
          if (p - q < 2)
            continue;  // omit swap char
          testsug(wlst, new string(candidate, 0, n), cpdsuggest, null, ref info);
        }
      }

      ctx.PushBuffer(candidate);
    }

    // generate a set of suggestions for very poorly spelled words
    void ngsuggest(Context ctx,
                   List<string> wlst,
                   string word,
                   CapType captype)
    {
      int n = word.Length;
      // ofz#59067 a replist entry can generate a very long word, abandon
      // ngram if that odd-edge case arises
      if (n > MAXWORDLEN * 4)
        return;

      // word reversing wrapper for complex prefixes
      if (complexprefixes)
      {
        word = ctx.reverseword(word);
      }

      // exhaustively search through all root words
      // keeping track of the MAX_ROOTS most similar root words
      ctx.GetRootsBuffers(out var roots, out var rootsphon, out var scores, out var scoresphon);

      Array.Clear(roots, 0, MAX_ROOTS);
      Array.Clear(rootsphon, 0, MAX_ROOTS);
      for (int i = 0; i < MAX_ROOTS; ++i)
        scores[i] = scoresphon[i] = -100 * (MAX_ROOTS - 1 - i);

      var target = phone == null ? null : phone.phonet(ctx, textinfo.ToUpper(word));  // XXX phonet() is 8-bit (nc, not n)

      foreach (var hp in walk_hashtable())
      {
        // skip exceptions
        if (
              // skip it, if the word length different by 5 or
              // more characters (to avoid strange suggestions)
              // (except Unicode characters over BMP)
              (Math.Abs(n - hp.word.Length) > 4) ||
              // don't suggest capitalized dictionary words for
              // lower case misspellings in ngram suggestions, except
              // - PHONE usage, or
              // - in the case of German, where not only proper
              //   nouns are capitalized, or
              // - the capitalized word has special pronunciation
              ((captype == CapType.NOCAP) && (hp.var & H_OPT.INITCAP) != 0 &&
                phone == null && (langnum != LANG.de) && (hp.var & H_OPT.PHON) == 0) ||
              // or it has one of the following special flags
              (hp.astr != null &&
                (TESTAFF(hp.astr, forbiddenword) ||
                TESTAFF(hp.astr, ONLYUPCASEFLAG) ||
                nosuggest != 0 && TESTAFF(hp.astr, nosuggest) ||
                nongramsuggest != 0 && TESTAFF(hp.astr, nongramsuggest) ||
                onlyincompound != 0 && TESTAFF(hp.astr, onlyincompound)))
            )
          continue;

        int leftcommon = leftcommonsubstring(word, hp.word);
        int sc = ngram(3, word, hp.word, NGRAM_LONGER_WORSE | NGRAM_LOWERING) + leftcommon;

        // check special pronunciation
        string f;
        if ((hp.var & H_OPT.PHON) != 0 &&
            (f = copy_field(hp.data, 0, MORPH.PHON)) != null)
        {
          leftcommon = leftcommonsubstring(word, f);
          int sc2 = ngram(3, word, f, NGRAM_LONGER_WORSE | NGRAM_LOWERING) + leftcommon;
          if (sc2 > sc)
            sc = sc2;
        }

        if (sc > scores[0])
        {
          scores[0] = sc;
          roots[0] = hp;
          Heapify(scores, roots);
        }

        if (phone != null && (sc > 2) && (Math.Abs(n - hp.word.Length) <= 3))
        {
          f = phone.phonet(ctx, textinfo.ToUpper(hp.word));
          sc = 2 * ngram(3, target, f, NGRAM_LONGER_WORSE);

          if (sc > scoresphon[0])
          {
            scoresphon[0] = sc;
            rootsphon[0] = hp.word;
            Heapify(scoresphon, rootsphon);
          }
        }
      }

      if (scores[0] == -100 * (MAX_ROOTS - 1) && scoresphon[0] == -100 * (MAX_ROOTS - 1))
      {
        // with no roots there will be no guesses and no point running
        // ngram
        return;
      }

      // find minimum threshold for a passable suggestion
      // mangle original word three differnt ways
      // and score them to generate a minimum acceptable score
      int thresh = 0;
      var lword = textinfo.ToLower(word);
      var mw = ctx.PeekBuffer(n);
      for (int sp = 1; sp < 4; sp++)
      {
        lword.CopyTo(0, mw, 0, n);
        for (int k = sp; k < n; k += 4)
          mw[k] = '*';

        thresh += ngram(n, word, new string(mw, 0, n), NGRAM_ANY_MISMATCH);
      }
      thresh = (thresh / 3) - 1;

      // now expand affixes on each of these root words and
      // and use length adjusted ngram scores to select
      // possible suggestions
      ctx.GetGuessBuffers(out var guess, out var gscore, out var glst);

      Array.Clear(guess, 0, MAX_GUESS);
      for (int i = 0; i < MAX_GUESS; ++i)
        gscore[i] = Math.Max(thresh, -100 * (MAX_GUESS - 1 - i));

      for (int i = 0; i < MAX_ROOTS; ++i)
      {
        var rp = roots[i];
        if (rp == null) continue;

        int nw = expand_rootword(ctx, glst, rp.word, rp.astr, word, (rp.var & H_OPT.PHON) != 0 ? copy_field(rp.data, 0, MORPH.PHON) : null);

        for (int k = 0; k < nw; k++)
        {
          var f = glst[k].word;

          int leftcommon = leftcommonsubstring(word, f);

          int sc = ngram(n, word, f, NGRAM_ANY_MISMATCH | NGRAM_LOWERING) + leftcommon;

          if (sc > gscore[0])
          {
            gscore[0] = sc;
            guess[0] = new Guess(glst[k]);
            Heapify(gscore, guess);
          }
        }
      }

      // now we are done generating guesses
      // weight suggestions with a similarity index, based on
      // the longest common subsequent algorithm and resort

      double fact = maxdiff < 0 ? 1.0 : ((10.0 - maxdiff) / 5.0);

      for (int i = 0; i < MAX_GUESS; i++)
      {
        var gl = guess[i].word;
        if (gl == null)
        {
          gscore[i] = int.MinValue;
          continue;
        }

        // lowering guess[i]
        gl = textinfo.ToLower(guess[i].word);
        var len = gl.Length;

        int _lcs = lcslen(ctx, word, gl);

        // same characters with different casing
        if ((n == len) && (n == _lcs))
        {
          gscore[i] += 2000;
          break;
        }
        // using 2-gram instead of 3, and other weightening

        //gl is lowercase already at this point
        int re = ngram(2, word, gl, NGRAM_ANY_MISMATCH | NGRAM_WEIGHTED) +
                 ngram(2, gl, word, NGRAM_ANY_MISMATCH | NGRAM_WEIGHTED | NGRAM_LOWERING);

        int ngram_score, leftcommon_score;
        //gl is lowercase already at this point
        ngram_score = ngram(4, word, gl, NGRAM_ANY_MISMATCH);
        leftcommon_score = leftcommonsubstring(word, gl);
        gscore[i] =
            // length of longest common subsequent minus length difference
            2 * _lcs - Math.Abs(n - len) +
            // weight length of the left common substring
            leftcommon_score +
            // weight equal character positions
            (commoncharacterpositions(word, gl, out var is_swap) != 0
                 ? 1
                 : 0) +
            // swap character (not neighboring)
            ((is_swap) ? 10 : 0) +
            // ngram
            ngram_score +
            // weighted ngrams
            re +
            // different limit for dictionaries with PHONE rules
            (phone != null ? (re < len * fact ? -1000 : 0)
                        : (re < (n + len) * fact ? -1000 : 0));
      }
      
      Array.Sort(gscore, guess, 0, MAX_GUESS);

      // copy over
      int oldns = wlst.Count;

      bool same = false;
      for (int i = MAX_GUESS - 1; i >= 0; --i)
      {
        if (guess[i].word == null) break;

        if ((wlst.Count < oldns + maxngramsugs) && (wlst.Count < maxSug) &&
            (!same || (gscore[i] > 1000)))
        {
          bool unique = true;
          // leave only excellent suggestions, if exists
          if (gscore[i] > 1000)
            same = true;
          else if (gscore[i] < -100)
          {
            same = true;
            // keep the best ngram suggestions, unless in ONLYMAXDIFF mode
            if (wlst.Count > oldns || onlymaxdiff)
            {
              continue;
            }
          }
          for (int j = wlst.Count - 1; j >= 0; --j)
          {
            // don't suggest previous suggestions or a previous suggestion with
            // prefixes or affixes
            if ((guess[i].orig == null && guess[i].word.Contains(wlst[j])) ||
                (guess[i].orig != null && guess[i].orig.Contains(wlst[j])))
            {
              unique = false;
              break;
            }
          }
          if (unique &&
              // check forbidden words
              checkword(guess[i].word, 0, null) != 0)
          {
            wlst.Add(guess[i].orig ?? guess[i].word);
          }
        }
      }

      // phonetic version
      if (phone != null)
      {
        oldns = wlst.Count;

        for (int i = 0; i < MAX_ROOTS; i++)
        {
          var gl = rootsphon[i];
          if (gl == null)
          {
            scoresphon[i] = int.MinValue;
            continue;
          }

          // lowering rootphon[i]
          gl = textinfo.ToLower(gl);
          var len = gl.Length;

          // weight length of the left common substring
          int leftcommon_score = leftcommonsubstring(word, gl);
          // heuristic weigthing of ngram scores
          scoresphon[i] += 2 * lcslen(ctx, word, gl) - Math.Abs(n - len) + leftcommon_score;
        }

        Array.Sort(scoresphon, rootsphon, 0, MAX_ROOTS);

        for (int i = MAX_ROOTS - 1; i >= 0; --i)
        {
          var gl = rootsphon[i];
          if (gl == null) break;

          if ((wlst.Count < oldns + MAXPHONSUGS) && (wlst.Count < maxSug))
          {
            bool unique = true;
            for (int j = wlst.Count - 1; j >= 0; --j)
            {
              // don't suggest previous suggestions or a previous suggestion with
              // prefixes or affixes
              if (gl.Contains(wlst[j]))
              {
                unique = false;
                break;
              }
            }
            if (unique &&
                // check forbidden words
                checkword(gl, 0, null) != 0)
            {
              wlst.Add(gl);
            }
          }
        }
      }
    }

    // see if a candidate suggestion is spelled correctly
    // needs to check both root words and words with affixes

    // obsolote MySpell-HU modifications:
    // return value 2 and 3 marks compounding with hyphen (-)
    // `3' marks roots without suffix
    int checkword(string word,
                  int cpdsuggest,
                  CountdownTimer timer)
    {
      // check time limit
      if (timer != null && timer.CheckExpired()) return 0;

      hentry rv = null;
      int nosuffix = 0;

      if (cpdsuggest >= 1)
      {
        if (get_compound())
        {
          hentry rv2 = null;
          SPELL info = (cpdsuggest == 1) ? SPELL.COMPOUND_2 : 0;
          rv = compound_check(word, 0, 0, 0, null, false, true, info);  // EXT
          // TODO filter 3-word or more compound words, as in spell()
          // (it's too slow to call suggest() here for all possible compound words)
          if (rv != null &&
              ((rv2 = lookup(word)) == null || rv2.astr == null ||
                !(TESTAFF(rv2.astr, forbiddenword) ||
                  nosuggest != 0 && TESTAFF(rv2.astr, nosuggest))))
            return 3;  // XXX obsolote categorisation + only ICONV needs affix
                       // flag check?
        }
        return 0;
      }

      rv = lookup(word);

      if (rv != null)
      {
        if (rv.astr != null &&
            (TESTAFF(rv.astr, forbiddenword) ||
             nosuggest != 0 && TESTAFF(rv.astr, nosuggest) ||
             substandard != 0 && TESTAFF(rv.astr, substandard)))
          return 0;
        while (rv != null)
        {
          if (rv.astr != null &&
              (needaffix != 0 && TESTAFF(rv.astr, needaffix) ||
               TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
               onlyincompound != 0 && TESTAFF(rv.astr, onlyincompound)))
          {
            rv = rv.next_homonym;
          }
          else
            break;
        }
      }
      else
        rv = prefix_check(word, 0, word.Length, 0);  // only prefix, and prefix + suffix XXX

      if (rv != null)
      {
        nosuffix = 1;
      }
      else
      {
        rv = suffix_check(word, 0, word.Length, 0, null, 0, 0, IN_CPD.NOT);  // only suffix
      }

      if (rv == null && havecontclass)
      {
        rv = suffix_check_twosfx(word, 0, word.Length, 0, null, 0);
        if (rv == null)
          rv = prefix_check_twosfx(word, 0, word.Length, 0, 0);
      }

      // check forbidden words
      if (rv != null && rv.astr != null &&
          (TESTAFF(rv.astr, forbiddenword) ||
           TESTAFF(rv.astr, ONLYUPCASEFLAG) ||
           nosuggest != 0 && TESTAFF(rv.astr, nosuggest) ||
           onlyincompound != 0 && TESTAFF(rv.astr, onlyincompound)))
        return 0;

      if (rv != null)
      {  // XXX obsolote
        if (compoundflag != 0 &&
            TESTAFF(rv.astr, compoundflag))
          return 2 + nosuffix;
        return 1;
      }
      return 0;
    }

    bool check_forbidden(string word)
    {
      hentry rv = lookup(word);
      if (rv != null && rv.astr != null &&
          (needaffix != 0 && TESTAFF(rv.astr, needaffix) ||
           onlyincompound != 0 && TESTAFF(rv.astr, onlyincompound)))
        rv = null;
      int len = word.Length;
      if (prefix_check(word, 0, len, IN_CPD.BEGIN) == null)
        rv = suffix_check(word, 0, len, 0, null, 0, 0, IN_CPD.NOT);  // prefix+suffix, suffix
                                                                // check forbidden words
      if (rv != null && rv.astr != null &&
          TESTAFF(rv.astr, forbiddenword))
        return true;

      return false;
    }

    string suggest_morph(Context ctx, string w)
    {
      hentry rv = null;

      var result = ctx.PopStringBuilder();

      // word reversing wrapper for complex prefixes
      if (complexprefixes)
      {
        w = ctx.reverseword(w);
      }

      rv = lookup(w);

      while (rv != null)
      {
        if (rv.astr == null ||
            !(TESTAFF(rv.astr, forbiddenword) ||
              needaffix != 0 && TESTAFF(rv.astr, needaffix) ||
              onlyincompound != 0 && TESTAFF(rv.astr, onlyincompound)))
        {
          if (!rv.Contains(MORPH.STEM))
          {
            result.Append(MSEP.FLD);
            result.Append(MORPH.STEM);
            result.Append(w);
          }
          if (rv.data != null)
          {
            result.Append(MSEP.FLD);
            result.Append(rv.data);
          }
          result.Append(MSEP.REC);
        }
        rv = rv.next_homonym;
      }

      affix_check_morph(result, w, 0, w.Length);

      if (get_compound() && result.Length == 0)
      {
        compound_check_morph(w, 0, 0, 0, null, false, result, null);
      }

      return line_uniq(ctx.ToStringPushStringBuilder(result));
    }

    static int get_sfxcount(string morph)
    {
      if (string.IsNullOrEmpty(morph))
        return 0;
      int n = 0;

      int i = morph.IndexOf(MORPH.DERI_SFX);
      if (i < 0)
        i = morph.IndexOf(MORPH.INFL_SFX);
      if (i < 0)
        i = morph.IndexOf(MORPH.TERM_SFX);

      while (i >= 0)
      {
        n++;
        int j = i + 1;
        i = morph.IndexOf(MORPH.DERI_SFX, j);
        if (i < 0)
          i = morph.IndexOf(MORPH.INFL_SFX, j);
        if (i < 0)
          i = morph.IndexOf(MORPH.TERM_SFX, j);
      }
      return n;
    }

    /* affixation */
    void suggest_hentry_gen(StringBuilder result, hentry rv, string pattern)
    {
      int sfxcount = get_sfxcount(pattern);

      if (get_sfxcount(rv.data) > sfxcount)
        return;

      if (rv.data != null)
      {
        string aff = morphgen(rv.word, rv.astr, rv.data, pattern, 0);
        if (aff.Length > 0)
        {
          result.Append(aff);
          result.Append(MSEP.REC);
        }
      }

      // check all allomorphs
      int p = rv.data != null ? rv.data.IndexOf(MORPH.ALLOMORPH) : -1;
      while (p >= 0)
      {
        p += MORPH.TAG_LEN;
        int plen = fieldlen(rv.data, p);
        hentry rv2 = lookup(rv.data, p, plen);
        while (rv2 != null)
        {
          //            if (HENTRY_DATA(rv2) && get_sfxcount(HENTRY_DATA(rv2)) <=
          //            sfxcount) {
          if (rv2.data != null)
          {
            int st = rv2.data.IndexOf(MORPH.STEM);
            if (st >= 0 && (string.CompareOrdinal(rv2.data, st + MORPH.TAG_LEN, rv.word, 0, fieldlen(rv2.data, st + MORPH.TAG_LEN)) == 0))
            {
              string aff = morphgen(rv2.word, rv2.astr, rv2.data, pattern, 0);
              if (aff.Length > 0)
              {
                result.Append(aff);
                result.Append(MSEP.REC);
              }
            }
          }
          rv2 = rv2.next_homonym;
        }
        p = rv.data.IndexOf(MORPH.ALLOMORPH, p + plen);
      }
    }

    string suggest_gen(Context ctx, List<string> desc, string pattern)
    {
      if (desc.Count == 0)
        return string.Empty;

      StringBuilder result = ctx.PopStringBuilder(), result2 = ctx.PopStringBuilder(), sg = null;
      hentry rv = null;

      // search affixed forms with and without derivational suffixes
      while (true)
      {
        for (int k = 0; k < desc.Count; ++k)
        {
          result.Length = 0;

          // add compound word parts (except the last one)
          string s = desc[k];
          int part = s.IndexOf(MORPH.PART);
          if (part >= 0)
          {
            int nextpart;
            while ((nextpart = s.IndexOf(MORPH.PART, part + 1)) >= 0)
            {
              copy_field(result, s, part, MORPH.PART);
              part = nextpart;
            }
          }

          if (part >= 0) s = s.Substring(part);
          var tok = s.Replace(" | ", " \v ");
          var pl = tok.Split(MSEP.ALT);
          for (int pli = 0; pli < pl.Length; ++pli)
          {
            var i = pl[pli];
            if (i.Length == 0) continue;
            // remove inflectional and terminal suffixes
            int @is = i.IndexOf(MORPH.INFL_SFX);
            if (@is >= 0)
              i = i.Remove(@is);
            i = i.Replace(MORPH.TERM_SFX, "_s:");
            if ((tok = copy_field(s, 0, MORPH.STEM)) != null)
            {
              rv = lookup(tok);
              while (rv != null)
              {
                if (sg == null) sg = ctx.PopStringBuilder(); else sg.Length = 0;
                suggest_hentry_gen(sg, rv, i + pattern);
                if (sg.Length == 0)
                  suggest_hentry_gen(sg, rv, pattern);
                if (sg.Length > 0)
                {
                  var gen = sg.ToString().Split(MSEP.REC_as_array, StringSplitOptions.RemoveEmptyEntries);
                  for (int j = 0; j < gen.Length; ++j)
                  {
                    result2.Append(MSEP.REC);
                    result2.Append(result);
                    copy_field(result2, i, 0, MORPH.SURF_PFX);
                    result2.Append(gen[j]);
                  }
                }
                rv = rv.next_homonym;
              }
            }
          }
        }

        if (result2.Length > 0 || pattern.IndexOf(MORPH.DERI_SFX) < 0)
          break;

        pattern = pattern.Replace(MORPH.DERI_SFX, MORPH.TERM_SFX);
      }
      if (sg != null) ctx.PushStringBuilder(sg);
      ctx.PushStringBuilder(result);
      return ctx.ToStringPushStringBuilder(result2);
    }

    // generate an n-gram score comparing s1 and s2
    int ngram(int n,
              string s1,
              string s2,
              int opt)
    {
      int nscore = 0, ns, l1, l2 = s2.Length;

      if (l2 == 0)
        return 0;

      // lowering dictionary word
      if ((opt & NGRAM_LOWERING) != 0) s2 = textinfo.ToLower(s2);

      l1 = s1.Length;
      for (int j = 1; j <= n; j++)
      {
        ns = 0;
        for (int i = 0; i <= (l1 - j); i++)
        {
          int l = 0;
          if (j == 1)
            l = s2.IndexOf(s1[i], l, l2 - j - l + 1) + 1;
          else if (j <= l2)
            while ((l = s2.IndexOf(s1[i], l, l2 - j - l + 1) + 1) > 0)
              if (string.CompareOrdinal(s1, i + 1, s2, l, j - 1) == 0)
                break;
          if (l > 0)
            ns++;
          else if ((opt & NGRAM_WEIGHTED) != 0)
          {
            ns--;
            if (i == 0 || i == l1 - j)
              ns--;  // side weight
          }
        }
        nscore += ns;
        if (ns < 2 && (opt & NGRAM_WEIGHTED) == 0)
          break;
      }

      ns = 0;
      if ((opt & NGRAM_LONGER_WORSE) != 0)
        ns = (l2 - l1) - 2;
      else if ((opt & NGRAM_ANY_MISMATCH) != 0)
        ns = Math.Abs(l2 - l1) - 2;
      ns = (nscore - ((ns > 0) ? ns : 0));
      return ns;
    }

    // length of the left common substring of s1 and (decapitalised) s2
    int leftcommonsubstring(string s1, string s2)
    {
      int l1 = s1.Length, l2 = s2.Length;
      if (complexprefixes)
      {
        if (l1 > 0 && l1 <= l2 && s1[l1 - 1] == s2[l2 - 1])
          return 1;
      }
      else
      {
        // decapitalise dictionary word
        if (l1 == 0 || l2 == 0 || (s1[0] != s2[0]) && (s1[0] != textinfo.ToLower(s2[0]))) return 0;
        int i;
        for (i = 1; i < l1 && i < l2 && s1[i] == s2[i]; ++i) ;
        return i;
      }
      return 0;
    }

    int commoncharacterpositions(string s1,
                                 string s2,
                                 out bool is_swap)
    {
      int num = 0, diff = 0;
      char diff0s1 = default, diff0s2 = default, diff1s1 = default, diff1s2 = default;
      is_swap = false;
      int i;
      // decapitalize dictionary word
      for (i = 0; i < s2.Length && i < s1.Length; ++i)
      {
        char t = s2[i];
        if (!complexprefixes || i == s2.Length - 1) t = textinfo.ToLower(t);
        if (s1[i] == t)
        {
          num++;
        }
        else
        {
          switch (diff)
          {
            case 0: diff0s1 = s1[i]; diff0s2 = t; break;
            case 1: diff1s1 = s1[i]; diff1s2 = t;  break;
          }
          diff++;
        }
      }
      if ((diff == 2) && (i >= s1.Length && i >= s2.Length) &&
          (diff0s1 == diff1s2) &&
          (diff1s1 == diff0s2))
        is_swap = true;
      return num;
    }

    const byte LCS_UP = 0, LCS_LEFT = 1, LCS_UPLEFT = 2;

    // longest common subsequence
    byte[] lcs(Context ctx, string s, string s2)
    {
      int n, m, i, j;
      m = s.Length + 1;
      n = s2.Length + 1;
      var c = ctx.lcsC;
      var b = ctx.lcsB;
      if (b == null || b.Length < m * n)
      {
        ctx.lcsB = b = new byte[Math.Max(m * n, 25 * 25)];
        ctx.lcsC = c = new byte[b.Length];
      }
      for (i = 1; i < m; i++)
        c[i * n] = 0;
      for (j = 0; j < n; j++)
        c[j] = 0;
      for (i = 1; i < m; i++)
      {
        for (j = 1; j < n; j++)
        {
          if (s[i - 1] == s2[j - 1])
          {
            c[i * n + j] = (byte)(c[(i - 1) * n + j - 1] + 1);
            b[i * n + j] = LCS_UPLEFT;
          }
          else if (c[(i - 1) * n + j] >= c[i * n + j - 1])
          {
            c[i * n + j] = c[(i - 1) * n + j];
            b[i * n + j] = LCS_UP;
          }
          else
          {
            c[i * n + j] = c[i * n + j - 1];
            b[i * n + j] = LCS_LEFT;
          }
        }
      }
      return b;
    }

    int lcslen(Context ctx, string s, string s2)
    {
      var result = lcs(ctx, s, s2);
      int m = s.Length, n = s2.Length, len = 0;
      int i = m, j = n++;
      while ((i != 0) && (j != 0))
      {
        if (result[i * n + j] == LCS_UPLEFT)
        {
          len++;
          i--;
          j--;
        }
        else if (result[i * n + j] == LCS_UP)
        {
          i--;
        }
        else
          j--;
      }
      return len;
    }
  }
}
