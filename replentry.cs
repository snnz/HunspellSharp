namespace HunspellSharp
{
  // two character arrays
  class replentry
  {
    public string pattern;
    public string[] outstrings; // med, ini, fin, isol

    public replentry(string ph, string os, int type = 0) 
    {
      pattern = ph;
      outstrings = new string[4];
      outstrings[type] = os;
    }
  }
}
