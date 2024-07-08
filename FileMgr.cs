using System.IO;

namespace HunspellSharp
{
  abstract class FileMgr
  {
    protected Stream fin;
    int linenum;

    protected FileMgr(Stream stream)
    {
      fin = stream;
    }

    public bool getline(out Bytes dest)
    {
      if (!ReadLine(out dest)) return false;
      ++linenum;
      return true;
    }

    public string Name => fin is FileStream file ? file.Name : null;
    public int getlinenum() => linenum;

    protected abstract bool ReadLine(out Bytes dest);
  }
}
