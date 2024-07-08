using System;

namespace HunspellSharp
{
  /// <summary>
  /// Info options
  /// </summary>
  [Flags]
  public enum SPELL : ushort
  {
    /// <summary>
    /// the result is a compound word
    /// </summary>
    COMPOUND   = (1 << 0),

    /// <summary>
    /// an explicit forbidden word
    /// </summary>
    FORBIDDEN  = (1 << 1),

    /// <summary>
    /// unused
    /// </summary>
    ALLCAP     = (1 << 2),

    /// <summary>
    /// unused
    /// </summary>
    NOCAP      = (1 << 3),

    /// <summary>
    /// internal
    /// </summary>
    INITCAP    = (1 << 4),

    /// <summary>
    /// internal
    /// </summary>
    ORIGCAP = (1 << 5),

    /// <summary>
    /// possibly forbidden word
    /// </summary>
    WARN       = (1 << 6),

    /// <summary>
    /// permit only 2 dictionary words in the compound
    /// </summary>
    COMPOUND_2 = (1 << 7),

    /// <summary>
    /// limit suggestions for the best ones, i.e. ph:
    /// </summary>
    BEST_SUG   = (1 << 8), 
  }
}
