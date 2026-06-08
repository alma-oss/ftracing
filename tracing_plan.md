# Tracing plán (Istio + OpenTelemetry + Grafana Tempo)

## 1. Cíl
- Modernizovat tracing stack
- Použít:
  - OpenTelemetry (OTLP)
  - Grafana Tempo jako backend
  - OpenTelemetry Collector
  - Istio tracing (W3C Trace Context)

---

## 2. Architektura

App (F#)
  ↓
OTel SDK (OTLP)
  ↓
OpenTelemetry Collector
  ↓
Grafana Tempo
  ↓
Grafana UI

+ Istio sidecar přidává vlastní spans

---

## 3. Kroky implementace

### 3.1 Nasazení Grafana Tempo
- Použít Helm chart
- Minimal config:
  - režim: monolithic (na začátek)
  - storage: local nebo object storage

---

### 3.2 Nasazení OpenTelemetry Collector
- Deployment + ClusterIP service
- Otevřít port:
  - 4317 (gRPC)
  - 4318 (HTTP)

#### Minimal config:
```yaml
receivers:
  otlp:
    protocols:
      grpc:
      http:

exporters:
  otlp:
    endpoint: tempo:4317
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlp]
```

---

### 3.3 Napojení Istio

Upravit meshConfig:

```yaml
defaultConfig:
  tracing:
    sampling: 100.0
    zipkin:
      address: otel-collector:9411
```

NEBO (moderněji přes OTLP pokud dostupné):
- směrovat na collector

---

### 3.4 Aktualizace .NET / F# knihovny

#### Odebrat:
- Jaeger exporter

#### Přidat:
- OpenTelemetry.Exporter.OpenTelemetryProtocol

#### Setup:
```fsharp
AddOpenTelemetry()
  .WithTracing(fun b ->
    b
      .AddAspNetCoreInstrumentation()
      .AddHttpClientInstrumentation()
      .AddGrpcClientInstrumentation()
      .AddOtlpExporter()
  )
```

---

## 4. Propagace trace context

### 4.1 Standard
Používat:
- W3C Trace Context
  - traceparent
  - tracestate

---

### 4.2 Kafka

#### Aktuální stav:
- používáš B3 headers

#### Nový stav:

Ukládat do Kafka headers:
- traceparent
- tracestate

#### Inject:
```csharp
propagator.Inject(context, headers, setter);
```

#### Extract:
```csharp
propagator.Extract(default, headers, getter);
```

Stávající tracing v http knihovne
```fs
let private handleResponseTracedError (trace, error) =
        use trace = trace |> Trace.addError (TracedError.ofError HttpError.format error)

        error
        |> HttpError.statusCode
        |> Option.iter (fun statusCode ->
            trace
            |> Trace.addTags [ "http.status_code", statusCode |> HttpStatusCode.asString ]
            |> ignore
        )

        error

    let private handleResponseTracedSuccess f (trace, response: HttpResponseMessage) =
        use trace = trace |> Trace.addTags [ "http.status_code", response.StatusCode |> HttpStatusCode.asString ]

        response.Content
        |> HttpContent.asString
        |> AsyncResult.mapError (fun e ->
            trace
            |> Trace.addError (TracedError.ofExn e)
            |> ignore

            HttpError.ApiError e
        )
        |> AsyncResult.map (f response)

    let private handleResponse f (result: AsyncResult<Trace * HttpResponseMessage, Trace * HttpError>): AsyncResult<_, HttpError> =
        result
        |> AsyncResult.mapError handleResponseTracedError
        |> AsyncResult.bind (handleResponseTracedSuccess f)

    let private assertSuccessfulResponse (response: HttpResponseMessage): AsyncResult<unit, HttpError> = asyncResult {
        if response.StatusCode |> HttpStatusCode.isError then
            let! responseError =
                response
                |> ResponseError.fromResponse
                |> AsyncResult.mapError HttpError.GenericResponseError

            return! AsyncResult.ofError (HttpError.ResponseError responseError)
    }

    let private useHeaders trace (client: HttpClient) (requestBodyContent: HttpContent option) headers =
        headers
        |> Http.inject trace
        |> List.iter (fun (key, value) ->
            // Try to add to request headers first, fallback to content headers if it fails
            if not (client.DefaultRequestHeaders.TryAddWithoutValidation(key, value)) then
                match requestBodyContent with
                | None -> ()
                | Some requestBodyContent ->
                    try requestBodyContent.Headers.Remove(key) |> ignore with _ -> ()
                    requestBodyContent.Headers.TryAddWithoutValidation(key, value) |> ignore
        )

    let head headers (Url url): AsyncResult<HeadResponse, HttpError> =
        asyncResult {
            let trace =
                "[HTTP] Head response"
                |> Trace.ChildOf.continueOrStart Trace.Active.current
                |> Trace.addTags [
                    "component", (sprintf "fWebApplication (%s)" AssemblyVersionInformation.AssemblyVersion)
                    "http.method", "HEAD"
                    "span.kind", "client"
                ]

            let trace = trace |> Trace.addTags [ "http.url", url ]

            use client = new HttpClient()

            headers |> useHeaders trace client None
            let tracedError error = trace, error

            let! (response: HttpResponseMessage) =
                client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url))
                |> AsyncResult.ofTaskCatch (HttpError.ApiError >> tracedError)

            do! assertSuccessfulResponse response |> AsyncResult.mapError tracedError

            return trace, response
        }
        |> handleResponse (fun response content ->
            {
                Content = content
                StatusCode = response.StatusCode
                Headers = response.Headers |> Seq.map (fun kv -> kv.Key, (kv.Value |> String.concat ",")) |> List.ofSeq
            }
        )
```

Stávající tracing v kafka knihovne
```fs
namespace Alma.Kafka

[<RequireQualifiedAccess>]
module internal Trace =
    open Alma.Tracing
    open Alma.Tracing.Extension

    let private kafkaHeadersToList headers =
        headers
        |> List.map (fun header -> header |> Header.key |> HeaderKey.value, header |> Header.valueAsString)

    let extractFromHeaders (headers: Header list) =
        headers
        |> kafkaHeadersToList
        |> Http.extractFromHeaders

    let extractFromKafkaHeaders (headers: Confluent.Kafka.Headers) () =
        match headers with
        | null -> None
        | headers ->
            headers
            |> Seq.map Header.fromKafkaHeader
            |> List.ofSeq
            |> extractFromHeaders

    let inject trace (headers: Header list) =
        match trace with
        | Inactive -> headers
        | _ ->
            headers
            |> kafkaHeadersToList
            |> Http.inject trace
            |> List.map (fun (key, value) -> value |> Header.ofString (HeaderKey key))
```

---

### 4.3 Kompatibilita (volitelné)

Pokud máš staré služby:

```csharp
CompositeTextMapPropagator:
- TraceContextPropagator
- B3Propagator
```

---

## 5. Strategie rollout

### Fáze 1
- Nasadit Tempo + Collector
- Zapnout tracing v Istio
- Bez změn aplikace

### Fáze 2
- Aktualizovat F# služby na OTLP
- Přidat service.name

### Fáze 3
- Upravit Kafka headers (W3C)
- Volitelně zachovat B3 fallback

### Fáze 4
- (Později) PHP tracing

---

## 6. Kontrola funkčnosti

- Grafana → Explore → Traces
- hledat podle:
  - service.name
  - trace ID

Ověřit:
- end-to-end trace přes služby
- Istio spans přítomné
- Kafka propojení (ručně ověřit headers)

---

## 7. Rizika

- chybějící service.name → nečitelné traces
- mix B3/W3C → rozpad trace
- Kafka bez propagation → přerušený trace

---

## 8. Shrnutí

- Přechod z Jaeger exporter → OTLP
- B3 → W3C Trace Context
- Istio + app tracing kombinovat
- Kafka vyžaduje manuální propagaci

