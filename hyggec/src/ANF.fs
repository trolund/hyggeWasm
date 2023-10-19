// hyggec - The didactic compiler for the Hygge programming language.
// Copyright (C) 2023 Technical University of Denmark
// Author: Alceste Scalas <alcsc@dtu.dk>
// Released under the MIT license (see LICENSE.md for details)

/// Transformation of a program AST into Administrative Normal Form.
module ANF

open AST

/// ANF definition: a variable name with a boolean mutability boolean flag, and
/// the AST node (expected to be in ANF) that initialises the variable.
type internal ANFDef<'E,'T>(var: string, isMutable: bool, init: Node<'E,'T>, export: bool) =
    /// Variable introduced by this ANF definition.
    member this.Var = var
    /// Is this ANF definition introducing a mutable variable?
    member this.IsMutable = isMutable
    /// Initialisation expression (in ANF) of this ANF definition.
    member this.Init = init
    member this.Export = export

    /// Construct an ANF definition with an autogenerated unique variable name.
    new(isMutable: bool, init: Node<'E,'T>, export: bool) =
        ANFDef(Util.genSymbol "$anf", isMutable, init, export)


/// List of ANF definitions.
type internal ANFDefs<'E,'T> = List<ANFDef<'E,'T>>


/// Utility function to generate a unique variable name for binding ANF
/// definitions.
let internal anfVar() = Util.genSymbol "$anf"


/// Given the AST 'node', return a new AST node where every free occurrence of
/// the variable called 'var' is substituted by variable 'var2'.
let rec substVar (node: Node<'E,'T>) (var: string) (var2: string): Node<'E,'T> =
    match node.Expr with
    | UnitVal
    | IntVal(_)
    | BoolVal(_)
    | FloatVal(_)
    | StringVal(_) -> node // The substitution has no effect

    | Pointer(_) -> node // The substitution has no effect

    | Var(vname) when vname = var -> {node with Expr = Var(var2)} // Substitution applied
    | Var(_) -> node // The substitution has no effect

    | Add(lhs, rhs) ->
        {node with Expr = Add((substVar lhs var var2), (substVar rhs var var2))}
    | Sub(lhs, rhs) ->
        {node with Expr = Sub((substVar lhs var var2), (substVar rhs var var2))}
    | Mult(lhs, rhs) ->
        {node with Expr = Mult((substVar lhs var var2), (substVar rhs var var2))}
    | Div(lhs, rhs) ->
        {node with Expr = Div((substVar lhs var var2), (substVar rhs var var2))}

    | Min(lhs, rhs) ->
        {node with Expr = Min((substVar lhs var var2), (substVar rhs var var2))}
    | Max(lhs, rhs) ->
        {node with Expr = Max((substVar lhs var var2), (substVar rhs var var2))}

    | And(lhs, rhs) ->
        {node with Expr = And((substVar lhs var var2), (substVar rhs var var2))}
    | Or(lhs, rhs) ->
        {node with Expr = Or((substVar lhs var var2), (substVar rhs var var2))}
    | Not(arg) ->
        {node with Expr = Not(substVar arg var var2)}

    | Eq(lhs, rhs) ->
        {node with Expr = Eq((substVar lhs var var2), (substVar rhs var var2))}
    | Less(lhs, rhs) ->
        {node with Expr = Less((substVar lhs var var2), (substVar rhs var var2))}

    | ReadInt
    | ReadFloat -> node // The substitution has no effect

    | Print(arg) ->
        {node with Expr = Print(substVar arg var var2)}
    | PrintLn(arg) ->
        {node with Expr = PrintLn(substVar arg var var2)}

    | If(cond, ifTrue, ifFalse) ->
        {node with Expr = If((substVar cond var var2), (substVar ifTrue var var2),
                                                   (substVar ifFalse var var2))}

    | Seq(nodes) ->
        let substNodes = List.map (fun n -> (substVar n var var2)) nodes
        {node with Expr = Seq(substNodes)}

    | Ascription(tpe, node) ->
        {node with Expr = Ascription(tpe, (substVar node var var2))}

    | Let(vname, tpe, init, scope, export) when vname = var ->
        // Do not substitute the variable in the "let" scope
        {node with Expr = Let(vname, tpe, (substVar init var var2), scope, export)}
    | Let(vname, tpe, init, scope, export) ->
        {node with Expr = Let(vname, tpe, (substVar init var var2),
                              (substVar scope var var2), export)}

    | LetMut(vname, tpe, init, scope, export) when vname = var ->
        // Do not substitute the variable in the "let mutable" scope
        {node with Expr = LetMut(vname, tpe, (substVar init var var2), scope, export)}
    | LetMut(vname, tpe, init, scope, export) ->
        {node with Expr = LetMut(vname, tpe, (substVar init var var2),
                                 (substVar scope var var2), export)}

    | Assign(target, expr) ->
        {node with Expr = Assign((substVar target var var2), (substVar expr var var2))}

    | While(cond, body) ->
        let substCond = substVar cond var var2
        let substBody = substVar body var var2
        {node with Expr = While(substCond, substBody)}

    | For(init, cond, update, body) ->
        let substInit = substVar init var var2
        let substCond = substVar cond var var2
        let substUpdate = substVar update var var2
        let substBody = substVar body var var2
        {node with Expr = For(substInit, substCond, substUpdate, substBody)}

    | Assertion(arg) ->
        {node with Expr = Assertion(substVar arg var var2)}

    | Type(tname, def, scope) ->
        {node with Expr = Type(tname, def, (substVar scope var var2))}

    | Lambda(args, body) ->
        /// Arguments of this lambda term, without their pretypes
        let (argVars, _) = List.unzip args
        if (List.contains var argVars) then node // No substitution
        else {node with Expr = Lambda(args, (substVar body var var2))}

    | Application(expr, args) ->
        let substExpr = substVar expr var var2
        let substArgs = List.map (fun n -> (substVar n var var2)) args
        {node with Expr = Application(substExpr, substArgs)}

    | Struct(fields) ->
        let (fieldNames, initNodes) = List.unzip fields
        let substInitNodes = List.map (fun e -> (substVar e var var2)) initNodes
        {node with Expr = Struct(List.zip fieldNames substInitNodes)}

    | FieldSelect(target, field) ->
        {node with Expr = FieldSelect((substVar target var var2), field)}

    | UnionCons(label, expr) ->
        {node with Expr = UnionCons(label, (substVar expr var var2))}

    | Match(expr, cases) ->
        /// Mapper function to propagate the substitution along a match case
        let substCase(lab: string, v: string, cont: Node<'E,'T>) =
            if (v = var) then (lab, v, cont) // Variable bound, no substitution
            else (lab, v, (substVar cont var var2))
        let cases2 = List.map substCase cases
        {node with Expr = Match((substVar expr var var2), cases2)}
    | x -> failwithf "substVar: unhandled case %A" node


/// Convert a given AST node (expected to contain a variable) and a list of ANF
/// definitions (with the most recent at the head of the list) into a
/// corresponding series nested of "let" binders in ANF, having the given AST
/// node in the innermost scope.
let rec internal toANF (node: Node<'E,'T>, defs: ANFDefs<'E,'T>): Node<'E,'T> =
    match defs with
    | [] -> node
    | anfDef :: rest ->
        /// Dummy pretype node used for defining 'Let' expression below
        let pretypeNode = {PretypeNode.Pos = node.Pos
                           PretypeNode.Pretype = Pretype.TId("_")}
        /// Binder for this ANF definition
        let binder = if anfDef.IsMutable then
                         LetMut(anfDef.Var, pretypeNode, anfDef.Init, node, anfDef.Export)
                     else
                         Let(anfDef.Var, pretypeNode, anfDef.Init, node, anfDef.Export)
        toANF ({node with Expr = binder}, rest)


/// Transform the given AST node into a variable, plus a list of ANF definitions
/// --- i.e. the variable names with their definitions (also in ANF) that are
/// required to compute the returned variable. NOTE: the list of definitions is
/// constructed in reverse, with the most recent addition at the front (to
/// improve performance).
let rec internal toANFDefs (node: Node<'E,'T>): Node<'E,'T> * ANFDefs<'E,'T> =
    match node.Expr with
    | UnitVal
    | BoolVal(_)
    | IntVal(_)
    | FloatVal(_)
    | StringVal(_) as expr ->
        /// Definition binding this value to a variable
        let anfDef = ANFDef(false, {node with Expr = expr}, false)
        ({node with Expr = Var(anfDef.Var)}, [anfDef])

    | Var(_) ->
        (node, []) // This AST node is already in ANF
    | Sub(lhs, rhs)
    | Add(lhs, rhs)
    | Mult(lhs, rhs)
    | Div(lhs, rhs)
    | And(lhs, rhs)
    | Or(lhs, rhs)
    | Eq(lhs, rhs)
    | Less(lhs, rhs)
    | Min(lhs, rhs)
    | Max(lhs, rhs) as expr ->
        /// Left-hand-side argument in ANF and related definitions
        let (lhsANF, lhsDefs) = toANFDefs lhs
        /// Right-hand-side argument in ANF and related definitions
        let (rhsANF, rhsDefs) = toANFDefs rhs
        /// This expression in ANF
        let anfExpr = match expr with
                      | Add(_,_) -> Add(lhsANF, rhsANF)
                      | Sub(_,_) -> Sub(lhsANF, rhsANF)
                      | Mult(_,_) -> Mult(lhsANF, rhsANF)
                      | Div(_, _) -> Div(lhsANF, rhsANF)
                      | And(_,_) -> And(lhsANF, rhsANF)
                      | Or(_,_) -> Or(lhsANF, rhsANF)
                      | Eq(_,_) -> Eq(lhsANF, rhsANF)
                      | Less(_,_) -> Less(lhsANF, rhsANF)
                      | Min(_,_) -> Min(lhsANF, rhsANF)
                      | Max(_,_) -> Max(lhsANF, rhsANF)
                      | e -> failwith $"BUG: unexpected expression: %O{e}"
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = anfExpr}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: (rhsDefs @ lhsDefs))

    | ReadInt
    | ReadFloat as expr ->
        /// Definition binding this expression to a variable
        let anfDef = ANFDef(false, {node with Expr = expr}, false)
        ({node with Expr = Var(anfDef.Var)}, [anfDef])

    | Not(arg)
    | Print(arg)
    | PrintLn(arg)
    | Assertion(arg) as expr ->
        /// Argument in ANF and related definitions
        let (argANF, argDefs) = toANFDefs arg
        /// This expression in ANF
        let anfExpr = match expr with
                      | Not(_) -> Not(argANF)
                      | Print(_) -> Print(argANF)
                      | PrintLn(_) -> PrintLn(argANF)
                      | Assertion(_)  -> Assertion(argANF)
                      | e -> failwith $"BUG: unexpected expression: %O{e}"
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = anfExpr}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: argDefs)

    | Seq(nodes) ->
        match (List.rev nodes) with
        | [] ->
            failwith $"BUG: empty AST Seq node at %O{node.Pos}"
        | last :: rest ->
            /// Last AST node in the sequence in ANF and related definitions
            let (lastANF, lastDefs) = toANFDefs last
            /// ANF definitions of the rest of the Seq nodes (in reverse order).
            /// We collect the definitions and ignore the variable bound to each
            /// node of the sequence, because such variable will be unused in
            /// the sequence.
            let restANFDefs = List.collect snd (List.map toANFDefs rest)

            (lastANF, lastDefs @ restANFDefs)


    | Ascription(tpe, arg) ->
        /// Argument in ANF and related definitions
        let (argANF, argDefs) = toANFDefs arg
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Ascription(tpe, argANF)}, false)
        ({node with Expr = Var(anfDef.Var)}, anfDef :: argDefs)

    | If(condition, ifTrue, ifFalse) ->
        /// Condition in ANF and related definitions
        let (condANF, condDefs) = toANFDefs condition
        /// True branch in ANF
        let ifTrueANF = toANF (toANFDefs ifTrue)
        /// True branch in ANF
        let ifFalseANF = toANF (toANFDefs ifFalse)
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = If(condANF, ifTrueANF, ifFalseANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: condDefs)

    | Let(name, _tpe, init, scope, export)
    | LetMut(name, _tpe, init, scope, export) as expr ->
        // ANF requires variable names to be unique, so we rewrite the scope
        /// Variable name bound by this 'let' expression, made unique
        let uniqName = Util.genSymbol name
        /// Rewritten scope with 'name' replaced by 'uniqName'
        let scope = substVar scope name uniqName
        /// Variable name (now made unique) bound by this 'let' expression
        let name = uniqName
        /// Initialization expression in ANF and related definitions
        let (initANF, initDefs) = match init.Expr with
                                  | UnitVal
                                  | BoolVal(_)
                                  | IntVal(_)
                                  | FloatVal(_)
                                  | StringVal(_)
                                  | Var(_) ->
                                      (init, []) // 'init' is already in ANF
                                  | _ ->
                                      toANFDefs init
        /// Scope expression in ANF and its related definitions
        let (scopeANF, scopeDefs)  = toANFDefs scope
        /// Is this a mutable "let"?
        let isMutable = match expr with
                        | Let(_,_,_,_,_) -> false
                        | LetMut(_,_,_,_,_) -> true
                        | e -> failwith $"BUG: unexpected expression: %O{e}"
        /// Definition binding the "let" variable to the init expression in ANF
        let letDef = ANFDef(name, isMutable, initANF, export)

        ({node with Expr = scopeANF.Expr}, scopeDefs @ letDef :: initDefs)

    | Assign(target, asgnExpr) ->
        /// Source expression of the assignment in ANF and related definitions
        let (asgnExprANF, asnExprDefs) = toANFDefs asgnExpr
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Assign(target, asgnExprANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: asnExprDefs)

    | While(cond, body) ->
        /// Condition expression in ANF and related definitions
        let condANF = toANF (toANFDefs cond)
        /// Body of the 'while' loop in ANF
        let bodyANF = toANF (toANFDefs body)
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = While(condANF, bodyANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, [anfDef])

    | For(init, cond, update, body) ->
        /// Initialization expression in ANF
        let initANF = toANF (toANFDefs init)
        /// Condition expression in ANF
        let condANF = toANF (toANFDefs cond)
        /// Update expression in ANF
        let updateANF = toANF (toANFDefs update)
        /// Body expression in ANF
        let bodyANF = toANF (toANFDefs body)

        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = For(initANF, condANF, updateANF, bodyANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, [anfDef])
    
    | Type(name, def, scope) ->
        /// Scope expression in ANF
        let scopeANF = toANF (toANFDefs scope)
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Type(name, def, scopeANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, [anfDef])

    | Lambda(args, body) ->
        // ANF requires variable names to be unique, so we rewrite the body
        /// Variable names bound by the lambda term, and their types
        let (argNames, argTypes) = List.unzip args
        /// Variable names bound by the lambda term, made unique
        let uniqArgNames = List.map Util.genSymbol argNames
        /// Rewritten lambda term body with renamed variables
        let uniqVarsBody = List.fold (fun body (v, v2) -> substVar body v v2)
                                     body (List.zip argNames uniqArgNames)
        /// Lambda term body in ANF
        let bodyANF = toANF (toANFDefs uniqVarsBody)
        /// Unique lambda term argument names and respective types
        let uniqArgs = List.zip uniqArgNames argTypes

        ({node with Expr = Lambda(uniqArgs, bodyANF)}, [])

    | Application(appExpr, args) ->
        /// Applied expression in ANF and related definitions
        let (appExprANF, appExprDefs) = toANFDefs appExpr
        /// Application arguments in ANF and related definitions
        let (argsANF, argsDefs) = List.unzip (List.map toANFDefs args)
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Application(appExprANF, argsANF)}, false)

        // Remember: the ANF definitions are ordered with the most recent first
        ({node with Expr = Var(anfDef.Var)},
         anfDef :: List.concat (List.rev argsDefs) @ appExprDefs)

    | Struct(fields) ->
        let (fieldNames, fieldNodes) = List.unzip fields
        /// Struct fields in ANF and related definitions
        let (fieldsANF, fieldsDefs) = List.unzip (List.map toANFDefs fieldNodes)
        /// Updated structure fields (names and nodes) in ANF
        let fields2 = List.zip fieldNames fieldsANF
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Struct(fields2)}, false)

        // Remember: the ANF definitions are ordered with the most recent first
        ({node with Expr = Var(anfDef.Var)},
         anfDef :: List.concat (List.rev fieldsDefs))

    | FieldSelect(target, field) ->
        /// Target expression in ANF and related definitions
        let (targetANF, targetDefs) = toANFDefs target
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = FieldSelect(targetANF, field)}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: targetDefs)

    | Pointer(_) ->
        failwith "BUG: pointers cannot be converted to ANF (by design!)"

    | UnionCons(label, init) ->
        /// Union initialization expression in ANF and related definitions
        let (initANF, initDefs) = toANFDefs init
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = UnionCons(label, initANF)}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: initDefs)

    | Match(matchExpr, cases) ->
        /// Matched expression in ANF and related definitions
        let (matchExprANF, matchExprDefs) = toANFDefs matchExpr
        let (casesLabels, casesVars, casesConts) = List.unzip3 cases
        // ANF requires unique var names, so we rewrite the match continuations
        /// Match case variables, made unique
        let uniqCasesVars = List.map Util.genSymbol casesVars
        /// Rewritten match continuations, using the unique variables
        let uniqCasesConts = List.map (fun (c, v, v2) -> substVar c v v2)
                                      (List.zip3 casesConts casesVars uniqCasesVars)
        /// Match cases continuations in ANF
        let casesContsANF = List.map (fun n -> toANF (toANFDefs n))
                                     uniqCasesConts
        /// Updated match cases with continuations in ANF
        let cases2 = List.zip3 casesLabels uniqCasesVars casesContsANF
        /// Definition binding this expression in ANF to its variable
        let anfDef = ANFDef(false, {node with Expr = Match(matchExprANF, cases2)}, false)

        ({node with Expr = Var(anfDef.Var)}, anfDef :: matchExprDefs)
        | x -> failwith (sprintf "BUG: unhandled node in ANF conversion: %A" node)

/// Apply copy propagation to the given list of ANF definitions
let rec internal applyCopyPropagation (anfDefs : ANFDefs<'E,'T>) : ANFDefs<'E,'T> = 
    match anfDefs with
    | [] -> []
    | def :: defs -> 
        match def.Init.Expr with
        | Var(vname) ->
            if (not def.IsMutable) || (def.IsMutable && not (List.contains def.Var (List.map (fun (anfDef:ANFDef<'E,'T>) -> anfDef.Var) defs)))
            then applyCopyPropagation (List.map (fun (anfDef:ANFDef<'E,'T>) -> ANFDef(anfDef.Var, anfDef.IsMutable, substVar anfDef.Init def.Var vname, anfDef.Export)) defs)
            else def :: applyCopyPropagation defs
        | _ -> def :: applyCopyPropagation defs

/// Check if a given expression is pure
let isPure (expr : Expr<'E,'T>) : bool = 
    match expr with
    | Sub(_, _)
    | Add(_, _)
    | Mult(_, _)
    | Div(_, _)
    | And(_, _)
    | Or(_, _)
    | Eq(_, _)
    | Less(_, _)
    | Min(_, _)
    | Max(_, _)
    | Not(_)
    | Assertion(_) -> true
    | _ -> false

let rec prettyPrint (expr : Expr<'E,'T>) : string =
    match expr with
    | Var(vname) -> vname
    | Add(lhs, rhs) -> "Add " + prettyPrint(lhs.Expr) + ", " + prettyPrint(rhs.Expr)
    | Mult(lhs, rhs) -> "Mult " + prettyPrint(lhs.Expr) + ", " + prettyPrint(rhs.Expr)
    | _ -> ""

let areEqual (node1 : Node<'E,'T>) (node2 : Node<'E,'T>) : bool =
    let prettyPrint1 = prettyPrint node1.Expr
    let prettyPrint2 = prettyPrint node2.Expr
    prettyPrint1 = prettyPrint2

/// Apply common subexpression elimination to the given list of ANF definitions
let rec internal applyCSE (anfDefs : ANFDefs<'E,'T>) : ANFDefs<'E,'T> = 
    match anfDefs with
    | [] -> []
    | def :: defs ->
        if isPure def.Init.Expr
        then
            let mutable defsCopy = defs
            let mutable ok = 1
            while (ok <> 0) do
                let res = defsCopy |> List.tryFind (fun anfDef -> areEqual def.Init anfDef.Init)
                match res with
                | Some resDef ->
                    let indexTo = (defsCopy |> List.tryFindIndex (fun anfDef -> areEqual def.Init anfDef.Init)).Value
                    let defsSlice = defs[0..indexTo]
                    if (not def.IsMutable) || (def.IsMutable && not (List.contains def.Var (List.map (fun (anfDef:ANFDef<'E,'T>) -> anfDef.Var) defsSlice)))
                    then
                        defsCopy <- defsCopy |> List.removeAt indexTo
                        defsCopy <- defsCopy |> List.insertAt indexTo (ANFDef(resDef.Var, resDef.IsMutable, {resDef.Init with Expr = Var(def.Var)}, resDef.Export))
                | None ->
                    ok <- 0
            def :: applyCSE defsCopy
        else def :: applyCSE defs

/// Transform the given AST node into an optimized Administrative Normal Form.
let transformOpt (ast: Node<'E,'T>): Node<'E,'T> =
    let anfDefs = toANFDefs ast
    let anfDefsOpt = List.rev (applyCSE (applyCopyPropagation (List.rev (snd anfDefs))))
    toANF (fst anfDefs, anfDefsOpt)

/// Transform the given AST node into Administrative Normal Form.
let transform (ast: Node<'E,'T>): Node<'E,'T> =
    toANF (toANFDefs ast)