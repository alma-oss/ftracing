# Preferred Patterns

## Core Principles

- **Span lifetime follows the binding keyword.** A trace bound with `use` is finished automatically when it leaves scope (it implements `IDisposable`). A trace bound with `let` must be ended explicitly with `Trace.finish`. Pick `use` for "span covers the rest of this function" and `let` for "span ends at a precise earlier point".
- **`use __ =`** is the idiomatic form when the trace value is never read again — it still finishes on scope exit.
- **The active trace lives in `AsyncLocal`.** `Trace.Active.start` registers the span as current; `Trace.Active.current()` retrieves it within the same logical async flow. Continuity holds across `async`/`task` continuations on the same logical thread, but not when work is handed to an unrelated thread or stored for later (see Composition → cross-async).
- **Graceful degradation is built in.** When required env vars are missing the tracer is a `NoopTracer`; every API call still type-checks and returns an `Inactive` trace instead of throwing. Never guard calls with manual env checks — call the API unconditionally.
- **`Trace` is a value you thread through code.** Annotation functions (`addTags`, `addEvent`, `addError`) take a trace and return the same trace, so they compose with `|>`.

## Recommended API Usage

- **Top-level span per function:** start with `Trace.Active.start "Name"`, optionally pipe through `Trace.addTags`. See `examples.md` → Basic Active Span.
- **Child spans for inner work:** when you hold a parent trace use `Trace.ChildOf.start parent`; when you only know there is a current active trace use `Trace.ChildOf.startFromActive`. See `examples.md` → Child Span Tree.
- **Continue an incoming trace or start fresh:** `Trace.ChildOf.continueOrStartActive extractFn` continues the upstream trace when `extractFn` yields one, otherwise starts a new active span. See `examples.md` → Incoming HTTP Request.
- **Reading identity:** `Trace.id`, `Trace.traceId`, `Trace.spanId`, `Trace.parentId` all return `string option` (`None` when the trace is `Inactive`); `Trace.context` returns `TraceContext option` for propagation.
- **`FollowFrom` vs `ChildOf`:** both build a referenced span and share the same function names; use `ChildOf` for synchronous causal nesting and `FollowFrom` for fire-and-forget / asynchronous causation where the parent does not wait for the child.

## Error Handling

- Annotate a span with a failure using `Trace.addError`, which converts a `TracedError` into the conventional OpenTelemetry error tags (`error`, `event`, `error.object`, `message`, and optionally `stack`, `error.kind`).
- Build the `TracedError` with `TracedError.ofExn` from a caught exception, or `TracedError.ofError format` from a domain error value plus a formatting function. See `examples.md` → Tagging an Error.
- Because annotation returns the trace, error tagging composes inside `Result` / `AsyncResult` pipelines without breaking the flow.

## Composition

- **Span trees** mirror call structure: one active parent, child spans started inside callees. A finished tree reads as the parent name with its children nested beneath.
- **Cross-async persistence:** when the active trace would be lost (work resumed on a different thread, or different functions that cannot share a variable), store it with `TracingState.storeActiveTrace identifier` and retrieve it later with `TracingState.loadActiveTrace identifier`. Always pair a store with a `clearActiveTrace` (or use `ScopedTrace`, which clears on dispose) to avoid leaking entries. See `examples.md` → Custom Tracing Scope.
- Each stored trace needs a **unique identifier**; reusing an identifier across unrelated flows causes collisions.

## Integration with Other Libraries

- **Logging into spans:** register `TracingProvider.create()` as a provider on `Alma.Logging`'s `LoggerFactory.create`. Resulting log calls become events on the current active trace. Only messages at or above the active log level are recorded — set level `Tracing` to capture everything. See `examples.md` → Logging Into a Span.
- **HTTP propagation:** for incoming requests extract from the `HttpContext` (`Http.extractFromContext`) or a raw header sequence (`Http.extractFromHeaders`); for outgoing requests inject a specific trace with `Http.inject` or the current active trace with `Http.injectActive`. The library adds an `X-B3-ParentSpanId` workaround so parent ids survive propagation. See `examples.md` → Incoming HTTP Request and Outgoing HTTP Request.

## Naming Conventions

- Span names are human-readable phrases describing the operation ("Receive request", "Validation"), not identifiers.
- Tags are `(string * string)` pairs; a `"component"` tag identifying the module is a common, useful convention.
- Custom-scope identifiers are short stable strings (e.g. `"main"`) unique to one logical flow.

## Testing Recommendations

- Tests run under Expecto via `./build.sh -t tests`.
- Without the required env vars the tracer is a `NoopTracer`, so traced code is safe to exercise in tests and `Trace.*` reads return `None` / `Inactive`. Assert on this `Inactive` behavior, or set the env vars to assert on real ids. See `examples.md` → Test Against Inactive Tracer.
