namespace HunspellSharp
{
  /// <summary>
  /// Warning handler interface
  /// </summary>
  public interface IHunspellWarningHandler
  {
    /// <summary>
    /// Receives an error message.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <returns><code>true</code> if the message was handled. Otherwise the default handler is used.</returns>
    bool HandleWarning(string message);
  }
}
