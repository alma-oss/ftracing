// Learn more about F# at http://fsharp.org

open System
open Microsoft.Extensions.Logging
open Alma.Logging
open Alma.Tracing
open Alma.Tracing.Example

[<EntryPoint>]
let main argv =
    printfn "Tracing Example"
    printfn "==============="

    use loggerFactory = LoggerFactory.create [
        UseLevel LogLevel.Trace
        LogToConsole
    ]

    let initTrace () =
        let exampleTrace = Trace.Active.start "Example trace"

        let logger = loggerFactory.CreateLogger("Example - trace")
        logger.LogInformation("Trace {trace}", exampleTrace |> Trace.id)

        exampleTrace

    match ExampleSettings.run with
    | ExampleSettings.Run.OpenTelemetry ->
        OpenTelemetryExample.run loggerFactory

    | ExampleSettings.Run.KafkaExample ->
        let exampleTrace = initTrace()
        KafkaExample.run loggerFactory exampleTrace
        exampleTrace |> Trace.finish

    | ExampleSettings.Run.AsyncResultExample ->
        let exampleTrace = initTrace()
        AsyncResultExample.run loggerFactory exampleTrace
        exampleTrace |> Trace.finish

    | ExampleSettings.Run.All ->
        let exampleTrace = initTrace ()
        KafkaExample.run loggerFactory exampleTrace
        AsyncResultExample.run loggerFactory exampleTrace
        exampleTrace |> Trace.finish

    Tracer.finishTracerProvider()

    printfn "waiting ..."
    System.Threading.Thread.Sleep 2000

    printfn "\nDone\n"
    0 // return an integer exit code
