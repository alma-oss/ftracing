namespace Lmc.Tracing.Example

open System
open System.Diagnostics
open System.Collections.Generic

open Microsoft.Extensions.Logging

[<RequireQualifiedAccess>]
module OpenTelemetryExample =
    open Lmc.ErrorHandling

    open OpenTelemetry
    open OpenTelemetry.Trace
    open OpenTelemetry.Resources

    open Lmc.Tracing
    open Lmc.Tracing.LoggerProvider

    type ErrorMessage = exn

    let log (span: TelemetrySpan) =
        printfn "Example: %A (%A.%A)" span span.Context.TraceId span.Context.SpanId

    let additionalWork () =
        use _ =
            "additional work"
            |> Trace.FollowFrom.startFromActive
            |> Trace.addError (TracedError.ofError id "Err: additional work error")
        ()

    let run (loggerFactory: ILoggerFactory) =
        loggerFactory.AddProvider (TracingProvider.create())
        let items =
            [ 1 .. 2 ]
            |> List.map (sprintf "item[%A]")

        let logger = loggerFactory.CreateLogger("OpenTelemetry")

        let current = Trace.Active.current()
        printfn "Current active trace %A (%A)" current (current |> Trace.id)

        use exampleSpan = Trace.Active.start "example_span"
        exampleSpan
        |> Trace.addBaggage "Test event"
        |> Trace.addTags [ "span-tag", "tag-value" ]
        |> ignore

        let ctx: TraceContext option = exampleSpan |> Trace.context
        printfn "[Span.Ctx] id: %A" (ctx |> Option.map TraceContext.id)
        printfn "[Span.Ctx] spanId: %A" (ctx |> Option.map TraceContext.spanId)
        printfn "[Span.Ctx] traceId: %A" (ctx |> Option.map TraceContext.traceId)

        items
        |> List.iter (fun i -> Async.RunSynchronously <| async {
            logger.LogInformation("Starting item {item} ...", i)
            let name = "s_" + i

            use span =
                name
                |> Trace.ChildOf.startActive exampleSpan
                |> Trace.addTags [ "span_attr", "attr_value" ]

            additionalWork()

            try failwith "error" with
            | e -> span |> Trace.addError (TracedError.ofExn e) |> ignore

            logger.LogInformation("Span {span}", span |> Trace.id)

            logger.LogInformation("Handle item {item} ...", i)
            do! Async.Sleep 1000

            logger.LogInformation("Done with item {item} ...", i)
            return ()
        })

        logger.LogInformation("MainSpan {span}", exampleSpan |> Trace.id)

        ()
