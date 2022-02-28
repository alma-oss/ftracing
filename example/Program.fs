// Learn more about F# at http://fsharp.org

open System
open Microsoft.Extensions.Logging
open Lmc.Logging
open Lmc.Tracing
open Lmc.Tracing.Example

[<EntryPoint>]
let main argv =
    printfn "Tracing Example"
    printfn "==============="

    let exampleTrace = Trace.Active.start "Example trace"

    use loggerFactory = LoggerFactory.create [
        UseLevel LogLevel.Trace
        LogToConsole
    ]

    let logger = loggerFactory.CreateLogger("Example - trace")
    logger.LogInformation("Trace {trace}", exampleTrace |> Trace.id)

    match ExampleSettings.run with
    | ExampleSettings.Run.KafkaExample -> KafkaExample.run loggerFactory exampleTrace
    | ExampleSettings.Run.AsyncResultExample -> AsyncResultExample.run loggerFactory exampleTrace
    | ExampleSettings.Run.All ->
        KafkaExample.run loggerFactory exampleTrace
        AsyncResultExample.run loggerFactory exampleTrace

    exampleTrace |> Trace.finish

    printfn "waiting ..."
    System.Threading.Thread.Sleep 2000

    printfn "\nDone\n"
    0 // return an integer exit code
