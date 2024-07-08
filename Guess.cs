namespace HunspellSharp
{
  struct Guess
  {
    public string word, orig;

    public Guess(guessword src)
    {
      word = src.word;
      orig = src.orig;
    }
  }
}
