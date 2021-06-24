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
