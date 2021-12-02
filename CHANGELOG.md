# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased

## 2.4.1 - 2021-11-02
- Fix extracting headers with multiple values (_ignoring other than the first value_)

## 2.4.0 - 2021-10-04
- Make `TracingLogger` to be enabled only if Tracer is available

## 2.3.0 - 2021-10-04
- Use `Lmc.Logging` to create a logger
- Allow to trace log messages into active trace
    - Add `LoggerProvider.TracingProvider` class
    - Add `LoggerProvider.TracingLogger` class

## 2.2.1 - 2021-09-30
- Fix `Tracer.tracer` function to be lazy

## 2.2.0 - 2021-09-13
- Allow to activate a trace
    - `Trace.Active.activate`
- Add `CustomTracingScope` module and functions

## 2.1.0 - 2021-09-03
- Allow to change logging level by environment variable `JAEGER_LOG_LEVEL`
- Add function to get all ids out of a Trace
    - `Trace.id`
    - `Trace.spanId`
    - `Trace.traceId`
    - `ActiveTrace.id`
    - `ActiveTrace.spanId`
    - `ActiveTrace.traceId`
    - `TraceContext.id`
    - `TraceContext.spanId`
    - `TraceContext.traceId`
- Add more debug logs for some actions (like finishing a span, etc)

## 2.0.0 - 2021-08-03
- Add `TraceContext` type and module
- Add `ActiveTrace` type and module
- [**BC**] Use `TraceContext` and `ActiveTrace` in `Trace` type as a cases subtypes
- [**BC**] Extract trace as a `TraceContext` in trace extension(s)

## 1.5.0 - 2021-08-03
- Add error tag automatically in `addError` function

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
