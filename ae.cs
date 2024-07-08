using System;

namespace HunspellSharp
{
  [Flags]
  enum ae : byte
  {
    XPRODUCT = (1 << 0),
//    UTF8 = (1 << 1),
    ALIASF = (1 << 2),
    ALIASM = (1 << 3),
//    LONGCOND = (1 << 4)
  }
}
