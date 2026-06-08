# AGENTS.md — Alma.Tracing

## Project Purpose

`Alma.Tracing` is an F# NuGet library for distributed tracing in web applications. Built on OpenTelemetry, it provides a high-level F# API for creating and managing trace spans (active, child, custom-scoped), HTTP context propagation (W3C Trace Context), parent-based sampling, and a tracing-aware logger provider. It exports traces via OTLP (OpenTelemetry Protocol) and optionally to the console.

## Tech Stack

- **Language:** F# (.NET 10)
- **Package manager:** Paket
- **Build system:** FAKE (F# Make) via `build.sh`
- **Test framework:** Expecto
- **NuGet package:** `Alma.Tracing`
- **Repository:** <https://github.com/alma-oss/ftracing>

## Key Dependencies

- `FSharp.Core ~> 10.0`, `FSharp.Data ~> 6.0`
- `OpenTelemetry ~> 1.7` — core tracing SDK
- `OpenTelemetry.Api ~> 1.7` — API abstractions
- `OpenTelemetry.Exporter.Console ~> 1.7` — optional console exporter
- `OpenTelemetry.Exporter.OpenTelemetryProtocol ~> 1.7` — OTLP exporter (gRPC/HTTP)
- `OpenTelemetry.Instrumentation.AspNetCore ~> 1.7` — automatic ASP.NET Core instrumentation
- `OpenTelemetry.Instrumentation.Http ~> 1.7` — automatic HTTP client instrumentation
- `Microsoft.AspNetCore.Http ~> 2.2` — `HttpContext` for header extraction
- `Microsoft.Extensions.Logging ~> 10.0` — logger abstractions
- `Feather.ErrorHandling ~> 2.0` — `AsyncResult`, `Result` operators
- `Alma.Logging ~> 12.0` — `LoggerFactory.create` with Serilog support
- `Alma.State ~> 11.0` — `ConcurrentStorage.State` for custom tracing scope

## Commands

```bash
# Install dependencies
dotnet paket install

# Build
./build.sh build

# Run tests
./build.sh -t tests
```

## Environment Variables

The library supports standard OTel env vars (precedence) and custom `TRACING_*` fallbacks.

### Required

| Standard (precedence) | Custom (fallback) | Description |
|---|---|---|
| `OTEL_SERVICE_NAME` | `TRACING_SERVICE_NAME` | OpenTelemetry service name |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `TRACING_OTLP_ENDPOINT` | OTLP collector endpoint (e.g., `http://otel-collector:4317`) |

### Sampling

| Standard (precedence) | Custom (fallback) | Description |
|---|---|---|
| `OTEL_TRACES_SAMPLER` | `TRACING_SAMPLER` | Root sampler: `always_on` (default), `always_off`, `traceidratio` |
| `OTEL_TRACES_SAMPLER_ARG` | `TRACING_SAMPLER_ARG` | Sampler argument (ratio for `traceidratio`, e.g. `0.1`) |

### Recommended

| Variable | Description |
|---|---|
| `TRACING_TAGS` | Comma-separated `key=value` pairs added as resource attributes |

### Optional (logging)

| Variable | Description |
|---|---|
| `TRACING_LOG_TO` | Log destination (see `Alma.Logging`) |
| `TRACING_LOG_LEVEL` | Log level for tracing internals |
| `TRACING_LOG_META` | Log metadata (e.g., `domain:DOMAIN; context:CONTEXT`) |
| `TRACING_EXPORT_CONSOLE` | Set to `"on"` to enable console span export (debugging) |

## Project Structure

```
├── Tracing.fsproj                # Project file (version, package metadata)
├── AssemblyInfo.fs               # Auto-generated assembly info
├── src/
│   ├── Utils.fs                  # Internal helpers (getEnvVarValue, getEnvVarWithFallback, etc.)
│   ├── Tracing.fs                # Core: AlmaTracer, Tracer init, Trace module (Active, ChildOf, finish, addTags, addEvent)
│   ├── Extension.fs              # HTTP propagation: Http.extractFromContext, Http.inject, Http.injectActive
│   ├── CustomTracingScope.fs     # Custom scope: TracingState (store/load/clear), ScopedTrace disposable
│   ├── ServiceCollectionExtensions.fs # TracingConfig.configureTracing for ASP.NET Core / Giraffe / Saturn
│   └── Logger.fs                 # TracingLogger + TracingProvider (ILoggerProvider that writes to active trace)
├── tests/
│   ├── tests.fsproj              # Test project
│   ├── Tests.fs                  # Test runner entry point
│   ├── Trace.fs                  # Core tracing tests
│   ├── ActiveTrace.fs            # Active trace tests
│   ├── Propagation.fs            # W3C Trace Context propagation tests
│   └── KafkaPropagation.fs       # Kafka header propagation tests
├── example/                      # Example usage (if present)
├── build/                        # FAKE build scripts
├── paket.dependencies            # Dependency definitions
├── paket.references              # References for main project
└── fsharplint.json               # Lint config
```

## Architecture

### Core Modules

1. **`Tracing`** — main module:
   - `AlmaTracer` — `ActiveTracer of Tracer | NoopTracer` (graceful degradation when tracing is unavailable)
   - `Tracer.buildTracer()` — lazy tracer initialization; returns `NoopTracer` if env vars are missing
   - `Trace` DU — `Active of TelemetrySpan | Inactive`
   - **`Trace.Active`** — `start`, `current()`, `activate`
   - **`Trace.ChildOf`** — `start parent`, `startFromActive`, `continueOrStartActive extractFn`
   - **`Trace.finish`**, **`Trace.addTags`**, **`Trace.addEvent`**, **`Trace.id`**, **`Trace.context`**

2. **`Extension`** (`Alma.Tracing.Extension`) — HTTP propagation:
   - `Http.extractFromContext httpContext` — extracts W3C trace context from incoming request headers
   - `Http.extractFromHeaders headers` — extracts from raw header sequence
   - `Http.inject trace headers` — injects W3C Trace Context headers into outgoing request
   - `Http.injectActive headers` — injects active trace into outgoing request
   - Uses `TraceContextPropagator` (W3C Trace Context standard)

3. **`CustomTracingScope`** — cross-async trace persistence:
   - `TracingState.storeActiveTrace identifier trace` / `loadActiveTrace` / `clearActiveTrace`
   - `ScopedTrace` — disposable wrapper that auto-finishes and clears on dispose
   - Uses `Alma.State.ConcurrentStorage` with `TraceIdentifier` keys

4. **`LoggerProvider`** — `TracingLogger` implements `ILogger`:
   - Writes log messages as events on the current active trace span
   - `TracingProvider.create()` returns `ILoggerProvider` for use with `LoggerFactory`

### Trace Lifecycle

```
Trace.Active.start "Name"          → creates active span (stored in AsyncLocal)
    → Trace.ChildOf.start parent   → creates child span
    → Trace.addTags [...]          → annotates span
    → Trace.addEvent "..."         → adds event to span
    → Trace.finish                 → ends span (or use `use` for auto-dispose)
```

### HTTP Propagation (W3C Trace Context)

```
Incoming request → Http.extractFromContext ctx → TraceContext option
    (uses W3C traceparent/tracestate headers)
    → Trace.ChildOf.continueOrStartActive extractFn → continues or starts new trace

Outgoing request → Http.injectActive headers → headers with W3C traceparent/tracestate
```

## Conventions

- **`use` keyword** for automatic span finishing — `use trace = Trace.Active.start "Name"` disposes at scope end
- **`let` keyword** requires explicit `Trace.finish` — for spans that must end at a specific point
- **`use __ =`** — idiomatic pattern when the trace variable is not needed
- **`AsyncLocal`** — active trace is stored in `AsyncLocal` (safe across async continuations within the same logical thread)
- **Graceful degradation** — if tracing env vars are missing, `NoopTracer` is used; no exceptions
- **W3C Trace Context propagation** — uses `TraceContextPropagator` for `traceparent`/`tracestate` header propagation
- **`[<RequireQualifiedAccess>]`** on public modules

## CI/CD

| Workflow | Trigger | What it does |
|---|---|---|
| `tests.yaml` | PR, daily at 03:00 UTC | `./build.sh -t tests` on ubuntu-latest with .NET 10 |
| `publish.yaml` | Tag push (`X.Y.Z`) | `./build.sh -t publish` → NuGet.org |
| `pr-check.yaml` | PR | Blocks fixup commits, runs ShellCheck |

## Release Process

1. Increment `<Version>` in `Tracing.fsproj`
2. Update `CHANGELOG.md`
3. Commit and push a git tag matching the version (e.g., `13.0.0`)

## Pitfalls

- **No docker-compose / no local environment** — this is a pure library; an OpenTelemetry Collector (or compatible backend) must be running separately for tracing to export
- **OTLP exporter** — uses `OpenTelemetry.Exporter.OpenTelemetryProtocol`; endpoint configured via `TRACING_OTLP_ENDPOINT`
- **Environment variable dependency** — `Tracer.buildTracer()` silently returns `NoopTracer` if `TRACING_SERVICE_NAME` or `TRACING_OTLP_ENDPOINT` are unset; no error is thrown
- **`CustomTracingScope`** — uses global mutable `State`; identifiers must be unique across the application to avoid collisions
- **OTLP endpoint** — configured via `TRACING_OTLP_ENDPOINT` environment variable, expects a full URL (e.g., `http://otel-collector:4317`)
- **`TracingLogger`** — only writes to active spans; if no active trace exists, log messages are silently dropped
