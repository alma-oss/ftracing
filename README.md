F-Tracing
=========

> A library to help with Jaeger tracing.

## Install

Add following into `paket.dependencies`
```
git ssh://git@bitbucket.lmc.cz:7999/archi/nuget-server.git master Packages: /nuget/
# LMC Nuget dependencies:
nuget Lmc.Tracing
```

Add following into `paket.references`
```
Lmc.Tracing
```

## Configuration
You can use environment values to configure tracing and inner logger

List of required/recommended environment variables
```
JAEGER_SERVICE_NAME
JAEGER_GRPC_TARGET
JAEGER_TRACEID_128BIT
JAEGER_SAMPLER_PARAM
JAEGER_SAMPLER_TYPE
JAEGER_SENDER_FACTORY
JAEGER_PROPAGATION
JAEGER_TAGS
```

List of optional logging environment variables (see https://bitbucket.lmc.cz/projects/ARCHI/repos/flogging/browse for more information)
```
JAEGER_LOG_TO
JAEGER_LOG_LEVEL
JAEGER_LOG_META
```

**TIP**: You can add instance data to `JAEGER_LOG_META` by `JAEGER_LOG_META="domain:DOMAIN; context:CONTEXT; purpose:PURPOSE; version:VERSION"`

## Usage

### Start active span for the whole function

With explicit finishing
```fs
open Lmc.Tracing

module MyApplication =
    let someAction args =
        let someActionTrace =
            "Some Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        // do some action ...

        someActionTrace |> Trace.finish     // trace is created with `let` keyword so we need to finish it explicitly

        0
```

With implicit finishing
```fs
open Lmc.Tracing

module MyApplication =
    let someAction args =
        use someActionTrace =
            "Some Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        // do some action ...

        // someActionTrace |> Trace.finish     // trace is created with `use` keyword so is disposed in the end by default

        0
```

**TIP**: If you don't need to use a trace variable anywhere, you can use just `use __ = "Name" |> Trace.Active.start`

### Use child spans for low-level functions
```fs
open Lmc.Tracing

module internal Logic =
    let doSomeWork trace args =
        use __ = "Do some work" |> Trace.ChildOf.start trace      // given trace is used as a parent of a new span, which in this case is automatically disposed in the end
        // actually do some work ...
        "return value"

module internal OtherLogic =
    let doSomeMoreWork value =
        use __ = "Do some more work" |> Trace.ChildOf.startFromActive       // in this case we don't have any given trace, so we will just continue in current active trace
        // actually do some more work ...
        "return value"

module MyApplication =
    let mainAction args =
        use mainActionTrace =
            "Main Action"
            |> Trace.Active.start
            |> Trace.addTags [ "component", "MyApplication" ]

        // do some work ...
        args
        |> Logic.doSomeWork mainActionTrace
        |> OtherLogic.doSomeMoreWork

        0       // in the end of the mainAction the mainActionTrace is automatically disposed (finished)
```

Trace from the previous example should look like:
```
Main Action
    - Do some work
    - Do some more work
```

### Trace received HTTP request with Extension
```fs
open Lmc.Tracing
open Lmc.Tracing.Extension

let entryPoint (ctx: Microsoft.AspNetCore.Http.HttpContext) args =
    use receiveRequestTrace =       // with `use` trace will be finished automatically in the end of the method
        "Receive request"
        |> Trace.ChildOf.continueOrStartActive (fun () -> ctx |> Http.extractFromContext)     // this will either extract the trace from request headers or start an active span
        |> Trace.addTags [ "component", "Example" ]

    // debug trace id
    receiveRequestTrace |> Trace.id |> printfn "[Debug] Trace id: %A"

    let validationTrace = "Validation" |> Trace.ChildOf.start receiveRequestTrace      // `let` means, the trace must be finished by Trace.finish
    let validated = args |> validation
    validationTrace |> Trace.finish

    use _ = "Some work" |> Trace.ChildOf.start receiveRequestTrace     // `use` will finish someWorkTrace automatically in the end of the method
    validated |> someWork
```

### Trace sent HTTP request
```fs
open FSharp.Data
open FSharp.Data.HttpRequestHeaders
open Lmc.ErrorHandling

open Lmc.Tracing
open Lmc.Tracing.Extension

let httpRequest url = asyncResult {
    let! rawResponse =
        Http.AsyncRequestString (
            url,
            httpMethod = "GET",
            headers = (
                [
                    Accept HttpContentTypes.Json
                    // other headers ...
                ]
                |> Http.injectActive      // this will trace with an active span, if you want to pass your own, use `inject` instead
            )
        )
        |> AsyncResult.ofAsyncCatch ApiError

    let response = Schema.Parse(rawResponse)

    return response // ...
}
```

### Custom Tracing Scope
Default tracer uses `AsyncLocal` class as a repository for an Active trace - so it is available safely in an async "thread".
If you need to persist an Active trace between async threads, you need to store it somewhere else.
There is a custom tracing scope for this purpose.

```fs
open Lmc.Tracing
open Lmc.Tracing.CustomTracingScope

let example () =
    let mainTrace = Trace.Active.start "example"   // in this case, you could store an active trace in variable and pass it to asyncs, without using a custom state, but if your asyncs would be different functions etc, it could be easier to ask Tracer for an active trace "globally" instead. But you still need a unique identifier to retrieve a trace you want!

    mainTrace |> TracingState.storeActiveTrace "main"

    async {
        // let mainTrace = Trace.Active.current()  // this would normally doesn't return an active trace since there is no active trace in this async
        let mainTrace = TracingState.loadActiveTrace "main" // but we have stored an active trace in tracing state

        // ... do something, trace children, ...
    }
    |> Async.RunSynchronously

    async {
        let mainTrace = TracingState.loadActiveTrace "main"

        // ... do something, trace children, ...

        // if we want to finish the current "main" trace here now, in the async
        // we need to clear the trace from our tracing state, otherwise it would stack there forever

        mainTrace
        |> Trace.finish

        TracingState.clearActiveTrace "main"
    }
    |> Async.RunSynchronously

    0
```

#### Scoped Trace
There is also a shortcut for the above example, if you use a `ScopedTrace` disposable object.

```fs
open Lmc.Tracing
open Lmc.Tracing.CustomTracingScope

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
        let scopedMainTrace = new ScopedTrace("main")   // we can use `use` here so it would be disposed and cleared automatically
        let mainTrace = scopedMainTrace.Trace

        // ... do something, trace children, ...

        // if we want to finish the current "main" trace here now, in the async

        scopedMainTrace.Finish()    // it will automatically finish the trace and clear it out of a tracing state

        // or by function
        // scopedMainTrace |> ScopedTrace.finish
    }
    |> Async.RunSynchronously

    0
```

## Release
1. Increment version in `Tracing.fsproj`
2. Update `CHANGELOG.md`
3. Commit new version and tag it
4. Run `$ fake build target release`
5. Go to `nuget-server` repo, run `faket build target copyAll` and push new versions

## Development
### Requirements
- [dotnet core](https://dotnet.microsoft.com/learn/dotnet/hello-world-tutorial)
- [FAKE](https://fake.build/fake-gettingstarted.html)

### Build
```bash
./build.sh
```

### Watch
```bash
./build.sh -t watch
```
