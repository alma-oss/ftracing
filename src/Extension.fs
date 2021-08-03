namespace Lmc.Tracing.Extension

open OpenTracing
open OpenTracing.Propagation
open OpenTracing.Tag

open Lmc.Tracing

[<AutoOpen>]
module private Headers =
    open System.Collections.Generic

    type Headers = Dictionary<string, string>
    type IHeaders = IDictionary<string, string>

    type HeaderSeq = (string * string) seq

    let headersToDictionary (headerList: HeaderSeq): IHeaders =
        headerList
        |> Seq.fold
            (fun (headers: Headers) (key, value) ->
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
        let httpHeadersCarrier = TextMapExtractAdapter(headers |> headersToDictionary) :> ITextMap

        match Tracer.tracer().Extract(BuiltinFormats.HttpHeaders, httpHeadersCarrier) with
        | null -> None
        | context -> Some (TraceContext context)

    let extractFromContext (httpContext: HttpContext) =
        httpContext
        |> headersFromContext
        |> extractFromHeaders

    let inject trace headers =
        match trace |> Trace.context with
        | Some (TraceContext context) ->
            let headersDict = headers |> headersToDictionary
            let httpHeadersCarrier = TextMapInjectAdapter(headersDict) :> ITextMap

            Tracer.tracer().Inject(context, BuiltinFormats.HttpHeaders, httpHeadersCarrier)

            headersDict
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Seq.toList

        | _ -> headers

    let injectActive headers =
        headers |> inject (Trace.Active.current())
