# Changelog

## 1.0.2 - 2024-12-01

### Changed

- FLAG option value is checked for `num`, `long`, `UTF-8` *substrings*, just like the original Hunspell does.
- Checks that flag options are not null added where they were missing in the original Hunspell.
  This affects dictionaries that use the numeric zero flag.
- Duplicates are removed from flag sets.
- The README file has been edited.


## 1.0.1 - 2024-07-08

### Added

- Mapping tables for ISO-8859-10 and ISO-8859-14 encodings, that are not supported by frameworks.


## 1.0.0 - 2024-07-08

- Initial release
