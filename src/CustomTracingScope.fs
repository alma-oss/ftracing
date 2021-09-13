namespace Lmc.Tracing

type TraceIdentifier = TraceIdentifier of string

module CustomTracingScope =
    open System
    open Lmc.State
    open Lmc.State.ConcurrentStorage

    let private state: State<TraceIdentifier, Trace> = State.empty()

    [<RequireQualifiedAccess>]
    module TracingState =
        let storeActiveTrace identifier trace =
            state
            |> State.set (Key identifier) trace

        let loadActiveTrace identifier =
            state
            |> State.tryFind (Key identifier)
            |> Option.defaultValue Inactive
            |> Trace.Active.activate

        let clearActiveTrace identifier =
            state
            |> State.tryRemove (Key identifier)

    type ScopedTrace(identifier) =
        member __.Save(trace) = TracingState.storeActiveTrace identifier trace
        member __.Trace with get() = TracingState.loadActiveTrace identifier

        member this.Finish() =
            this.Trace
            |> Trace.finish

            TracingState.clearActiveTrace identifier

        interface IDisposable with
            member this.Dispose() = this.Finish()

    [<RequireQualifiedAccess>]
    module ScopedTrace =
        let finish (scopedTrace: ScopedTrace) =
            scopedTrace.Finish()
