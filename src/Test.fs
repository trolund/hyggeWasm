// hyggec - The didactic compiler for the Hygge programming language.
// Copyright (C) 2023 Technical University of Denmark
// Author: Alceste Scalas <alcsc@dtu.dk>
// Released under the MIT license (see LICENSE.md for details)

/// Compiler test suite.
module Test

open Expecto // See https://github.com/haf/expecto
open WasmTimeDriver


/// Collect and sort the test files in a test directory.
let internal getFilesInTestDir paths =
    let curDir = System.IO.Directory.GetCurrentDirectory()
    let dir = List.toArray(curDir :: "tests" :: paths) |> System.IO.Path.Combine
    System.IO.Directory.EnumerateFiles(dir, "*.hyg") |> Seq.toList |> List.sort


/// Format a bunch of type errors into a single string.
let internal formatErrors (es: Typechecker.TypeErrors): string =
    List.fold (fun acc e -> acc + (Util.formatMsg e) + Util.nl) "" es


let internal runRARS tast expected = 
    let asm = RISCVCodegen.codegen tast
    let explainExpected = RARS.explainExitCode expected
    let exit = RARS.launch (asm.ToString()) false
    let explainExit = RARS.explainExitCode exit
    Expect.equal exit expected ($"RARS should have exited with code %d{expected} (%s{explainExpected}), "
                                        + $"got %d{exit} (%s{explainExit})")

let internal runWasmTime tast expected = 
    // let anf = ANF.transform tast
    let asm = (WASMCodegen.implicit tast).ToString()
    let explainExpected = RARS.explainExitCode expected
    
    let vm = WasmVM()
    let exit: int = vm.RunWatString(asm.ToString()) :?> int
    Log.debug (sprintf "WasmTime exit code: %d" exit)
    let explainExit = RARS.explainExitCode exit
    Expect.equal exit expected ($"WasmTime should have exited with code %d{expected} (%s{explainExpected}), "
                                        + $"got %d{exit} (%s{explainExit})")


let internal testWasmCodegen (file: string) (expected: int) =
    match (Util.parseFile file) with
    | Error(e) -> failwith $"Parsing failed: %s{e}"
    | Ok(ast) ->
        match (Typechecker.typecheck ast) with
        | Error(es) -> failwith $"Typing failed: %s{formatErrors es}"
        | Ok(tast) ->
            runWasmTime tast expected

/// Compile a source file and run the resulting assembly code on RARS, checking
/// whether its return code matches the expected one.
let internal testCodegen (file: string) (expected: int) =
    match (Util.parseFile file) with
    | Error(e) -> failwith $"Parsing failed: %s{e}"
    | Ok(ast) ->
        match (Typechecker.typecheck ast) with
        | Error(es) -> failwith $"Typing failed: %s{formatErrors es}"
        | Ok(tast) ->
            runRARS tast expected

/// Compile a source file after transforming it in ANF, and run the resulting
/// assembly code on RARS, checking whether its return code matches the expected
/// one.
let internal testANFCodegen (file: string) (expected: int) =
    match (Util.parseFile file) with
    | Error(e) -> failwith $"Parsing failed: %s{e}"
    | Ok(ast) ->
        match (Typechecker.typecheck ast) with
        | Error(es) -> failwith $"Typing failed: %s{formatErrors es}"
        | Ok(tast) ->
            let anf = ANF.transform tast
            let asm = ANFRISCVCodegen.codegen anf 18u
            let explainExpected = RARS.explainExitCode expected
            let exit = RARS.launch (asm.ToString()) false
            let explainExit = RARS.explainExitCode exit
            Expect.equal exit expected ($"RARS should have exited with code %d{expected} (%s{explainExpected}), "
                                        + $"got %d{exit} (%s{explainExit})")


[<Tests>]
let tests = testList "tests" [
    testList "lexer" [
        testList "pass" (
            getFilesInTestDir ["lexer"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    Expect.isOk (Util.lexFile file) "Lexing failed"
            )
        )
        testList "fail" (
            getFilesInTestDir ["lexer"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    Expect.isError (Util.lexFile file) "Lexing should have failed"
            )
        )
    ]
    testList "parser" [
        testList "pass" (
            getFilesInTestDir ["parser"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    Expect.isOk (Util.parseFile file) "Parsing failed"
            )
        )
        testList "fail" (
            getFilesInTestDir ["parser"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    Expect.isError (Util.parseFile file) "Parsing should have failed"
            )
        )
    ]
    testList "typechecker" [
        testList "pass" (
            getFilesInTestDir ["typechecker"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) -> Expect.isOk (Typechecker.typecheck ast) "Typing failed"
            )
        )
        testList "fail" (
            getFilesInTestDir ["typechecker"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) -> Expect.isError (Typechecker.typecheck ast) "Typing should have failed"
            )
        )
    ]
    testList "interpreter" [
        testList "pass" (
            getFilesInTestDir ["interpreter"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) ->
                        let last = Interpreter.reduceFully ast (Some (fun _ -> "")) (Some ignore)
                        Expect.isFalse (Interpreter.isStuck last) "Interpreter reached a stuck expression"
            )
        )
        testList "fail" (
            getFilesInTestDir ["interpreter"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) ->
                        let last = Interpreter.reduceFully ast (Some (fun _ -> "")) (Some ignore)
                        Expect.isTrue (Interpreter.isStuck last)
                                      "Interpreter should have reached a stuck expression"
            )
        )
    ]
    testList "interpreter-anf" [
        testList "pass" (
            getFilesInTestDir ["interpreter-anf"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) ->
                        let anf = ANF.transform ast
                        let last = Interpreter.reduceFully anf (Some (fun _ -> "")) (Some ignore)
                        Expect.isFalse (Interpreter.isStuck last) "Interpreter reached a stuck expression"
            )
        )
        testList "fail" (
            getFilesInTestDir ["interpreter-anf"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    match (Util.parseFile file) with
                    | Error(e) -> failwith $"Parsing failed: %s{e}"
                    | Ok(ast) ->
                        let anf = ANF.transform ast
                        let last = Interpreter.reduceFully anf (Some (fun _ -> "")) (Some ignore)
                        Expect.isTrue (Interpreter.isStuck last)
                                      "Interpreter should have reached a stuck expression"
            )
        )
    ]
    testList "codegen" [
        testList "pass" (
            getFilesInTestDir ["codegen"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testCodegen file 0
            )
        )
        testList "fail" (
            getFilesInTestDir ["codegen"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testCodegen file RISCVCodegen.assertExitCode
            )
        )
    ]
    testList "codegen-anf" [
        testList "pass" (
            getFilesInTestDir ["codegen-anf"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testANFCodegen file 0
            )
        )
        testList "fail" (
            getFilesInTestDir ["codegen-anf"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testANFCodegen file RISCVCodegen.assertExitCode
            )
        )
    ]
    // Wasm tests are disabled for now
    testList "wasm" [
        testList "pass" (
            getFilesInTestDir ["codegen"; "pass"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testWasmCodegen file 0
            )
        )
        testList "fail" (
            getFilesInTestDir ["codegen"; "fail"] |> List.map ( fun file ->
                testCase (System.IO.Path.GetFileNameWithoutExtension file) <| fun _ ->
                    testWasmCodegen file RISCVCodegen.assertExitCode
            )
        )
    ]
]


/// Run the tests according to command line options
let run (opts: CmdLine.TestOptions): int =
    let argsDebug = if opts.Verbose then ["--debug"] else []
    let argsFilter = match opts.Filter with
                     | null -> []
                     | f -> ["--filter"; $"tests.%s{f}"]
    let args = argsDebug @ argsFilter
    Expecto.Tests.runTestsWithCLIArgs [] (Array.ofList args) tests

/// Run the tests according to command line options
/// [<EntryPoint>]
// let runTests (opts: CmdLine.TestOptions): int =
//     let argsDebug =  ["--debug"]
//     let argsFilter =  []
//     let args = argsDebug @ argsFilter
//     Expecto.Tests.runTestsWithCLIArgs [] (Array.ofList args) tests