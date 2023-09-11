namespace Alma.Tracing.Example

open Microsoft.Extensions.Logging
open Alma.Tracing
open Alma.ErrorHandling

[<RequireQualifiedAccess>]
module KafkaExample =
    (*
    Legend:
        - S     -> Service
        - s     -> span
        - sX.a  -> active span
        - |-->  -> span life-time
        - |==>  -> active span life-time

    |========================================================================================================================>|     // The whole example life-time
    Example<s1.a> ===========================================================================================================>|
        |=====================================================>|        // Service 1 life-time
        S1.receive interaction<s2.a> (start process) |========>|        // child of s1
            S1.working<s3>                            |-->|             // child of s2
            S1.produce<s4> (finished when produced)        |->|         // child of s2
                                                            |================================================================>|     // Service 2 life-time
                                                            S2.consume<s5> (finished when consume is done) |-->|                    // follows from s4
                                                                S2.handle<s6.a>                                   |==========>|     // child of s5
                                                                    S2.process<s7.a?> (app)                        |-=-=-=-=>|      // child of s6  [NOTE: this span might be active, it depends on shouldActivateApplicationSpan]
                                                                    S2.working<s8> (app)                            |------>|       // child of s6 (when shouldActivateApplicationSpan=false) or s7 (when shouldActivateApplicationSpan=true)
    *)

    [<RequireQualifiedAccess>]
    module Service1 =
        let work (loggerFactory: ILoggerFactory) trace message = async {
            let service = "Service 1"
            let logger = loggerFactory.CreateLogger(service)

            logger.LogInformation("Receive interaction via HTTP")
            use receiveTrace =
                "Receive message"
                |> Trace.ChildOf.continueOrStartActive (fun () -> trace)
                |> Trace.addTags [ "message", message ]

            do! simulateWork logger (if ExampleSettings.numberOfMessages > 1 then Dice.roll() else 1)

            { Message = $"received interaction: {message}"; Trace = receiveTrace }
            |> Stream.produce
        }

    [<RequireQualifiedAccess>]
    module Service2 =
        let work (loggerFactory: ILoggerFactory) trace = asyncResult {
            let service = "Service 2"
            let logger = loggerFactory.CreateLogger(service)

            logger.LogInformation("Consume stream")

            do! Handler.consumeEvents loggerFactory (fun { Message = message; Trace = trace } -> async {
                use _ =
                    if ExampleSettings.shouldActivateApplicationSpan
                        then "Process event" |> Trace.ChildOf.startActive trace
                        else "Process event" |> Trace.ChildOf.start trace

                do! simulateWork logger 1

                logger.LogInformation("Message {message} is handled!", message)
            })
        }

    let run loggerFactory exampleTrace =
        // Service 1 is responsible for receiving interactions and persisting them to the stream
        [
            "one"
            "two"
            "three"
            "four"
            "five"
        ]
        |> List.take ExampleSettings.numberOfMessages
        |> List.map (Service1.work loggerFactory exampleTrace)
        |> Async.Parallel
        |> Async.Ignore
        |> Async.Start

        // Service 2 is responsible for consuming stream events and process them
        Service2.work loggerFactory exampleTrace
        |> Async.RunSynchronously
        |> Result.orFail
