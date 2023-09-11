module Propagation

open Expecto
open Alma.Tracing
open Alma.Tracing.Extension

let dbg ctx msg = printfn "[%s] %A" ctx msg
let tid = Trace.id >> Option.defaultValue "-"
let pid = Trace.parentId >> Option.defaultValue "-"
let dbgT ctx detail t = dbg ctx <| sprintf "%s: %s (parent: %s)" detail (t |> tid) (t |> pid)

[<Tests>]
let checkTracePropagation =
    testList "Tracing - trace propagation" [
        testCase "should inject trace to headers" <| fun _ ->
            let span = Trace.Span.start "span"
            let headers = Http.inject span []

            Expect.isNonEmpty headers "Injected headers should not be empty"
            Expect.hasLength headers 4 (sprintf "There should be 3 injected headers, but there are %s" (headers |> List.map (fun (h, v) -> $"{h} ({v})") |> String.concat ", "))

            headers
            |> List.iter (fun (key, _) -> Expect.stringStarts key "X-B3-" "Injected header should start with X-B3-")

            let map = headers |> Map.ofList
            Expect.equal (span |> Trace.traceId) (map |> Map.tryFind "X-B3-TraceId") "Headers should have traceId header."
            Expect.equal (span |> Trace.spanId) (map |> Map.tryFind "X-B3-SpanId") "Headers should have spanId header."

        testCase "should inject child trace to headers" <| fun _ ->
            let span = "main" |> Trace.Span.start
            let child = "child" |> Trace.ChildOf.start span

            let headers = Http.inject child []

            Expect.isNonEmpty headers "Injected headers should not be empty"
            Expect.hasLength headers 4 "There should be 4 injected headers"

            headers
            |> List.iter (fun (key, _) -> Expect.stringStarts key "X-B3-" "Injected header should start with X-B3-")

            let map = headers |> Map.ofList
            Expect.equal (child |> Trace.traceId) (map |> Map.tryFind "X-B3-TraceId") "Headers should have traceId header."
            Expect.equal (child |> Trace.spanId) (map |> Map.tryFind "X-B3-SpanId") "Headers should have spanId header."
            Expect.equal (child |> Trace.parentId) (map |> Map.tryFind "X-B3-ParentSpanId") "Headers should have parentSpanId header."

        testCase "should inject trace to headers with old trace information" <| fun _ ->
            let old = "old" |> Trace.Span.start
            let headers = Http.inject old []

            let span = "main" |> Trace.Span.start
            let child = "child" |> Trace.ChildOf.start span

            let headers = Http.inject child headers

            Expect.isNonEmpty headers "Injected headers should not be empty"
            Expect.hasLength headers 4 "There should be 4 injected headers"

            headers
            |> List.iter (fun (key, _) -> Expect.stringStarts key "X-B3-" "Injected header should start with X-B3-")

            let map = headers |> Map.ofList
            Expect.equal (child |> Trace.traceId) (map |> Map.tryFind "X-B3-TraceId") "Headers should have traceId header."
            Expect.equal (child |> Trace.spanId) (map |> Map.tryFind "X-B3-SpanId") "Headers should have spanId header."
            Expect.equal (child |> Trace.parentId) (map |> Map.tryFind "X-B3-ParentSpanId") "Headers should have parentSpanId header."

        testCase "should inject inactive trace and extract it from headers" <| fun _ ->
            let span = Inactive
            Expect.isNone (span |> Trace.context) "Inactive span should not have any context"

            let headers = Http.inject span []

            let extracted = Http.extractFromHeaders headers
            Expect.isNone extracted "Extracted trace context from inactive trace should be None"
            Expect.equal (span |> Trace.context) extracted (sprintf "inject inactive trace (%s) to headers and extract it again to (%s)" (string span) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.isSome (childOfExtracted |> Trace.spanId) "Extracted span should have span id"

            // [MemoryLeakContextProblem] ignore following expectations since the context is now created for all new spans with generated ids, so even extract from inactive would have a random parent id
            // Expect.isNone (childOfExtracted |> Trace.parentId) "Extracted span should not have parent span id"
            // Expect.equal (childOfExtracted |> Trace.parentId) (span |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original span (%s)" (string childOfExtracted) (string span))

        testCase "should extract inactive trace from empty headers" <| fun _ ->
            printfn "--- should extract inactive trace from empty headers ---"
            let tee f a =
                f a
                a

            let loop i =
                printfn "--- loop[%d] ------------" i
                Expect.equal (Trace.Active.current()) Inactive $"There should not be an active trace on start of the loop[{i}]"

                let extracted = Http.extractFromHeaders []
                Expect.isNone extracted
                    (sprintf "extract headers (%s) - loop[%i]" (string extracted) i)

                use loopTrace =
                    "loop"
                    |> Trace.FollowFrom.continueOrStartActive (fun () ->
                        let extractedTrace = extracted |> Trace.ofContextOption
                        Expect.equal Inactive extractedTrace $"Extracted trace should be inactive, since there are empty headers (loop[{i}])"

                        extractedTrace
                    )
                    |> tee (string >> printfn "[%i]Loop: %A" i)

                Expect.notEqual Inactive loopTrace $"Loop trace should be active (loop[{i}])"

                let work () =
                    use workTrace =
                        "work"
                        |> Trace.ChildOf.start loopTrace
                        |> tee (string >> printfn "[%i]Work: %A" i)
                    Expect.equal (loopTrace |> Trace.traceId) (workTrace |> Trace.traceId) $"Trace should be same for all child spans (loop[{i}])"

                work()

            [ 1 .. 3 ]
            |> List.iter loop

        testCase "should extract injected trace from headers" <| fun _ ->
            let span = Trace.Span.start "span"
            let headers = Http.inject span []

            let extracted = Http.extractFromHeaders headers

            Expect.equal (span |> Trace.context) extracted (sprintf "inject trace (%s) to headers and extract it again to (%s)" (string span) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal (childOfExtracted |> Trace.parentId) (span |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original span (%s)" (string childOfExtracted) (string span))

        testCase "should extract injected child trace from headers" <| fun _ ->
            let span = "main" |> Trace.Span.start
            let child = "child" |> Trace.ChildOf.start span
            let headers = Http.inject child []

            let extracted = Http.extractFromHeaders headers

            Expect.equal (child |> Trace.context) extracted (sprintf "inject trace (%s) to headers and extract it again to (%s)" (string child) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal (childOfExtracted |> Trace.parentId) (child |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original child span (%s)" (string childOfExtracted) (string child))

        testCase "should extract trace from headers" <| fun _ ->
            let headers = [
                "X-B3-TraceId", "7fd53ebb12e81ce2b66bec6bfc47b29b"
                "X-B3-SpanId", "71a6d5979a7a70a7"
                "X-B3-Sampled", "1"
                "X-B3-ParentSpanId", "cd8baa01e6cf0597"
            ]

            let extracted = Http.extractFromHeaders headers

            Expect.equal
                (extracted |> Option.map TraceContext.id)
                (Some "7fd53ebb12e81ce2b66bec6bfc47b29b.71a6d5979a7a70a7")
                (sprintf "extract headers (%s)" (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStartActive (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal
                (childOfExtracted |> Trace.parentId)
                (Some "71a6d5979a7a70a7")
                (sprintf "Parent of extracted trace (%s) should original span" (string childOfExtracted))

        testCase "should extract trace from headers (lowercase)" <| fun _ ->
            let headers = [
                "x-b3-traceid", "7fd53ebb12e81ce2b66bec6bfc47b29b"
                "x-b3-spanid", "71a6d5979a7a70a7"
                "x-b3-sampled", "1"
                "x-b3-parentspanid", "cd8baa01e6cf0597"
            ]

            let extracted = Http.extractFromHeaders headers

            Expect.equal
                (extracted |> Option.map TraceContext.id)
                (Some "7fd53ebb12e81ce2b66bec6bfc47b29b.71a6d5979a7a70a7")
                (sprintf "extract headers (%s)" (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStartActive (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal
                (childOfExtracted |> Trace.parentId)
                (Some "71a6d5979a7a70a7")
                (sprintf "Parent of extracted trace (%s) should original span" (string childOfExtracted))
    ]
