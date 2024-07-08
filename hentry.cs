namespace HunspellSharp
{
  class hentry
  {
    public ushort[] astr;        // affix flag vector
    public hentry next;          // next word with same hash code
    public hentry next_homonym;  // next homonym word (with same hash code)
    public H_OPT var;            // bit vector of H_OPT hentry options
    public string word;          // variable-length word
    public string data;
    public uint hash;

    public hentry(string word, ushort[] astr, H_OPT var, uint hash)
    {
      this.astr = astr;
      this.var = var;
      this.word = word;
      this.hash = hash;
    }

    public bool Contains(string p) => data != null ? data.Contains(p) : false;
  }
}
