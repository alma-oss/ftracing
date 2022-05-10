namespace Lmc.Tracing.Extension

open System.Collections.Generic

open OpenTelemetry
open OpenTelemetry.Context.Propagation

open Lmc.Tracing

// see: https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/context/api-propagators.md#propagators-distribution
// see: https://www.mytechramblings.com/posts/getting-started-with-opentelemetry-and-dotnet-core/

[<AutoOpen>]
module private Headers =
    type Headers = Dictionary<string, string>
    type IHeaders = IDictionary<string, string>

    type HeaderSeq = (string * string) seq

    let headersToDictionary (headerList: HeaderSeq): IHeaders =
        headerList
        |> Seq.fold
            (fun (headers: Headers) (key, value) ->
                if not <| headers.ContainsKey key then
                    headers.Add(key, value)

                headers
            )
            (Headers())
        :> IHeaders

[<RequireQualifiedAccess>]
module Http =
    open Microsoft.AspNetCore.Http

    let private headersFromContext (httpContext: HttpContext): HeaderSeq =
        httpContext.Request.Headers
        |> Seq.collect (fun kv -> kv.Value |> Seq.map (fun value -> kv.Key, value))

    let extractFromHeaders headers =
        let parentContext =
            B3Propagator().Extract(
                PropagationContext(),
                headers |> headersToDictionary,
                System.Func<IHeaders, string, IEnumerable<string>> (fun props key ->
                    match props.TryGetValue(key) with
                    | true, value -> [ value ]
                    | _ -> []
                )
            )

        Baggage.Current <- parentContext.Baggage

        let ctx = parentContext.ActivityContext

        Trace.SpanContext(&ctx)
        |> TraceContext
        |> Some

    let extractFromContext (httpContext: HttpContext) =
        httpContext
        |> headersFromContext
        |> extractFromHeaders

    let inject trace headers =
        match trace |> Trace.context with
        | Some (TraceContext context) ->
            let headersDict = headers |> headersToDictionary

            B3Propagator().Inject(
                PropagationContext(context, Baggage.Current),
                headersDict,
                System.Action<IHeaders, string, string> (fun props key value ->
                    props.Add(key, value)
                )
            )

            // fix for missing parent id propagation (caused probably by https://github.com/open-telemetry/opentelemetry-dotnet/issues/2025)
            match trace.ParentId() with
            | Some parent when headersDict.ContainsKey "X-B3-ParentSpanId" |> not ->
                headersDict.Add("X-B3-ParentSpanId", parent.ToHexString())
            | _ -> ()

            headersDict
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Seq.toList

        | _ -> headers

    let injectActive headers =
        headers |> inject (Trace.Active.current())
