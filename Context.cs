namespace HunspellSharp
{
  class Context : StringHelper
  {
    public string sfxappnd;       // BUG: not stateless // previous suffix for counting syllables of the suffix
    public int sfxextra;          // BUG: not stateless // modifier for syllable count of sfxappnd
    public ushort sfxflag;        // BUG: not stateless
    public SfxEntry sfx;          // BUG: not stateless
    public PfxEntry pfx;          // BUG: not stateless

    public readonly Timer compoundCheckTimer = new Timer();
    public readonly CountdownTimer suggestInnerTimer = new CountdownTimer();

    public byte[] lcsB, lcsC;

    public metachar_data[] btinfo;

    hentry[] rwords;              // buffer for COMPOUND pattern checking

    public hentry[] GetCompoundCheckBuffer()
    {
      if (rwords == null) rwords = new hentry[100];
      return rwords;
    }

    hentry[] roots;
    string[] rootsphon;
    Guess[] guess;
    int[] scores, scoresphon, gscore;
    guessword[] guessword;

    public void GetRootsBuffers(out hentry[] roots, out string[] rootsphon, out int[] scores, out int[] scoresphon)
    {
      if (this.roots == null)
      {
        this.roots = new hentry[Hunspell.MAX_ROOTS];
        this.rootsphon = new string[Hunspell.MAX_ROOTS];
        this.scores = new int[Hunspell.MAX_ROOTS];
        this.scoresphon = new int[Hunspell.MAX_ROOTS];
      }
      roots = this.roots;
      rootsphon = this.rootsphon;
      scores = this.scores;
      scoresphon = this.scoresphon;
    }

    public void GetGuessBuffers(out Guess[] guess, out int[] gscore, out guessword[] guessword)
    {
      if (this.guess == null)
      {
        this.guess = new Guess[Hunspell.MAX_GUESS];
        this.gscore = new int[Hunspell.MAX_GUESS];
        this.guessword = new guessword[Hunspell.MAX_WORDS];
      }
      guess = this.guess;
      gscore = this.gscore;
      guessword = this.guessword;
    }
  }
}
