namespace Alma.Tracing.Example

[<RequireQualifiedAccess>]
module ExampleSettings =
    [<RequireQualifiedAccess>]
    type Run =
        | OpenTelemetry
        | KafkaExample
        | AsyncResultExample
        | All

    let run = Run.OpenTelemetry

    /// 5 is maximum
    let numberOfMessages = 2

    /// whether to use StartActive or just Start for an "application" trace
    let shouldActivateApplicationSpan = true

    /// Duration of the working simulation (waiting in this case)
    let workingBaseDuration = 500

    /// Whether to log debug the handle functions explicitely
    let debugHandleFunction = false
