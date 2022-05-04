namespace Lmc.Tracing

open System
open Jaeger
open Jaeger.Propagation
open Jaeger.Senders.Grpc
open Jaeger.Samplers
open OpenTracing
open OpenTracing.Propagation
open OpenTracing.Tag
open OpenTracing.Util

open Microsoft.Extensions.Logging

open Lmc.ErrorHandling
open Lmc.Logging

[<RequireQualifiedAccess>]
module Tracer =
    let requiredEnvironmentVariables =
        [
            "JAEGER_SERVICE_NAME"
            "JAEGER_GRPC_TARGET"
            "JAEGER_TRACEID_128BIT"
            "JAEGER_SAMPLER_PARAM"
            "JAEGER_SAMPLER_TYPE"
            "JAEGER_SENDER_FACTORY"
            "JAEGER_PROPAGATION"
            "JAEGER_TAGS"
        ]

    [<RequireQualifiedAccess>]
    module Check =
        let environment () =
            requiredEnvironmentVariables
            |> List.map getEnvVarValue
            |> Validation.ofResults
            |> Result.map ignore

        let isTracerAvailable () =
            match environment () with
            | Ok () -> true
            | Error _ -> false

    let internal loggerFactory () =
        LoggerFactory.create [
            LogToFromEnvironment "JAEGER_LOG_TO"
            UseLevelFromEnvironment "JAEGER_LOG_LEVEL"

            LogToSerilog [
                AddMetaFromEnvironment "JAEGER_LOG_META"
            ]
        ]

    let private initTracer scopeManager =
        use loggerFactory = loggerFactory()
        let logger = loggerFactory.CreateLogger("Trace.Tracer")

        // todo<idea> - build config from values which should be the same (propagation=b3, ...)
        let config = Configuration.FromEnv(loggerFactory)

        config.ReporterConfig.SenderConfig.SenderResolver.RegisterSenderFactory<GrpcSenderFactory>()
        |> ignore

        let tracer =
            config
                .GetTracerBuilder()
                .WithScopeManager(scopeManager)
                .Build()
        logger.LogDebug("Tracer initialized with ScopeManager<{ScopeManager}>.", tracer.ScopeManager.GetType())

        tracer

    let buildTracer scopeManager =
        let mutable tracerCache: Tracer option = None

        fun () ->
            match tracerCache with
            | Some tracer -> tracer
            | None ->
                let tracer = initTracer scopeManager
                tracerCache <- Some tracer
                tracer

    /// Default tracer for a common usage,
    /// where an async "thread" is a scope for an active trace
    let tracer =
        buildTracer (AsyncLocalScopeManager())

type TracedError<'Error> = {
    Error: 'Error
    Message: string
    Stack: string option
    Kind: string option
}

[<RequireQualifiedAccess>]
module TracedError =
    open System.Collections.Generic

    let ofExn (e: exn) =
        {
            Error = e
            Message = e.Message
            Stack = Some e.StackTrace
            Kind = Some (e.GetType().ToString())
        }

    let ofError format error: TracedError<'Error> =
        {
            Error = error
            Message = error |> format
            Stack = None
            Kind = try error.GetType().ToString() |> Some with _ -> None
        }

    let internal toErrorDictionary error =
        [
            "event", "error" :> obj
            "error.object", error.Error :> obj
            "message", error.Message :> obj

            match error.Stack with
            | Some stack -> "stack", stack :> obj
            | _ -> ()

            match error.Kind with
            | Some kind -> "error.kind", kind :> obj
            | _ -> ()
        ]
        |> List.map (fun (k, v) -> KeyValuePair(k, v))
        |> Dictionary

type TraceContext = TraceContext of ISpanContext

[<RequireQualifiedAccess>]
module TraceContext =
    let id (TraceContext context) = $"{context.TraceId}.{context.SpanId}"
    let traceId (TraceContext context) = context.TraceId
    let spanId (TraceContext context) = context.SpanId

type LiveTrace =
    | Span of ISpan
    | Scope of IScope

    member this.Finish() =
        use factory = Tracer.loggerFactory()
        let logger = factory.CreateLogger("Trace.finish")

        match this with
        | Scope scope ->
            logger.LogTrace("{type}.Finish({span}) in scope {scope}", "Scoped", scope.Span, scope)
            scope.Dispose()

        | Span span ->
            logger.LogTrace("{type}.Finish({span})", "Span", span)
            span.Finish()

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module LiveTrace =
    let finish (trace: LiveTrace) = trace.Finish()

    let context = function
        | Span span -> TraceContext span.Context
        | Scope scope -> TraceContext scope.Span.Context

    let id = context >> TraceContext.id
    let traceId = context >> TraceContext.traceId
    let spanId = context >> TraceContext.spanId

type Trace =
    | Live of LiveTrace
    | Context of TraceContext
    | Inactive

    member this.Finish() =
        match this with
        | Live trace -> trace.Finish()
        | _ -> ()

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module Trace =
    open System.Collections.Generic

    let ofContextOption = function
        | Some context -> Context context
        | _ -> Inactive

    let finish (trace: Trace) = trace.Finish()

    let context = function
        | Live trace -> Some (trace |> LiveTrace.context)
        | _ -> None

    let id = context >> Option.map TraceContext.id
    let traceId = context >> Option.map TraceContext.traceId
    let spanId = context >> Option.map TraceContext.spanId

    //
    // Update spans
    //

    let addBaggage (baggage: (string * string) list) trace =
        let logs () =
            baggage
            |> List.fold
                (fun (acc: IDictionary<string, obj>) (key, value) ->
                    acc.Add(key, value)
                    acc
                )
                (Dictionary<string, obj>())

        match trace with
        | Live (Span span) -> span.Log(logs()) |> ignore
            // span.SetBaggageItem(key, value) |> ignore
        | Live (Scope scope) -> scope.Span.Log(logs()) |> ignore
            // scope.Span.SetBaggageItem(key, value) |> ignore
        | Context _
        | Inactive -> ()
        trace

    let addTags (tags: (string * string) list) trace =
        tags
        |> List.iter (fun (tag, value) ->
            match trace with
            | Live (Span span) -> span.SetTag(tag, value) |> ignore
            | Live (Scope scope) -> scope.Span.SetTag(tag, value) |> ignore
            | Context _
            | Inactive -> ()
        )
        trace

    let addError error trace =
        match trace |> addTags [ "error", "true" ] with
        | Live (Span span) -> span.Log(error |> TracedError.toErrorDictionary) |> ignore
        | Live (Scope scope) -> scope.Span.Log(error |> TracedError.toErrorDictionary) |> ignore
        | Context _
        | Inactive -> ()
        trace

    [<RequireQualifiedAccess>]
    module internal Build =
        type Reference =
            | AsChildOf of Trace
            | AsFollowsFrom of Trace

        let span name =
            Tracer.tracer().BuildSpan(name)

        let spanAt startTime name =
            (span name).WithStartTimestamp(startTime)

        let private createReference (span: string -> ISpanBuilder) reference name =
            match reference with
            | AsChildOf Inactive
            | AsFollowsFrom Inactive -> None

            | AsChildOf (Live (Span parent)) -> (span name).AsChildOf(parent.Context) |> Some
            | AsChildOf (Live (Scope parent)) -> (span name).AsChildOf(parent.Span.Context) |> Some
            | AsChildOf (Context (TraceContext parent)) -> (span name).AsChildOf(parent) |> Some

            | AsFollowsFrom (Live (Span parent)) -> (span name).AddReference(References.FollowsFrom, parent.Context) |> Some
            | AsFollowsFrom (Live (Scope parent)) -> (span name).AddReference(References.FollowsFrom, parent.Span.Context) |> Some
            | AsFollowsFrom (Context (TraceContext parent)) -> (span name).AddReference(References.FollowsFrom, parent) |> Some

        let reference reference name =
            createReference span reference name

        let referenceAt reference startTime name =
            createReference (spanAt startTime) reference name

    //
    // Public Span modules
    //

    [<RequireQualifiedAccess>]
    module Span =
        let start name =
            (Build.span name).Start() |> Span |> Live

        let startAt startTime name =
            (Build.spanAt startTime name).Start() |> Span |> Live

    [<RequireQualifiedAccess>]
    module Active =
        let private currentIn (tracer: ITracer) =
            use factory = Tracer.loggerFactory()
            let logger = factory.CreateLogger("Trace.Active.current")

            logger.LogTrace("Manager<{type}>", tracer.ScopeManager.GetType())

            match tracer.ActiveSpan with
            | null ->
                logger.LogTrace("No Active Trace")
                Inactive
            | span ->
                logger.LogTrace("Active Trace: {traceId} with Span: {spanId}", span.Context.TraceId, span.Context.SpanId)
                Live (Span span)

        let current () =
            currentIn (Tracer.tracer())

        let start name =
            (Build.span name).StartActive() |> Scope |> Live

        let finish = current >> finish

        /// Activate the trace in current scope manager as an Active trace.
        let activate trace =
            match trace with
            | Live (Span s) -> Tracer.tracer().ScopeManager.Activate(s, true) |> ignore
            | _ -> ()
            |> current

    [<RequireQualifiedAccess>]
    module internal Reference =
        type private BuildReference = Trace -> Build.Reference

        let start (reference: BuildReference) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.reference (reference parentTrace) with
            | Some trace -> trace.Start() |> Span |> Live
            | _ -> Inactive

        let startAt (reference: BuildReference) (startTime: DateTimeOffset) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.referenceAt (reference parentTrace) startTime with
            | Some trace -> trace.Start() |> Span |> Live
            | _ -> Inactive

        let startFromActive (reference: BuildReference) (name: string): Trace =
            name |> start reference (Active.current())

        let startActive (reference: BuildReference) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.reference (reference parentTrace) with
            | Some trace -> trace.StartActive() |> Scope |> Live
            | _ -> Inactive

        let startActiveAt (reference: BuildReference) (startTime: DateTimeOffset) (parentTrace: Trace) (name: string) : Trace=
            match name |> Build.referenceAt (reference parentTrace) startTime with
            | Some trace -> trace.StartActive() |> Scope |> Live
            | _ -> Inactive

        let startActiveFromActive (reference: BuildReference) (name: string) : Trace=
            name |> startActive reference (Active.current())

        let continueOrStartActive (reference: BuildReference) (extract: unit -> Trace) (name: string): Trace =
            name
            |> match extract() with
                | Inactive -> Active.start
                | trace -> startActive reference trace

        let continueOrStartActiveFromActive (reference: BuildReference): string -> Trace = continueOrStartActive reference Active.current

        let continueOrStart (reference: BuildReference) (extract: unit -> Trace) (name: string): Trace =
            name
            |> match extract() with
                | Inactive -> Span.start
                | trace -> start reference trace

        let continueOrStartAt (reference: BuildReference) (extract: unit -> Trace) (startTime: DateTimeOffset) (name: string): Trace =
            name
            |> match extract() with
                | Inactive -> Span.startAt startTime
                | trace -> startAt reference startTime trace

    [<RequireQualifiedAccess>]
    module ChildOf =
        let start = Reference.start Build.AsChildOf
        let startFromActive = Reference.startFromActive Build.AsChildOf
        let startActive = Reference.startActive Build.AsChildOf
        let startActiveAt = Reference.startActiveAt Build.AsChildOf
        let startActiveFromActive = Reference.startActiveFromActive Build.AsChildOf
        let continueOrStartActive = Reference.continueOrStartActive Build.AsChildOf
        let continueOrStart = Reference.continueOrStart Build.AsChildOf
        let continueOrStartAt = Reference.continueOrStartAt Build.AsChildOf
        let continueOrStartActiveFromActive = Reference.continueOrStartActiveFromActive Build.AsChildOf

    [<RequireQualifiedAccess>]
    module FollowFrom =
        let start = Reference.start Build.AsFollowsFrom
        let startFromActive = Reference.startFromActive Build.AsFollowsFrom
        let startActive = Reference.startActive Build.AsFollowsFrom
        let startActiveAt = Reference.startActiveAt Build.AsFollowsFrom
        let startActiveFromActive = Reference.startActiveFromActive Build.AsFollowsFrom
        let continueOrStartActive = Reference.continueOrStartActive Build.AsFollowsFrom
        let continueOrStart = Reference.continueOrStart Build.AsFollowsFrom
        let continueOrStartAt = Reference.continueOrStartAt Build.AsFollowsFrom
        let continueOrStartActiveFromActive = Reference.continueOrStartActiveFromActive Build.AsFollowsFrom
