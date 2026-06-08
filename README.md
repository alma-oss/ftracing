F-Tracing
=========

[![NuGet](https://img.shields.io/nuget/v/Alma.Tracing.svg)](https://www.nuget.org/packages/Alma.Tracing)
[![NuGet Downloads](https://img.shields.io/nuget/dt/Alma.Tracing.svg)](https://www.nuget.org/packages/Alma.Tracing)
[![Tests](https://github.com/alma-oss/ftracing/actions/workflows/tests.yaml/badge.svg)](https://github.com/alma-oss/ftracing/actions/workflows/tests.yaml)

> F# distributed tracing library built on OpenTelemetry. Exports traces via OTLP (gRPC), propagates context using W3C Trace Context (`traceparent`/`tracestate`), and supports parent-based sampling.

## Install

Add following into `paket.references`
```
Alma.Tracing
```

## Architecture

```
App (F# / Giraffe / Saturn)
  ↓ OTLP (gRPC, port 4317)
OpenTelemetry Collector
  ↓
Backend (Grafana Tempo, Jaeger, etc.)
  ↓
Grafana UI
```

## Configuration

### Environment Variables

The library supports both standard OpenTelemetry environment variables and custom `TRACING_*` variables.
**Standard OTel variables take precedence** when both are set.

#### Required (service name + OTLP endpoint)

| Standard (precedence) | Custom (fallback) | Description |
|---|---|---|
| `OTEL_SERVICE_NAME` | `TRACING_SERVICE_NAME` | OpenTelemetry service name |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `TRACING_OTLP_ENDPOINT` | OTLP collector endpoint (e.g. `http://otel-collector:4317`) |

If neither the standard nor the custom variable is set, the library silently uses a `NoopTracer` — no exceptions, no traces exported.

#### Sampling

| Standard (precedence) | Custom (fallback) | Description |
|---|---|---|
| `OTEL_TRACES_SAMPLER` | `TRACING_SAMPLER` | Root sampler: `always_on` (default), `always_off`, `traceidratio` |
| `OTEL_TRACES_SAMPLER_ARG` | `TRACING_SAMPLER_ARG` | Sampler argument (ratio for `traceidratio`, e.g. `0.1` = 10%) |

The sampler is **parent-based** — if an incoming request carries a sampled trace (e.g. from Istio or another service), the sampling decision is inherited. The root sampler only applies when there is no parent trace.

#### Resource attributes

| Variable | Description |
|---|---|
| `TRACING_TAGS` | Comma-separated `key=value` pairs added as resource attributes (e.g. `env=production,version=1.0`) |

#### Logging (optional)

See [Alma.Logging](https://github.com/alma-oss/flogging) for more information.

| Variable | Description |
|---|---|
| `TRACING_LOG_TO` | Log destination |
| `TRACING_LOG_LEVEL` | Log level for tracing internals |
| `TRACING_LOG_META` | Log metadata (e.g. `domain:DOMAIN; context:CONTEXT; purpose:PURPOSE; version:VERSION`) |

#### Debugging

| Variable | Description |
|---|---|
| `TRACING_EXPORT_CONSOLE` | Set to `"on"` to enable ConsoleExporter (outputs span information to stdout) |

## Connecting your application

### ASP.NET Core / Giraffe / Saturn (recommended)

The library provides `TracingConfig.configureTracing` for one-line integration into your ASP.NET Core pipeline.
It configures OTLP export, incoming HTTP request instrumentation, outgoing HTTP client instrumentation, and parent-based sampling — all from environment variables.

```fs
open Microsoft.Extensions.DependencyInjection
open OpenTelemetry

open Alma.Tracing

let configureServices (services: IServiceCollection) =
    services.AddOpenTelemetry()
    |> TracingConfig.configureTracing
    |> ignore

    services
```

In Saturn:
```fs
application {
    service_config configureServices
    // ...
}
```

In Giraffe (Startup.fs):
```fs
member _.ConfigureServices(services: IServiceCollection) =
    services.AddOpenTelemetry()
    |> TracingConfig.configureTracing
    |> ignore
```

This sets up:
- **OTLP gRPC export** to the configured endpoint
- **ASP.NET Core instrumentation** — automatic spans for incoming HTTP requests
- **HTTP client instrumentation** — automatic spans for outgoing HTTP requests
- **Parent-based sampling** — respects upstream sampling decisions (e.g. from Istio sidecar)
- **W3C Trace Context propagation** — `traceparent`/`tracestate` headers

### Standalone (without ASP.NET Core)

If you don't use ASP.NET Core, the library initializes tracing automatically on first use. Just set the environment variables and start tracing:

```fs
open Alma.Tracing

let main () =
    use trace = Trace.Active.start "my-operation"
    // ... your code ...
    0
```

## Trace Context Propagation (W3C)

The library uses **W3C Trace Context** (`traceparent`/`tracestate`) for propagation.
This is the standard used by Istio, OpenTelemetry, and most modern tracing systems.

### How it works with Istio

1. Istio sidecar receives an incoming request
2. If no `traceparent` header exists, Istio creates a new trace
3. Istio adds its own spans and forwards the `traceparent` header to your app
4. Your app extracts the trace context and creates child spans
5. Outgoing requests from your app include the `traceparent` header
6. Istio sidecar on the outgoing side adds its own spans

**Result**: end-to-end trace across services with both Istio and application spans.

### Extracting trace from incoming HTTP request

```fs
open Alma.Tracing
open Alma.Tracing.Extension

let entryPoint (ctx: Microsoft.AspNetCore.Http.HttpContext) args =
    use trace =
        "Receive request"
        |> Trace.ChildOf.continueOrStartActive (fun () -> ctx |> Http.extractFromContext)
        |> Trace.addTags [ "component", "MyService" ]

    // ... handle request ...
```

### Injecting trace into outgoing HTTP request

```fs
open FSharp.Data
open FSharp.Data.HttpRequestHeaders
open Alma.Tracing
open Alma.Tracing.Extension

let callOtherService url =
    Http.AsyncRequestString (
        url,
        httpMethod = "GET",
        headers = (
            [ Accept HttpContentTypes.Json ]
            |> Http.injectActive    // injects traceparent/tracestate from current active trace
        )
    )
```

### Kafka propagation

The same `Http.inject` / `Http.extractFromHeaders` functions work for Kafka headers.
W3C `traceparent`/`tracestate` are stored as Kafka message headers:

```fs
// Inject into Kafka headers
let kafkaHeaders = Http.inject trace [ (* existing headers *) ]

// Extract from Kafka headers (in consumer)
let traceContext = Http.extractFromHeaders kafkaHeaders
```

## Usage

### Start active span for the whole function

With implicit finishing (`use`):
```fs
open Alma.Tracing

module MyApplication =
    let someAction args =
        use someActionTrace =
            "Some Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        // do some action ...
        // trace is automatically finished at end of scope

        0
```

With explicit finishing (`let`):
```fs
open Alma.Tracing

module MyApplication =
    let someAction args =
        let someActionTrace =
            "Some Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        // do some action ...

        someActionTrace |> Trace.finish

        0
```

**TIP**: If you don't need to use a trace variable anywhere, you can use just `use __ = "Name" |> Trace.Active.start`

### Use child spans for low-level functions
```fs
open Alma.Tracing

module internal Logic =
    let doSomeWork trace args =
        use __ = "Do some work" |> Trace.ChildOf.start trace
        // actually do some work ...
        "return value"

module internal OtherLogic =
    let doSomeMoreWork value =
        use __ = "Do some more work" |> Trace.ChildOf.startFromActive
        // actually do some more work ...
        "return value"

module MyApplication =
    let mainAction args =
        use mainActionTrace =
            "Main Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        args
        |> Logic.doSomeWork mainActionTrace
        |> OtherLogic.doSomeMoreWork
```

Trace from the previous example:
```
Main Action
    ├── Do some work
    └── Do some more work
```

### Custom Tracing Scope
Default tracer uses `AsyncLocal` class as a repository for an Active trace — so it is available safely in an async "thread".
If you need to persist an Active trace between async threads, you need to store it somewhere else.
There is a custom tracing scope for this purpose.

```fs
open Alma.Tracing
open Alma.Tracing.CustomTracingScope

let example () =
    let mainTrace = Trace.Active.start "example"

    mainTrace |> TracingState.storeActiveTrace "main"

    async {
        let mainTrace = TracingState.loadActiveTrace "main"
        // ... do something, trace children, ...
    }
    |> Async.RunSynchronously

    async {
        let mainTrace = TracingState.loadActiveTrace "main"
        // ... do something, trace children, ...

        mainTrace |> Trace.finish
        TracingState.clearActiveTrace "main"
    }
    |> Async.RunSynchronously

    0
```

#### Scoped Trace (shortcut)

```fs
open Alma.Tracing
open Alma.Tracing.CustomTracingScope

let example () =
    let mainTrace = Trace.Active.start "example"

    let scopedMainTrace = new ScopedTrace("main")
    scopedMainTrace.Save(mainTrace)

    async {
        let mainTrace = (new ScopedTrace("main")).Trace
        // ... do something, trace children, ...
    }
    |> Async.RunSynchronously

    async {
        use scopedMainTrace = new ScopedTrace("main")
        let mainTrace = scopedMainTrace.Trace
        // ... do something, trace children, ...
        // scopedMainTrace is automatically finished and cleared at end of scope
    }
    |> Async.RunSynchronously

    0
```

## Tracing log messages
> Requires `Alma.Logging` library

There is a `TracingLogger` which writes log messages as events on the current active trace span.

```fs
open Alma.Tracing
open Alma.Tracing.LoggerProvider
open Alma.Logging

LoggerFactory.create [
    UseProvider (TracingProvider.create())
    // ... other options
]
```

**Note**: TracingLogger will add log messages only for a current log level. If you need to add all log messages into tracing, set log level to `Tracing`.

## Sampling

The library uses **parent-based sampling** by default:

| Scenario | Behavior |
|---|---|
| Incoming request has sampled `traceparent` | Trace is sampled (inherits parent decision) |
| Incoming request has unsampled `traceparent` | Trace is NOT sampled (inherits parent decision) |
| No incoming trace (new root trace) | Root sampler decides (configurable via env var) |

### Root sampler options

| Value | Description |
|---|---|
| `always_on` (default) | Sample all root traces |
| `always_off` | Don't sample any root traces |
| `traceidratio` | Sample a percentage of root traces (set ratio via `OTEL_TRACES_SAMPLER_ARG` / `TRACING_SAMPLER_ARG`) |

### Example: sample 10% of root traces
```bash
export OTEL_TRACES_SAMPLER=traceidratio
export OTEL_TRACES_SAMPLER_ARG=0.1
```

### Istio integration

When Istio is configured with tracing, it creates the root trace and sets the sampling decision.
Your application inherits this decision via the `traceparent` header — no additional configuration needed in the application.

## Release
1. Increment version in `Tracing.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)

### Build
```bash
./build.sh build
```

### Tests
```bash
./build.sh -t tests
```
