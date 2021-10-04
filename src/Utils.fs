namespace Lmc.Tracing

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
