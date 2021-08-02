# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 1.4.2 - 2021-08-02
- Fix `format` argument of `TracedError.ofError` to receive an `'Error` not a `TracedError` instance

## 1.4.1 - 2021-08-02
- Move `TracedError` type and module to the namespace `Lmc.Tracing` directly

## 1.4.0 - 2021-08-02
- Add `TracedError` type and module
- Add `Trace.addError` function
- Fix extract trace from http not to add tags, since it should be added on the referencing trace

## 1.3.0 - 2021-07-27
- Extract trace from http context with specific tags for HTTP

## 1.2.0 - 2021-07-26
- Add Extension module with `Http` `inject/extract` functions

## 1.1.0 - 2021-07-26
- Add module `Span`
- Add `ChildOf` and `FollowFrom` functions

## 1.0.0 - 2021-07-22
- Initial implementation
