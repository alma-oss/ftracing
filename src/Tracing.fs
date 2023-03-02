namespace Lmc.Tracing

open System
open System.Diagnostics
open System.Collections.Generic

open OpenTelemetry
open OpenTelemetry.Trace
open OpenTelemetry.Resources
open Microsoft.Extensions.Logging

open Lmc.ErrorHandling
open Lmc.Logging

[<RequireQualifiedAccess>]
module Tracer =
    let requiredEnvironmentVariables = [
        "TRACING_SERVICE_NAME"
        "TRACING_THRIFT_HOST"
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
            LogToFromEnvironment "TRACING_LOG_TO"
            UseLevelFromEnvironment "TRACING_LOG_LEVEL"

            LogToSerilog [
                AddMetaFromEnvironment "TRACING_LOG_META"
            ]
        ]

    let private env = getEnvVarValue >> Result.orFail

    let mutable private globalTracerProvider: TracerProvider option = None

    let private initTracer () =
        use loggerFactory = loggerFactory()
        let logger = loggerFactory.CreateLogger "OpenTelemetry.Tracer"

        let serviceName = env "TRACING_SERVICE_NAME"

        let tracerProvider =
            match globalTracerProvider with
            | Some tracerProvider ->
                logger.LogDebug("Use global tracer provider")
                tracerProvider

            | _ ->
                logger.LogDebug("Init tracer provider")
                let attributes =
                    match getEnvVarValue "TRACING_TAGS" with
                    | Ok tags ->
                        tags.Split ","
                        |> Seq.choose (fun tag ->
                            match tag.Split "=" |> Seq.toList with
                            | [] | [ _ ] -> None
                            | [ key; value ] -> Some (key, value)
                            | _ -> None
                        )
                        |> Seq.map (fun (k, v) -> KeyValuePair (k, v :> obj))
                    | _ -> Seq.empty

                let sampler = AlwaysOnSampler()

                let provider =
                    Sdk.CreateTracerProviderBuilder()
                        .AddSource(serviceName)
                        .SetResourceBuilder(
                            ResourceBuilder.CreateDefault()
                                .AddService(serviceName = serviceName)
                                .AddAttributes(attributes)
                                .AddTelemetrySdk()
                        )
                        .AddHttpClientInstrumentation()
                        .AddJaegerExporter(fun opt ->
                            let logger = loggerFactory.CreateLogger("Jaeger")

                            let logConf scope (conf: Exporter.JaegerExporterOptions) =
                                logger.LogDebug("Settings<{scope}> ... Endpoint: {opt}", scope, conf.Endpoint)
                                logger.LogDebug("Settings<{scope}> ... ExportProcessorType: {opt}", scope, conf.ExportProcessorType)
                                logger.LogDebug("Settings<{scope}> ... MaxPayloadSizeInBytes: {opt}", scope, conf.MaxPayloadSizeInBytes)
                                logger.LogDebug("Settings<{scope}> ... AgentHost: {opt}", scope, conf.AgentHost)
                                logger.LogDebug("Settings<{scope}> ... AgentPort: {opt}", scope, conf.AgentPort)
                                logger.LogDebug("Settings<{scope}> ... Protocol: {opt}", scope, conf.Protocol)
                                logger.LogDebug("Settings<{scope}> ... HttpClientFactory: {opt}", scope, conf.HttpClientFactory.Invoke())

                            opt |> logConf "default"

                            let host = env "TRACING_THRIFT_HOST"
                            opt.Endpoint <- Uri($"http://{host}/api/traces")
                            opt.Protocol <- Exporter.JaegerExportProtocol.HttpBinaryThrift

                            opt |> logConf "app"
                        )
                        .SetSampler(sampler)

                let provider =
                    match getEnvVarValue "TRACING_EXPORT_CONSOLE" |> Result.map(fun s -> s.ToLowerInvariant()) with
                    | Ok "on" -> provider.AddConsoleExporter()
                    | _ -> provider

                let tracerProvider = provider.Build()
                globalTracerProvider <- Some tracerProvider
                tracerProvider

        tracerProvider.GetTracer(serviceName)

    let buildTracer () =
        let mutable tracerCache: Tracer option = None

        fun () ->
            use loggerFactory = loggerFactory()
            let logger = loggerFactory.CreateLogger("OpenTelemetry.Tracer.Build")

            match tracerCache with
            | Some tracer ->
                logger.LogDebug("Getting cached tracer")
                tracer
            | None ->
                logger.LogDebug("Initalizing new tracer")
                let tracer = initTracer ()
                tracerCache <- Some tracer
                tracer

    /// Default tracer for a common usage,
    /// where an async "thread" is a scope for an active trace
    let tracer =
        buildTracer ()

    let finishTracerProvider () =
        use loggerFactory = loggerFactory()
        let logger = loggerFactory.CreateLogger "OpenTelemetry.Tracer"

        match globalTracerProvider with
        | Some tracerProvider ->
            logger.LogInformation("Finishing global tracer provider ...")
            tracerProvider.Dispose()
            globalTracerProvider <- None
        | _ ->
            logger.LogWarning("There is no global tracer provider.")

[<RequireQualifiedAccess>]
module TelemetrySpanContext =
    let (|IsAlive|_|): SpanContext -> SpanContext option = fun context ->
        if context.IsValid then Some (IsAlive context)
        else None

    let internal format (context: SpanContext) =
        $"{context.TraceId}.{context.SpanId}"

[<RequireQualifiedAccess>]
module TelemetrySpan =
    /// OpenTelemetry span is created as Noop when it is inactive
    /// It may be just zeros - 000
    /// It may contain a parent span - 000.000
    /// NOTE: number of zeros is not always the same
    let private isNoop (id: string) =
        id.Replace(".", "") |> Seq.forall ((=) '0')

    let (|IsAlive|_|): TelemetrySpan -> TelemetrySpan option = fun span ->
        match span.Context with
        | TelemetrySpanContext.IsAlive _ -> Some (IsAlive span)
        | _ -> None

    let (|IsAliveChild|_|): TelemetrySpan -> TelemetrySpan option = function
        | IsAlive span when (string span.ParentSpanId) |> isNoop |> not -> Some (IsAliveChild span)
        | _ -> None

    let internal format (span: TelemetrySpan) =
        let context = span.Context
        let parentSuffix =
            match span with
            | IsAliveChild child -> $".{child.ParentSpanId}"
            | _ -> ""

        $"{context |> TelemetrySpanContext.format}{parentSuffix}"

[<CustomEquality; NoComparison>]
type TraceContext =
    | TraceContext of SpanContext

    member internal this.Value () =
        match this with
        | TraceContext context -> context |> TelemetrySpanContext.format

    override this.GetHashCode() =
        this.Value() |> hash

    override this.ToString() =
        this.Value()

    override ctxA.Equals (b) =
        match b with
        | :? TraceContext as ctxB -> ctxA.Value() = ctxB.Value()
        | _ -> false

[<RequireQualifiedAccess>]
module TraceContext =
    let id: TraceContext -> string = fun ctx -> ctx.Value()
    let traceId (TraceContext context): string = string context.TraceId
    let spanId (TraceContext context): string = string context.SpanId

[<CustomEquality; NoComparison>]
type LiveTrace =
    | Span of TelemetrySpan

    member this.Finish() =
        use factory = Tracer.loggerFactory()
        let logger = factory.CreateLogger("Trace.finish")

        match this with
        | Span span ->
            logger.LogTrace("{type}.Finish({span})", "Span", span |> TelemetrySpan.format)
            span.End()

    member this.Context () =
        match this with
        | Span span -> TraceContext span.Context

    override this.GetHashCode() =
        this.Context() |> hash

    override spanA.Equals (b) =
        match b with
        | :? LiveTrace as spanB -> spanA.Context() = spanB.Context()
        | :? TraceContext as ctxB -> spanA.Context() = ctxB
        | _ -> false

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module LiveTrace =
    let finish (trace: LiveTrace): unit = trace.Finish()

    let context: LiveTrace -> TraceContext = function
        | Span span -> TraceContext span.Context

    let id: LiveTrace -> string = context >> TraceContext.id
    let traceId: LiveTrace -> string = context >> TraceContext.traceId
    let spanId: LiveTrace -> string = context >> TraceContext.spanId

[<CustomEquality; NoComparison>]
type Trace =
    | Live of LiveTrace
    | Context of TraceContext
    | Inactive

    member this.Finish() =
        match this with
        | Live trace -> trace.Finish()
        | _ -> ()

    member internal this.ParentId() =
        match this with
        | Live (Span (TelemetrySpan.IsAliveChild child)) -> Some child.ParentSpanId
        | _ -> None

    member private this.Value () =
        match this with
        | Live trace -> trace.Context().Value()
        | Context context -> context.Value()
        | Inactive -> ""

    override this.GetHashCode() =
        this.Value() |> hash

    override traceA.Equals (b) =
        match b with
        | :? Trace as traceB -> traceA.Value() = traceB.Value()
        | :? TraceContext as ctxB -> traceA.Value() = ctxB.Value()
        | _ -> false

    override this.ToString() =
        match this with
        | Live (Span span) -> sprintf "Trace.Live (%s)" (span |> TelemetrySpan.format)
        | Context context -> sprintf "Trace.Context (%s)" (context.Value())
        | Inactive -> "Trace.Inactive"

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module Trace =
    let ofContextOption: TraceContext option -> Trace = function
        | Some context -> Context context
        | _ -> Inactive

    let finish (trace: Trace): unit = trace.Finish()

    let context: Trace -> TraceContext option = function
        | Live trace -> Some (trace |> LiveTrace.context)
        | _ -> None

    let id: Trace -> string option = context >> Option.map TraceContext.id
    let traceId: Trace -> string option = context >> Option.map TraceContext.traceId
    let spanId: Trace -> string option = context >> Option.map TraceContext.spanId
    let parentId: Trace -> string option = fun trace -> trace.ParentId() |> Option.map string

    //
    // Update spans
    //

    let addEvent (event: string) trace =
        match trace with
        | Live (Span span) -> span.AddEvent(event) |> ignore
        | Context _
        | Inactive -> ()
        trace

    let addTags (tags: (string * string) list) trace =
        tags
        |> List.iter (fun (tag, value) ->
            match trace with
            | Live (Span span) -> span.SetAttribute(tag, value) |> ignore
            | Context _
            | Inactive -> ()
        )
        trace

    let addError error =
        addTags (error |> TracedError.asTags)

    [<RequireQualifiedAccess>]
    module internal Build =
        type Reference =
            | AsChildOf of Trace
            | AsFollowsFrom of Trace

        // name             : string *
        // kind             : SpanKind *
        // parentContext    : inref<SpanContext> *
        // initialAttributes: SpanAttributes *
        // links            : IEnumerable<Link> *
        // startTime        : DateTimeOffset

        /// [MemoryLeakContextProblem]
        /// This function creates a static context with random ids, instead of doing it in a tracer.
        /// The reason for it is bypass a memory leak when doing it in the tracer itself.
        ///
        /// Curently I'm not entirely sure why is that, since I debugged it quite some time ago.
        let private ctx (): SpanContext =
            let tId = ActivityTraceId.CreateRandom()
            let sId = ActivitySpanId.CreateRandom()
            SpanContext(&tId, &sId, ActivityTraceFlags.Recorded, false)

        let span (name: string) =
            let ctx = ctx ()
            Tracer.tracer().StartSpan(name, parentContext = &ctx)

        let spanAt (startTime: DateTimeOffset) (name: string) =
            let ctx = ctx ()
            Tracer.tracer().StartSpan(name = name, startTime = startTime, parentContext = &ctx)

        let startActive name =
            let ctx = ctx ()
            let span = Tracer.tracer().StartActiveSpan(name, parentContext = &ctx)
            Tracer.WithSpan(span)

        [<RequireQualifiedAccess>]
        module private ChildOf =
            let private childOf (parent: SpanContext) name =
                Tracer.tracer().StartSpan(name = name, parentContext = &parent)

            let private childOfAt (startTime: DateTimeOffset) (parent: SpanContext) name =
                Tracer.tracer().StartSpan(name = name, startTime = startTime, parentContext = &parent)

            let at = function
                | Some startTime -> childOfAt startTime
                | _ -> childOf

        let private createReference childOf reference name =
            match reference with
            | AsChildOf Inactive
            | AsFollowsFrom Inactive -> None

            | AsChildOf (Live (Span parent)) -> name |> childOf parent.Context |> Some
            | AsChildOf (Context (TraceContext parent)) -> name |> childOf parent |> Some

            | AsFollowsFrom (Live (Span parent)) -> name |> childOf parent.Context |> Some
            | AsFollowsFrom (Context (TraceContext parent)) -> name |> childOf parent |> Some

        let reference reference name =
            createReference (ChildOf.at None) reference name

        let referenceAt reference startTime name =
            createReference (ChildOf.at (Some startTime)) reference name

        [<RequireQualifiedAccess>]
        module private ActiveChildOf =
            let private childOf (parent: SpanContext) name =
                Tracer.tracer().StartActiveSpan(name = name, parentContext = &parent)

            let private childOfAt (startTime: DateTimeOffset) (parent: SpanContext) name =
                Tracer.tracer().StartActiveSpan(name = name, startTime = startTime, parentContext = &parent)

            let at = function
                | Some startTime -> childOfAt startTime
                | _ -> childOf

        let activeReference reference name =
            createReference (ActiveChildOf.at None) reference name

        let activeReferenceAt reference startTime name =
            createReference (ActiveChildOf.at (Some startTime)) reference name

    //
    // Public Span modules
    //

    [<RequireQualifiedAccess>]
    module Span =
        let start name =
            name |> Build.span |> Span |> Live

        let startAt startTime name =
            name |> Build.spanAt startTime |> Span |> Live

    [<RequireQualifiedAccess>]
    module Active =
        let private (|NoopSpan|_|): TelemetrySpan -> _ = function
            | TelemetrySpan.IsAlive _span -> None
            | _ -> Some NoopSpan

        let private currentIn () =
            use factory = Tracer.loggerFactory()
            let logger = factory.CreateLogger("Trace.Active.current")

            match Tracer.CurrentSpan with
            | null | NoopSpan ->
                logger.LogTrace("No Active Trace")
                Inactive
            | span ->
                logger.LogTrace("Active Trace: {traceId} with Span: {spanId}", string span.Context.TraceId, string span.Context.SpanId)
                Live (Span span)

        let current = currentIn

        let start name =
            name |> Build.startActive |> Span |> Live

        let finish = current >> finish

        /// Activate the trace in current scope manager as an Active trace.
        let activate trace =
            match trace with
            | Live (Span s) -> Tracer.WithSpan(s) |> ignore
            | _ -> ()
            |> current

    [<RequireQualifiedAccess>]
    module internal Reference =
        type private BuildReference = Trace -> Build.Reference

        let start (reference: BuildReference) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.reference (reference parentTrace) with
            | Some (TelemetrySpan.IsAlive span) -> span |> Span |> Live
            | _ -> Inactive

        let startAt (reference: BuildReference) (startTime: DateTimeOffset) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.referenceAt (reference parentTrace) startTime with
            | Some (TelemetrySpan.IsAlive span) -> span |> Span |> Live
            | _ -> Inactive

        let startFromActive (reference: BuildReference) (name: string): Trace =
            name |> start reference (Active.current())

        let startActive (reference: BuildReference) (parentTrace: Trace) (name: string): Trace =
            match name |> Build.activeReference (reference parentTrace) with
            | Some (TelemetrySpan.IsAlive activeSpan) -> activeSpan |> Span |> Live
            | _ -> Inactive

        let startActiveAt (reference: BuildReference) (startTime: DateTimeOffset) (parentTrace: Trace) (name: string) : Trace=
            match name |> Build.activeReferenceAt (reference parentTrace) startTime with
            | Some (TelemetrySpan.IsAlive activeSpan) -> activeSpan |> Span |> Live
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
