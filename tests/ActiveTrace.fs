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

        testCase "should start second trace as a separate, when previous active was finished" <| fun _ ->
            printfn "--- should start second trace as a separate, when previous active was finished ---"
            let tee f a =
                f a
                a

            Expect.equal (Trace.Active.current()) Inactive "There should not be any trace at start"

            let firstActiveSpan =
                "first-active-span"
                |> Trace.ChildOf.continueOrStartActive (fun _ -> Inactive)
                |> tee (string >> printfn "First  %A")
            let current = Trace.Active.current()
            Expect.equal current firstActiveSpan "Active span should be same as current active span."
            Expect.notEqual firstActiveSpan Inactive "Active span should be live."

            // [MemoryLeakContextProblem] ignore following expectations since the context is now created for all new spans with generated ids, so even extract from inactive would have a random parent id
            // Expect.isNone (current.ParentId()) "There should not be any parent for the active span (by member)"
            // Expect.isNone (current |> Trace.parentId) "There should not be any parent for the active span (by helper function)"
            firstActiveSpan.Finish()

            Expect.equal (Trace.Active.current()) Inactive "There should not be any trace at start"

            let secondActiveSpan =
                "second-active-span"
                |> Trace.ChildOf.continueOrStartActive (fun _ -> Inactive)
                |> tee (string >> printfn "Second %A")
            let current = Trace.Active.current()
            Expect.equal current secondActiveSpan "Active span should be same as current active span."
            Expect.notEqual secondActiveSpan Inactive "Active span should be live."

            // [MemoryLeakContextProblem] ignore following expectations since the context is now created for all new spans with generated ids, so even extract from inactive would have a random parent id
            // Expect.isNone (current.ParentId()) "There should not be any parent for the active span (by member)"
            // Expect.isNone (current |> Trace.parentId) "There should not be any parent for the active span (by helper function)"
            current.Finish()

            Expect.notEqual (firstActiveSpan |> Trace.id) (secondActiveSpan |> Trace.id) "Trace id should be unique for both traces"

        testCase "should start second trace as a separate, when previous active was not finished" <| fun _ ->
            printfn "--- should start second trace as a separate, when previous active was not finished ---"
            let tee f a =
                f a
                a

            Expect.equal (Trace.Active.current()) Inactive "There should not be any trace at start"

            let firstActiveSpan =
                "first-active-span"
                // |> Trace.ChildOf.continueOrStartActive (fun _ -> Inactive)
                |> Trace.Active.start
                |> tee (string >> printfn "First  %A")
            let current = Trace.Active.current()
            Expect.equal current firstActiveSpan "Active span should be same as current active span."
            Expect.notEqual firstActiveSpan Inactive "Active span should be live."

            // [MemoryLeakContextProblem] ignore following expectations since the context is now created for all new spans with generated ids, so even extract from inactive would have a random parent id
            // Expect.isNone (current.ParentId()) "There should not be any parent for the active span (by member)"
            // Expect.isNone (current |> Trace.parentId) "There should not be any parent for the active span (by helper function)"

            let secondActiveSpan =
                "second-active-span"
                // |> Trace.ChildOf.continueOrStartActive (fun _ -> Inactive)
                |> Trace.Active.start
                |> tee (string >> printfn "Second %A")

            Expect.notEqual (firstActiveSpan |> Trace.id) (secondActiveSpan |> Trace.id) "Trace id should be unique for both traces"

            let current = Trace.Active.current()
            Expect.equal current secondActiveSpan "Active span should be same as current active span."
            Expect.notEqual secondActiveSpan Inactive "Active span should be live."

            // [MemoryLeakContextProblem] ignore following expectations since the context is now created for all new spans with generated ids, so even extract from inactive would have a random parent id
            // Expect.isNone (current.ParentId()) "There should not be any parent for the active span (by member)"
            // Expect.isNone (current |> Trace.parentId) "There should not be any parent for the active span (by helper function)"

            firstActiveSpan.Finish()
            secondActiveSpan.Finish()
    ]
