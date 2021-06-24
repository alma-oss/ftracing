namespace Lmc.Tracing

open System
open Jaeger
open Jaeger.Propagation
open Jaeger.Senders.Grpc
open Jaeger.Samplers
open OpenTracing
open OpenTracing.Propagation
open OpenTracing.Tag

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

    let tracer =
        let loggerFactory =
            LoggerFactory.Create(fun builder ->
                builder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole()
                |> ignore
            )

        // todo<idea> - build config from values which should be the same (propagation=b3, ...)
        let config = Configuration.FromEnv(loggerFactory)

        config.ReporterConfig.SenderConfig.SenderResolver.RegisterSenderFactory<GrpcSenderFactory>()
        |> ignore

        fun () -> config.GetTracer()

type Trace =
    | Scope of IScope
    | Span of ISpan
    | Context of ISpanContext
    | Inactive

    member this.Finish() =
        match this with
        | Scope scope -> scope.Dispose()
        | Span span -> span.Finish()
        | Context _ -> ()
        | Inactive -> ()

    interface IDisposable with
        member this.Dispose() =
            this.Finish()

[<RequireQualifiedAccess>]
module Trace =
    let finish (item: Trace) = item.Finish()

    let context = function
        | Scope scope -> Some scope.Span.Context
        | Span span -> Some span.Context
        | Context context -> Some context
        | Inactive -> None

    let id = context >> Option.map (fun context -> context.TraceId)

    [<RequireQualifiedAccess>]
    module private Build =
        type Reference =
            | AsChildOf of Trace
            | AsFollowsFrom of Trace

        let span name =
            Tracer.tracer().BuildSpan(name)

        let reference reference name =
            match reference with
            | AsChildOf Inactive
            | AsFollowsFrom Inactive -> None

            | AsChildOf (Scope parent) -> (span name).AsChildOf(parent.Span.Context) |> Some
            | AsChildOf (Span parent) -> (span name).AsChildOf(parent.Context) |> Some
            | AsChildOf (Context parent) -> (span name).AsChildOf(parent) |> Some

            | AsFollowsFrom (Scope parent) -> (span name).AddReference(References.FollowsFrom, parent.Span.Context) |> Some
            | AsFollowsFrom (Span parent) -> (span name).AddReference(References.FollowsFrom, parent.Context) |> Some
            | AsFollowsFrom (Context parent) -> (span name).AddReference(References.FollowsFrom, parent) |> Some

    //
    // Public Span modules
    //

    [<RequireQualifiedAccess>]
    module Active =
        let current () =
            match Tracer.tracer().ActiveSpan with
            | null -> Inactive
            | span -> Span span

        let start name =
            (Build.span name).StartActive() |> Scope

        let finish = current >> finish

    [<RequireQualifiedAccess>]
    module ChildOf =
        let start parentTrace name =
            match name |> Build.reference (Build.AsChildOf parentTrace) with
            | Some trace -> trace.Start() |> Span
            | _ -> Inactive

        let startFromActive name =
            name |> start (Active.current())

        let startActive parentTrace name =
            match name |> Build.reference (Build.AsChildOf parentTrace) with
            | Some trace -> trace.StartActive() |> Scope
            | _ -> Inactive

        let startActiveFromActive name =
            name |> startActive (Active.current())

        let continueOrStartActive extract name =
            name
            |> match extract() with
                | Inactive -> Active.start
                | trace -> startActive trace

        let continueOrStartActiveFromActive = continueOrStartActive Active.current

    [<RequireQualifiedAccess>]
    module FollowFrom =
        let start parentTrace name =
            match name |> Build.reference (Build.AsFollowsFrom parentTrace) with
            | Some trace -> trace.Start() |> Span
            | _ -> Inactive

        let startFromActive name =
            name |> start (Active.current())

        let startActive parentTrace name =
            match name |> Build.reference (Build.AsFollowsFrom parentTrace) with
            | Some trace -> trace.StartActive() |> Scope
            | _ -> Inactive

        let startActiveFromActive name =
            name |> startActive (Active.current())

        let continueOrStartActive extract name =
            name
            |> match extract() with
                | Inactive -> Active.start
                | trace -> startActive trace

        let continueOrStartActiveFromActive = continueOrStartActive Active.current

    //
    // Update spans
    //

    let addBaggage baggage trace =
        baggage
        |> List.iter (fun (key, value) ->
            match trace with
            | Span span -> span.SetBaggageItem(key, value) |> ignore
            | Scope scope -> scope.Span.SetBaggageItem(key, value) |> ignore
            | Context _ -> ()
            | Inactive -> ()
        )
        trace

    let addTags (tags: (string * string) List) trace =
        tags
        |> List.iter (fun (tag, value) ->
            match trace with
            | Span span -> span.SetTag(tag, value) |> ignore
            | Scope scope -> scope.Span.SetTag(tag, value) |> ignore
            | Context _ -> ()
            | Inactive -> ()
        )
        trace
