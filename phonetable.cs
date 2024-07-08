using System;
using System.Collections.Generic;
using System.Text;

namespace HunspellSharp
{
  class phonetable
  {
    const int MAXPHONETLEN = 256;

    private List<char[]> rules = new List<char[]>();
    private Dictionary<char, int> hash;

    public void Add(string rule)
    {
      var chars = new char[rule.Length + 1];
      rule.CopyTo(0, chars, 0, rule.Length);
      rules.Add(chars);
    }

    public int Count => rules.Count;

    public void init_hash()
    {
      hash = new Dictionary<char, int>();

      for (int i = 0; i < rules.Count; i += 2)
      {
        /*  set hash value  */
        var c = rules[i][0];
        if (!hash.ContainsKey(c)) hash.Add(c, i);
      }
    }

    static void strmove(char[] s, int dest, int src)
    {
      while (s[src] != '\0')
        s[dest++] = s[src++];
      s[dest] = '\0';
    }

    public string phonet(Context ctx, string inword)
    {
      int i, k = 0, p, z, k0, n0, p0 = -333;
      char c;

      int len = inword.Length;
      if (len > MAXPHONETLEN)
        return string.Empty;

      var word = new char[inword.Length + 1];
      inword.CopyTo(0, word, 0, inword.Length);

      var target = ctx.PopStringBuilder();
      /*  check word  */
      i = z = 0;
      while ((c = word[i]) != '\0') {
        int z0 = 0;

        if (hash.TryGetValue(c, out var n) && rules[n][0] != '\0')
        {
          /*  check all rules for the same letter  */
          while (rules[n][0] == c) {
            /*  check whole string  */
            k = 1; /* number of found letters  */
            p = 5; /* default priority  */
            int s = 1; /*  important for (see below)  "*(s-1)"  */
            var rule = rules[n];

            while (rule[s] != '\0' && word[i + k] == rule[s] && !char.IsDigit(rule[s]) &&
                   "(-<^$".IndexOf(rule[s]) < 0) {
              k++;
              s++;
            }
            if (rule[s] == '(') {
              /*  check letters in "(..)"  */
              if (char.IsLetter(word[i + k])  // ...could be implied?
                  && Array.IndexOf(rule, word[i + k], s + 1) >= 0) {
                k++;
                while (rule[s] != '\0' && rule[s] != ')')
                  s++;
                if (rule[s] == ')')
                  s++;
              }
            }
            p0 = rule[s];
            k0 = k;
            while (rule[s] == '-' && k > 1)
            {
              k--;
              s++;
            }
            if (rule[s] == '<')
              s++;
            if (char.IsDigit(rule[s])) {
              /*  determine priority  */
              p = rule[s] - '0';
              s++;
            }
            if (rule[s] == '^' && rule[s + 1] == '^')
              s++;

            if (rule[s] == '\0'  || (rule[s] == '^' && (i == 0 || !char.IsLetter(word[i - 1])) &&
                                     (rule[s + 1] != '$' || !char.IsLetter(word[i + k0]))) ||
                (rule[s] == '$' && i > 0 && char.IsLetter(word[i - 1]) &&
                 !char.IsLetter(word[i + k0])))
            {
              /*  search for followup rules, if:     */
              /*  parms.followup and k > 1  and  NO '-' in searchstring */
              char c0 = word[i + k - 1];

              //            if (parms.followup  &&  k > 1  &&  n0 >= 0
              if (k > 1 && hash.TryGetValue(c0, out n0) && p0 != '-' && word[i + k] != '\0' && rules[n0][0] != '\0')
              {
                /*  test follow-up rule for "word[i+k]"  */
                while (rules[n0][0] == c0)
                {
                  /*  check whole string  */
                  k0 = k;
                  p0 = 5;
                  s = 1;
                  rule = rules[n0];
                  while (rule[s] != '\0' && word[i + k0] == rule[s] &&
                         !char.IsDigit(rule[s]) &&
                         "(-<^$".IndexOf(rule[s]) < 0) {
                    k0++;
                    s++;
                  }
                  if (rule[s] == '(')
                  {
                    /*  check letters  */
                    if (char.IsLetter(word[i + k0]) &&
                        Array.IndexOf(rule, word[i + k0], s + 1) >= 0)
                    {
                      k0++;
                      while (rule[s] != ')' && rule[s] != '\0')
                        s++;
                      if (rule[s] == ')')
                        s++;
                    }
                  }
                  while (rule[s] == '-')
                  {
                    /*  "k0" gets NOT reduced   */
                    /*  because "if (k0 == k)"  */
                    s++;
                  }
                  if (rule[s] == '<')
                    s++;
                  if (char.IsDigit(rule[s])) {
                    p0 = rule[s] - '0';
                    s++;
                  }

                  if (rule[s] == '\0'
                      /*  *s == '^' cuts  */
                      || (rule[s] == '$' && !char.IsLetter(word[i + k0])))
                  {
                    if (k0 == k)
                    {
                      /*  this is just a piece of the string  */
                      n0 += 2;
                      continue;
                    }

                    if (p0 < p)
                    {
                      /*  priority too low  */
                      n0 += 2;
                      continue;
                    }
                    /*  rule fits; stop search  */
                    break;
                  }
                  n0 += 2;
                } /*  End of "while (parms.rules[n0][0] == c0)"  */

                if (p0 >= p && rules[n0][0] == c0)
                {
                  n += 2;
                  continue;
                }
              } /* end of follow-up stuff */

              /*  replace string  */
              s = 0;
              rule = rules[n + 1];
              p0 = (rules[n][0] != '\0' &&
                    Array.IndexOf(rules[n], '<', 1) >= 0)
                       ? 1
                       : 0;
              if (p0 == 1 && z == 0)
              {
                /*  rule with '<' is used  */
                if (target.Length > 0 && rule[s] != '\0' &&
                    (target[target.Length - 1] == c || target[target.Length - 1] == rule[s]))
                {
                  --target.Length;
                }
                z0 = 1;
                z = 1;
                k0 = 0;
                while (rule[s] != '\0' && word[i + k0] != '\0')
                {
                  word[i + k0] = rule[s];
                  k0++;
                  s++;
                }
                if (k > k0)
                  strmove(word, i + k0, i + k);

                /*  new "actual letter"  */
                c = word[i];
              }
              else
              { /* no '<' rule used */
                i += k - 1;
                z = 0;
                while (rule[s] != '\0' && rule[s + 1] != '\0' && target.Length < len)
                {
                  if (target.Length == 0 || target[target.Length - 1] != rule[s])
                  {
                    target.Append(rule[s]);
                  }
                  s++;
                }
                /*  new "actual letter"  */
                c = rule[s];
                if (rules[n][0] != '\0' &&
                    Array.IndexOf(rules[n], "^^", 1) >= 0)
                {
                  if (c != '\0')
                  {
                    target.Append(c);
                  }
                  strmove(word, 0, i + 1);
                  i = 0;
                  z0 = 1;
                }
              }
              break;
            } /* end of follow-up stuff */
            n += 2;
          } /*  end of while (parms.rules[n][0] == c)  */
        }   /*  end of if (n >= 0)  */
        if (z0 == 0)
        {
          if (k != 0 && p0 == 0 && target.Length < len && c != '\0')
          {
            /*  condense only double letters  */
            target.Append(c);
            // printf("\n setting \n");
          }

          i++;
          z = 0;
          k = 0;
        }
      } /*  end of   while ((c = word[i]) != '\0')  */

      return ctx.ToStringPushStringBuilder(target);
    } /*  end of function "phonet"  */
  }
}
