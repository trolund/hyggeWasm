module WGF.Utils

open WGF.Types
open System.Text

/// generate function type string
let GenFuncTypeName (t) =
    // generate function type name
    let locals = fst t
    let ret = snd t

    let l =
        List.fold
            (fun str (i, x) ->
                let (_, t) = x
                str + (if i > 0 then "_" else "") + t.ToString())
            ""
            (List.indexed locals)

    let r = List.fold (fun str x -> str + "_" + x.ToString()) "" ret

    $"{l}_=>{r}"

let C instrs =
    instrs |> List.map (fun x -> Commented(x, ""))

let I instrs : Commented<'a> list = instrs |> List.map (fun x -> fst x)


// write wasm module to file
let writeWasmFile (wasm: string) (path: string) =
        let file = System.IO.File.Create(path)
        let bytes = wasm |> Encoding.UTF8.GetBytes
        file.Write(bytes, 0, bytes.Length)
        file.Close()