using System.Diagnostics;

namespace HunspellSharp
{
  class Timer : Stopwatch
  {
    public bool IsExpired { get; set; }
    new public void Restart() { IsExpired = false; base.Restart(); }
  }
}
