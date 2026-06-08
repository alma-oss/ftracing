---
name: add-trace
description: 'Add OpenTelemetry tracing spans to F# code in this repo. Use when instrumenting a function, adding child spans, propagating trace context over HTTP, annotating spans with tags/events, or using custom tracing scope. Trigger phrases: "add tracing", "instrument with spans", "add a span", "trace this function", "propagate trace context", "inject traceparent".'
argument-hint: 'Describe what you want to trace (function name, module, HTTP call, etc.)'
---

# Add Tracing to F# Code

## Key Types

- `Trace` DU — `Live of TelemetrySpan | Inactive`
- `AlmaTracer` — `ActiveTracer of Tracer | NoopTracer` (graceful degradation when env vars missing)

## Span Lifecycle Patterns

### Auto-dispose with `use` (preferred)
```fsharp
use trace = Trace.Span.start "operation-name"
// span ends automatically at scope exit
```

### Manual finish with `let` (when span must outlive the lexical scope)
```fsharp
let trace = Trace.Span.start "operation-name"
// ... pass trace around ...
Trace.finish trace
```

### Ignore the binding
```fsharp
use __ = Trace.Span.start "background-work"
```

## Child Spans

```fsharp
let parent = Trace.Span.start "parent"
use child = Trace.ChildOf.start parent
// or start from whatever is currently active:
use child = Trace.ChildOf.startFromActive ()
```

## Tags and Events

```fsharp
trace |> Trace.addTags [ "userId", "123"; "env", "prod" ]
trace |> Trace.addEvent "cache-miss"
```

## HTTP Propagation (W3C Trace Context)

### Extract from incoming request (ASP.NET Core)
```fsharp
open Alma.Tracing.Extension

let traceContext = Http.extractFromContext httpContext   // returns TraceContext option
// or from raw headers:
let traceContext = Http.extractFromHeaders headers       // seq<string * string>

use trace = Trace.ChildOf.continueOrStartActive (fun () -> traceContext)
```

### Inject into outgoing request
```fsharp
let headers = ResizeArray<string * string>()
Http.injectActive headers  // injects traceparent/tracestate of current active trace
// or for a specific trace:
Http.inject trace headers
```

## Custom Scoped Trace (cross-async boundary)

```fsharp
open Alma.Tracing.CustomTracingScope

let identifier = httpContext.TraceIdentifier
let trace = Trace.Span.start "long-running"
TracingState.storeActiveTrace identifier trace

// later, in another async continuation:
let trace = TracingState.loadActiveTrace identifier
use scoped = new ScopedTrace(identifier, trace)  // auto-finishes and clears on dispose
```

## Module Placement

- HTTP propagation: `src/Extension.fs` (`Alma.Tracing.Extension`)
- Custom scope: `src/CustomTracingScope.fs` (`Alma.Tracing.CustomTracingScope`)
- Core spans: `src/Tracing.fs` (`Alma.Tracing`)

## Pitfalls

- `use` disposes immediately at end of `let` binding block — for async code, prefer `use` inside `async { }` or `task { }` to avoid premature disposal
- `TracingState` uses global mutable state — identifiers must be unique (use `HttpContext.TraceIdentifier`)
- `NoopTracer` is returned silently when `TRACING_SERVICE_NAME`/`TRACING_OTLP_ENDPOINT` are not set — no exception is thrown
- All public modules must have `[<RequireQualifiedAccess>]`
