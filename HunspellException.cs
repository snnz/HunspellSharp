using System;

namespace HunspellSharp
{
  /// <summary>
  /// HunspellSharp exception
  /// </summary>
  public class HunspellException : Exception
  {
    internal HunspellException(string message) : base(message) { }
  }
}
