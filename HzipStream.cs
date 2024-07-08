using System.IO;

namespace HunspellSharp
{
  /// <summary>
  /// Not a real hzip stream, it is only used to indicate that the wrapped stream contains hzip data,
  /// for the sake of passing it to the <see cref="Hunspell"/> constructor.
  /// All overridden methods just call the methods of the wrapped stream; unpacked data is not available from outside.
  /// </summary>
  public class HzipStream : Stream
  {
    internal Stream _stream;
    internal byte[] _key;

    /// <summary>
    /// Initializes a new instance of the HzipStream for the specific stream and an optional hzip key.
    /// </summary>
    /// <param name="stream">The underlying stream</param>
    /// <param name="key">Optional hzip key</param>
    public HzipStream(Stream stream, byte[] key = null)
    {
      _stream = stream;
      _key = key;
    }

    internal Hunzip CreateHunzip() => new Hunzip(_stream, _key);

    /// <inheritdoc/>
    public override bool CanRead => _stream.CanRead;
    /// <inheritdoc/>
    public override bool CanSeek => _stream.CanSeek;
    /// <inheritdoc/>
    public override bool CanWrite => _stream.CanWrite;
    /// <inheritdoc/>
    public override long Length => _stream.Length;
    /// <inheritdoc/>
    public override long Position { get => _stream.Position; set => _stream.Position = value; }
    /// <inheritdoc/>
    public override void Flush() => _stream.Flush();
    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);
    /// <inheritdoc/>
    public override void SetLength(long value) => _stream.SetLength(value);
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => _stream.Write(buffer, offset, count);
    /// <inheritdoc/>
    public override void Close()
    {
      try
      {
        _stream.Close();
      }
      finally
      {
        base.Dispose(true);
      }
    }
    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
      try
      {
        if (disposing) _stream.Dispose();
      }
      finally
      {
        base.Dispose(disposing);
      }
    }
  }
}
