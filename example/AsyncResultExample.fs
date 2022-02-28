namespace Lmc.Tracing.Example

open System
open Microsoft.Extensions.Logging
open Lmc.Tracing

[<RequireQualifiedAccess>]
module AsyncResultExample =
    open Lmc.ErrorHandling

    type ErrorMessage = exn

    module Service =
        type PersonId = PersonId of int

        [<RequireQualifiedAccess>]
        module Person =
            type Identify =
                | Exists of PersonId
                | NotExists

            let private startTrace name =
                name
                |> sprintf "[Person] %s"
                |> Trace.ChildOf.startActiveFromActive
                //|> Trace.ChildOf.startFromActive
                |> Trace.addTags [
                    "component", "Person"
                ]

            let identify logger interaction: AsyncResult<_, ErrorMessage> = asyncResult {
                use trace = startTrace "Identify person"
                do! simulateWork logger 1

                if interaction = "is-there" then
                    trace |> Trace.addTags [ "person", "exists"; "person.id", "42" ] |> ignore
                    return Exists (PersonId 42)
                else
                    trace |> Trace.addTags [ "person", "not-exists" ] |> ignore
                    return NotExists
            }

            let create logger interaction: AsyncResult<PersonId, ErrorMessage> = asyncResult {
                use trace = startTrace "Create person"
                let traceCreated (PersonId id) = trace |> Trace.addTags [ "person.id", string id ] |> ignore
                do! simulateWork logger 1

                return PersonId 66 |> tee traceCreated
            }

            type State =
                | Consistent
                | Inconsistent

            let loadState logger (PersonId id): AsyncResult<_, ErrorMessage> = asyncResult {
                use trace =
                    startTrace "Load state"
                    |> Trace.addTags [ "person.id", string id ]
                do! simulateWork logger 1

                if id = 66 then
                    trace |> Trace.addTags [ "state", "inconsistent" ] |> ignore
                    return Inconsistent
                else
                    trace |> Trace.addTags [ "state", "consistent" ] |> ignore
                    return Consistent
            }

            let buffer logger (event, PersonId personId): AsyncResult<_, ErrorMessage> = asyncResult {
                use _ =
                    startTrace "Buffer async event"
                    |> Trace.addTags [ "person.id", string id ]
                do! simulateWork logger 1
                return ()
            }

        type IdentifyResult =
            | PersonExistsAndIsConsistentSoPersonilizeInteractionAndContinue of PersonId
            | PersonAggregateHandlesPersonAndEventWasBufferedSoContinue

        let private identifyPerson (loggerFactory: ILoggerFactory) deriveTrace interaction: AsyncResult<IdentifyResult, ErrorMessage> = asyncResult {
            let logger = loggerFactory.CreateLogger("Identify Person")
            let identify = Person.identify logger >> (AsyncResult.tee (fun _ -> logger.LogInformation("identify done")))
            let create = Person.create logger >> (AsyncResult.tee (fun _ -> logger.LogInformation("create done")))
            let loadState = Person.loadState logger >> (AsyncResult.tee (fun _ -> logger.LogInformation("loadState done")))
            let buffer = Person.buffer logger >> (AsyncResult.tee (fun _ -> logger.LogInformation("buffer done")))

            let log msg =
                printfn ""
                logger.LogInformation(msg)

            log "identify ..."
            match! interaction |> identify with
            | Person.Identify.NotExists ->
                log "create ..."
                let! (personId: PersonId) = interaction |> create
                log "buffer ..."
                do! (interaction, personId) |> buffer
                return PersonAggregateHandlesPersonAndEventWasBufferedSoContinue

            | Person.Identify.Exists id ->
                log "load state ..."
                match! id |> loadState with
                | Person.State.Consistent -> return PersonExistsAndIsConsistentSoPersonilizeInteractionAndContinue id
                | Person.State.Inconsistent ->
                    log "buffer ..."
                    do! (interaction, id) |> buffer
                    return PersonAggregateHandlesPersonAndEventWasBufferedSoContinue
        }

        let deriveEvent loggerFactory (interactionEvent, trace) = asyncResult {
            use deriveTrace = "Derive Interaction" |> Trace.ChildOf.startActive trace

            let! identifyResult = interactionEvent |> identifyPerson loggerFactory deriveTrace

            let personalizedEvents =
                match identifyResult with
                | PersonExistsAndIsConsistentSoPersonilizeInteractionAndContinue id ->
                    [
                        $"personalized<{id}>:" + interactionEvent
                    ]

                | PersonAggregateHandlesPersonAndEventWasBufferedSoContinue ->
                    []

            return personalizedEvents |> List.map (fun event -> event, deriveTrace)
        }

        let work (loggerFactory: ILoggerFactory) exampleTrace = asyncResult {
            let service = "Service"
            let logger = loggerFactory.CreateLogger(service)

            logger.LogInformation("Consume stream")

            do! Handler.consumeEvents loggerFactory (fun { Message = interaction; Trace = trace } -> async {
                let! personalizedEvents =
                    (interaction, trace)
                    |> deriveEvent loggerFactory

                let personalizedEvents = personalizedEvents |> Result.orFail

                logger.LogInformation("Personalized events for {interactino}:{events}", interaction, personalizedEvents)
            })
        }

    let run loggerFactory exampleTrace: unit =
        // prepare events
        [
            "is-not-there-yet"
            "is-there"
            "other-one"
            "other-two"
            "other-three"
        ]
        |> List.take ExampleSettings.numberOfMessages
        |> List.map (fun interaction -> { Message = interaction; Trace = exampleTrace })
        |> List.iter Stream.produce

        Service.work loggerFactory exampleTrace
        |> Async.RunSynchronously
        |> Result.orFail
