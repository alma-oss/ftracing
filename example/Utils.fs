namespace Lmc.Tracing.Example

open System
open Microsoft.Extensions.Logging
open Lmc.Tracing

[<AutoOpen>]
module ExampleUtils =
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
        let roll () = Random().Next(0, 6)

    /// This is a kafka library in real-life applications
    [<RequireQualifiedAccess>]
    module Stream =
        open System.Collections.Concurrent
        open Lmc.Tracing.Extension

        type private RawMessage = {
            Message: string
            Headers: Map<string, string>
        }

        let private inject trace headers =
            match trace with
            | Inactive -> headers
            | _ -> headers |> Map.toList |> Http.inject trace |> Map.ofList

        let private extract (headers: Map<string, string>) () =
            match headers |> Map.toList with
            | [] -> None
            | headers -> headers |> Http.extractFromHeaders

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

    let simulateWork (logger: ILogger) multiplicator = async {
        logger.LogTrace("[Working] Start")

        let workingTime = multiplicator * ExampleSettings.workingBaseDuration
        let workingTrace = "Working" |> Trace.ChildOf.startFromActive |> Trace.addTags [ "working.for", string workingTime ]
        logger.LogTrace("[Working] Working for {time} ...", workingTime)

        do! Async.Sleep workingTime

        workingTrace |> Trace.finish
        logger.LogTrace("[Working] Done")
    }

    let simulateWorkWith trace (logger: ILogger) multiplicator = async {
        logger.LogTrace("[Working] Start")

        let workingTime = multiplicator * ExampleSettings.workingBaseDuration
        let workingTrace = "Working" |> Trace.ChildOf.start trace |> Trace.addTags [ "working.for", string workingTime ]
        logger.LogTrace("[Working] Working for {time} ...", workingTime)

        do! Async.Sleep workingTime

        workingTrace |> Trace.finish
        logger.LogTrace("[Working] Done")
    }

    [<RequireQualifiedAccess>]
    /// This is a kafkaApplication library in the real-life apps
    module Handler =
        open Lmc.ErrorHandling

        let sequence (validations: Validation<'Success, 'Failure> seq): Validation<'Success seq, 'Failure> =
            let (<*>) = Validation.apply
            let (<!>) = Result.map
            let cons head tail = seq {
                yield head
                yield! tail
            }
            let consR headR tailR = cons <!> headR <*> tailR
            let initialValue = Ok Seq.empty // empty list inside Result

            // loop through the list, prepending each element
            // to the initial value
            Seq.foldBack consR validations initialValue

        let ofResults (xR: Result<'Success, 'Failure> seq): Validation<'Success seq, 'Failure> =
            xR
            |> Seq.map Validation.ofResult
            |> sequence

        let ofSequentialAsyncResultsSeq<'Success, 'Error> (f: exn -> 'Error) (results: AsyncResult<'Success, 'Error> seq): AsyncResult<'Success seq, 'Error list> =
            results
            |> Seq.map (AsyncResult.mapError List.singleton)
            |> Async.Sequential
            |> AsyncResult.ofAsyncCatch (f >> List.singleton)
            |> AsyncResult.bind (
                ofResults
                >> Result.mapError List.concat
                >> AsyncResult.ofResult
            )

        let consumeEvents (loggerFactory: ILoggerFactory) handle =
            let logger = loggerFactory.CreateLogger("Handler")

            let startHandle (consumeTrace: Trace) (message: string) =
                if ExampleSettings.debugHandleFunction then
                    logger.LogTrace("Start handle {message} with {trace}", message, consumeTrace)
                {
                    Message = message
                    Trace =
                        "Handle event"
                        |> Trace.ChildOf.startActive consumeTrace
                        |> Trace.addTags [
                            "peer.service", "stream"
                            "component", "handler-lib"
                        ]
                        |> tee (fun handleTrace -> if ExampleSettings.debugHandleFunction then logger.LogTrace("Handle with {trace}", handleTrace))
                }

            logger.LogTrace("Consuming stream")
            Stream.consume
            |> Seq.take ExampleSettings.numberOfMessages    // in real-life application, this would go "forever", but we need to stop an example in the end
            |> Seq.map (fun { Message = message; Trace = trace } -> async {
                (* tohle je jedna moznost - uzavrit zvlast handle trace (tracedMessage) a pak jeste finishnout i tu traced message po handle (coz je asi zbytecne a vyjde nastejno)
                    use tracedMessage =
                    message
                    |> startHandle trace

                tracedMessage
                |> tee (handle >> Async.RunSynchronously)       //! problem muze byt ale tady v tom RunSychronously, coz muze rozbit prave ten AsyncLocal scope pro span
                |> TracedMessage.finish *)

                use tracedMessage =
                    message
                    |> startHandle trace

                do! handle tracedMessage
            })
            |> List.ofSeq
            |> AsyncResult.ofSequentialAsyncs id
            |> AsyncResult.ignore
            (* |> ofSequentialAsyncResultsSeq id
            |> AsyncResult.ignore *)
