module ActiveTrace

open Expecto
open Lmc.Tracing

[<Tests>]
let checkActiveTrace =
    testList "Tracing - active trace" [
        testCase "should not be active before starting" <| fun _ ->
            let current = Trace.Active.current()

            Expect.equal current Inactive "Active trace should be inactive when no trace was started as active."

        testCase "should not be active before starting active" <| fun _ ->
            let commonSpan = Trace.Span.start "span"
            let current = Trace.Active.current()

            Expect.equal current Inactive "Active trace should be inactive when no trace was started as active."
            Expect.notEqual commonSpan Inactive "Common span should be live."

        testCase "should be active after starting active" <| fun _ ->
            let activeSpan = Trace.Active.start "active-span"
            let current = Trace.Active.current()

            Expect.equal current activeSpan "Active span should be same as current active span."
            Expect.notEqual activeSpan Inactive "Active span should be live."
    ]
