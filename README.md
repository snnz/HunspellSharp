# HunspellSharp

This is a C# port of [Hunspell library](https://github.com/hunspell/hunspell).


## Features

- Targets .NET Framework (4.0 the lowest), .NET 6+ and .NET Standard 2.0.
- Uses only safe managed code.
- Querying methods ([`Spell`](#spelling), [`Suggest`](#suggestions), etc) are thread-safe.


## Usage

### Preliminary

If your application is targeting .NET or .NET Standard and intended to work with dictionaries encoded not as ISO-8859-1 or UTF-8,
install [System.Text.Encoding.CodePages package](https://www.nuget.org/packages/System.Text.Encoding.CodePages/) and make additional encodings available by calling
`Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` before loading Hunspell files.

### Constructors

With file paths:

```csharp
var hunspell = new Hunspell("en.aff", "en.dic");
```

Like original Hunspell, it tries to open hzipped files (.hz) in case the plain files are not found. Optional hzip key can be added:

```csharp
var hunspell = new Hunspell("en.aff", "en.dic", key);
```

The `key` argument is a byte array. Use `Encoding.GetBytes` of the appropriate encoding to get byte array from a string.

With streams:

```csharp
var hunspell = new Hunspell(affStream, dicStream);
```

In case the stream is packed with `hzip`, wrap it in `HzipStream`. The `HzipStream` contructor also accepts optional hzip key.

### Disposing

A `Hunspell` instance uses some disposable resources and thus is disposable itself, so do not forget to add a `using` statement or explicitly call `hunspell.Dispose()` when it is no longer needed.


### Spelling

```csharp
bool result = hunspell.Spell("sample");
```

There are also methods that provide additional info and the root word:

```csharp
result = hunspell.Spell("sample", out var info);
result = hunspell.Spell("sample", out var info, out var root);
```


### Suggestions

Generate suggestions for a misspelled word:

```csharp
List<string> suggestions = hunspell.Suggest("sapmle");
```

Simplified XML API input is supported. See the [Hunspell manual](https://github.com/hunspell/hunspell/blob/master/man/hunspell.3) for a description.

Suggest words by applying suffix rules to the root word:

```csharp
List<string> suggestions = hunspell.SuffixSuggest("sample");
```

### Morphology

Get morphlogical description:

```csharp
List<string> description = hunspell.Analyze("examples");
```

Generate words using morphlogical description:

```csharp
List<string> results = hunspell.Generate("sample", description);
```

or by example:

```csharp
List<string> results = hunspell.Generate("sample", "examples");
```

Get stem(s):

```csharp
List<string> stems = hunspell.Stem("samples");
```

Using previous result of the morphological analysis:

```csharp
List<string> stems = hunspell.Stem("samples", description);
```

### Dictionary manipulation

> [!NOTE]
> These methods are not thread-safe and have to be run exclusively.

Append extra dictionary, from a file path or a stream:

```csharp
hunspell.AddDic("some.dic");
hunspell.AddDic(dicStream);
```

As in the contructors, optional hzip key or HzipStream can be used:

```csharp
hunspell.AddDic("some.dic", key);
hunspell.AddDic(new HzipStream(hzDicStream, key));
```

Add a word to the run-time dictionary:

```csharp
hunspell.Add("word");
```

With flags and morphological description:

```csharp
hunspell.AddWithFlags("word", flags, description);
```

With affixes using an example word:

```csharp
hunspell.AddWithAffix("word", "example");
```

Remove word from the run-time dictionary:

```csharp
hunspell.Remove("word");
```

### Various dictionary properties and methods

Dictionary encoding:

```csharp
Encoding encoding = hunspell.DicEncoding;
```

Dictionary language number; enum values match the numbers in the original Hunspell:

```csharp
LANG langnum = hunspell.LangNum;
```

Affix and dictionary file version:

```csharp
string version = hunspell.Version;
```

Extra word characters defined in the affix file:

```csharp
char[] wordchars = hunspell.Wordchars;
```

Input conversion according to the ICONV table specified in the affix file:

```csharp
string output = hunspell.InputConv("input");
```


### Error handling

HunspellSharp throws exceptions of the type `HunspellException` on severe affix/dictionary format errors. In case you need the behavior of the original Hunspell, which always just issues warnings but continues execution, set static property `StrictFormat` to `false`:

```csharp
Hunspell.StrictFormat = false;
```

### Warning handling

By default, HunspellSharp sends warning messages to `System.Diagnostics.Debug`. To change this, create a class implementing `IHunspellWarningHandler` interface and pass the reference to an instance of it to the static method `SetWarningHandler`:

```csharp
class CustomWarningHandler : IHunspellWarningHandler
{
  public bool HandleWarning(string message)
  {
    Console.WriteLine(message);
    return true;
  }
}

...

Hunspell.SetWarningHandler(new CustomWarningHandler());
```


## Techinical notes

HunspellSharp relies on `System.Globalization` features when converting characters to lower- or uppercase.
If language is specified in the affix file, appropriate `CultureInfo` is used. 
If not, the culture is either guessed from the encoding, or defaults to the invariant culture.
In the latter case, some results may differ from the original Hunspell that uses embedded case conversion tables.
For example, the invariant culture does not convert capital 'İ' to lowercase 'i', so Turkish words containing 'İ'
will not be recognized as forms of lowercase dictionary words if no language is specified in the affix file and
dictionary encoding is not ISO-8859-9. To avoid this, specify correct dictionary language explicitly.

N-gram suggestions may sometimes differ from the original ones, because their choice depends on the order of words in the internal hash tables,
sorting algorithm and other factors, which are not the same as in the original Hunspell.

This port is rather straightforward. Non-public source code does not follow usual C# conventions deliberately, in attempt to keep resemblance to the original C++ code wherever possible.
