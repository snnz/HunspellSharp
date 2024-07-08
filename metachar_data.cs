namespace HunspellSharp
{
  struct metachar_data
  {
    public short btpp;  // metacharacter (*, ?) position for backtracking
    public short btwp;  // word position for metacharacters
    public int btnum;   // number of matched characters in metacharacter
  }
}
