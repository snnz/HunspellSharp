using System;

namespace HunspellSharp
{
  [Flags]
  enum H_OPT : byte
  {
    PHON    = (1 << 2), // is there ph: field in the morphological data?
    INITCAP = (1 << 3)  // is dictionary word capitalized?
  }
}
