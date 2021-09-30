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

    let checkEnvironment getEnvironmentValue =
        requiredEnvironmentVariables
        |> List.map getEnvironmentValue
        |> Validation.ofResults
        |> Result.map ignore

    let mutable private tracerLoggerFactory: ILoggerFactory option = None

    let internal loggerFactory () =
        match tracerLoggerFactory with
        | Some loggerFactory -> loggerFactory
        | _ ->
            let environmentLevel =
                try Environment.GetEnvironmentVariable "JAEGER_LOG_LEVEL" |> string
                with _ -> "<default>"

            let parsedLevel =
                match environmentLevel.ToLowerInvariant().Trim() with
                | "trace" -> LogLevel.Trace
                | "debug" -> LogLevel.Debug
                | "information" -> LogLevel.Information
                | "warning" -> LogLevel.Warning
                | "error" -> LogLevel.Error
                | "critical" -> LogLevel.Critical
                | _ -> LogLevel.None

            let factory =
                LoggerFactory.Create(fun builder ->
                    builder
                        .SetMinimumLevel(parsedLevel)
                        .AddConsole()
                    |> ignore
                )

            tracerLoggerFactory <- Some factory
            factory

    let private initTracer scopeManager =
        let loggerFactory = loggerFactory()
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

type ActiveTrace =
    | Scope of IScope
    | Span of ISpan

    member this.Finish() =
        let logger = Tracer.loggerFactory().CreateLogger("Trace.finish")

        match this with
        | Scope scope ->
            logger.LogDebug("Scope.Dispose(Trace: {traceId}, Span: {spanId})", scope.Span.Context.TraceId, scope.Span.Context.SpanId)
            scope.Dispose()
        | Span span ->
            logger.LogDebug("Span.Finish(Trace: {traceId}, Span: {spanId})", span.Context.TraceId, span.Context.SpanId)
            span.Finish()

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module ActiveTrace =
    let finish (trace: ActiveTrace) = trace.Finish()

    let context = function
        | Scope scope -> TraceContext scope.Span.Context
        | Span span -> TraceContext span.Context

    let id = context >> TraceContext.id
    let traceId = context >> TraceContext.traceId
    let spanId = context >> TraceContext.spanId

type Trace =
    | Active of ActiveTrace
    | Context of TraceContext
    | Inactive

    member this.Finish() =
        match this with
        | Active active -> active.Finish()
        | _ -> ()

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module Trace =
    let ofContextOption = function
        | Some context -> Context context
        | _ -> Inactive

    let finish (trace: Trace) = trace.Finish()

    let context = function
        | Active active -> Some (active |> ActiveTrace.context)
        | _ -> None

    let id = context >> Option.map TraceContext.id
    let traceId = context >> Option.map TraceContext.traceId
    let spanId = context >> Option.map TraceContext.spanId

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

            | AsChildOf (Active (Scope parent)) -> (span name).AsChildOf(parent.Span.Context) |> Some
            | AsChildOf (Active (Span parent)) -> (span name).AsChildOf(parent.Context) |> Some
            | AsChildOf (Context (TraceContext parent)) -> (span name).AsChildOf(parent) |> Some

            | AsFollowsFrom (Active (Scope parent)) -> (span name).AddReference(References.FollowsFrom, parent.Span.Context) |> Some
            | AsFollowsFrom (Active (Span parent)) -> (span name).AddReference(References.FollowsFrom, parent.Context) |> Some
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
            (Build.span name).Start() |> Span |> Active

        let startAt startTime name =
            (Build.spanAt startTime name).Start() |> Span |> Active

    [<RequireQualifiedAccess>]
    module Active =
        let private currentIn (tracer: ITracer) =
            let logger = Tracer.loggerFactory().CreateLogger("Trace.Active.current")
            let manager = tracer.ScopeManager

            logger.LogDebug("Manager<{type}>", manager.GetType())

            match manager.Active with
            | null ->
                logger.LogDebug("No Active Trace")
                Inactive
            | scope ->
                let span = scope.Span
                logger.LogDebug("Active Trace: {traceId} with Span: {spanId}", span.Context.TraceId, span.Context.SpanId)
                Active (Span span)

        let current () =
            currentIn (Tracer.tracer())

        let start name =
            (Build.span name).StartActive() |> Scope |> Active

        let finish = current >> finish

        /// Activate the trace in current scope manager as an Active trace.
        let activate trace =
            match trace with
            | Active (Scope s) -> Tracer.tracer().ScopeManager.Activate(s.Span, true) |> ignore
            | Active (Span s) -> Tracer.tracer().ScopeManager.Activate(s, true) |> ignore
            | _ -> ()
            |> current

    [<RequireQualifiedAccess>]
    module internal Reference =
        let start (reference: Trace -> Build.Reference) parentTrace name =
            match name |> Build.reference (reference parentTrace) with
            | Some trace -> trace.Start() |> Span |> Active
            | _ -> Inactive

        let startAt (reference: Trace -> Build.Reference) startTime parentTrace name =
            match name |> Build.referenceAt (reference parentTrace) startTime with
            | Some trace -> trace.Start() |> Span |> Active
            | _ -> Inactive

        let startFromActive reference name =
            name |> start reference (Active.current())

        let startActive reference parentTrace name =
            match name |> Build.reference (reference parentTrace) with
            | Some trace -> trace.StartActive() |> Scope |> Active
            | _ -> Inactive

        let startActiveAt reference startTime parentTrace name =
            match name |> Build.referenceAt (reference parentTrace) startTime with
            | Some trace -> trace.StartActive() |> Scope |> Active
            | _ -> Inactive

        let startActiveFromActive reference name =
            name |> startActive reference (Active.current())

        let continueOrStartActive reference extract name =
            name
            |> match extract() with
                | Inactive -> Active.start
                | trace -> startActive reference trace

        let continueOrStartActiveFromActive reference = continueOrStartActive reference Active.current

        let continueOrStart reference extract name =
            name
            |> match extract() with
                | Inactive -> Span.start
                | trace -> start reference trace

        let continueOrStartAt reference extract startTime name =
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

    //
    // Update spans
    //

    let addBaggage baggage trace =
        baggage
        |> List.iter (fun (key, value) ->
            match trace with
            | Active (Span span) -> span.SetBaggageItem(key, value) |> ignore
            | Active (Scope scope) -> scope.Span.SetBaggageItem(key, value) |> ignore
            | Context _
            | Inactive -> ()
        )
        trace

    let addTags (tags: (string * string) List) trace =
        tags
        |> List.iter (fun (tag, value) ->
            match trace with
            | Active (Span span) -> span.SetTag(tag, value) |> ignore
            | Active (Scope scope) -> scope.Span.SetTag(tag, value) |> ignore
            | Context _
            | Inactive -> ()
        )
        trace

    let addError error trace =
        match trace |> addTags [ "error", "true" ] with
        | Active (Span span) -> span.Log(error |> TracedError.toErrorDictionary) |> ignore
        | Active (Scope scope) -> scope.Span.Log(error |> TracedError.toErrorDictionary) |> ignore
        | Context _
        | Inactive -> ()
        trace
