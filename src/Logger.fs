namespace Lmc.Tracing

module LoggerProvider =
    open System
    open Microsoft.Extensions.Logging

    type TracingLogger(categoryName: string) =
        interface ILogger with
            member __.Log<'TState>(logLevel, eventId, state: 'TState, exn, formatter) =
                match Trace.Active.current() with
                | Inactive -> ()
                | activeTrace ->
                    let formattedMessage = formatter.Invoke(state, exn)

                    activeTrace
                    |> Trace.addEvent $"[{categoryName}][{logLevel}] {formattedMessage}"
                    |> ignore

            member __.IsEnabled(logLevel) = logLevel <> LogLevel.None && Tracer.Check.isTracerAvailable()
            member __.BeginScope<'TState>(state: 'TState) = null :> IDisposable

    type TracingProvider () =
        interface ILoggerProvider with
            member __.CreateLogger(categoryName: string): ILogger =
                TracingLogger(categoryName) :> ILogger

        interface IDisposable with
            member __.Dispose() = ()

    [<RequireQualifiedAccess>]
    module TracingProvider =
        let create (): ILoggerProvider =
            new TracingProvider() :> ILoggerProvider
