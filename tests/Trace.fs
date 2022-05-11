module Trace

open Expecto
open Lmc.Tracing

[<Tests>]
let checkTraces =
    testList "Tracing - trace" [
        testCase "should cast to string" <| fun _ ->
            let span = Trace.Span.start "span"
            let traceId =
                match span |> Trace.id with
                | Some traceId -> traceId
                | _ -> failtest "Trace must have an id."

            let stringifiedSpan = string span

            Expect.stringContains stringifiedSpan traceId "Strinified span should contain a trace id"
            Expect.stringStarts stringifiedSpan "Trace.Live" "Strinified span should start with a trace type"

        testCase "should cast child span to string" <| fun _ ->
            let main = "main" |> Trace.Span.start
            let child = "child" |> Trace.ChildOf.start main

            let traceId =
                match child |> Trace.id with
                | Some traceId -> traceId
                | _ -> failtest "Trace must have an id."

            let parentSpanId =
                match main |> Trace.spanId with
                | Some spanId -> spanId
                | _ -> failtest "Trace must have an id."

            let stringifiedSpan = string child

            Expect.stringContains stringifiedSpan (traceId.TrimEnd(')')) "Strinified span should contain a trace id"
            Expect.stringContains stringifiedSpan ("." + parentSpanId + ")") "Strinified child span should contain a parent span id"
            Expect.stringStarts stringifiedSpan "Trace.Live" "Strinified span should start with a trace type"

        testCase "should cast inactive to string" <| fun _ ->
            let span = Inactive
            let stringifiedSpan = string span

            Expect.equal stringifiedSpan "Trace.Inactive" "Strinified span should equal the trace type"

        testCase "should equal" <| fun _ ->
            let spanA = Trace.Span.start "spanA"
            let spanA2 = spanA

            Expect.equal spanA spanA2 "The same trace"

            match spanA |> Trace.context with
            | Some context -> Expect.isTrue (spanA.Equals context) "Context of the trace"
            | _ -> failtest "Span should have a context."

        testCase "should not equal" <| fun _ ->
            let spanA = Trace.Span.start "spanA"
            let spanB = Trace.Span.start "spanB"

            Expect.notEqual spanA spanB "Two different traces"
    ]
