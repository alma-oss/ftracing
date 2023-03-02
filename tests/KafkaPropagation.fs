module KafkaPropagation

open Expecto
open Lmc.Tracing
open Lmc.Tracing.Extension

[<RequireQualifiedAccess>]
module internal KafkaTrace =
    type HeaderKey = HeaderKey of string

    type Header = {
        Key: HeaderKey
        Value: string
    }

    [<RequireQualifiedAccess>]
    module Header =
        let key { Key = key } = key
        let valueAsString { Value = value } = value

        let ofString key value =
            { Key = key; Value = value }

        let fromKafkaHeader kv = kv ||> ofString

    [<RequireQualifiedAccess>]
    module HeaderKey =
        let value (HeaderKey key) = key

    let private kafkaHeadersToList headers =
        headers
        |> List.map (fun header -> header |> Header.key |> HeaderKey.value, header |> Header.valueAsString)

    let extractFromHeaders (headers: Header list) =
        headers
        |> kafkaHeadersToList
        |> Http.extractFromHeaders

    let extractFromKafkaHeaders (headers) () =
        match headers with
        | null -> None
        | headers ->
            headers
            |> Seq.map Header.fromKafkaHeader
            |> List.ofSeq
            |> extractFromHeaders

    let inject trace (headers: Header list) =
        match trace with
        | Inactive -> headers
        | _ ->
            headers
            |> kafkaHeadersToList
            |> Http.inject trace
            |> List.map (fun (key, value) -> value |> Header.ofString (HeaderKey key))

[<Tests>]
let checkTracePropagation =
    testList "Tracing - trace propagation in kafka" [
        (* testCase "should inject trace to headers" <| fun _ ->
            let span = Trace.Span.start "span"
            let headers = Http.inject span []

            Expect.isNonEmpty headers "Injected headers should not be empty"
            Expect.hasLength headers 3 "There should be 3 injected headers"

            headers
            |> List.iter (fun (key, _) -> Expect.stringStarts key "X-B3-" "Injected header should start with X-B3-")

            let map = headers |> Map.ofList
            Expect.equal (span |> Trace.traceId) (map |> Map.tryFind "X-B3-TraceId") "Headers should have traceId header."
            Expect.equal (span |> Trace.spanId) (map |> Map.tryFind "X-B3-SpanId") "Headers should have spanId header." *)

        (* testCase "should inject child trace to headers" <| fun _ ->
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
            Expect.equal (child |> Trace.parentId) (map |> Map.tryFind "X-B3-ParentSpanId") "Headers should have parentSpanId header." *)

        (* testCase "should inject trace to headers with old trace information" <| fun _ ->
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
            Expect.equal (child |> Trace.parentId) (map |> Map.tryFind "X-B3-ParentSpanId") "Headers should have parentSpanId header." *)

        (* testCase "should inject inactive trace and extract it from headers" <| fun _ ->
            let span = Inactive
            Expect.isNone (span |> Trace.context) "Inactive span should not have any context"

            let headers = Http.inject span []

            let extracted = Http.extractFromHeaders headers
            Expect.isNone extracted
                "Extracted trace context from inactive trace should be None"

            Expect.equal (span |> Trace.context) extracted (sprintf "inject inactive trace (%s) to headers and extract it again to (%s)" (string span) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.isSome (childOfExtracted |> Trace.spanId) "Extracted span should have span id"
            Expect.isNone (childOfExtracted |> Trace.parentId) "Extracted span should not have parent span id"
            Expect.equal (childOfExtracted |> Trace.parentId) (span |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original span (%s)" (string childOfExtracted) (string span)) *)

        testCase "should extract inactive trace from empty kafka headers" <| fun _ ->
            printfn "--- should extract inactive trace from empty kafka headers ---"
            let tee f a =
                f a
                a

            let parse (consumeTrace, i) =
                use parseTrace =
                    "parse"
                    |> Trace.ChildOf.start consumeTrace
                    |> tee (string >> printfn "<kafka>[%i]parse:   %A" i)
                Expect.equal (consumeTrace |> Trace.traceId) (parseTrace |> Trace.traceId) $"Trace should be same for all child spans (consume[{i}])"
                (consumeTrace, i)

            let handle (consumeTrace, i) =
                let handleTrace =
                    "handle"
                    |> Trace.ChildOf.startActive consumeTrace
                    |> tee (string >> printfn "<kafka>[%i]handle:  %A" i)
                Expect.equal (consumeTrace |> Trace.traceId) (handleTrace |> Trace.traceId) $"Trace should be same for all child spans (consume[{i}])"
                handleTrace

            let consume i =
                printfn "--- loop[%d] ------------" i
                Expect.equal (Trace.Active.current()) Inactive $"There should not be an active trace on start of the consume[{i}]"

                let extracted = KafkaTrace.extractFromKafkaHeaders [] ()
                Expect.isNone extracted
                    (sprintf "extract headers (%s) - consume[%i]" (string extracted) i)

                let consumeTrace =
                    "consume"
                    |> Trace.FollowFrom.continueOrStartActive (fun () ->
                        let extractedTrace = extracted |> Trace.ofContextOption
                        Expect.equal Inactive extractedTrace $"Extracted trace should be inactive, since there are empty headers (consume[{i}])"

                        extractedTrace
                    )
                    |> tee (string >> printfn "<kafka>[%i]consume: %A" i)

                Expect.notEqual Inactive consumeTrace $"consume trace should be active (consume[{i}])"

                (consumeTrace, i)

            [ 1 .. 3 ]
            |> List.iter (fun i ->
                let (consumedTrace, i) = consume i
                consumedTrace.Finish()

                (consumedTrace, i)
                |> parse
                |> handle
                |> Trace.finish
            )

        testCase "should extract inactive trace from empty kafka headers - async" <| fun _ ->
            printfn "--- should extract inactive trace from empty kafka headers - async ---"
            let tee f a =
                f a
                a

            let parse (consumeTrace, i) =
                use parseTrace =
                    "parse"
                    |> Trace.ChildOf.start consumeTrace
                    |> tee (string >> printfn "<kafka*>[%i]parse:   %A" i)
                Expect.equal (consumeTrace |> Trace.traceId) (parseTrace |> Trace.traceId) $"Trace should be same for all child spans (parse[{i}])"
                (consumeTrace, i)

            let handle f (consumeTrace, i) =
                let handleTrace =
                    "handle"
                    |> Trace.ChildOf.startActive consumeTrace
                    |> tee (string >> printfn "<kafka*>[%i]handle:  %A" i)
                Expect.equal (consumeTrace |> Trace.traceId) (handleTrace |> Trace.traceId) $"Trace should be same for all child spans (handle[{i}])"

                (handleTrace, i) |> f

                handleTrace

            let consume i = async {
                printfn "--- loop[%d] ------------" i
                Expect.equal (Trace.Active.current()) Inactive $"There should not be an active trace on start of the consume[{i}]"

                let extracted = KafkaTrace.extractFromKafkaHeaders [] ()
                Expect.isNone extracted
                    (sprintf "extract headers (%s) - consume[%i]" (string extracted) i)

                let consumeTrace =
                    "consume"
                    |> Trace.FollowFrom.continueOrStartActive (fun () ->
                        let extractedTrace = extracted |> Trace.ofContextOption
                        Expect.equal Inactive extractedTrace $"Extracted trace should be inactive, since there are empty headers (consume[{i}])"

                        extractedTrace
                    )
                    |> tee (string >> printfn "<kafka*>[%i]consume: %A" i)

                Expect.notEqual Inactive consumeTrace $"consume trace should be active (consume[{i}])"

                return (consumeTrace, i)
            }

            [ 1 .. 3 ]
            |> List.iter (fun i ->
                async {
                    let! (consumedTrace, i) = consume i
                    consumedTrace.Finish()

                    (consumedTrace, i)
                    |> parse
                    |> handle (fun (trace, i) ->
                        use deriveTrace =
                            "Derive Event"
                            |> Trace.ChildOf.startActive trace
                            |> tee (string >> printfn "<kafka*>[%i]derive:  %A" i)
                        Expect.notEqual Inactive deriveTrace $"derive trace should be active (derive[{i}])"
                    )
                    |> Trace.finish
                }
                |> Async.RunSynchronously
            )

        (* testCase "should extract injected trace from headers" <| fun _ ->
            let span = Trace.Span.start "span"
            let headers = Http.inject span []

            let extracted = Http.extractFromHeaders headers

            Expect.equal (span |> Trace.context) extracted (sprintf "inject trace (%s) to headers and extract it again to (%s)" (string span) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal (childOfExtracted |> Trace.parentId) (span |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original span (%s)" (string childOfExtracted) (string span)) *)

        (* testCase "should extract injected child trace from headers" <| fun _ ->
            let span = "main" |> Trace.Span.start
            let child = "child" |> Trace.ChildOf.start span
            let headers = Http.inject child []

            let extracted = Http.extractFromHeaders headers

            Expect.equal (child |> Trace.context) extracted (sprintf "inject trace (%s) to headers and extract it again to (%s)" (string child) (string extracted))

            let childOfExtracted =
                "continue"
                |> Trace.ChildOf.continueOrStart (fun () -> extracted |> Trace.ofContextOption)

            Expect.equal (childOfExtracted |> Trace.parentId) (child |> Trace.spanId) (sprintf "Parent of extracted trace (%s) should original child span (%s)" (string childOfExtracted) (string child)) *)

        (* testCase "should extract trace from headers" <| fun _ ->
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
                (sprintf "Parent of extracted trace (%s) should original span" (string childOfExtracted)) *)

        (* testCase "should extract trace from headers (lowercase)" <| fun _ ->
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
                (sprintf "Parent of extracted trace (%s) should original span" (string childOfExtracted)) *)
    ]
