---
name: ftracing
description: Use whenever generating or reviewing F# code that creates distributed-tracing spans with the Alma.Tracing library — calls to Trace.Active.start, Trace.ChildOf.start / startFromActive / continueOrStartActive, Trace.finish, Trace.addTags, Trace.addEvent, Trace.addError, or that uses HTTP B3 propagation (Http.extractFromContext, Http.inject, Http.injectActive), the cross-async custom scope (TracingState, ScopedTrace), or the tracing logger (TracingProvider.create). Trigger also on mentions of OpenTelemetry spans in F#, Jaeger Thrift export, NoopTracer / AlmaTracer, TRACING_SERVICE_NAME / TRACING_THRIFT_HOST configuration, or parent/child span trees in F# web services and workers.
---

# F-Tracing

Library: [alma-oss/ftracing](https://github.com/alma-oss/ftracing)
NuGet: `Alma.Tracing`

## Purpose

`Alma.Tracing` is an F# library for distributed tracing built on OpenTelemetry. It provides a high-level, idiomatic F# API for creating and managing trace spans (active, child, follows-from, custom-scoped), propagating trace context over HTTP using the B3 format, and surfacing log messages as span events. Traces export to Jaeger over Thrift, and optionally to the console.

## When to Use

- Instrumenting F# functions, web request handlers, or background workers with spans.
- Building parent/child span trees that mirror call structure.
- Propagating trace context between services over HTTP (incoming extract, outgoing inject).
- Persisting an active trace across async boundaries that lose `AsyncLocal` continuity.
- Forwarding log messages into the current span.

## When NOT to Use

- General application logging (use `Alma.Logging` directly).
- Metrics or non-trace OpenTelemetry signals (not covered by this library).
- Configuring the Jaeger exporter or sampler by hand — initialization is driven by environment variables, not public API.

## Main Concepts

- `AlmaTracer` — `ActiveTracer of Tracer | NoopTracer`; the library degrades to `NoopTracer` (no-op, no exceptions) when required env vars are absent.
- `Trace` — the core value: `Live of LiveTrace | Context of TraceContext | Inactive`. Most API functions accept and return a `Trace`.
- `Trace.Active` — span stored in `AsyncLocal` as the current active trace: `start`, `current`, `activate`, `finish`.
- `Trace.Span` — a standalone span not registered as active: `start`, `startAt`.
- `Trace.ChildOf` / `Trace.FollowFrom` — start a span referencing a parent; variants include `start`, `startFromActive`, `startActive`, `startActiveFromActive`, `continueOrStartActive`, `continueOrStart`.
- `Trace.finish` / `addTags` / `addEvent` / `addError` — end or annotate a span; `id` / `traceId` / `spanId` / `parentId` / `context` read its identity.
- `TraceContext` — the propagatable identity of a span (trace id + span id).
- `TracedError` — error payload turned into span tags by `Trace.addError`.
- `Http` (namespace `Alma.Tracing.Extension`) — B3 context propagation: `extractFromContext`, `extractFromHeaders`, `inject`, `injectActive`.
- `TracingState` / `ScopedTrace` (`Alma.Tracing.CustomTracingScope`) — store/load/clear an active trace by string identifier across async threads.
- `TracingProvider` (`Alma.Tracing.LoggerProvider`) — an `ILoggerProvider` whose loggers write log messages as events on the current active trace.

## Configuration (environment variables)

- Required: `TRACING_SERVICE_NAME`, `TRACING_THRIFT_HOST`. If either is missing, the tracer is a `NoopTracer` and nothing is exported.
- Recommended: `TRACING_TAGS` (comma-separated `key=value` resource attributes).
- Optional: `TRACING_LOG_TO`, `TRACING_LOG_LEVEL`, `TRACING_LOG_META` (internal logging), `TRACING_EXPORT_CONSOLE="on"` (debug span dump to stdout).

## Related Libraries

- `Alma.Logging` — required for `TracingProvider`; configures the library's internal logger.
- `Alma.State` — backs the custom tracing scope storage.
- `Feather.ErrorHandling` — `Result` / `AsyncResult` composition used around traced calls.
- `Microsoft.AspNetCore.Http` — `HttpContext` source for incoming-header extraction.

## Keywords for Search

Alma.Tracing, ftracing, F# tracing, OpenTelemetry F#, distributed tracing, span, active span, child span, Trace.Active.start, Trace.ChildOf, continueOrStartActive, Trace.finish, addTags, addEvent, addError, NoopTracer, AlmaTracer, TraceContext, B3 propagation, Http.injectActive, Http.extractFromContext, X-B3-ParentSpanId, ScopedTrace, TracingState, custom tracing scope, AsyncLocal, TracingProvider, Jaeger, Thrift, TRACING_SERVICE_NAME, TRACING_THRIFT_HOST

## Reference Files

- For composition principles, finishing rules, error handling, integration, and recommended API usage, read `references/preferred-patterns.md`.
- For known pitfalls, incorrect assumptions, and legacy usage to avoid, read `references/anti-patterns.md`.
- For worked, self-contained code examples (the only place code lives in this skill), read `references/examples.md`.
