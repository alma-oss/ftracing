# Examples

This file is the single source of truth for code in this skill. Examples are ordered by increasing complexity and each is self-contained.

## Basic Active Span

Start a span for the whole function. With `use` it finishes automatically on scope exit.

```fsharp
open Alma.Tracing

module WebApi =
    let handleAction args =
        use __ =
            "Handle Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "WebApi" ]

        // do the work; span is finished automatically when this scope ends
        0
```

If you need the span to end before the function returns, bind with `let` and finish explicitly:

```fsharp
open Alma.Tracing

module WebApi =
    let handleAction args =
        let actionTrace =
            "Handle Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "WebApi" ]

        // do the work ...

        actionTrace |> Trace.finish   // `let` requires explicit finishing
        0
```

## Child Span Tree

Inner functions create child spans. Pass a known parent to `Trace.ChildOf.start`, or continue the current active trace with `Trace.ChildOf.startFromActive`.

```fsharp
open Alma.Tracing

module internal Logic =
    let doWork parentTrace args =
        use __ = "Do work" |> Trace.ChildOf.start parentTrace
        "result"

module internal MoreLogic =
    let doMoreWork value =
        use __ = "Do more work" |> Trace.ChildOf.startFromActive   // no parent passed; uses current active trace
        "result"

module Worker =
    let run args =
        use mainTrace =
            "Main"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "Worker" ]

        args
        |> Logic.doWork mainTrace
        |> MoreLogic.doMoreWork
        |> ignore
        0
```

The resulting tree:

```
Main
    - Do work
    - Do more work
```

## Tagging an Error

Convert an exception or a domain error into span error tags with `Trace.addError`.

```fsharp
open Alma.Tracing

let traced (trace: Trace) action =
    try
        action ()
    with e ->
        trace
        |> Trace.addError (TracedError.ofExn e)
        |> ignore
        reraise ()

// From a domain error value plus a formatter:
let tagDomainError (trace: Trace) (error: string) =
    trace
    |> Trace.addError (TracedError.ofError id error)
    |> ignore
```

## Incoming HTTP Request

Continue an upstream trace when present, otherwise start a fresh active span.

```fsharp
open Alma.Tracing
open Alma.Tracing.Extension

let entryPoint (ctx: Microsoft.AspNetCore.Http.HttpContext) args =
    use requestTrace =
        "Receive request"
        |> Trace.ChildOf.continueOrStartActive (fun () -> ctx |> Http.extractFromContext)
        |> Trace.addTags [ "component", "WebApi" ]

    // a span that must end before the handler returns:
    let validationTrace = "Validation" |> Trace.ChildOf.start requestTrace
    let validated = args |> validate
    validationTrace |> Trace.finish

    use _ = "Process" |> Trace.ChildOf.start requestTrace
    validated |> process
```

## Outgoing HTTP Request

Inject the current active trace into outgoing request headers (B3 format).

```fsharp
open FSharp.Data
open FSharp.Data.HttpRequestHeaders
open Feather.ErrorHandling

open Alma.Tracing.Extension

let callDownstream url = asyncResult {
    let! raw =
        Http.AsyncRequestString(
            url,
            httpMethod = "GET",
            headers = (
                [ Accept HttpContentTypes.Json ]
                |> Http.injectActive   // use `Http.inject trace` to propagate a specific trace instead
            )
        )
        |> AsyncResult.ofAsyncCatch ApiError

    return raw
}
```

## Logging Into a Span

Register the tracing provider on the logger factory; log calls become events on the current active trace.

```fsharp
open Alma.Tracing.LoggerProvider
open Alma.Logging

let loggerFactory =
    LoggerFactory.create [
        UseProvider (TracingProvider.create())
        // ... other options
    ]
```

## Custom Tracing Scope

Persist an active trace across async boundaries that lose `AsyncLocal`. Always clear what you store.

```fsharp
open Alma.Tracing
open Alma.Tracing.CustomTracingScope

let run () =
    let mainTrace = Trace.Active.start "Main"
    mainTrace |> TracingState.storeActiveTrace "main"

    async {
        let mainTrace = TracingState.loadActiveTrace "main"  // retrieve in a fresh async thread
        use __ = "Step one" |> Trace.ChildOf.start mainTrace
        ()
    }
    |> Async.RunSynchronously

    async {
        let mainTrace = TracingState.loadActiveTrace "main"
        use __ = "Step two" |> Trace.ChildOf.start mainTrace
        ()
    }
    |> Async.RunSynchronously

    // finish and clear the stored trace so it does not leak
    TracingState.loadActiveTrace "main" |> Trace.finish
    TracingState.clearActiveTrace "main" |> ignore
    0
```

`ScopedTrace` wraps store/load/clear in a disposable shortcut:

```fsharp
open Alma.Tracing
open Alma.Tracing.CustomTracingScope

let run () =
    let mainTrace = Trace.Active.start "Main"
    (new ScopedTrace("main")).Save(mainTrace)

    async {
        let trace = (new ScopedTrace("main")).Trace
        use __ = "Step" |> Trace.ChildOf.start trace
        ()
    }
    |> Async.RunSynchronously

    let scoped = new ScopedTrace("main")
    scoped.Finish()   // finishes the trace and clears it from the scope
    0
```

## Test Against Inactive Tracer

Without the required env vars the tracer is a `NoopTracer`, so spans are `Inactive` and identity reads return `None`.

```fsharp
open Expecto
open Alma.Tracing

[<Tests>]
let tests =
    testList "tracing" [
        test "no active trace yields Inactive" {
            let trace = Trace.Active.current()
            Expect.equal trace Inactive "expected Inactive without a tracer"
        }

        test "id of an inactive trace is None" {
            let id = Trace.Active.start "Name" |> Trace.id
            Expect.isNone id "expected None under NoopTracer"
        }
    ]
```
