// Learn more about F# at http://fsharp.org

open System
open Microsoft.Extensions.Logging
open Lmc.Tracing
open Lmc.Logging

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
module Example =
    // ===================================================================== //
    //             Settings for example
    //    ----------------------------------------

    /// 5 is maximum
    let numberOfMessages = 1

    /// whether to use StartActive or just Start for an "application" trace
    let shouldActivateApplicationSpan = true

    /// Duration of the working simulation (waiting in this case)
    let workingBaseDuration = 500

    // ===================================================================== //

    let tee f a =
        f a
        a

    type TracedMessage =
        {
            Message: string
            Trace: Trace
        }

        member this.Finish() =
            this.Trace |> Trace.finish

        interface IDisposable with
            member this.Dispose() =
                this.Finish()

    [<RequireQualifiedAccess>]
    module TracedMessage =
        let create message trace = { Message = message; Trace = trace }
        let finish (message: TracedMessage) = message.Finish()

    [<RequireQualifiedAccess>]
    module Dice =
        let roll () = System.Random().Next(0, 6)

    [<RequireQualifiedAccess>]
    /// This is a kafka library in real-life applications
    module Stream =
        open System.Collections.Concurrent
        open OpenTracing.Propagation

        type private RawMessage = {
            Message: string
            Headers: Map<string, string>
        }

        [<AutoOpen>]
        module private Utils =
            open System.Collections.Generic

            type Headers = Dictionary<string, string>
            type IHeaders = IDictionary<string, string>

            type HeaderSeq = Map<string, string>

            let headersToDictionary (headerList: HeaderSeq): IHeaders =
                headerList
                |> Map.toSeq
                |> Seq.fold
                    (fun (headers: Headers) (key, value) ->
                        headers.Add(key, value)
                        headers
                    )
                    (Headers())
                :> IHeaders

        let private inject trace headers =
            match trace |> Trace.context with
            | Some (TraceContext context) ->
                let headersDict = headers |> headersToDictionary
                let kafkaHeadersCarrier = TextMapInjectAdapter(headersDict) :> ITextMap

                Tracer.tracer().Inject(context, BuiltinFormats.HttpHeaders, kafkaHeadersCarrier)

                headersDict
                |> Seq.map (fun kv -> kv.Key, kv.Value)
                |> Seq.toList
                |> Map.ofSeq

            | _ -> headers

        let private extract (headers: Map<string, string>) () =
            let httpHeadersCarrier = TextMapExtractAdapter(headers |> headersToDictionary) :> ITextMap

            match Tracer.tracer().Extract(BuiltinFormats.HttpHeaders, httpHeadersCarrier) with
            | null -> None
            | context -> Some (TraceContext context)

        [<RequireQualifiedAccess>]
        module private RawMessage =
            let create (message: TracedMessage) =
                {
                    Message = message.Message
                    Headers = Map.empty |> inject message.Trace
                }

        let private stream = ConcurrentQueue<RawMessage>()

        let produce (message: TracedMessage) =
            let produceTrace =
                let rawMessageWithOriginalTrace = message |> RawMessage.create

                "Produce event"
                |> Trace.ChildOf.continueOrStart (extract rawMessageWithOriginalTrace.Headers >> Trace.ofContextOption)
                |> Trace.addTags [
                    "peer.service", "stream"
                    "component:", "stream"
                    "span.kind", "producer"
                ]

            { message with Trace = produceTrace }
            |> RawMessage.create
            |> stream.Enqueue

            produceTrace
            |> Trace.addTags [
                "stream.offset", string stream.Count
            ]
            |> Trace.finish

        let consume =
            seq {
                while true do
                    match stream.TryDequeue() with
                    | true, consumeResult ->
                        let consumeTrace =
                            "Consume event"
                            |> Trace.FollowFrom.continueOrStart (extract consumeResult.Headers >> Trace.ofContextOption)
                            |> Trace.addTags [
                                "peer.service", "stream"
                                "component:", "stream"
                                "span.kind", "consumer"
                            ]

                        yield consumeTrace |> TracedMessage.create consumeResult.Message

                    | _ -> ()
            }
            |> Seq.map (tee TracedMessage.finish)

    let private simulateWork (logger: ILogger) multiplicator = async {
        let workingTime = multiplicator * workingBaseDuration
        use _ = "Working" |> Trace.ChildOf.startFromActive |> Trace.addTags [ "working.for", string workingTime ]
        logger.LogTrace("Working ...")

        do! Async.Sleep workingTime
    }

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

            do! simulateWork logger (if numberOfMessages > 1 then Dice.roll() else 1)

            { Message = $"received interaction: {message}"; Trace = receiveTrace }
            |> Stream.produce
        }

    [<RequireQualifiedAccess>]
    /// This is a kafkaApplication library in the real-life apps
    module Handler =
        let consumeEvents (loggerFactory: ILoggerFactory) handle =
            let logger = loggerFactory.CreateLogger("Handler")

            let startHandle (trace: Trace) (message: string) =
                logger.LogTrace("Start handle {message} with {trace}", message, trace)
                {
                    Message = message
                    Trace =
                        "Handle event"
                        |> Trace.ChildOf.startActive trace
                        |> Trace.addTags [
                            "peer.service", "stream"
                            "component", "handler-lib"
                        ]
                        |> tee (fun t -> logger.LogTrace("Handle with {trace}", t))
                }

            logger.LogTrace("Consuming stream")
            Stream.consume
            |> Seq.take numberOfMessages    // in real-life application, this would go "forever", but we need to stop an example in the end
            |> Seq.iter (fun { Message = message; Trace = trace } ->
                message
                |> startHandle trace
                |> tee (handle >> Async.RunSynchronously)
                |> TracedMessage.finish
            )

    [<RequireQualifiedAccess>]
    module Service2 =
        let work (loggerFactory: ILoggerFactory) trace = async {
            let service = "Service 2"
            let logger = loggerFactory.CreateLogger(service)

            logger.LogInformation("Consume stream")

            Handler.consumeEvents loggerFactory (fun { Message = message; Trace = trace } -> async {
                use _ =
                    if shouldActivateApplicationSpan
                        then "Process event" |> Trace.ChildOf.startActive trace
                        else "Process event" |> Trace.ChildOf.start trace

                do! simulateWork logger 1

                logger.LogInformation("Message {message} is handled!", message)
            })
        }

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

    // Service 1 is responsible for receiving interactions and persisting them to the stream
    [
        "one"
        "two"
        "three"
        "four"
        "five"
    ]
    |> List.take Example.numberOfMessages
    |> List.map (Example.Service1.work loggerFactory exampleTrace)
    |> Async.Parallel
    |> Async.Ignore
    |> Async.Start

    // Service 2 is responsible for consuming stream events and process them
    Example.Service2.work loggerFactory exampleTrace
    |> Async.RunSynchronously

    exampleTrace |> Trace.finish

    printfn "waiting ..."
    System.Threading.Thread.Sleep 2000

    printfn "\nDone\n"
    0 // return an integer exit code
