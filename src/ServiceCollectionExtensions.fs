namespace Alma.Tracing

open System
open Microsoft.Extensions.DependencyInjection

open OpenTelemetry
open OpenTelemetry.Trace
open OpenTelemetry.Resources

/// High-level integration for ASP.NET Core (Giraffe / Saturn).
/// Adds OpenTelemetry tracing to the service collection with OTLP export, HTTP instrumentation, and parent-based sampling.
[<RequireQualifiedAccess>]
module TracingConfig =
    /// Configure OpenTelemetry tracing on the given IServiceCollection.
    /// Uses the same environment variables as the Tracer module (standard OTel vars take precedence over TRACING_* vars).
    /// Returns the service collection for chaining.
    ///
    /// Usage in Giraffe/Saturn:
    ///   services |> TracingConfig.configureTracing
    let configureTracing (services: IServiceCollection): IServiceCollection =
        let serviceName =
            getEnvVarWithFallback "OTEL_SERVICE_NAME" "TRACING_SERVICE_NAME"
            |> Result.defaultValue "unknown-service"

        let endpoint =
            getEnvVarWithFallback "OTEL_EXPORTER_OTLP_ENDPOINT" "TRACING_OTLP_ENDPOINT"
            |> Result.toOption

        let attributes =
            match getEnvVarValue "TRACING_TAGS" with
            | Ok tags ->
                tags.Split ","
                |> Seq.choose (fun tag ->
                    match tag.Split "=" |> Seq.toList with
                    | [ key; value ] -> Some (key, value :> obj)
                    | _ -> None
                )
                |> Seq.map (fun (k, v) -> System.Collections.Generic.KeyValuePair(k, v))
            | _ -> Seq.empty

        let rootSampler =
            let samplerName =
                getEnvVarWithFallback "OTEL_TRACES_SAMPLER" "TRACING_SAMPLER"
                |> Result.map (fun s -> s.ToLowerInvariant())
                |> Result.defaultValue "always_on"

            match samplerName with
            | "always_off" -> AlwaysOffSampler() :> Sampler
            | "traceidratio" ->
                let ratio =
                    getEnvVarWithFallback "OTEL_TRACES_SAMPLER_ARG" "TRACING_SAMPLER_ARG"
                    |> Result.bind (fun s ->
                        match tryParseFloat s with
                        | Some v -> Ok v
                        | None -> Error $"Invalid sampler ratio: {s}"
                    )
                    |> Result.defaultValue 1.0
                TraceIdRatioBasedSampler(ratio) :> Sampler
            | _ -> AlwaysOnSampler() :> Sampler

        services
            .AddOpenTelemetry()
            .WithTracing(fun (tracing: TracerProviderBuilder) ->
                tracing
                    .AddSource(serviceName)
                    .SetResourceBuilder(
                        ResourceBuilder.CreateDefault()
                            .AddService(serviceName = serviceName)
                            .AddAttributes(attributes)
                            .AddTelemetrySdk()
                    )
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .SetSampler(ParentBasedSampler(rootSampler))
                |> fun b ->
                    match endpoint with
                    | Some ep -> b.AddOtlpExporter(fun opt -> opt.Endpoint <- Uri(ep))
                    | None -> b
                |> fun b ->
                    match getEnvVarValue "TRACING_EXPORT_CONSOLE" |> Result.map (fun s -> s.ToLowerInvariant()) with
                    | Ok "on" -> b.AddConsoleExporter()
                    | _ -> b
                |> ignore
            )
        |> ignore

        services
