module WASMCodegen

open AST
open Type
open Typechecker
open Wat.WFG
open System.Text

let errorExitCode = 42
let successExitCode = 0

// adress and size
type Var =
    | GloVar of int * int
    | LocVar of int * int

/// Storage information for variables.
[<RequireQualifiedAccess; StructuralComparison; StructuralEquality>]
type internal Storage =
    /// label of local or global variable
    | Label of label: string
    /// index of local variable
    | Offset of offset: int // idex
    /// address in linear memory
    | Memory of offset: int // idex
    /// function reference in table
    | Tabel of label: string * int
    /// function reference in table
    | FuncRef of label: string * int
    | Id of id: int
    | Stack of label: string


/// A memory allocator that allocates memory in pages of 64KB.
/// The allocator keeps track of the current allocation position.
type internal StaticMemoryAllocator() =

    let mutable allocationPosition = 0
    let pageSize = 64 * 1024 // 64KB

    // list of allocated memory
    let mutable allocatedMemory: List<int * int> = []

    // get head of allocated memory list
    member this.GetAllocated() = (allocatedMemory.Head)

    // 4 bytes stride
    member this.stride: int = 4

    // get number of pages needed to allocate size bytes
    member this.GetNumPages() =
        let numPages = allocationPosition / pageSize

        if allocationPosition % pageSize <> 0 then
            numPages + 1
        else
            numPages

    /// function that call the dynamic allocator with the number of bytes needed and return the start position
    /// Allocate in linear memory a block of 'size' bytes.
    /// will return the start position and size of the allocated memory
    member this.Allocate(size: int) =
        if size <= 0 then
            failwith "Size must be positive"

        let startPosition = allocationPosition

        // added to allocated memory
        allocatedMemory <- (startPosition, size) :: allocatedMemory

        allocationPosition <- allocationPosition + size
        startPosition

    member this.GetAllocationPosition() = allocationPosition

// function that repeat code pattern and accumulate it
let rec repeat (n: int) (f: int -> List<Commented<Instr>>) =
    match n with
    | 0 -> []
    | _ -> f n @ repeat (n - 1) f


/// function that generates code for calling "env" "malloc" function with the size of in bytes
/// it assumes that the size is on the stack
/// it will return the start position or -1 if there is no more memory
/// TODO: use or remove!?
let allocate =
    let m =
        Module()
            .AddImport("env", "malloc", FunctionType("malloc", Some([ (None, I32) ], [ I32 ])))

    let instr = [ (Call "malloc", "call malloc function") ]
    m.AddCode(instr)

type internal CodegenEnv =
    { funcIndexMap: Map<string, List<Instr>>
      currFunc: string
      // // name, type, allocated address
      varEnv: Map<string, Var * ValueType>
      memoryAllocator: StaticMemoryAllocator
      VarStorage: Map<string, Storage> } // function refances in table


// reduce expression to a single value
// then find the vlaue that is returned
// TODO: this is not correct
let rec findReturnType (expr: TypedAST) : ValueType list =
    match expr.Expr with
    | UnitVal -> []
    | IntVal _ -> [ I32 ]
    | FloatVal _ -> [ F32 ]
    | StringVal _ -> [ I32 ]
    | BoolVal _ -> [ I32 ]
    | Var v -> [ I32 ]
    | PreIncr e -> findReturnType e
    | PostIncr e -> findReturnType e
    | PreDcr e -> findReturnType e
    | PostDcr e -> findReturnType e
    | MinAsg(lhs, rhs)
    | DivAsg(lhs, rhs)
    | MulAsg(lhs, rhs)
    | RemAsg(lhs, rhs)
    | AddAsg(lhs, rhs) -> findReturnType lhs
    | Max(e1, e2)
    | Min(e1, e2) -> findReturnType e1
    | Sqrt e -> findReturnType e
    | Add(lhs, rhs)
    | Sub(lhs, rhs)
    | Rem(lhs, rhs)
    | Div(lhs, rhs)
    | Mult(lhs, rhs) -> findReturnType lhs
    | And(e1, e2)
    | Or(e1, e2)
    | Xor(e1, e2)
    | Less(e1, e2)
    | LessOrEq(e1, e2)
    | Greater(e1, e2)
    | GreaterOrEq(e1, e2)
    | Eq(e1, e2) -> findReturnType e1
    | Not e -> findReturnType e
    | ReadInt -> [ I32 ]
    | ReadFloat -> [ F32 ]
    | PrintLn e -> []
    | AST.If(condition, ifTrue, ifFalse) -> findReturnType ifTrue
    | Assertion e -> findReturnType e
    | Application(f, args) -> findReturnType f
    | Lambda(args, body) -> findReturnType body
    | Seq(nodes) ->
        // fold over all nodes and find the return type of the last node
        let rec findReturnType' (typesAcc: ValueType list) (nodes: TypedAST list) : ValueType list =
            match nodes with
            | [] -> typesAcc
            | x :: xs -> findReturnType x @ (findReturnType' typesAcc xs)

        findReturnType' [] nodes
    | While(cond, body) -> findReturnType body
    | DoWhile(cond, body) -> findReturnType body
    | For(init, cond, update, body) -> findReturnType body
    | Array(length, data) -> findReturnType length
    | ArrayLength(target) -> [ I32 ]
    | Struct(fields) -> [ I32 ]
    | FieldSelect(target, field) -> findReturnType target
    | ShortAnd(lhs, rhs) -> [ I32 ]
    | ShortOr(lhs, rhs) -> [ I32 ]
    | CSIncr(arg) -> [ I32 ]
    | CSDcr(arg) -> [ I32 ]
    | Print(arg) -> []
    | Ascription(tpe, node) -> []
    | Let(name, tpe, init, scope) -> []
    | LetMut(name, tpe, init, scope) -> []
    | LetRec(name, tpe, init, scope) -> []
    | Assign(target, expr) -> []
    | AST.Type(name, def, scope) -> []
    | ArrayElement(target, index) -> findReturnType target
    | ArraySlice(target, start, ending) -> findReturnType target
    | Pointer(addr) -> [ I32 ]
    | UnionCons(label, expr) -> [ I32 ]
    | Match(expr, cases) -> findReturnType expr


/// look up variable in var env
let internal lookupLabel (env: CodegenEnv) (e: TypedAST) =
    match e.Expr with
    | Var v ->
        match env.VarStorage.TryFind v with
        | Some(Storage.Label(l)) -> Named(l)
        | Some(Storage.Offset(o)) -> Index(o)
        | Some(Storage.Memory(o)) -> Address(o)
        | _ -> failwith "not implemented"
    | _ -> failwith "not implemented"

let rec internal doCodegen (env: CodegenEnv) (node: TypedAST) (m: Module) : Module =
    match node.Expr with
    | UnitVal -> m
    | IntVal i -> m.AddCode([ (I32Const i, (sprintf "push %i on stack" (i))) ])
    | BoolVal b -> m.AddCode([ I32Const(if b then 1 else 0) ])
    | FloatVal f -> m.AddCode([ F32Const f ])
    | StringVal s ->
        // allocate for struct like structure
        let ptr = env.memoryAllocator.Allocate(2 * 4)

        let stringSizeInBytes = Encoding.BigEndianUnicode.GetByteCount(s)

        // allocate string in memory
        let daraPtr = env.memoryAllocator.Allocate(stringSizeInBytes)

        // store data pointer and length in struct
        // leave pointer to string on stack
        m
            .AddMemory("memory", Unbounded(env.memoryAllocator.GetNumPages()))
            .AddData(I32Const(daraPtr), s) // store string in memory
            .AddCode(
                [ (I32Const ptr, "offset in memory")
                  (I32Const daraPtr, "data pointer to store")
                  (I32Store, "store size in bytes")
                  (I32Const(ptr + 4), "offset in memory")
                  (I32Const stringSizeInBytes, "length to store")
                  (I32Store, "store data pointer")
                  (I32Const(ptr), "leave pointer to string on stack") ]
            )
    | Var v ->
        // load variable
        // TODO
        let instrs =
            match env.VarStorage.TryFind v with
            | Some(Storage.Label(l)) -> [ LocalGet(Named(l)) ]
            | Some(Storage.Offset(o)) -> [ LocalGet(Index(o)) ]
            | Some(Storage.Memory(o)) -> [ I32Const o; I32Load ]
            | Some(Storage.Id(i)) -> [ I32Const i ]
            | Some(Storage.Stack(l)) -> [ LocalTee(Named(l)) ]
            | Some(Storage.Tabel(l, i)) -> [ LocalGet(Named(l))  ]
            | Some(Storage.FuncRef(l, i)) -> [ LocalGet(Named(l)) ]
            | _ -> failwith "not implemented"

        m.AddCode(instrs)

    | PreIncr(e) ->
        let m' = doCodegen env e m

        let label = lookupLabel env e

        let instrs =
            match e.Type with
            | t when (isSubtypeOf e.Env t TInt) -> m'.GetTempCode() @ C [ I32Const 1; I32Add; LocalTee(label) ]
            | _ -> failwith "not implemented"

        C [ Comment "Start PreIncr" ]
        ++ (m'.ResetTempCode().AddCode(instrs @ (C [ Comment "End PreIncr" ])))
    | PostIncr(e) ->
        let m' = doCodegen env e m

        let label = lookupLabel env e

        let instrs =
            match e.Type with
            | t when (isSubtypeOf e.Env t TInt) ->
                m'.GetTempCode() @ C [ LocalGet(label); I32Const 1; I32Add; LocalSet(label) ]
            | _ -> failwith "not implemented"

        C [ Comment "Start PostIncr" ]
        ++ (m'.ResetTempCode().AddCode(instrs @ (C [ Comment "End PostIncr" ])))
    | PreDcr(e) ->
        let m' = doCodegen env e m

        let label = lookupLabel env e

        let instrs =
            match e.Type with
            | t when (isSubtypeOf e.Env t TInt) -> m'.GetTempCode() @ C [ I32Const 1; I32Sub; LocalTee(label) ]
            | _ -> failwith "not implemented"

        C [ Comment "Start PreDecr" ]
        ++ (m'.ResetTempCode().AddCode(instrs @ (C [ Comment "End PreDecr" ])))
    | PostDcr(e) ->
        let m' = doCodegen env e m

        let label = lookupLabel env e

        let instrs =
            match e.Type with
            | t when (isSubtypeOf e.Env t TInt) ->
                m'.GetTempCode() @ C [ LocalGet(label); I32Const 1; I32Sub; LocalSet(label) ]
            | _ -> failwith "not implemented"

        C [ Comment "Start PostDecr" ]
        ++ (m'.ResetTempCode().AddCode(instrs @ (C [ Comment "End PostDecr" ])))

    | MinAsg(lhs, rhs)
    | DivAsg(lhs, rhs)
    | MulAsg(lhs, rhs)
    | RemAsg(lhs, rhs)
    | AddAsg(lhs, rhs) ->
        let lhs' = doCodegen env lhs m
        let rhs' = doCodegen env rhs m

        let opCode =
            match node.Type with
            | t when (isSubtypeOf node.Env t TInt) ->
                match node.Expr with
                | AddAsg(_, _) -> I32Add
                | MinAsg(_, _) -> I32Sub
                | MulAsg(_, _) -> I32Mul
                | DivAsg(_, _) -> I32DivS
                | RemAsg(_, _) -> I32RemS
                | _ -> failwith "not implemented"
            | t when (isSubtypeOf node.Env t TFloat) ->
                match node.Expr with
                | AddAsg(_, _) -> F32Add
                | MinAsg(_, _) -> F32Sub
                | MulAsg(_, _) -> F32Mul
                | DivAsg(_, _) -> F32Div
                | _ -> failwith "not implemented"

        let label = lookupLabel env lhs

        let instrs =
            match node.Type with
            | t when (isSubtypeOf node.Env t TInt) ->
                lhs'.GetTempCode() @ rhs'.GetTempCode() @ C [ opCode; LocalTee(label) ]
            | t when (isSubtypeOf node.Env t TFloat) ->
                lhs'.GetTempCode() @ rhs'.GetTempCode() @ C [ opCode; LocalTee(label) ]
            | _ -> failwith "not implemented"

        C [ Comment "Start AddAsgn/MinAsgn" ]
        ++ (lhs' + rhs')
            .ResetTempCode()
            .AddCode(instrs @ (C [ Comment "End AddAsgn/MinAsgn" ]))

    | Max(e1, e2)
    | Min(e1, e2) ->

        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        let instrs =
            match node.Type with
            | t when (isSubtypeOf node.Env t TFloat) ->
                match node.Expr with
                | Max(_, _) -> C [ F32Max ]
                | Min(_, _) -> C [ F32Min ]
                | _ -> failwith "not implemented"
            | t when (isSubtypeOf node.Env t TInt) ->
                match node.Expr with
                | Max(_, _) -> m'.GetTempCode() @ m''.GetTempCode() @ C [ I32GtS; Select ]
                | Min(_, _) -> m'.GetTempCode() @ m''.GetTempCode() @ C [ I32LtS; Select ]
                | _ -> failwith "not implemented"
            | _ -> failwith "failed type of max/min"

        C [ Comment "Max/min start" ]
        ++ (m' + m'').AddCode(instrs @ C [ Comment "Max/min end" ])

    | Sqrt e ->
        let m' = doCodegen env e m
        let instrs = m'.GetTempCode() @ C [ F32Sqrt ]
        m'.ResetTempCode().AddCode(instrs)
    | Add(lhs, rhs)
    | Sub(lhs, rhs)
    | Rem(lhs, rhs)
    | Div(lhs, rhs)
    | Mult(lhs, rhs) as expr ->
        let lhs' = doCodegen env lhs m
        let rhs' = doCodegen env rhs m

        let opCode =
            match node.Type with
            | t when (isSubtypeOf node.Env t TInt) ->
                match expr with
                | Add(_, _) -> I32Add
                | Sub(_, _) -> I32Sub
                | Rem(_, _) -> I32RemS
                | Div(_, _) -> I32DivS
                | Mult(_, _) -> I32Mul
                | _ -> failwith "not implemented"
            | t when (isSubtypeOf node.Env t TFloat) ->
                match expr with
                | Add(_, _) -> F32Add
                | Sub(_, _) -> F32Sub
                | Div(_, _) -> F32Div
                | Mult(_, _) -> F32Mul
                | _ -> failwith "not implemented"

        (lhs' + rhs').AddCode([ opCode ])
    | And(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m
        let instrs = [ I32And ]
        (m' + m'').AddCode(instrs)
    | Or(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m
        let instrs = [ I32Or ]
        (m' + m'').AddCode(instrs)
    | Not(e) ->
        let m' = doCodegen env e m
        let instrs = [ I32Eqz ]
        m'.AddCode(instrs)
    | Greater(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        // find type of e1 and e2 and check if they are equal
        let opcode =
            match e1.Type, e2.Type with
            | t1, t2 when ((isSubtypeOf e1.Env t1 TInt) & (isSubtypeOf e1.Env t2 TInt)) -> [ I32GtS ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TFloat) & (isSubtypeOf e1.Env t2 TFloat)) -> [ F32Gt ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TBool) & (isSubtypeOf e1.Env t2 TBool)) -> [ I32GtS ]
            | _ -> failwith "type mismatch"

        (m' + m'').AddCode(opcode)
    | Eq(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        // find type of e1 and e2 and check if they are equal
        let opcode =
            match e1.Type, e2.Type with
            | t1, t2 when ((isSubtypeOf e1.Env t1 TInt) & (isSubtypeOf e1.Env t2 TInt)) -> [ I32Eq ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TFloat) & (isSubtypeOf e1.Env t2 TFloat)) -> [ F32Eq ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TBool) & (isSubtypeOf e1.Env t2 TBool)) -> [ I32Eq ]
            | _ -> failwith "type mismatch"

        let instrs = m'.GetTempCode() @ m''.GetTempCode() @ C opcode
        (m + m'.ResetTempCode() + m''.ResetTempCode()).AddCode(instrs)
    | Xor(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m
        let instrs = [ I32Xor ]
        (m' + m'').AddCode(instrs)
    | Less(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        // find type of e1 and e2 and check if they are equal
        let opcode =
            match e1.Type, e2.Type with
            | t1, t2 when ((isSubtypeOf e1.Env t1 TInt) & (isSubtypeOf e1.Env t2 TInt)) -> [ I32LtS ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TFloat) & (isSubtypeOf e1.Env t2 TFloat)) -> [ F32Lt ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TBool) & (isSubtypeOf e1.Env t2 TBool)) -> [ I32LtS ]
            | _ -> failwith "type mismatch"

        (m' + m'').AddCode(opcode)

    | LessOrEq(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        // find type of e1 and e2 and check if they are equal
        let opcode =
            match e1.Type, e2.Type with
            | t1, t2 when ((isSubtypeOf e1.Env t1 TInt) & (isSubtypeOf e1.Env t2 TInt)) -> [ I32LeS ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TFloat) & (isSubtypeOf e1.Env t2 TFloat)) -> [ F32Le ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TBool) & (isSubtypeOf e1.Env t2 TBool)) -> [ I32LeS ]
            | _ -> failwith "type mismatch"

        (m' + m'').AddCode(opcode)

    | GreaterOrEq(e1, e2) ->
        let m' = doCodegen env e1 m
        let m'' = doCodegen env e2 m

        // find type of e1 and e2 and check if they are equal
        let opcode =
            match e1.Type, e2.Type with
            | t1, t2 when ((isSubtypeOf e1.Env t1 TInt) & (isSubtypeOf e1.Env t2 TInt)) -> [ I32GeS ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TFloat) & (isSubtypeOf e1.Env t2 TFloat)) -> [ F32Ge ]
            | t1, t2 when ((isSubtypeOf e1.Env t1 TBool) & (isSubtypeOf e1.Env t2 TBool)) -> [ I32GeS ]
            | _ -> failwith "type mismatch"

        (m' + m'').AddCode(opcode)

    | ReadInt ->
        // import readInt function
        let readFunctionSignature: FunctionSignature = ([], [ I32 ])

        let m' =
            m.AddImport("env", "readInt", FunctionType("readInt", Some(readFunctionSignature)))
        // perform host (system) call
        m'.AddCode([ (Call "readInt", "call host function") ])
    | ReadFloat ->
        // import readFloat function
        let readFunctionSignature: FunctionSignature = ([], [ F32 ])

        let m' =
            m.AddImport("env", "readFloat", FunctionType("readFloat", Some(readFunctionSignature)))
        // perform host (system) call
        m'.AddCode([ (Call "readFloat", "call host function") ])
    | PrintLn e
    | Print e ->
        // TODO make print and println different
        // TODO support more types
        let m' = doCodegen env e m

        // TODO not correct!!!!
        match e.Type with
        | t when (isSubtypeOf node.Env t TInt) ->
            // import writeInt function
            let writeFunctionSignature: FunctionSignature = ([ (None, I32) ], [])

            let m'' =
                m'.AddImport("env", "writeInt", FunctionType("writeInt", Some(writeFunctionSignature)))
            // perform host (system) call
            m''.AddCode([ (Call "writeInt", "call host function") ])
        | t when (isSubtypeOf node.Env t TFloat) -> failwith "not implemented"
        | t when (isSubtypeOf node.Env t TBool) -> failwith "not implemented"
        | t when (isSubtypeOf node.Env t TString) ->
            // import writeS function
            let writeFunctionSignature: FunctionSignature = ([ (None, I32); (None, I32) ], [])

            let m'' =
                m
                    .AddImport("env", "writeS", FunctionType("writeS", Some(writeFunctionSignature)))
                    .AddCode(
                        // push string pointer to stack
                        m'.GetTempCode()
                        @ [ (I32Load, "Load string pointer") ]
                        @ m'.GetTempCode()
                        @ [ (I32Const 4, "length offset")
                            (I32Add, "add offset to pointer")
                            (I32Load, "Load string length") ]
                    )

            // perform host (system) call
            (m'.ResetTempCode() ++ m'').AddCode([ (Call "writeS", "call host function") ])
        | _ -> failwith "not implemented"
    | AST.If(condition, ifTrue, ifFalse) ->
        let m' = doCodegen env condition m
        let m'' = doCodegen env ifTrue m
        let m''' = doCodegen env ifFalse m

        // get subtype of ifTrue and ifFalse
        // IFTRUE AND IF FASLE ARE always THE SAME
        let t = findReturnType ifTrue

        let instrs =
            m'.GetTempCode() @ C [ (If(t, m''.GetTempCode(), Some(m'''.GetTempCode()))) ]

        (m' + m'' + m''').ResetTempCode().AddCode(instrs)
    | Assertion(e) ->
        let m' = doCodegen env e (m.ResetTempCode())

        let instrs =
            m'.GetTempCode()
            @ C
                [ (If(
                      [],
                      [ (Nop, "do nothing - if all correct") ],
                      Some(
                          [ (I32Const errorExitCode, "error exit code push to stack")
                            (Return, "return exit code") ]
                      )
                  )) ]

        m'.ResetTempCode().AddCode(instrs)

    | Application(expr, args) ->

        let argm = List.fold (fun m arg -> m + doCodegen env arg (m.ResetTempCode())) m args

        let appTermCode =
            Module().AddCode([ Comment "Load expression to be applied as a function" ])
            ++ (doCodegen env expr m)

        match expr.Expr with
        | Var v ->
            match env.VarStorage.TryFind v with
            | Some(Storage.Label(l)) ->
                let instrs = argm.GetTempCode() @ [ (Call l, sprintf "call function %s" l) ]

                argm.ResetTempCode().AddCode(instrs)
            | Some(Storage.FuncRef(l, i)) ->
                // type to function signature
                let s = typeToFuncSiganture expr.Type

                let instrs =
                    argm.GetTempCode()
                    @ appTermCode.GetTempCode()
                    @ [ (CallIndirect__(s), sprintf "call function %s" l) ]

                argm.ResetTempCode().AddCode(instrs)
            // todo make function pointer
            | _ -> failwith "not implemented"
        | _ ->
            // type to function signature
            let s = typeToFuncSiganture expr.Type

            let instrs = [ (CallIndirect__(s), sprintf "call function") ]

            argm ++ appTermCode.AddCode(instrs)



    | Lambda(args, body) ->
        // Label to mark the position of the lambda term body
        let funLabel = Util.genSymbol "lambda"

        // capture variables in body
        let l = captureVars body

        // resolve type of captured variables
        let l' = List.map (fun v -> (v, body.Env.Vars[v])) l

        /// Names of the Lambda arguments
        let (argNames, _) = List.unzip args

        let argNamesTypes = (List.map (fun a -> (a, body.Env.Vars[a])) argNames) @ l'

        let m' = m.AddFuncRefElement(funLabel)

        // add func ref to env
        let env' =
            { env with
                VarStorage = env.VarStorage.Add(funLabel, Storage.Offset(m'.funcTableSize - 1)) }

        // intrctions that get all locals used in lamda and push them to stack
        let instrs =
            List.map
                (fun (n, t) ->
                    match t with
                    | TInt -> LocalGet(Named(n))
                    | TFloat -> LocalGet(Named(n))
                    | TBool -> LocalGet(Named(n))
                    // | TString -> (LocalGet(Named(n)), "get local var")
                    | TUnit -> failwith "not implemented")
                argNamesTypes

        let l = (C instrs) @ (C [ Call funLabel ])

        l ++ (compileFunction funLabel argNamesTypes body env' m')
    | Seq(nodes) ->
        // We collect the code of each sequence node by folding over all nodes
        List.fold (fun m node -> (m + doCodegen env node (m.ResetTempCode()))) m nodes

    | While(cond, body) ->
        let cond' = doCodegen env cond m
        let body' = doCodegen env body m

        let exitl = Util.genSymbol $"loop_exit"
        let beginl = Util.genSymbol $"loop_begin"

        let loop =
            C
                [ Loop(
                      beginl,
                      [],
                      cond'.GetTempCode()
                      @ C [ I32Eqz; BrIf exitl ]
                      @ body'.GetTempCode()
                      @ C [ Br beginl ]
                  ) ]

        let block = C [ (Block(exitl, [], loop @ C [ Nop ])) ]

        (cond'.ResetTempCode() + body'.ResetTempCode()).AddCode(block)

    | DoWhile(cond, body) ->
        (doCodegen env body m)
        ++ (doCodegen env { node with Expr = While(cond, body) } m)

    | For(init, cond, update, body) ->
        (doCodegen env init m)
        ++ (doCodegen
                env
                { node with
                    Expr =
                        While(
                            cond,
                            { node with
                                Expr = Seq([ body; update ]) }
                        ) }
                m)
    | Array(length, data) ->
        let length' = doCodegen env length m
        let data' = doCodegen env data m

        // check that length is bigger then 1 - if not return 42
        let lengthCheck =
            length'.GetTempCode()
            @ [ (I32Const 1, "put one on stack")
                (I32LeS, "check if length is <= 1")
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that length of array is bigger then 1 - if not return 42") ]

        // node with literal value 0
        // data poiner of struct is first zoro.
        let zero = { node with Expr = IntVal 0 }

        // create struct with length and data
        let structm =
            doCodegen
                env
                { node with
                    Expr = Struct([ ("data", zero); ("length", length) ]) }
                m

        // pointer to struct in local var
        let structPointerLabel = Util.genSymbol $"arr_ptr"

        let structPointer =
            structm // pointer to struct on stack
                .AddLocals([ (Some(Identifier(structPointerLabel)), I32) ]) // add local var
                .AddCode([ (LocalSet(Named(structPointerLabel)), "set struct pointer var") ]) // set struct pointer var

        let allocation = // allocate memory for array, return pointer to allocated memory
            length' // length of array on stack
                .AddImport("env", "malloc", FunctionType("malloc", Some([ (None, I32) ], [ I32 ]))) // import malloc function
                .AddCode(
                    [ (I32Const 4, "4 bytes")
                      (I32Mul, "multiply length with 4 to get size")
                      (Call "malloc", "call malloc function") ]
                )

        // set data pointer of struct
        let instr =
            [ (LocalGet(Named(structPointerLabel)), "get struct pointer var") ]
            // (I32Const 4, "offset of data field")
            // (I32Add, "add offset to base address to get data pointer field") ]
            @ allocation.GetTempCode() // get pointer to allocated memory - value to store in data pointer field
            @ [ (I32Store, "store pointer to data") ]


        // loop that runs length times and stores data in allocated memory
        let exitl = Util.genSymbol $"loop_exit"
        let beginl = Util.genSymbol $"loop_begin"
        let i = Util.genSymbol $"i"

        // body should set data in allocated memory
        // TODO: optimize loop so i just multiply index with 4 and add it to base address
        let body =
            [ (LocalGet(Named(structPointerLabel)), "get struct pointer var")
              (I32Const(8), "byte offset") // SHIFT TO POSITIONS
              (I32Add, "add offset to base address") // then data pointer + 4 (point to fist elem) is on top of stack = [length, data, fist elem, second elem, ...]

              (LocalGet(Named(i)), "get index")
              (I32Const(4), "byte offset")
              (I32Mul, "multiply index with byte offset") // then offset is on top of stack

              (I32Add, "add offset to base address") ] // then pointer to element is on top of stack
            @ data'.GetTempCode() // get value to store in allocated memory
            @ [ (I32Store, "store value in elem pos") ]

        // loop that runs length times and stores data in allocated memory
        let loop =
            C
                [ Loop(
                      beginl,
                      [],
                      length'.GetTempCode()
                      @ [ (LocalGet(Named i), "get i") ]
                      @ C [ I32Eq; BrIf exitl ]
                      @ body
                      @ [ (LocalGet(Named i), "get i")
                          (I32Const(1), "increment by 1")
                          (I32Add, "add 1 to i")
                          (LocalSet(Named i), "write to i") ]
                      @ C [ Br beginl ]
                  ) ]

        // block that contains loop and provides a way to exit the loop
        let block: Commented<Instr> list = C [ (Block(exitl, [], loop @ C [ Nop ])) ]

        let loopModule =
            data'.ResetTempCode().AddLocals([ (Some(Identifier(i)), I32) ]).AddCode(block)

        lengthCheck
        ++ structPointer.AddCode(instr)
        ++ loopModule.AddCode(
            [ (LocalGet(Named(structPointerLabel)), "leave pointer to allocated array struct on stack") ]
        )
    | ArrayLength(target) ->
        let m' = doCodegen env target m

        let instrs =
            m'.GetTempCode()
            @ [ (I32Const 4, "offset of length field")
                (I32Add, "add offset to base address")
                (I32Load, "load length") ]

        C [ Comment "start array length node" ]
        ++ m'.ResetTempCode().AddCode(instrs @ C [ Comment "end array length node" ])

    | ArrayElement(target, index) ->
        let m' = doCodegen env target m
        let m'' = doCodegen env index m

        // check that index is bigger then 0 - if not return 42
        // and that index is smaller then length - if not return 42
        let indexCheck =
            m''.GetTempCode() // index on stack
            @ [ (I32Const 0, "put zero on stack")
                (I32LtS, "check if index is >= 0")
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that index is >= 0 - if not return 42") ]
            @ m''.GetTempCode() // index on stack
            @ m'.GetTempCode() // struct pointer on stack
            @ [ (I32Const 4, "offset of length field")
                (I32Add, "add offset to base address")
                (I32Load, "load length") ]
            @ [ (I32GeU, "check if index is < length") // TODO check if this is correct
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that index is < length - if not return 42") ]


        let instrs =
            m'.GetTempCode() // struct pointer on stack
            @ [ (I32Load, "load data pointer") ]
            @ m''.GetTempCode() // index on stack
            @ [ (I32Const 4, "byte offset")
                (I32Mul, "multiply index with byte offset")
                (I32Add, "add offset to base address") ]
            @ [ (I32Load, "load value") ]

        indexCheck
        ++ (m' + m'')
            .ResetTempCode()
            .AddCode(instrs @ C [ Comment "end array element access node" ])

    // array slice creates a new struct with a pointer to the data of the original array
    | ArraySlice(target, start, ending) ->

        let length =
            { node with
                Expr = Sub(ending, start)
                Type = TInt }

        // compile target
        let targetm = doCodegen env target m
        // compile start
        let startm = doCodegen env start m

        let endingm = doCodegen env ending m

        // check of indecies are valid
        // check that start is bigger then 0 - if not return 42
        // and that start is smaller then length of the original array - if not return 42
        let startCheck =
            startm.GetTempCode() // start on stack
            @ [ (I32Const 0, "put zero on stack")
                (I32LtS, "check if start is >= 0")
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that start is >= 0 - if not return 42") ]
            @ startm.GetTempCode() // start on stack
            @ targetm.GetTempCode() // struct pointer on stack
            @ [ (I32Const 4, "offset of length field")
                (I32Add, "add offset to base address")
                (I32Load, "load length") ]
            @ [ (I32GeU, "check if start is < length") // TODO check if this is correct
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that start is < length - if not return 42") ]

        // TODO: end index check
        // check that end is bigger then 0 - if not return 42
        // and that end is smaller then length of the original array - if not return 42
        // and end is bigger then start - if not return 42
        // and the difference between end and start should be at least 1 - if not return 42

        // check that end index is smaller then length of the original array - if not return 42
        let endCheck =
            endingm.GetTempCode() // end index on stack
            @ targetm.GetTempCode() // struct pointer on stack
            @ [ (I32Const 4, "offset of length field")
                (I32Add, "add offset to base address")
                (I32Load, "load length") ]
            @ [ (I32GtU, "check if end is < length") // TODO check if this is correct
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that end is < length - if not return 42") ]


        // difference between end and start should be at least 1
        let atleastOne =
            endingm.GetTempCode() // end on stack
            @ startm.GetTempCode() // start on stack
            @ [ (I32Sub, "subtract end from start") ]
            @ [ (I32Const 1, "put one on stack")
                (I32LtU, "check if difference is < 1")
                (If(
                    [],
                    [ (I32Const errorExitCode, "error exit code push to stack")
                      (Return, "return exit code") ],
                    None
                 ),
                 "check that difference is <= 1 - if not return 42") ]

        // create struct with length and data
        let structm =
            doCodegen
                env
                { node with
                    Expr = Struct([ ("data", { node with Expr = IntVal 0 }); ("length", length) ]) }
                m

        let structPointerLabel = Util.genSymbol $"arr_slice_ptr"

        let structm' =
            structm
                .AddLocals([ (Some(Identifier(structPointerLabel)), I32) ]) // add local var
                .AddCode([ (LocalSet(Named(structPointerLabel)), "set struct pointer var") ]) // set struct pointer var

        // set data pointer of struct
        let instr =
            // here to store pointer to allocated memory
            [ (LocalGet(Named(structPointerLabel)), "get struct pointer var") ]

            // value to store in data pointer field
            @ targetm.GetTempCode() // pointer to exsisiting array struct on stack
            @ [ (I32Load, "Load data pointer from array struct") ]
            @ startm.GetTempCode() // index of start
            @ [ (I32Const 4, "offset of data field")
                (I32Mul, "multiply index with byte offset")
                (I32Add, "add offset to base address") ] // get pointer to allocated memory - value to store in data pointer field
            @ [ (I32Store, "store pointer to data") ]

        (C [ Comment "start array slice" ] @ atleastOne @ endCheck @ startCheck)
        ++ structm'
        ++ (targetm.ResetTempCode() + endingm.ResetTempCode())
            // .AddCode([ (LocalSet(Named(structPointerLabel)), "set struct pointer var") ])
            .AddCode(instr)
            .AddCode(
                [ (LocalGet(Named(structPointerLabel)), "leave pointer to allocated array struct on stack")
                  (Comment "end array slice", "") ]
            )
    | UnionCons(label, expr) ->
        // compute label id
        let id = Util.genSymbolId label
        // create node for label
        let idNode = { node with Expr = IntVal id }

        // in case there is no data aka a unit - we need to add a zero
        let data =
            match expr.Type with
            | TUnit -> { node with Expr = IntVal 0 } // unitvalue
            | _ -> expr

        // rewrite as struct
        let structNode =
            { node with
                Expr = Struct([ ("id", idNode); ("data", data) ]) }

        // codegen structNode
        C [ Comment "Start of union contructor" ]
        ++ (doCodegen env structNode m).AddCode([ (Comment "End of union contructor") ])
    | Match(target, cases) ->
        let targetm = doCodegen env target m

        let (labels, vars, exprs) = List.unzip3 cases
        let indexedLabels = List.indexed labels

        // TODO: is this correct?
        let reusltType = findReturnType (exprs[0])

        // let matchResult = Util.genSymbol $"match_result"
        let matchEndLabel = Util.genSymbol $"match_end"
        // fold over indexedLabels to generate code for each case
        let folder =
            fun (acc: Module) (i: int * string) ->
                let (index, label) = i
                let id = Util.genSymbolId label
                let expr = exprs.[index]
                let var = vars.[index]

                let varName = Util.genSymbol $"match_var_{var}"

                // map case var to label address
                let scopeVarStorage = env.VarStorage.Add(var, Storage.Label(varName))

                /// Environment for compiling the 'case' scope
                let scopeEnv =
                    { env with
                        VarStorage = scopeVarStorage }

                let scope =
                    (doCodegen scopeEnv expr m).AddLocals([ (Some(Identifier(varName)), I32) ])

                // address to data
                let dataPointer =
                    targetm.GetTempCode()
                    @ [ (I32Const 4, "offset of data field")
                        (I32Add, "add offset to base address")
                        (I32Load, "load data pointer")
                        (LocalSet(Named(varName)), "set local var") ]

                let caseCode =
                    dataPointer
                    ++ scope
                        .AddLocals([ (Some(Identifier(var)), I32) ])
                        .AddCode([ (Br matchEndLabel, "break out of match") ])

                let condition =
                    targetm.GetTempCode()
                    @ [ (I32Load, "load label")
                        (I32Const id, $"put label id {id} on stack")
                        (I32Eq, "check if index is equal to target") ]

                // if case is not the last case
                let case = condition @ C [ (If([], caseCode.GetTempCode(), None)) ]

                acc
                ++ (caseCode.ResetTempCode())
                    .AddCode(C [ Comment $"case for id: ${id}, label: {label}" ] @ case)

        let casesCode = List.fold folder (Module()) indexedLabels

        let defaultCase =
            [ (Comment "no case was matched, therefore return exit error code", "")
              (I32Const errorExitCode, "error exit code push to stack")
              (Return, "return exit code") ]

        // block that contains all cases
        let block =
            C [ (Block(matchEndLabel, reusltType, casesCode.GetTempCode() @ defaultCase)) ]

        (casesCode.ResetTempCode()).AddCode(block)
    | Assign(name, value) ->
        let value' = doCodegen env value m

        match name.Expr with
        | Var(name) ->

            let varLabel =
                match env.VarStorage.TryFind name with
                | Some(Storage.Label(l)) -> Named(l)
                | _ -> failwith "not implemented"

            // is nested? - is multiple assignment
            let isNested =
                match value.Expr with
                | Assign(v, _) ->
                    let nestedName =
                        match v.Expr with
                        | Var(n) ->
                            match env.VarStorage.TryFind n with
                            | Some(Storage.Label(l)) -> Named(l)
                            | _ -> failwith "not implemented"
                        | _ -> failwith "not implemented"

                    [ (LocalGet nestedName, "get local var") ]
                | _ -> []

            let instrs =
                value'.GetTempCode() @ isNested @ [ (LocalSet varLabel, "set local var") ]

            value'.ResetTempCode().AddCode(instrs)
        | FieldSelect(target, field) ->

            let selTargetCode = doCodegen env target m

            /// Code for the 'rhs' expression of the assignment
            let rhsCode = doCodegen env value m

            match (expandType target.Env target.Type) with
            | TStruct(fields) ->
                /// Names of the struct fields
                let (fieldNames, _) = List.unzip fields
                /// Offset of the selected struct field from the beginning of
                /// the struct
                let offset = List.findIndex (fun f -> f = field) fieldNames

                // is literal
                let isLiteral =
                    match value.Expr with
                    | IntVal i -> true
                    | FloatVal f -> true
                    | BoolVal b -> true
                    | _ -> false

                /// Assembly code that performs the field value assignment
                let assignCode =
                    match name.Type with
                    | t when (isSubtypeOf value.Env t TUnit) -> [] // Nothing to do
                    | t when (isSubtypeOf value.Env t TFloat) ->
                        let instrs =
                            selTargetCode.GetTempCode()
                            @ [ (I32Const(offset * 4), "offset of field"); (I32Add, "add offset") ]
                            @ rhsCode.GetTempCode()
                            @ [ (F32Store, "store float in struct") ]
                            @ rhsCode.GetTempCode()

                        instrs
                    | x ->
                        let instrs =
                            selTargetCode.GetTempCode()
                            @ [ (I32Const(offset * 4), "offset of field"); (I32Add, "add offset") ]
                            @ rhsCode.GetTempCode()
                            @ [ (I32Store, "store int in struct") ]
                            @ rhsCode.GetTempCode() // TODO possible bug (leave a constant on the stack after execution)

                        instrs
                // Put everything together
                (assignCode) ++ (rhsCode.ResetTempCode() + selTargetCode.ResetTempCode())
        | ArrayElement(target, index) ->
            let selTargetCode = doCodegen env target m
            let indexCode = doCodegen env index m

            let rhsCode = doCodegen env value m

            // Check index >= 0 and index < length
            let indexCheck =
                indexCode.GetTempCode() // index on stack
                @ [ (I32Const 0, "put zero on stack")
                    (I32LtS, "check if index is >= 0")
                    (If(
                        [],
                        [ (I32Const errorExitCode, "error exit code push to stack")
                          (Return, "return exit code") ],
                        None
                     ),
                     "check that index is >= 0 - if not return 42") ]
                @ indexCode.GetTempCode() // index on stack
                @ selTargetCode.GetTempCode() // struct pointer on stack
                @ [ (I32Const 4, "offset of length field")
                    (I32Add, "add offset to base address")
                    (I32Load, "load length") ]
                @ [ (I32GeU, "check if index is < length") // TODO check if this is correct
                    (If(
                        [],
                        [ (I32Const errorExitCode, "error exit code push to stack")
                          (Return, "return exit code") ],
                        None
                     ),
                     "check that index is < length - if not return 42") ]

            let instrs =
                selTargetCode.GetTempCode() // struct pointer on stack
                @ [ (I32Load, "load data pointer") ]
                @ indexCode.GetTempCode() // index on stack
                @ [ (I32Const 4, "byte offset")
                    (I32Mul, "multiply index with byte offset")
                    (I32Add, "add offset to base address") ]
                @ rhsCode.GetTempCode()
                @ [ (I32Store, "store value in elem pos") ]

            (rhsCode.ResetTempCode()
             + indexCode.ResetTempCode()
             + selTargetCode.ResetTempCode())
                .AddCode(indexCheck @ instrs)
        | _ -> failwith "not implemented"
    | Ascription(_, node) ->
        // A type ascription does not produce code --- but the type-annotated
        // AST node does
        doCodegen env node m

    | Let(name,
          _,
          { Node.Expr = Lambda(args, body)
            Node.Type = TFun(targs, _) },
          scope) ->
        /// Assembly label to mark the position of the compiled function body.
        /// For readability, we make the label similar to the function name
        let funLabel = Util.genSymbol $"fun_%s{name}"

        /// Names of the lambda term arguments
        let (argNames, _) = List.unzip args
        /// List of pairs associating each function argument to its type
        let argNamesTypes = List.zip argNames targs
        /// Compiled function body
        let bodyCode: Module = compileFunction funLabel argNamesTypes body env m

        /// Storage info where the name of the compiled function points to the
        /// label 'funLabel'
        let varStorage2 = env.VarStorage.Add(name, Storage.Label(funLabel))

        let scopeModule: Module = (doCodegen { env with VarStorage = varStorage2 } scope m)

        scopeModule + bodyCode

    | Let(name, _, init, scope) ->
        let m' = doCodegen env init m

        let varName = Util.genSymbol $"var_%s{name}"

        let env' =
            { env with
                VarStorage = env.VarStorage.Add(name, Storage.Label(varName)) }

        match init.Type with
        | t when (isSubtypeOf init.Env t TUnit) -> m' ++ (doCodegen env scope m)
        | t when (isSubtypeOf init.Env t TInt) ->
            let varLabel = Named(varName)
            let initCode = m'.GetTempCode()

            let instrs =
                initCode // inizilize code
                @ [ (LocalSet varLabel, "set local var") ] // set local var

            let scopeCode = (doCodegen env' scope (m.ResetTempCode()))

            let combi = (instrs ++ scopeCode)

            C [ Comment "Start of let" ]
            ++ m'.ResetTempCode()
            ++ combi
                .AddLocals([ (Some(Identifier(varName)), I32) ])
                .AddCode([ Comment "End of let" ])
        | t when (isSubtypeOf init.Env t TFloat) ->
            let varLabel = Named(varName)
            let initCode = m'.GetTempCode()

            let instrs =
                initCode // inizilize code
                @ [ (LocalSet varLabel, "set local var") ] // set local var

            let scopeCode = (doCodegen env' scope (m.ResetTempCode()))

            let combi = (instrs ++ scopeCode)

            C [ Comment "Start of let" ]
            ++ m'.ResetTempCode()
            ++ combi
                .AddLocals([ (Some(Identifier(varName)), F32) ])
                .AddCode([ Comment "End of let" ])
        | TFun(_, _) ->
            // todo make function pointer
            let varLabel = Named(varName)

            // get function label
            let funcLabel =
                match init.Expr with
                | Var(v) ->
                    match env.VarStorage.TryFind v with
                    | Some(Storage.Label(l)) -> l
                    | _ -> failwith "not implemented"
                | _ -> failwith "not implemented"

            // add var to func ref
            let env' =
                { env' with
                    VarStorage = env.VarStorage.Add(name, Storage.FuncRef(funcLabel, 0)) }

            let initCode = m'.GetTempCode()

            let instrs =
                initCode // inizilize code
                @ [ (LocalSet varLabel, "set local var") ] // set local var

            let scopeCode = (doCodegen env' scope (m.ResetTempCode()))

            let combi = (instrs ++ scopeCode)

            C [ Comment "Start of let" ]
            ++ m'.ResetTempCode()
            ++ combi
                .AddLocals([ (Some(Identifier(varName)), I32) ])
                .AddCode([ Comment "End of let" ])
        | TStruct(_) ->
            let varLabel = Named(varName)
            let initCode = m'.GetTempCode()

            let instrs =
                initCode // inizilize code
                @ [ (LocalSet varLabel, "set local var") ] // set local var

            let scopeCode = (doCodegen env' scope (m.ResetTempCode()))

            let combi = (instrs ++ scopeCode)

            C [ Comment "Start of let" ]
            ++ m'.ResetTempCode()
            ++ combi
                .AddLocals([ (Some(Identifier(varName)), I32) ])
                .AddCode([ Comment "End of let" ])
        | _ ->
            // todo make function pointer
            let varLabel = Named(varName)
            let initCode = m'.GetTempCode()

            let instrs =
                initCode // inizilize code
                @ [ (LocalSet varLabel, "set local var") ] // set local var

            let scopeCode = (doCodegen env' scope (m.ResetTempCode()))

            let combi = (instrs ++ scopeCode)

            C [ Comment "Start of let" ]
            ++ m'.ResetTempCode()
            ++ combi
                .AddLocals([ (Some(Identifier(varName)), I32) ])
                .AddCode([ Comment "End of let" ])


    | LetMut(name, tpe, init, scope) ->
        // The code generation is not different from 'let...', so we recycle it
        doCodegen
            env
            { node with
                Expr = Let(name, tpe, init, scope) }
            m
    | LetRec(name,
             _,
             { Node.Expr = Lambda(args, body)
               Node.Type = TFun(targs, _) },
             scope) ->

        let funLabel = Util.genSymbol $"fun_%s{name}"

        /// Storage info where the name of the compiled function points to the
        /// label 'funLabel'
        let funcref = env.VarStorage.Add(name, Storage.Label(funLabel))

        // get the index of the function
        let funcindex = m.GetFuncTableSize

        // add function to function table
        let m = m.AddFuncRefElement(funLabel)

        // allocate memory for function pointer
        let ptr = env.memoryAllocator.Allocate(4)

        // index to hex
        let funcindexhex = Util.intToHex funcindex

        // create function pointer
        let funcPointer =
            m
                .AddData(I32Const ptr, funcindexhex)
                .AddLocals([ (Some(Identifier(funLabel)), I32) ])
                .AddCode(
                    [ (I32Const ptr, "pointer to function")
                      (I32Load, "load function pointer")
                      (LocalSet(Named(funLabel)), "set local var") ]
                )

        /// Names of the lambda term arguments
        let (argNames, _) = List.unzip args
        /// List of pairs associating each function argument to its type
        let argNamesTypes = List.zip argNames targs

        /// Compiled function body
        let bodyCode: Module =
            compileFunction funLabel argNamesTypes body { env with VarStorage = funcref } m

        let scopeModule: Module = (doCodegen { env with VarStorage = funcref } scope m)

        funcPointer + scopeModule + bodyCode

    | LetRec(name, tpe, init, scope) ->
        doCodegen
            env
            { node with
                Expr = Let(name, tpe, init, scope) }
            m
    | Pointer(_) -> failwith "BUG: pointers cannot be compiled (by design!)"

    | AST.Type(_, _, scope) ->
        // A type alias does not produce any code --- but its scope does
        doCodegen env scope m

    // data strctures

    | Struct(fields) ->
        let fieldNames = List.map (fun (n, _) -> n) fields
        let fieldTypes = List.map (fun (_, t) -> t) fields

        let structName = Util.genSymbol $"Sptr"

        // calculate size of struct (each field is 4 bytes)
        let size = List.length fields

        // allocate memory for struct in dynamic memory
        let allocate =
            m
                .AddImport("env", "malloc", FunctionType("malloc", Some([ (None, I32) ], [ I32 ])))
                .AddLocals([ (Some(Identifier(structName)), I32) ])
                .AddCode(
                    [ (I32Const size, "size of struct")
                      (I32Const 4, "4 bytes")
                      (I32Mul, "multiply length with 4 to get size")
                      (Call "malloc", "call malloc function")
                      (LocalSet(Named(structName)), "set struct pointer var") ]
                )

        // fold over fields and add them to struct with indexes
        let folder =
            fun (acc: Module) (fieldOffset: int, fieldName: string, fieldInit: TypedAST) ->

                let fieldOffsetBytes = fieldOffset * 4

                // initialize field
                let initField = doCodegen env fieldInit m

                // add comment to init field
                let initField' = C [ Comment $"init field {fieldName}" ] ++ initField

                // instr based on type of field
                // store field in memory
                let instr =
                    match fieldInit.Type with
                    | t when (isSubtypeOf fieldInit.Env t TFloat) ->
                        [ (LocalGet(Named(structName)), "get struct pointer var")
                          (I32Const fieldOffsetBytes, "push field offset to stack")
                          (I32Add, "add offset to base address") ]
                        @ initField'.GetTempCode()
                        @ [ (F32Store, "store field in memory") ]
                    | _ ->
                        [ (LocalGet(Named(structName)), "get struct pointer var")
                          (I32Const fieldOffsetBytes, "push field offset to stack")
                          (I32Add, "add offset to base address") ]
                        @ initField'.GetTempCode()
                        @ [ (I32Store, "store field in memory") ]

                // accumulate code
                acc ++ initField.ResetTempCode().AddCode(instr)


        let fieldsInitCode =
            List.fold folder m (List.zip3 [ 0 .. fieldNames.Length - 1 ] fieldNames fieldTypes)

        let combined =
            allocate
            ++ fieldsInitCode.AddCode([ (LocalGet(Named(structName)), "push struct address to stack") ])

        C [ Comment "start of struct contructor" ]
        ++ combined.AddCode(C [ Comment "end of struct contructor" ])
    | FieldSelect(target, field) ->
        let selTargetCode = doCodegen env target m

        let fieldAccessCode =
            match (expandType node.Env target.Type) with
            | TStruct(fields) ->
                let (fieldNames, fieldTypes) = List.unzip fields
                let offset = List.findIndex (fun f -> f = field) fieldNames

                match fieldTypes.[offset] with
                | t when (isSubtypeOf node.Env t TUnit) -> [] // Nothing to do
                | t when (isSubtypeOf node.Env t TFloat) ->
                    // Retrieve value of struct field
                    selTargetCode.GetTempCode()
                    @ [ //(I32Const 4, "4 bytes")
                        //(I32Mul, "multiply offset by 4")
                        (I32Const(offset * 4), "push field offset to stack")
                        (I32Add, "add offset to base address")
                        (F32Load, "load field") ]
                | _ ->
                    // Retrieve value of struct field
                    selTargetCode.GetTempCode()
                    @ [ // (I32Const 4, "4 bytes")
                        // (I32Mul, "multiply offset by 4")
                        (I32Const(offset * 4), "push field offset to stack")
                        (I32Add, "add offset to base address")
                        (I32Load, "load field") ]
            | t -> failwith $"BUG: FieldSelect codegen on invalid target type: %O{t}"

        // Put everything together: compile the target, access the field
        selTargetCode.ResetTempCode()
        ++ m.AddCode(
            C [ Comment "Start of field select" ]
            @ fieldAccessCode
            @ C [ Comment "End of field select" ]
        )
    | x -> failwith "not implemented"

/// create a function signature from a type
and typeToFuncSiganture (t: Type.Type) =
    match t with
    | TFun(args, ret) ->

        // map args to there types
        let argTypes: Local list =
            List.map
                (fun (t) ->
                    match t with
                    | TUnion _ -> (None, I32)
                    | TVar(_) -> (None, I32)
                    | TFun(_, _) -> (None, I32) // passing function as a index to function table
                    | TStruct(_) -> (None, I32)
                    | TArray(_) -> (None, I32)
                    | TInt -> (None, I32)
                    | TFloat -> (None, F32)
                    | TBool -> (None, I32)
                    | TString -> (None, I32)
                    | TUnit -> failwith "a function cannot have a unit argument")
                args

        // extract return type
        let retType =
            match ret with
            | TUnion _ -> [ I32 ]
            | TVar(_) -> [ I32 ]
            | TFun(_, _) -> [ I32 ] // passing function as a index to function table
            | TStruct(_) -> [ I32 ]
            | TArray(_) -> [ I32 ]
            | TInt -> [ I32 ]
            | TFloat -> [ F32 ]
            | TBool -> [ I32 ]
            | TString -> [ I32 ]
            | TUnit -> []

        let signature: FunctionSignature = (argTypes, retType)

        signature
    | _ -> failwith "type is not a function"

/// Compile a function with its arguments, body and return the resulting module
and internal compileFunction
    (name: string)
    (args: List<string * Type.Type>)
    (body: TypedAST)
    (env: CodegenEnv)
    (m: Module)
    : Module =

    // map args to there types
    let argTypes: Local list =
        List.map
            (fun (n, t) ->
                match t with
                | TUnion _ -> (Some(n), I32)
                | TVar(_) -> (Some(n), I32)
                | TFun(_, _) -> (Some(n), I32) // passing function as a index to function table
                | TStruct(_) -> (Some(n), I32)
                | TArray(_) -> (Some(n), I32)
                | TInt -> (Some(n), I32)
                | TFloat -> (Some(n), F32)
                | TBool -> (Some(n), I32)
                | TString -> (Some(n), I32)
                | TUnit -> failwith "a function cannot have a unit argument")
            args

    // extract return type
    let retType =
        match body.Type with
        | TUnion _ -> [ I32 ]
        | TVar(_) -> [ I32 ]
        | TFun(_, _) -> [ I32 ] // passing function as a index to function table
        | TStruct(_) -> [ I32 ]
        | TArray(_) -> [ I32 ]
        | TInt -> [ I32 ]
        | TFloat -> [ F32 ]
        | TBool -> [ I32 ]
        | TString -> [ I32 ]
        | TUnit -> []

    let signature: FunctionSignature = (argTypes, retType)

    // add each arg to var storage (all local vars)
    let env' =
        List.fold
            (fun env (n, t) ->
                match t with
                // TODO: no right!
                | TFun(args, ret) ->
                    { env with
                        VarStorage = env.VarStorage.Add(n, Storage.FuncRef(n, 0)) }
                | _ ->
                    { env with
                        VarStorage = env.VarStorage.Add(n, Storage.Label(n)) })
            env
            args

    // create function instance
    let f: Commented<FunctionInstance> =
        ({ typeIndex = 0
           locals = []
           signature = signature
           body = []
           name = Some(Identifier(name)) },
         sprintf "function %s" name)

    let m' = m.AddFunction(name, f, true)

    // compile function body
    let m'' = doCodegen { env' with currFunc = name } body m'

    // add code and locals to function
    m''
        .AddInstrs(name, m''.GetTempCode()) // add instructions to function
        .AddLocals(name, m''.GetLocals()) // set locals of function
        .ResetTempCode() // reset accumulated code
        .ResetLocals() // reset locals

// return a list of all variables in the given expression
and internal captureVars (node: TypedAST) =
    match node.Expr with
    | Var v -> [ v ]
    | Lambda(args, body) ->
        let (argNames, _) = List.unzip args
        let bodyVars = captureVars body
        List.filter (fun v -> not (List.contains v argNames)) bodyVars
    | Seq(nodes) -> List.concat (List.map captureVars nodes)
    | AST.If(cond, ifTrue, ifFalse) -> List.concat [ captureVars cond; captureVars ifTrue; captureVars ifFalse ]
    | While(cond, body) -> List.concat [ captureVars cond; captureVars body ]
    | DoWhile(cond, body) -> List.concat [ captureVars cond; captureVars body ]
    | For(init, cond, update, body) ->
        List.concat [ captureVars init; captureVars cond; captureVars update; captureVars body ]
    | Assign(name, value) -> List.concat [ captureVars name; captureVars value ]
    | Ascription(_, node) -> captureVars node
    | Let(name, _, init, scope) -> List.concat [ captureVars init; captureVars scope ]
    | LetMut(name, _, init, scope) -> List.concat [ captureVars init; captureVars scope ]
    | LetRec(name, _, init, scope) -> List.concat [ captureVars init; captureVars scope ]
    | Pointer(_) -> []
    | _ -> []


/// add special implicit main function
/// as the entry point of the program
let implicit (node: TypedAST) : Module =

    // _start function is the entry point of the program
    // _start name is a special name that is part of the WASI ABI.
    // https://github.com/WebAssembly/WASI/blob/main/legacy/application-abi.md
    let funcName = "_start"

    // signature of main function
    // the main function has no arguments and returns an 32 bit int (exit code)
    let signature = ([], [ I32 ])

    let f: Commented<FunctionInstance> =
        ({ typeIndex = 0
           locals = []
           signature = signature
           body = []
           name = Some(Identifier(funcName)) },
         "entry point of program (main function)")

    // add memory, start with 1 page of memory, can grow at runtime
    let m = Module().AddMemory(("memory", Unbounded(1)))

    let env =
        { currFunc = funcName
          funcIndexMap = Map.empty
          memoryAllocator = StaticMemoryAllocator()
          VarStorage = Map.empty
          varEnv = Map.empty }

    // add function to module and export it
    let m' =
        m.AddFunction(funcName, f).AddExport(funcName, FunctionType(funcName, None))

    // compile main function
    let m = doCodegen env node m'

    // get the heap base pointer
    // the offset is the current position of the memory allocator
    // before this offset only static data is allocated
    let staticOffset: int = env.memoryAllocator.GetAllocationPosition()

    let heapBase = "heap_base"

    m
        .AddLocals(env.currFunc, m.GetLocals()) // set locals of function
        .AddInstrs(env.currFunc, [ Comment "execution start here:" ])
        .AddInstrs(env.currFunc, m.GetTempCode()) // add code of main function
        .AddInstrs(env.currFunc, [ Comment "if execution reaches here, the program is successful" ])
        .AddInstrs(env.currFunc, [ (I32Const successExitCode, "exit code 0"); (Return, "return the exit code") ]) // return 0 if program is successful
        .AddGlobal((heapBase, (I32, Immutable), (I32Const staticOffset))) // add heap base pointer
        .AddExport(heapBase + "_ptr", GlobalType("heap_base")) // export heap base pointer
