// hyggec - The didactic compiler for the Hygge programming language.
// Copyright (C) 2023 Technical University of Denmark
// Author: Alceste Scalas <alcsc@dtu.dk>
// Released under the MIT license (see LICENSE.md for details)

/// Entry point of the Hygge compiler program, including the main function.
module Main

open System

/// Tokenize the given file with the given options, and print the result on the
/// terminal. Return 0 in case of success, non-zero otherwise.
let internal tokenize (opt: CmdLine.TokenizerOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.lexFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(tokens) ->
        Log.info "Lexing succeeded."
        printfn $"%A{tokens}"
        0 // Success!


/// Parse the given file with the given options, and print the result on the
/// terminal. Return 0 in case of success, non-zero otherwise.
let internal parse (opt: CmdLine.ParserOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.parseFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(ast) ->
        Log.info "Lexing and parsing succeeded."
        if (opt.ANF) then
            Log.debug $"Parsed AST:%s{Util.nl}%s{PrettyPrinter.prettyPrint ast}"
            Log.debug $"Transforming AST into ANF"
            let anf = if (opt.Optimize = 2u || opt.Optimize >= 4u)
                        then
                            Log.debug "Applying optimisation to ANF form" 
                            ANF.transformOpt ast
                        else ANF.transform ast
            printf $"%s{PrettyPrinter.prettyPrint anf}"
        else
            printf $"%s{PrettyPrinter.prettyPrint ast}"
        0 // Success!


/// Parse and type-check the given file with the given options, and print the
/// result on the terminal. Return 0 in case of success, non-zero otherwise.
let internal typecheck (opt: CmdLine.TypecheckerOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.parseFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(ast) ->
        Log.info "Lexing and parsing succeeded."
        match (Typechecker.typecheck ast) with
        | Error(typErrs) ->
            for posErr in typErrs do
                Log.error (Util.formatMsg posErr)
            1 // Non-zero exit code
        | Ok(tast) ->
            Log.info "Type checking succeeded."
            printf $"%s{PrettyPrinter.prettyPrint tast}"
            0 // Success!


/// Utility function that runs the Hygge interpreter.
let internal doInterpret (ast: AST.Node<'E,'T>) (verbose: bool): int =
    Log.info "Starting the interpreter."
    let expr = Interpreter.interpret ast verbose
    if (Interpreter.isStuck expr) then
        Log.error $"Reached stuck expression:%s{Util.nl}%s{PrettyPrinter.prettyPrint expr}"
        1 // Non-zero exit code
    else
        Log.info $"Program reduced to value:%s{Util.nl}%s{PrettyPrinter.prettyPrint expr}"
        0 // Success!


/// Run the Hygge interpreter with the given options, and return the exit code
/// (zero in case of success, non-zero in case of error).
let rec internal interpret (opt: CmdLine.InterpreterOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.parseFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(ast) ->
        Log.info "Lexing and parsing succeeded."
        if (not opt.Typecheck) then
            Log.info "Skipping type checking."
            if (opt.ANF) then
                Log.debug $"Parsed AST:%s{Util.nl}%s{PrettyPrinter.prettyPrint ast}"
                Log.debug $"Transforming AST into ANF"
                let anf = if (opt.Optimize = 2u || opt.Optimize >= 4u)
                            then 
                                Log.debug "Applying optimisation to ANF form"
                                ANF.transformOpt ast
                            else ANF.transform ast
                doInterpret anf (opt.LogLevel = Log.LogLevel.debug || opt.Verbose)
            else
                doInterpret ast (opt.LogLevel = Log.LogLevel.debug || opt.Verbose)
        else
            Log.info "Running type checker (as requested)."
            match (Typechecker.typecheck ast) with
            | Error(typErrs) ->
                for (pos, errMsg) in typErrs do
                    Log.error $"%s{opt.File}:%d{pos.LineStart}: %s{errMsg}"
                1 // Non-zero exit code
            | Ok(tast) ->
                Log.info "Type checking succeeded."
                if (opt.ANF) then
                    Log.debug $"Parsed and typed AST:%s{Util.nl}%s{PrettyPrinter.prettyPrint tast}"
                    Log.debug $"Transforming AST into ANF"
                    let anf = if (opt.Optimize = 2u || opt.Optimize >= 4u)
                                then 
                                    Log.debug "Applying optimisation to ANF form"
                                    ANF.transformOpt tast
                                else ANF.transform tast
                    doInterpret anf (opt.LogLevel = Log.LogLevel.debug || opt.Verbose)
                else
                    doInterpret tast (opt.LogLevel = Log.LogLevel.debug || opt.Verbose)


/// Run the Hygge compiler with the given options, and return the exit code
/// (zero in case of success, non-zero in case of error).
let internal compile (opt: CmdLine.CompilerOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.parseFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(ast) ->
        Log.info "Lexing and parsing succeeded."
        match (Typechecker.typecheck ast) with
        | Error(typErrs) ->
            for posErr in typErrs do
                Log.error (Util.formatMsg posErr)
            1 // Non-zero exit code
        | Ok(tast) ->
            Log.info "Type checking succeeded."
            let asm =
                if (opt.ANF) then
                    Log.debug $"Transforming AST into ANF"
                    let anf = if (opt.Optimize = 2u || opt.Optimize >= 4u)
                                then 
                                    Log.debug "Applying optimisation to ANF form"
                                    ANF.transformOpt tast
                                else ANF.transform tast
                    let registers =
                        if (opt.Registers >= 3u) && (opt.Registers <= 18u) then
                            opt.Registers
                        else if opt.Registers = 0u then
                            18u // Default
                        else
                            failwith $"The number of registers must be between 3 and 18 (got %d{opt.Registers} instead)"
                    ANFRISCVCodegen.codegen anf registers
                else
                    RISCVCodegen.codegen tast
            /// Assembly code after optimization (if enabled)
            let asm2 = if (opt.Optimize >= 3u)
                           then Peephole.optimize asm
                           else asm
            match opt.OutFile with
            | Some(f) ->
                try
                    System.IO.File.WriteAllText(f, asm2.ToString())
                    0 // Success!
                with e ->
                    Log.error $"Error writing file %s{f}: %s{e.Message}"
                    1 // Non-zero exit code
            | None ->
                printf $"%O{asm2}"
                0 // Success!


/// Compile and launch RARS with the compilation result, using the given
/// options.  Return 0 in case of success, and non-zero in case of error.
let internal launchRARS (opt: CmdLine.RARSLaunchOptions): int =
    Log.setLogLevel opt.LogLevel
    if opt.Verbose then Log.setLogLevel Log.LogLevel.debug
    Log.debug $"Parsed command line options:%s{Util.nl}%O{opt}"
    match (Util.parseFile opt.File) with
    | Error(msg) ->
        Log.error $"%s{msg}"; 1 // Non-zero exit code
    | Ok(ast) ->
        Log.info "Lexing and parsing succeeded."
        match (Typechecker.typecheck ast) with
        | Error(typErrs) ->
            for posErr in typErrs do
                Log.error (Util.formatMsg posErr)
            1
        | Ok(tast) ->
            Log.info "Type checking succeeded."
            let asm =
                if (opt.ANF) then
                    Log.debug $"Transforming AST into ANF"
                    let anf = if (opt.Optimize = 2u || opt.Optimize >= 4u)
                                then 
                                    Log.debug "Applying optimisation to ANF form"
                                    ANF.transformOpt tast
                                else ANF.transform tast
                    let registers =
                        if (opt.Registers >= 3u) && (opt.Registers <= 18u) then
                            opt.Registers
                        else if opt.Registers = 0u then
                            18u // Default
                        else
                            failwith $"The number of registers must be between 3 and 18 (got %d{opt.Registers} instead)"
                    ANFRISCVCodegen.codegen anf registers
                else
                    RISCVCodegen.codegen tast
            /// Assembly code after optimization (if enabled)
            let asm2 = if (opt.Optimize >= 3u)
                           then Peephole.optimize asm
                           else asm
            let exitCode = RARS.launch (asm2.ToString()) true
            exitCode


/// Compile and launch WasmTime with the compilation result, using the given
/// options. Return 0 in case of success, and non-zero in case of error.
let internal launchWasmTime (opt: CmdLine.WasmTimeLaunchOptions): int =
   Console.WriteLine(opt.File)
   0
    
    
/// The compiler entry point.  Must return zero on success, non-zero on error.
[<EntryPoint>]
let main (args: string[]): int =
    match (CmdLine.parse args) with
    | CmdLine.ParseResult.Error(exitCode) -> exitCode // Non-zero exit code
    | CmdLine.ParseResult.Tokenize(opts) -> tokenize opts
    | CmdLine.ParseResult.Parse(opts) -> parse opts
    | CmdLine.ParseResult.Typecheck(opts) -> typecheck opts
    | CmdLine.ParseResult.Interpret(opts) -> interpret opts
    | CmdLine.ParseResult.Compile(opts) -> compile opts
    | CmdLine.ParseResult.RARSLaunch(opts) -> launchRARS opts
    | CmdLine.ParseResult.WasmLaunch(opts) -> launchWasmTime opts
    | CmdLine.ParseResult.Test(opts) -> Test.run opts
