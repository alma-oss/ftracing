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
    open System
    open Microsoft.AspNetCore.Http

    let private headersFromContext (httpContext: HttpContext): HeaderSeq =
        httpContext.Request.Headers
        |> Seq.collect (fun kv -> kv.Value |> Seq.map (fun value -> kv.Key, value))

    let extractFromHeaders headers =
        let httpHeadersCarrier = TextMapExtractAdapter(headers |> headersToDictionary) :> ITextMap

        let trace =
            match Tracer.tracer().Extract(BuiltinFormats.HttpHeaders, httpHeadersCarrier) with
            | null -> Inactive
            | context -> Context context

        trace
        |> Trace.addTags [
            "component:", (sprintf "ftracing (%s)" AssemblyVersionInformation.AssemblyVersion)
        ]

    let extractFromContext (httpContext: HttpContext) =
        httpContext
        |> headersFromContext
        |> extractFromHeaders
        |> Trace.addTags [
            "http.method", httpContext.Request.Method
            "http.status_code", string httpContext.Response.StatusCode
            "http.url", sprintf "%s://%s%s%s" httpContext.Request.Scheme httpContext.Request.Host.Value httpContext.Request.Path.Value httpContext.Request.QueryString.Value
        ]

    let inject trace headers =
        match trace |> Trace.context with
        | Some context ->
            let headersDict = headers |> headersToDictionary
            let httpHeadersCarrier = TextMapInjectAdapter(headersDict) :> ITextMap

            Tracer.tracer().Inject(context, BuiltinFormats.HttpHeaders, httpHeadersCarrier)

            headersDict
            |> Seq.map (fun kv -> kv.Key, kv.Value)
            |> Seq.toList

        | _ -> headers

    let injectActive headers =
        headers |> inject (Trace.Active.current())
