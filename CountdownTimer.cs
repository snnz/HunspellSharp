using System.Diagnostics;

namespace HunspellSharp
{
  class CountdownTimer : Stopwatch
  {
    const long TIMELIMIT = 1000 / 20;
    int countdown = 100;

    public bool CheckExpired()
    {
      if (countdown == 0) return true;
      if (--countdown == 0)
      {
        if (ElapsedMilliseconds > TIMELIMIT) return true;
        countdown = 100;
      }
      return false;
    }

    public bool IsExpired { get => countdown == 0; set => countdown = 0; }

    new public void Restart() { countdown = 100; base.Restart(); }
  }
}
