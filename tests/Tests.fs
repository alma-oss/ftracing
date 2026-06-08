open Expecto

[<EntryPoint>]
let main argv =
    [
        "TRACING_SERVICE_NAME", "tracing-test"
        "TRACING_OTLP_ENDPOINT", "http://127.0.0.1:4317"
        "TRACING_SAMPLER", "always_on"
    ]
    |> List.iter System.Environment.SetEnvironmentVariable

    Tests.runTestsInAssemblyWithCLIArgs [ Sequenced ] argv
