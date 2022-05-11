open Expecto

[<EntryPoint>]
let main argv =
    [
        "TRACING_SERVICE_NAME", "tracing-test"
        "TRACING_THRIFT_HOST", "127.0.0.1"
    ]
    |> List.iter System.Environment.SetEnvironmentVariable

    Tests.runTestsInAssembly defaultConfig argv
