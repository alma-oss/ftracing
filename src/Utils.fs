namespace Alma.Tracing

open System

[<AutoOpen>]
module internal Utils =
    let getEnvVar name =
        try Environment.GetEnvironmentVariable name |> string
        with _ -> ""

    let getEnvVarValue name =
        match name |> getEnvVar with
        | null | "" -> Error $"{name} is not found."
        | value -> Ok value

/// see: https://opentracing.io/specification/conventions/
/// see: https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/exceptions.md
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

    let internal asTags error =
        [
            "error", "true"

            "event", "error"
            "error.object", sprintf "%A" error.Error
            "message", error.Message

            match error.Stack with
            | Some stack -> "stack", stack
            | _ -> ()

            match error.Kind with
            | Some kind -> "error.kind", kind
            | _ -> ()
        ]
