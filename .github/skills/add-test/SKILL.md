---
name: add-test
description: 'Add Expecto tests for Alma.Tracing. Use when writing new test cases, adding a test module, or understanding the test conventions in this repo. Trigger phrases: "add a test", "write tests", "test this", "add expecto test", "add test case".'
argument-hint: 'Describe what you want to test (module, function, or scenario)'
---

# Add Tests

## Test Framework: Expecto

Tests live in `tests/` and are compiled by `tests/tests.fsproj`.

## File Placement

| What | Where |
|---|---|
| Core span lifecycle | `tests/Trace.fs` |
| Active trace (AsyncLocal) | `tests/ActiveTrace.fs` |
| W3C HTTP propagation | `tests/Propagation.fs` |
| Kafka header propagation | `tests/KafkaPropagation.fs` |
| New module | `tests/<ModuleName>.fs` + add to `tests.fsproj` |

## Pattern

```fsharp
module MyModule   // top-level module, matches filename

open Expecto
open Alma.Tracing

[<Tests>]
let myTests =
    testList "Alma.Tracing - <topic>" [
        testCase "should <behaviour>" <| fun _ ->
            let span = Trace.Span.start "test-span"
            Expect.isTrue (span <> Inactive) "span should be live"
            Trace.finish span

        testCase "should <other behaviour>" <| fun _ ->
            // arrange
            let parent = Trace.Span.start "parent"
            use child = Trace.ChildOf.start parent
            // assert
            Expect.isSome (Trace.id child) "child should have a trace id"
    ]
```

## Useful `Expect` Assertions

```fsharp
Expect.equal actual expected "message"
Expect.isSome optionValue "message"
Expect.isTrue condition "message"
Expect.stringContains str substring "message"
Expect.stringStarts str prefix "message"
```

Use `failtest "message"` inside match arms to fail with a message.

## Add New Module to Project

In `tests/tests.fsproj`, add before the closing `</ItemGroup>`:
```xml
<Compile Include="MyModule.fs" />
```
Order matters — list files before `Tests.fs` (the entry point).

## Run Tests

```bash
./build.sh -t tests
```
