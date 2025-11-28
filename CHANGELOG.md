# Changelog

<!-- There is always Unreleased section on the top. Subsections (Add, Changed, Fix, Removed) should be Add as needed. -->
## Unreleased
- Move repository

# 11.0.0 - 2025-03-17
- [**BC**] Use net9.0

# 10.0.0 - 2024-01-11
- Add an `AlmaTracer`
- Use `AlmaTracer.NoopTracer` when `TracerProvider` is not available or there is a problem with a `Tracer`
- [**BC**] Return `AlmaTracer` instead of `OpenTelemetry.Tracer`

# 9.0.0 - 2024-01-09
- [**BC**] Use net8.0
- Fix package metadata

# 8.0.0 - 2023-09-11
- [**BC**] Use `Alma` namespace

# 7.0.0 - 2023-08-10
- [**BC**] Use net 7.0

# 6.6.0 - 2023-03-03
- Create a static context for new traces
    - Fix memory leak of the staring trace by pregenerate the context before starting a trace (see: [MemoryLeakContextProblem] in the code)

## 6.5.0 - 2022-06-02
- Add custom identifier as a tag to custom scoped trace

## 6.4.0 - 2022-05-23
- Extract trace from http headers in case-insensitive

## 6.3.0 - 2022-05-23
- Start reference span from alive spans only

## 6.2.0 - 2022-05-17
- Fix injecting trace headers to headers with existing keys
- Improve logging spans

## 6.1.0 - 2022-05-17
- Fix extraction of Inactive trace
- Add `TelemetrySpan` and `TelemetrySpanContext` modules with `IsAlive` active patterns

## 6.0.0 - 2022-05-11
- [**BC**] Use `OpenTelemetry` as a base library
- [**BC**] Change environment variables
- [**BC**] Rename `Trace.addBaggage` to `Trace.addEvent`
- Allow custom equality, comparability of Trace types
- Allow Trace types to cast to string

## 5.0.0 - 2022-02-28
- [**BC**] Add previously removed `LiveTrace.Scope` to fix a problem with unfinished scope

## 4.0.0 - 2022-02-22
- Update dependencies
- [**BC**] Remove `ActiveTrace.Scope` and leaving it only as `ActiveTrace.Span` to fix active span problem, when previous span was finished
- [**BC**] Rename `ActiveTrace` type and module to `LiveTrace` (_not to confuse with Active Span_)
    - [**BC**] Also rename `Trace.Active` to `Trace.Live`

## 3.0.0 - 2022-01-05
- [**BC**] Use net6.0

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
