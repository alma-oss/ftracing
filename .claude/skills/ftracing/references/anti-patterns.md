# Anti-Patterns

Each entry is **mistake → why → fix**.

## Lifetime & finishing

- **Binding a span with `let` and never calling `Trace.finish`** → the span never ends, so it is never exported and leaks resources. → Either bind with `use` (auto-finishes on scope exit) or keep `let` and call `Trace.finish` at the exact end point.
- **Calling `Trace.finish` on a `use`-bound span** → the span is finished twice (once explicitly, once on dispose). → Choose one mechanism: `use` for automatic finishing, `let` + explicit `Trace.finish` otherwise.
- **Expecting `Trace.Active.current()` to return the span after the async work moved to an unrelated thread** → the active trace lives in `AsyncLocal` and is lost across that boundary, so you get `Inactive`. → Persist it with `TracingState.storeActiveTrace` / `loadActiveTrace`, or use `ScopedTrace` (see `examples.md` → Custom Tracing Scope).

## Custom scope

- **Storing a trace in the custom scope and never clearing it** → the entry stays in the global state forever (a leak), and reusing the identifier later returns a stale finished trace. → Always pair `storeActiveTrace` with `clearActiveTrace`, or use `ScopedTrace` whose `Finish()`/dispose clears automatically.
- **Reusing the same custom-scope identifier across unrelated flows** → concurrent flows collide on one stored trace and corrupt each other's parent/child relationships. → Use a unique identifier per logical flow.

## Configuration assumptions

- **Assuming traces export as soon as you call `Trace.*`** → without `TRACING_SERVICE_NAME` and `TRACING_THRIFT_HOST` the tracer is a `NoopTracer`; calls succeed but nothing is recorded or exported. → Set both required env vars in the runtime environment; treat missing-config as "no traces", not an error.
- **Guarding tracing calls with manual `if env var set` checks** → redundant and error-prone; the library already no-ops safely. → Call the `Trace.*` API unconditionally and let `NoopTracer` handle the absent-config case.
- **Pattern-matching `Trace.id` / `Trace.spanId` as a plain `string`** → these return `string option` (`None` when `Inactive`), so a non-optional match will not compile or will mishandle the inactive case. → Handle the `option`; treat `None` as "no active trace".

## Propagation

- **Manually adding B3 headers (including `X-B3-ParentSpanId`) to outgoing requests** → you duplicate or fight the library's own injection and its parent-id workaround, producing malformed or conflicting headers. → Use `Http.inject` (specific trace) or `Http.injectActive` (current active trace) and let it manage all B3 headers.
- **Reading incoming context off raw strings by hand** → you reimplement B3 parsing and the lowercase-header fallback the library already provides. → Use `Http.extractFromContext` for an `HttpContext` or `Http.extractFromHeaders` for a raw header sequence.

## Logging integration

- **Expecting every log line to appear as a span event regardless of level** → `TracingLogger` only records messages at or above the active log level, and only when an active trace exists. → Set the log level to `Tracing` to capture all messages, and ensure a span is active when the logging happens.
- **Relying on `TracingProvider` to log when no span is active** → with no active trace the message is silently dropped (it has nowhere to attach). → Start an active span before the logging that must be captured.

## Wrong abstractions / legacy

- **Treating the `Trace` value as `Active of TelemetrySpan | Inactive`** → that two-case shape is outdated; the real type is `Live of LiveTrace | Context of TraceContext | Inactive`, and code assuming the old cases will not match. → Match `Live` / `Context` / `Inactive`, or prefer the `Trace.*` helper functions instead of matching the DU directly.
- **Reaching for the `OpenTelemetry.Exporter.Jaeger` API directly to reconfigure export** → that exporter is deprecated upstream and is wired internally from env vars; bypassing it couples your code to a moving, deprecated surface. → Configure via the documented env vars; do not call the exporter API from consuming code.
