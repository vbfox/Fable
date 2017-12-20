module Fable.Replacements
open Fable
open Fable.Core
open Fable.AST
open Fable.AST.Fable.Util

module Util =
    let [<Literal>] system = "System."
    let [<Literal>] fsharp = "Microsoft.FSharp."
    let [<Literal>] fableCore = "Fable.Core."
    let [<Literal>] genericCollections = "System.Collections.Generic."

    let inline (=>) first second = first, second

    let (|FloatToInt|) (x: float) = int x

    let (|StringLiteral|_|) = function
        | MaybeWrapped(Fable.Value(Fable.StringConst str)) -> Some str
        | _ -> None

    let (|Int32Literal|_|) = function
        | MaybeWrapped(Fable.Value(Fable.NumberConst(FloatToInt i, Int32))) -> Some i
        | _ -> None

    let (|UnionCons|_|) expr =
        let hasMultipleFields tag (cases: (string * Fable.Type list) list) =
            match List.tryItem tag cases with
            | Some(_,fieldTypes) -> List.isMultiple fieldTypes
            | None -> false
        match expr with
        | MaybeWrapped(Fable.Apply(_, args, Fable.ApplyCons, Fable.DeclaredType(ent, _), _)) ->
            match ent.Kind with
            | Fable.Union cases ->
                match args with
                | [Fable.Value(Fable.NumberConst(FloatToInt tag, _))] ->
                    Some(tag, [], cases)
                | [Fable.Value(Fable.NumberConst(FloatToInt tag, _));
                    Fable.Value(Fable.ArrayConst(Fable.ArrayValues fields, _))]
                    when hasMultipleFields (tag) cases ->
                    Some(tag, fields, cases)
                | [Fable.Value(Fable.NumberConst(FloatToInt tag, _)); expr] ->
                    Some(tag, [expr], cases)
                | _ -> None
            | _ -> None
        | _ -> None

    let (|Null|_|) = function
        | MaybeWrapped(Fable.Value Fable.Null) -> Some ()
        | _ -> None

    let (|Type|) (expr: Fable.Expr) = expr.Type

    let (|NumberType|_|) = function
        | Fable.Number kind -> Some kind
        | _ -> None

    let (|Number|ExtNumber|NoNumber|) = function
        | Fable.Number kind -> Number kind
        | Fable.ExtendedNumber kind -> ExtNumber kind
        | _ -> NoNumber

    let (|EntFullName|_|) (typ: Fable.Type) =
        match typ with
        | Fable.DeclaredType(ent, _) -> Some ent.FullName
        | _ -> None

    let (|IDictionary|_|) = function
        | EntFullName "System.Collections.Generic.IDictionary" -> Some IDictionary
        | _ -> None

    let (|IEnumerable|_|) = function
        | EntFullName "System.Collections.Generic.IEnumerable" -> Some IEnumerable
        | _ -> None

    let (|IEqualityComparer|_|) = function
        | EntFullName "System.Collections.Generic.IEqualityComparer" -> Some IEqualityComparer
        | _ -> None

    let (|DeclaredKind|_|) (typ: Fable.Type) =
        match typ with
        | Fable.DeclaredType(ent, _) -> Some ent.Kind
        | _ -> None

    let (|KeyValue|_|) (key: string) (value: string) (s: string) =
        if s = key then Some value else None

    let (|OneArg|_|) (callee: Fable.Expr option, args: Fable.Expr list) =
        match callee, args with None, arg::_ -> Some arg | _ -> None

    let (|TwoArgs|_|) (callee: Fable.Expr option, args: Fable.Expr list) =
        match callee, args with None, left::right::_ -> Some (left, right) | _ -> None

    let (|ThreeArgs|_|) (callee: Fable.Expr option, args: Fable.Expr list) =
        match callee, args with None, arg1::arg2::arg3::_ -> Some (arg1, arg2, arg3) | _ -> None

    let (|Integer|Float|) = function
        | Int8 | UInt8 | Int16 | UInt16 | Int32 | UInt32 -> Integer
        | Float32 | Float64 -> Float

    let (|Nameof|_|) = function
        | Fable.Value(Fable.IdentValue ident) -> Some ident.Name
        | Fable.Apply(_, [Fable.Value(Fable.StringConst prop)], Fable.ApplyGet, _, _) -> Some prop
        | Fable.Value(Fable.TypeRef(ent,_)) -> Some ent.Name
        | _ -> None

    let resolveTypeRef com (info: Fable.ApplyInfo) generic t =
        let genInfo =
            { makeGeneric = generic
            ; genericAvailability = info.genericAvailability }
        match t with
        | Fable.GenericParam _ when not info.genericAvailability ->
            "`typeof` is being called on a generic parameter, "
            + "consider inlining the method (for `internal` members) "
            + "or using `PassGenericsAttribute`."
            |> addWarning com info.fileName info.range
            makeTypeRef com genInfo t
        | t -> makeTypeRef com genInfo t

    let instanceArgs (callee: Fable.Expr option) (args: Fable.Expr list) =
        match callee with
        | Some callee -> (callee, args)
        | None -> (args.Head, args.Tail)

    let staticArgs (callee: Fable.Expr option) (args: Fable.Expr list) =
        match callee with
        | Some callee -> callee::args
        | None -> args

    let chainCall meth args t (e: Fable.Expr) =
        InstanceCall (e, meth, args)
        |> makeCall e.Range t

    let icall (i: Fable.ApplyInfo) meth =
        let c, args = instanceArgs i.callee i.args
        InstanceCall(c, meth, args)
        |> makeCall i.range i.returnType

    let ccall (i: Fable.ApplyInfo) cmod meth args =
        CoreLibCall(cmod, Some meth, false, args)
        |> makeCall i.range i.returnType

    let ccall_ r t cmod meth args =
        CoreLibCall(cmod, Some meth, false, args)
        |> makeCall r t

    let emit (i: Fable.ApplyInfo) emit args =
        makeEmit i.range i.returnType args emit

    let emitNoInfo emit args =
        makeEmit None Fable.Any args emit

    let wrap typ expr =
        Fable.Wrapped (expr, typ)

    let wrapInLambda args f =
        List.map (Fable.IdentValue >> Fable.Value) args
        |> f |> makeLambdaExpr args

    let genArg (t: Fable.Type) =
        match t.GenericArgs with
        | [genArg] -> genArg
        | _ -> Fable.Any

    let defaultof (t: Fable.Type) =
        match t with
        | Fable.Number _ -> makeIntConst 0
        | Fable.Boolean -> makeBoolConst false
        | _ -> Fable.Null |> Fable.Value

    let getProp r t callee (prop: string) =
        Fable.Apply(callee, [Fable.Value(Fable.StringConst prop)], Fable.ApplyGet, t, r)

    let newError r t args =
        Fable.Apply(makeIdentExpr "Error", args, Fable.ApplyCons, t, r)

    let toChar com (i: Fable.ApplyInfo) (sourceType: Fable.Type) (args: Fable.Expr list) =
        match sourceType with
        | Fable.Char
        | Fable.String -> args.Head
        | _ -> GlobalCall ("String", Some "fromCharCode", false, [args.Head])
               |> makeCall i.range i.returnType

    let toString com (i: Fable.ApplyInfo) (sourceType: Fable.Type) (args: Fable.Expr list) =
        match sourceType with
        | Fable.Char
        | Fable.String -> args.Head
        | Fable.Unit | Fable.Boolean
        | Fable.Array _ | Fable.Tuple _ | Fable.Function _ | Fable.Enum _ ->
            GlobalCall ("String", None, false, [args.Head])
            |> makeCall i.range i.returnType
        | Fable.Number _ | Fable.ExtendedNumber (Int64 | UInt64 | Decimal) ->
            let arg =
                match sourceType, args.Tail with
                | Fable.Number Int16, [_] -> emit i "((x,y) => x < 0 && y !== 10 ? 0xFFFF + x + 1 : x)($0,$1)" args
                | Fable.Number Int32, [_] -> emit i "((x,y) => x < 0 && y !== 10 ? 0xFFFFFFFF + x + 1 : x)($0,$1)" args
                | Fable.ExtendedNumber Int64, [_] -> emit i "((x,y) => x.isNegative() && y !== 10 ? x.toUnsigned() : x)($0,$1)" args
                | _ -> args.Head
            InstanceCall (arg, "toString", args.Tail)
            |> makeCall i.range i.returnType
        | Fable.MetaType | Fable.Any | Fable.GenericParam _
        | Fable.ExtendedNumber BigInt | Fable.DeclaredType _ | Fable.Option _ ->
            CoreLibCall ("Util", Some "toString", false, [args.Head])
            |> makeCall i.range i.returnType

    let toFloat com (i: Fable.ApplyInfo) (sourceType: Fable.Type) (args: Fable.Expr list) =
        match sourceType with
        | Fable.String ->
            CoreLibCall ("Double", Some "parse", false, args)
            |> makeCall i.range i.returnType
        | Fable.ExtendedNumber (Int64 | UInt64) ->
            InstanceCall (args.Head, "toNumber", args.Tail)
            |> makeCall i.range (Fable.Number Float64)
        | Fable.ExtendedNumber BigInt ->
            let meth = match i.returnType with
                        | Number Float32 -> "toSingle"
                        | Number Float64 -> "toDouble"
                        | ExtNumber Decimal -> "toDecimal"
                        | _ -> failwith "Unexpected conversion"
            CoreLibCall("BigInt", Some meth, false, args)
            |> makeCall i.range i.returnType
        | _ ->
            wrap i.returnType args.Head

    let toInt com (i: Fable.ApplyInfo) (sourceType: Fable.Type) (args: Fable.Expr list) =
        let kindIndex t =             //         0   1   2   3   4   5   6   7   8   9  10  11
            match t with              //         i8 i16 i32 i64  u8 u16 u32 u64 f32 f64 dec big
            | Number Int8 -> 0        //  0 i8   -   -   -   -   +   +   +   +   -   -   -   +
            | Number Int16 -> 1       //  1 i16  +   -   -   -   +   +   +   +   -   -   -   +
            | Number Int32 -> 2       //  2 i32  +   +   -   -   +   +   +   +   -   -   -   +
            | ExtNumber Int64 -> 3    //  3 i64  +   +   +   -   +   +   +   +   -   -   -   +
            | Number UInt8 -> 4       //  4 u8   +   +   +   +   -   -   -   -   -   -   -   +
            | Number UInt16 -> 5      //  5 u16  +   +   +   +   +   -   -   -   -   -   -   +
            | Number UInt32 -> 6      //  6 u32  +   +   +   +   +   +   -   -   -   -   -   +
            | ExtNumber UInt64 -> 7   //  7 u64  +   +   +   +   +   +   +   -   -   -   -   +
            | Number Float32 -> 8     //  8 f32  +   +   +   +   +   +   +   +   -   -   -   +
            | Number Float64 -> 9     //  9 f64  +   +   +   +   +   +   +   +   -   -   -   +
            | ExtNumber Decimal -> 10 // 10 dec  +   +   +   +   +   +   +   +   -   -   -   +
            | ExtNumber BigInt -> 11  // 11 big  +   +   +   +   +   +   +   +   +   +   +   -
            | NoNumber -> failwith "Unexpected non-number type"
        let needToCast typeFrom typeTo =
            let v = kindIndex typeFrom // argument type (vertical)
            let h = kindIndex typeTo   // return type (horizontal)
            ((v > h) || (v < 4 && h > 3)) && (h < 8) || (h <> v && (h = 11 || v = 11))
        let emitLong unsigned (args: Fable.Expr list) =
            match sourceType with
            | ExtNumber (Int64|UInt64) ->
                CoreLibCall("Long", Some "fromValue", false, args)
            | _ -> CoreLibCall("Long", Some "fromNumber", false, args@[makeBoolConst unsigned])
            |> makeCall i.range i.returnType
        let emitBigInt (args: Fable.Expr list) =
            match sourceType with
            | ExtNumber (Int64|UInt64) ->
                CoreLibCall("BigInt", Some "fromInt64", false, args)
            | _ -> CoreLibCall("BigInt", Some "fromInt32", false, args)
            |> makeCall i.range i.returnType
        let emitCast typeTo args =
            match typeTo with
            | ExtNumber BigInt -> emitBigInt args
            | ExtNumber UInt64 -> emitLong true args
            | ExtNumber Int64 -> emitLong false args
            | Number Int8 -> emit i "($0 + 0x80 & 0xFF) - 0x80" args
            | Number Int16 -> emit i "($0 + 0x8000 & 0xFFFF) - 0x8000" args
            | Number Int32 -> emit i "~~$0" args
            | Number UInt8 -> emit i "$0 & 0xFF" args
            | Number UInt16 -> emit i "$0 & 0xFFFF" args
            | Number UInt32 -> emit i "$0 >>> 0" args
            | Number Float32 -> emit i "$0" args
            | Number Float64 -> emit i "$0" args
            | ExtNumber Decimal -> emit i "$0" args
            | NoNumber -> failwith "Unexpected non-number type"
        let castBigIntMethod typeTo =
            match typeTo with
            | ExtNumber BigInt -> failwith "Unexpected conversion"
            | Number Int8 -> "toSByte"
            | Number Int16 -> "toInt16"
            | Number Int32 -> "toInt32"
            | ExtNumber Int64 -> "toInt64"
            | Number UInt8 -> "toByte"
            | Number UInt16 -> "toUInt16"
            | Number UInt32 -> "toUInt32"
            | ExtNumber UInt64 -> "toUInt64"
            | Number Float32 -> "toSingle"
            | Number Float64 -> "toDouble"
            | ExtNumber Decimal -> "toDecimal"
            | NoNumber -> failwith "Unexpected non-number type"
        let sourceType =
            match sourceType with
            | Fable.Enum _ -> Fable.Number Int32
            | t -> t
        match sourceType with
        | Fable.Char ->
            InstanceCall(args.Head, "charCodeAt", [makeIntConst 0])
            |> makeCall i.range i.returnType
        | Fable.String ->
            match i.returnType with
            | Fable.ExtendedNumber (Int64|UInt64 as kind) ->
                let unsigned = kind = UInt64
                let args = [args.Head]@[makeBoolConst unsigned]@args.Tail
                CoreLibCall ("Long", Some "fromString", false, args)
                |> makeCall i.range i.returnType
            | _ ->
                CoreLibCall ("Int32", Some "parse", false, args)
                |> makeCall i.range i.returnType
        | ExtNumber BigInt ->
            let meth = castBigIntMethod i.returnType
            CoreLibCall("BigInt", Some meth, false, args)
            |> makeCall i.range i.returnType
        | Number _ | ExtNumber _ as typeFrom ->
            match i.returnType with
            | Number _ | ExtNumber _ as typeTo when needToCast typeFrom typeTo ->
                match typeFrom, typeTo with
                | ExtNumber (UInt64|Int64), (ExtNumber Decimal | Number _) ->
                    InstanceCall (args.Head, "toNumber", args.Tail)
                    |> makeCall i.range (Fable.Number Float64)
                | (ExtNumber Decimal | Number Float), (Number Integer | ExtNumber(Int64|UInt64)) when i.ownerFullName = "System.Convert" ->
                    CoreLibCall("Util", Some "round", false, args)
                    |> makeCall i.range i.returnType
                | _, _ -> args.Head
                |> List.singleton
                |> emitCast typeTo
            | ExtNumber (UInt64|Int64 as kind) ->
                emitLong (kind = UInt64) [args.Head]
            | Number _ -> emit i "$0" [args.Head]
            | _ -> wrap i.returnType args.Head
        | _ -> wrap i.returnType args.Head

    let toList com (i: Fable.ApplyInfo) expr =
        CoreLibCall ("Seq", Some "toList", false, [expr])
        |> makeCall i.range i.returnType

    let toArray (com: Fable.ICompiler) (i: Fable.ApplyInfo) expr =
        let arrayFrom arrayCons expr =
            GlobalCall (arrayCons, Some "from", false, [expr])
            |> makeCall i.range i.returnType
        match expr, i.returnType with
        // Optimization
        | CoreMeth "List" "ofArray" [arr], _ -> arr
        | CoreCons "List" "default" [], _ ->
            Fable.ArrayConst(Fable.ArrayValues [], genArg i.returnType) |> Fable.Value
        // Typed arrays
        | _, Fable.Array(Fable.Number numberKind) when com.Options.typedArrays ->
            arrayFrom (getTypedArrayName com numberKind) expr
        | _ -> arrayFrom "Array" expr

    let getZero = function
        | Fable.DeclaredType(ent, _) as t ->
            match ent.FullName with
            | "System.TimeSpan" -> makeIntConst 0
            | "System.DateTime" -> ccall_ None t "Date" "minValue" []
            | "System.DateTimeOffset" -> ccall_ None t "DateOffset" "minValue" []
            | "Microsoft.FSharp.Collections.FSharpSet" -> ccall_ None t "Set" "create" []
            | _ ->
                let callee = Fable.TypeRef(ent,[]) |> Fable.Value
                makeGet None t callee (makeStrConst "Zero")
        | Fable.ExtendedNumber kind as t ->
            match kind with
            | Int64 | UInt64 ->
                ccall_ None t "Long" "fromInt" [makeIntConst 0]
            | Decimal -> makeDecConst 0m
            | BigInt ->
                ccall_ None t "BigInt" "fromInt32" [makeIntConst 0]
        | Fable.Char | Fable.String -> makeStrConst ""
        | _ -> makeIntConst 0

    let applyOpReplacedEntities =
        set ["System.TimeSpan"; "System.DateTime"; "System.DateTimeOffset";
             "Microsoft.FSharp.Collections.FSharpSet"]

    let applyOp range returnType (args: Fable.Expr list) meth =
        let (|CustomOp|_|) meth argTypes (ent: Fable.Entity) =
            if applyOpReplacedEntities.Contains ent.FullName then None else
            ent.TryGetMember(meth, Fable.Method, Fable.StaticLoc, argTypes)
            |> function None -> None | Some m -> Some(ent, m)
        let apply op args =
            Fable.Apply(Fable.Value op, args, Fable.ApplyMeth, returnType, range)
        let nativeOp leftOperand = function
            | "op_Addition" -> Fable.BinaryOp BinaryPlus
            | "op_Subtraction" -> Fable.BinaryOp BinaryMinus
            | "op_Multiply" -> Fable.BinaryOp BinaryMultiply
            | "op_Division" -> Fable.BinaryOp BinaryDivide
            | "op_Modulus" -> Fable.BinaryOp BinaryModulus
            | "op_LeftShift" -> Fable.BinaryOp BinaryShiftLeft
            | "op_RightShift" ->
                match leftOperand with
                | Fable.Number UInt32 -> Fable.BinaryOp BinaryShiftRightZeroFill // See #646
                | _ -> Fable.BinaryOp BinaryShiftRightSignPropagating
            | "op_BitwiseAnd" -> Fable.BinaryOp BinaryAndBitwise
            | "op_BitwiseOr" -> Fable.BinaryOp BinaryOrBitwise
            | "op_ExclusiveOr" -> Fable.BinaryOp BinaryXorBitwise
            | "op_LogicalNot" -> Fable.UnaryOp UnaryNotBitwise
            | "op_UnaryNegation" -> Fable.UnaryOp UnaryMinus
            | "op_BooleanAnd" -> Fable.LogicalOp LogicalAnd
            | "op_BooleanOr" -> Fable.LogicalOp LogicalOr
            | _ -> failwithf "Unknown operator: %s" meth
        let argTypes = List.map Fable.Expr.getType args
        match argTypes with
        | Fable.DeclaredType(CustomOp meth argTypes (ent, m), _)::_
        | _::[Fable.DeclaredType(CustomOp meth argTypes (ent, m), _)] ->
            let typRef = Fable.Value(Fable.TypeRef(ent,[]))
            InstanceCall(typRef, m.OverloadName, args)
            |> makeCall range returnType
        | Fable.ExtendedNumber BigInt::_ ->
            CoreLibCall ("BigInt", Some meth, false, args)
            |> makeCall range returnType
        | Fable.ExtendedNumber (Int64|UInt64)::_ ->
            let meth =
                match meth with
                | "op_Addition" -> "add"
                | "op_Subtraction" -> "sub"
                | "op_Multiply" -> "mul"
                | "op_Division" -> "div"
                | "op_Modulus" -> "mod"
                | "op_LeftShift" -> "shl"
                | "op_RightShift" -> "shr"
                | "op_BitwiseAnd" -> "and"
                | "op_BitwiseOr" -> "or"
                | "op_ExclusiveOr" -> "xor"
                | "op_LogicalNot" -> "not"
                | "op_UnaryNegation" -> "neg"
                | _ -> failwithf "Unknown operator: %s" meth
            InstanceCall (args.Head, meth, args.Tail)
            |> makeCall range returnType
        // Floor result of integer divisions (see #172)
        // Apparently ~~ is faster than Math.floor (see https://coderwall.com/p/9b6ksa/is-faster-than-math-floor)
        | Fable.Number Integer::_ when meth = "op_Division" ->
            apply (Fable.BinaryOp BinaryDivide) args
            |> List.singleton |> apply (Fable.UnaryOp UnaryNotBitwise)
            |> List.singleton |> apply (Fable.UnaryOp UnaryNotBitwise)
        | EntFullName (KeyValue "System.DateTime" "Date" modName)::_
        | EntFullName (KeyValue "System.DateTimeOffset" "DateOffset" modName)::_
        | EntFullName (KeyValue "Microsoft.FSharp.Collections.FSharpSet" "Set" modName)::_ ->
            CoreLibCall (modName, Some meth, false, args)
            |> makeCall range returnType
        | EntFullName "System.TimeSpan"::_
        | (Fable.Boolean | Fable.Char | Fable.String | Fable.Number _ | Fable.Enum _)::_ ->
            apply (nativeOp argTypes.Head meth) args
        | _ ->
            ccall_ range returnType "Util" "applyOperator" (args@[makeStrConst meth])

    let tryOptimizeLiteralAddition r t (args: Fable.Expr list) =
        match args with
        | [StringLiteral str1; StringLiteral str2] ->
            let e = Fable.Value(Fable.StringConst (str1 + str2))
            match t with Fable.String -> e | t -> Fable.Wrapped(e, t)
        | [Int32Literal i1; Int32Literal i2] ->
            let e = Fable.Value(Fable.NumberConst(float (i1 + i2), Int32))
            match t with Fable.String -> e | t -> Fable.Wrapped(e, t)
        | _ -> applyOp r t args "op_Addition"

    let equals equal com (i: Fable.ApplyInfo) (args: Fable.Expr list) =
        let op equal =
            if equal then BinaryEqualStrict else BinaryUnequalStrict
            |> Fable.BinaryOp |> Fable.Value
        let is equal expr =
            if equal then expr
            else makeUnOp i.range i.returnType [expr] UnaryNot
        let icall (args: Fable.Expr list) equal =
            InstanceCall(args.Head, "Equals", args.Tail)
            |> makeCall i.range i.returnType |> is equal |> Some
        match args.Head.Type with
        | EntFullName ("System.DateTime" | "System.DateTimeOffset") ->
            ccall i "Date" "equals" args |> is equal |> Some
        | Fable.DeclaredType(ent, _)
            when (ent.HasInterface "System.IEquatable"
                    && ent.FullName <> "System.Guid"
                    && ent.FullName <> "System.TimeSpan")
                || ent.FullName = "Microsoft.FSharp.Collections.FSharpList"
                || ent.FullName = "Microsoft.FSharp.Collections.FSharpMap"
                || ent.FullName = "Microsoft.FSharp.Collections.FSharpSet" ->
            icall args equal
        | EntFullName "System.Guid"
        | EntFullName "System.TimeSpan"
        | Fable.ExtendedNumber Decimal
        | Fable.Boolean | Fable.Char | Fable.String | Fable.Number _ | Fable.Enum _ ->
            Fable.Apply(op equal, args, Fable.ApplyMeth, i.returnType, i.range) |> Some
        | Fable.ExtendedNumber (Int64|UInt64|BigInt) ->
            icall args equal
        | Fable.Array _ | Fable.Tuple _
        | Fable.Unit | Fable.Any | Fable.MetaType | Fable.DeclaredType _
        | Fable.GenericParam _ | Fable.Option _ | Fable.Function _ ->
            CoreLibCall("Util", Some "equals", false, args)
            |> makeCall i.range i.returnType |> is equal |> Some

    let compareReplacedEntities =
        set ["System.Guid"; "System.TimeSpan"; "System.DateTime"; "System.DateTimeOffset"]

    /// Compare function that will call Util.compare or instance `CompareTo` as appropriate
    /// If passed an optional binary operator, it will wrap the comparison like `comparison < 0`
    let compare com r (args: Fable.Expr list) op =
        let wrapWith op comparison =
            match op with
            | None -> comparison
            | Some op -> makeEqOp r [comparison; makeIntConst 0] op
        let icall (args: Fable.Expr list) op =
            InstanceCall(args.Head, "CompareTo", args.Tail)
            |> makeCall r (Fable.Number Int32) |> wrapWith op
        match args.Head.Type with
        | Fable.DeclaredType(ent, _)
            when ent.HasInterface "System.IComparable"
                && not(compareReplacedEntities.Contains ent.FullName) ->
            icall args op
        | EntFullName ("System.DateTime" | "System.DateTimeOffset") ->
            ccall_ r (Fable.Number Int32) "Date" "compare" args |> wrapWith op
        | EntFullName "System.Guid"
        | EntFullName "System.TimeSpan"
        | Fable.ExtendedNumber Decimal
        | Fable.Boolean | Fable.Char | Fable.String | Fable.Number _ | Fable.Enum _ ->
            match op with
            | Some op -> makeEqOp r args op
            | None -> ccall_ r (Fable.Number Int32) "Util" "comparePrimitives" args
        | Fable.ExtendedNumber (Int64|UInt64|BigInt) ->
            icall args op
        | Fable.Array _ | Fable.Tuple _
        | Fable.Unit | Fable.Any | Fable.MetaType | Fable.DeclaredType _
        | Fable.GenericParam _ | Fable.Option _ | Fable.Function _ ->
            ccall_ r (Fable.Number Int32) "Util" "compare" args |> wrapWith op

    let makeComparer (typArg: Fable.Type option) =
        let f =
            match typArg with
            | Some(EntFullName "System.Guid")
            | Some(EntFullName "System.TimeSpan")
            | Some(Fable.Boolean | Fable.Char | Fable.String | Fable.Number _ | Fable.Enum _) ->
                makeCoreRef "Util" "comparePrimitives"
            | Some(EntFullName ("System.DateTime" | "System.DateTimeOffset")) ->
                emitNoInfo "(x,y) => x = x.getTime(), y = y.getTime(), x === y ? 0 : (x < y ? -1 : 1)" []
            | Some(Fable.ExtendedNumber (Int64|UInt64|BigInt)) ->
                emitNoInfo "(x,y) => x.CompareTo(y)" []
            | Some(Fable.DeclaredType(ent, _))
                when ent.HasInterface "System.IComparable" ->
                emitNoInfo "(x,y) => x.CompareTo(y)" []
            | Some _ | None ->
                makeCoreRef "Util" "compare"
        CoreLibCall("Comparer", None, true, [f])
        |> makeCall None Fable.Any

    let makeMapOrSetCons com (i: Fable.ApplyInfo) modName args =
        let typArg =
            match i.calleeTypeArgs, i.methodTypeArgs with
            | x::_, _ | [], x::_ -> Some x
            | [], [] -> None
        let args =
            (if List.isEmpty args then [Fable.Value Fable.Null] else args)
            @ [makeComparer typArg]
        CoreLibCall(modName, Some "create", false, args)
        |> makeCall i.range i.returnType

    let makeDictionaryOrHashSet r t modName forceFSharp typArg args =
        let makeFSharp typArg args =
            match args with
            | [iterable] -> [iterable; makeComparer (Some typArg)]
            | args -> args
            |> ccall_ r t modName "create"
        if forceFSharp
        then makeFSharp typArg args
        else
            match typArg with
            | Fable.ExtendedNumber (Int64|UInt64|BigInt)
            | Fable.Array _ | Fable.Tuple _ ->
                makeFSharp typArg args
            | Fable.DeclaredType(ent, _) when ent.HasInterface "System.IComparable"
                    && ent.FullName <> "System.TimeSpan"
                    && ent.FullName <> "System.Guid" ->
                makeFSharp typArg args
            | _ ->
                GlobalCall(modName, None, true, args) |> makeCall r t

    let makeJsLiteralFromLambda r arg =
        match arg with
        | Fable.Value(Fable.Lambda(args, lambdaBody, _)) ->
            (Some [], flattenSequential lambdaBody)
            ||> List.fold (fun acc statement ->
                match acc, statement with
                // Set of callee: Expr * property: Expr option * value: Expr * range: SourceLocation option
                | Some acc, Fable.Set(_,Some(Fable.Value(Fable.StringConst prop)),value,_) ->
                    (prop, value)::acc |> Some
                | _ -> None)
        | _ -> None
        |> Option.map (makeJsObject r)
        |> Option.defaultWith (fun () -> ccall_ r Fable.Any "Util" "jsOptions" [arg])

    let makeJsLiteral r caseRule keyValueList =
        let rec (|Fields|_|) caseRule = function
            | Fable.Value(Fable.ArrayConst(Fable.ArrayValues exprs, _)) ->
                (Some [], exprs) ||> List.fold (fun acc e ->
                    acc |> Option.bind (fun acc ->
                        match e with
                        | Fable.Value(Fable.TupleConst [Fable.Value(Fable.StringConst key); value]) ->
                            (key, value)::acc |> Some
                        | UnionCons(tag, fields, cases) ->
                            let key =
                                let key = cases |> List.item tag |> fst
                                match caseRule with
                                | CaseRules.LowerFirst -> Naming.lowerFirst key
                                | _ -> key
                            let value =
                                match fields with
                                | [] -> Fable.Value(Fable.BoolConst true) |> Some
                                | [CoreCons "List" "default" []] -> makeJsObject r [] |> Some
                                | [CoreMeth "List" "ofArray" [Fields caseRule fields]] -> makeJsObject r fields |> Some
                                | [expr] ->
                                    match expr.Type with
                                    // Lists references must be converted to objects at runtime
                                    | Fable.DeclaredType(ent,_) when ent.FullName = "Microsoft.FSharp.Collections.FSharpList" -> None
                                    | _ -> Some expr
                                | exprs -> Fable.Value(Fable.ArrayConst(Fable.ArrayValues exprs, Fable.Any)) |> Some
                            value |> Option.map (fun value -> (key, value)::acc)
                        | _ -> None))
                |> Option.map List.rev
            | _ -> None
        match keyValueList with
        | CoreCons "List" "default" [] -> makeJsObject r []
        | CoreMeth "List" "ofArray" [Fields caseRule fields] -> makeJsObject r fields
        | _ -> ccall_ r Fable.Any "Util" "createObj" [keyValueList; caseRule |> int |> makeIntConst]

module AstPass =
    open Util

    let rec fableCoreLib com (i: Fable.ApplyInfo) =
        let destruct = function
            | Fable.Value(Fable.TupleConst exprs) -> exprs
            | expr when expr.Type = Fable.Unit -> []
            | expr -> [expr]
        match i.methodName with
        | "importDynamic" ->
            GlobalCall ("import", None, false, i.args)
            |> makeCall i.range i.returnType |> Some
        | Naming.StartsWith "import" _ ->
            let fail() =
                sprintf "%s.%s only accepts literal strings" i.ownerFullName i.methodName
                |> addError com i.fileName i.range
            let selector, args =
                match i.methodName with
                | "import" ->
                    match i.args with
                    | Fable.Value(Fable.StringConst selector)::args -> selector, args
                    | _ -> fail(); "*", [makeStrConst "unknown"]
                | "importMember" -> Naming.placeholder, i.args
                | "importDefault" -> "default", i.args
                | "importSideEffects" -> "", i.args
                | _ -> "*", i.args // importAll
            let path =
                match args with
                | [Fable.Value(Fable.StringConst path)] -> path
                | _ -> fail(); "unknown"
            Fable.ImportRef(selector, path, Fable.CustomImport) |> Fable.Value |> Some
        | "op_BangBang" ->
            Fable.Wrapped (i.args.Head, i.methodTypeArgs.Head) |> Some
        // The (!^) is only for erased types so we don't need to wrap the result
        | "op_BangHat" -> i.args.Head |> Some
        | "op_Dynamic" ->
            let expr = makeGet i.range i.returnType i.args.Head i.args.Tail.Head
            let appType =
                let ent = Fable.Entity(lazy Fable.Interface, None, "Fable.Core.Applicable", lazy [])
                Fable.DeclaredType(ent, [Fable.Any; Fable.Any])
            // Wrapping is necessary so tuple arguments are destructured
            Fable.Wrapped(expr, appType) |> Some
        | "op_DynamicAssignment" ->
            match i.callee, i.args with
            | ThreeArgs (callee, prop, value) ->
                Fable.Set (callee, Some prop, value, i.range) |> Some
            | _ -> None
        | "op_Dollar" | "createNew" ->
            let args = destruct i.args.Tail.Head
            let applyMeth =
                if i.methodName = "createNew"
                then Fable.ApplyCons else Fable.ApplyMeth
            Fable.Apply(i.args.Head, args, applyMeth, i.returnType, i.range) |> Some
        | "op_EqualsEqualsGreater" ->
            Fable.TupleConst(List.take 2 i.args) |> Fable.Value |> Some
        | "createObj" ->
            makeJsLiteral i.range CaseRules.None i.args.Head |> Some
         | "keyValueList" ->
            match i.args with
            | [Fable.Wrapped(Fable.Value(Fable.NumberConst(rule, _)),_); keyValueList] ->
                let caseRule: CaseRules = enum (int rule)
                makeJsLiteral i.range caseRule keyValueList |> Some
            | [caseRule; keyValueList] ->
                ccall_ i.range Fable.Any "Util" "createObj" [keyValueList; caseRule] |> Some
            | _ -> None
        | "jsOptions" ->
            match i.args with
            | [arg] -> makeJsLiteralFromLambda i.range arg |> Some
            | _ -> None
        | "createEmpty" ->
            Fable.ObjExpr ([], [], None, i.range)
            |> wrap i.returnType |> Some
        | "nameof" ->
            match i.args with
            | [Nameof name] -> name
            | _ -> "Cannot infer name of expression" |> addError com i.fileName i.range; "unknown"
            |> makeStrConst |> Some
        | "nameofLambda" ->
            match i.args with
            | [Fable.Value(Fable.Lambda(_,Nameof name,_))] -> name
            | _ -> "Cannot infer name of expression" |> addError com i.fileName i.range; "unknown"
            |> makeStrConst |> Some
        | "areEqual" ->
            match i.args with
            | [expected; actual] -> Some [actual; expected]
            | [expected; actual; msg] -> Some [actual; expected; msg]
            | _ -> None
            |> Option.map (fun args ->
                CoreLibCall ("Assert", Some "equal", false, args)
                |> makeCall i.range i.returnType)
        | "async.AwaitPromise.Static" | "async.StartAsPromise.Static" ->
            let meth =
                if i.methodName = "async.AwaitPromise.Static"
                then "awaitPromise" else "startAsPromise"
            CoreLibCall("Async", Some meth, false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "ofJsonAsType" ->
            match i.args with
            | [arg; typ] -> fableCoreLib com { i with methodName = "ofJson"; args = [arg; makeJsObject None ["T", typ]] }
            | _ -> failwithf "Unexpected number of arguments for %s" i.methodName
        | "toJson" | "ofJson" | "deflate" | "inflate" | "toPlainJsObj"
        | "toJsonWithTypeInfo" | "ofJsonWithTypeInfo" ->
            let modName = if i.methodName = "toPlainJsObj" then "Util" else "Serialize"
            CoreLibCall(modName, Some i.methodName, false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "jsNative" ->
            // TODO: Fail at compile time?
            addWarning com i.fileName i.range "jsNative is being compiled without replacement, this will fail at runtime."
            "A function supposed to be replaced by JS native code has been called, please check."
            |> Fable.StringConst |> Fable.Value |> List.singleton
            |> newError i.range i.returnType
            |> fun err -> Fable.Throw(err, i.returnType, i.range) |> Some
        // TODO: Delete this after deprecating applySpread
        | "applySpread" ->
            let callee, args =
                match i.args with
                | [callee; Fable.Value(Fable.TupleConst args)] -> Some callee, args
                | [callee; arg] -> Some callee, [arg]
                | _ -> "Unexpected args passed to JsInterop.applySpread"
                       |> addError com i.fileName i.range; None, []
            let args =
                match List.rev args with
                | [] -> []
                | (CoreCons "List" "default" [])::rest -> List.rev rest
                | (CoreMeth "List" "ofArray" [Fable.Value(Fable.ArrayConst(Fable.ArrayValues spreadValues, _))])::rest ->
                    (List.rev rest) @ spreadValues
                | expr::rest -> (List.rev rest) @ [Fable.Value(Fable.Spread expr)]
            match callee with
            | Some callee -> Fable.Apply(callee, args, Fable.ApplyMeth, i.returnType, i.range) |> Some
            | None -> Fable.Value Fable.Null |> Some
        | _ -> None

    let references com (i: Fable.ApplyInfo) =
        match i.methodName with
        | ".ctor" ->
            makeJsObject i.range [("contents", i.args.Head)] |> Some
        | "contents" | "value" ->
            let prop = makeStrConst "contents"
            match i.methodKind with
            | Fable.Getter _ ->
                makeGet i.range i.returnType i.callee.Value prop |> Some
            | Fable.Setter _ ->
                Fable.Set(i.callee.Value, Some prop, i.args.Head, i.range) |> Some
            | _ -> None
        | _ -> None

    let fsFormat com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "value" ->
            makeGet None i.returnType i.callee.Value (makeStrConst "input")
            |> Some
        | "printFormatToStringThen" ->
            match i.args with
            | [_] ->
                ccall i "String" "toText" i.args |> Some
            | [cont; fmt] ->
                InstanceCall(fmt, "cont", [cont])
                |> makeCall i.range i.returnType |> Some
            | _ -> None
        | "printFormatToString" ->
            ccall i "String" "toText" i.args |> Some
        | "printFormatLine" -> ccall i "String" "toConsole" i.args |> Some
        | "printFormatToTextWriter"
        | "printFormatLineToTextWriter" ->
            // addWarning com i.fileName i.range "fprintfn will behave as printfn"
            ccall i "String" "toConsole" i.args.Tail |> Some
        | "printFormat" ->
            // addWarning com i.fileName i.range "printf will behave as printfn"
            ccall i "String" "toConsole" i.args |> Some
        | "printFormatThen" ->
            let cont = makeGet None i.returnType i.args.Tail.Head (makeStrConst "cont")
            Fable.Apply(cont, [i.args.Head], Fable.ApplyMeth, i.returnType, i.range)
            |> Some
        | "printFormatToStringThenFail" ->
            ccall i "String" "toFail" i.args |> Some
        | ".ctor" ->
            ccall i "String" "printf" [i.args.Head] |> Some
        | _ -> None

    let operators (com: ICompiler) (info: Fable.ApplyInfo) =
        let math range typ (args: Fable.Expr list) methName =
            match methName, typ with
            | "abs", Fable.ExtendedNumber Int64 ->
                InstanceCall (args.Head, "abs", args.Tail)
                |> makeCall range typ |> Some
            | "abs", Fable.ExtendedNumber BigInt ->
                ccall info "BigInt" "abs" info.args |> Some
             | _ ->
                GlobalCall ("Math", Some methName, false, args)
                |> makeCall range typ |> Some
        let r, typ, args = info.range, info.returnType, info.args
        match info.methodName with
        | "keyValuePattern" ->
            info.args.Head |> Some
        | "defaultArg" ->
            match args with
            | [Fable.Value(Fable.IdentValue _) as arg1; arg2] ->
                let cond = makeEqOp r [arg1; Fable.Value Fable.Null] BinaryUnequal
                Fable.IfThenElse(cond, arg1, arg2, r) |> Some
            | args -> ccall info "Option" "defaultArg" args |> Some
        | "defaultAsyncBuilder" -> makeCoreRef "AsyncBuilder" "singleton" |> Some
        // Negation
        | "not" -> makeUnOp r info.returnType args UnaryNot |> Some
        // Equality
        | "op_Inequality" | "neq" ->
            match args with
            | [Fable.Value Fable.Null; _]
            | [_; Fable.Value Fable.Null] -> makeEqOp r args BinaryUnequal |> Some
            | _ -> equals false com info args
        | "op_Equality" | "eq" ->
            match args with
            | [Fable.Value Fable.Null; _]
            | [_; Fable.Value Fable.Null] -> makeEqOp r args BinaryEqual |> Some
            | _ -> equals true com info args
        | "isNull" -> makeEqOp r [args.Head; Fable.Value Fable.Null] BinaryEqual |> Some
        | "hash" ->
            CoreLibCall("Util", Some "hash", false, args)
            |> makeCall r typ |> Some
        // Comparison
        | "compare" -> compare com info.range args None |> Some
        | "op_LessThan" | "lt" -> compare com info.range args (Some BinaryLess) |> Some
        | "op_LessThanOrEqual" | "lte" -> compare com info.range args (Some BinaryLessOrEqual) |> Some
        | "op_GreaterThan" | "gt" -> compare com info.range args (Some BinaryGreater) |> Some
        | "op_GreaterThanOrEqual" | "gte" -> compare com info.range args (Some BinaryGreaterOrEqual) |> Some
        | "min" | "max" ->
            let op = if info.methodName = "min" then BinaryLess else BinaryGreater
            let comparison = compare com info.range args (Some op)
            Fable.IfThenElse(comparison, args.Head, args.Tail.Head, info.range) |> Some
        // Operators
        | "op_Addition" ->
            tryOptimizeLiteralAddition info.range info.returnType args |> Some
        | "op_Subtraction" | "op_Multiply" | "op_Division"
        | "op_Modulus" | "op_LeftShift" | "op_RightShift"
        | "op_BitwiseAnd" | "op_BitwiseOr" | "op_ExclusiveOr"
        | "op_LogicalNot" | "op_UnaryNegation" | "op_BooleanAnd" | "op_BooleanOr" ->
            applyOp info.range info.returnType args info.methodName |> Some
        | "log" -> // log with base value i.e. log(8.0, 2.0) -> 3.0
            match info.args with
            | [_] -> math r typ args info.methodName
            | [_; _] ->  emit info "Math.log($0) / Math.log($1)" info.args |> Some
            | _ -> None
        // Math functions
        // TODO: optimize square pow: x * x
        | "pow" | "powInteger" | "op_Exponentiation" -> math r typ args "pow"
        | "ceil" | "ceiling" -> math r typ args "ceil"
        | "abs" | "acos" | "asin" | "atan" | "atan2"
        | "cos"  | "exp" | "floor" | "log" | "log10"
        | "sin" | "sqrt" | "tan" ->
            math r typ args info.methodName
        | "round" ->
            CoreLibCall("Util", Some "round", false, args)
            |> makeCall r typ |> Some
        // Numbers
        | "infinity" | "infinitySingle" -> emit info "Number.POSITIVE_INFINITY" [] |> Some
        | "naN" | "naNSingle" -> emit info "Number.NaN" [] |> Some
        // Function composition
        | "op_ComposeRight" | "op_ComposeLeft" ->
            match args, info.methodName with
            | [arg1; arg2], "op_ComposeRight" -> Some(arg1, arg2)
            | [arg1; arg2], "op_ComposeLeft" -> Some(arg2, arg1)
            | _ -> None
            |> Option.map (fun (f1, f2) ->
                let tempVar = com.GetUniqueVar() |> makeIdent
                [Fable.IdentValue tempVar |> Fable.Value]
                |> makeApply com info.range Fable.Any f1
                |> List.singleton
                |> makeApply com info.range Fable.Any f2
                |> makeLambdaExpr [tempVar])
        // Reference
        | "op_Dereference" -> makeGet r info.returnType args.Head (makeStrConst "contents") |> Some
        | "op_ColonEquals" -> Fable.Set(args.Head, Some(makeStrConst "contents"), args.Tail.Head, r) |> Some
        | "ref" -> makeJsObject r [("contents", args.Head)] |> Some
        | "increment" | "decrement" ->
            if info.methodName = "increment" then "++" else "--"
            |> sprintf "void($0.contents%s)"
            |> emit info <| args |> Some
        // Conversions
        | "createSequence" | "identity" | "box" | "unbox" -> wrap typ args.Head |> Some
        | "toSByte" | "toByte"
        | "toInt8" | "toUInt8"
        | "toInt16" | "toUInt16"
        | "toInt" | "toUInt"
        | "toInt32" | "toUInt32"
        | "toInt64" | "toUInt64"
            -> toInt com info info.methodTypeArgs.Head args |> Some
        | "toSingle" | "toDouble" | "toDecimal" -> toFloat com info info.methodTypeArgs.Head args |> Some
        | "toChar" -> toChar com info info.methodTypeArgs.Head args |> Some
        | "toString" -> toString com info info.methodTypeArgs.Head args |> Some
        | "toEnum" -> args.Head |> Some
        | "createDictionary" ->
            makeDictionaryOrHashSet r typ "Map" false info.methodTypeArgs.Head args |> Some
        | "createSet" ->
            makeMapOrSetCons com info "Set" args |> Some
        // Ignore: wrap to keep Unit type (see Fable2Babel.transformFunction)
        | "ignore" -> Fable.Wrapped (args.Head, Fable.Unit) |> Some
        // Ranges
        | "op_Range" | "op_RangeStep" ->
            let meth =
                match info.methodTypeArgs.Head with
                | Fable.Char -> "rangeChar"
                | _ -> if info.methodName = "op_Range" then "range" else "rangeStep"
            CoreLibCall("Seq", Some meth, false, args)
            |> makeCall r typ |> Some
        // Tuples
        | "fst" | "snd" ->
            if info.methodName = "fst" then 0 else 1
            |> makeIntConst
            |> makeGet r typ args.Head |> Some
        // Strings
        | "printFormatToString"             // sprintf
        | "printFormatToStringThen"         // Printf.sprintf
        | "printFormat" | "printFormatLine" // printf / printfn
        | "printFormatThen"                 // Printf.kprintf
        | "printFormatToStringThenFail" ->  // Printf.failwithf
            fsFormat com info
        // Exceptions
        | "raise" ->
            Fable.Throw (args.Head, typ, r) |> Some
        | "reraise" ->
            match info.caughtException with
            | Some ex ->
                let ex = Fable.IdentValue ex |> Fable.Value
                Fable.Throw (ex, typ, r) |> Some
            | None ->
                "`reraise` used in context where caught exception is not available, please report"
                |> addError com info.fileName info.range
                Fable.Throw (newError None Fable.Any [], typ, r) |> Some
        | "failWith"  | "invalidOp" | "invalidArg" ->
            let args =
                match info.methodName with
                | "invalidArg" -> [makeEmit None Fable.String args "$1 + '\\nParameter name: ' + $0"]
                | _ -> args
            Fable.Throw (newError None Fable.Any args, typ, r) |> Some
        // Type ref
        | "typeOf" | "typeDefOf" ->
            info.methodTypeArgs.Head
            |> resolveTypeRef com info (info.methodName = "typeOf")
            |> Some
        // Concatenates two lists
        | "op_Append" ->
          CoreLibCall("List", Some "append", false, args)
          |> makeCall r typ |> Some
        | _ -> None

    let chars com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "toUpper" -> icall i "toLocaleUpperCase" |> Some
        | "toUpperInvariant" -> icall i "toUpperCase" |> Some
        | "toLower" -> icall i "toLocaleLowerCase" |> Some
        | "toLowerInvariant" -> icall i "toLowerCase" |> Some
        | "isLetter" | "isNumber" | "isDigit"
        | "isLetterOrDigit" | "isWhiteSpace"
        | "isUpper" | "isLower"
        | "parse" ->
            let methName =
                match i.methodName with
                | "isNumber" ->
                    addWarning com i.fileName i.range "Char.IsNumber is compiled as Char.IsDigit"
                    "isDigit"
                | methName -> methName
            match i.args with
            | [str; idx] -> [makeGet None Fable.Char str idx]
            | args -> args
            |> ccall i "Char" methName |> Some
        | _ -> None

    let strings com (i: Fable.ApplyInfo) =
        let icall2 meth (callee, args) =
            InstanceCall (callee, meth, args)
            |> makeCall i.range i.returnType
        match i.methodName with
        | ".ctor" ->
            match i.args.Head.Type with
            | Fable.String ->
                match i.args with
                | [c; n] -> emit i "Array($1 + 1).join($0)" i.args |> Some // String(char, int)
                | _ -> failwith "Unexpected arguments in System.String constructor."
            | Fable.Array _ ->
                match i.args with
                | [c] -> emit i "$0.join('')" i.args |> Some // String(char[])
                | [c; s; l] -> emit i "$0.join('').substr($1, $2)" i.args |> Some // String(char[], int, int)
                | _ -> failwith "Unexpected arguments in System.String constructor."
            | _ ->
                fsFormat com i
        | "length" ->
            let c, _ = instanceArgs i.callee i.args
            makeGet i.range i.returnType c (makeStrConst "length") |> Some
        | "equals" ->
            match i.callee, i.args with
            | Some x, [y]
            | None, [x; y] ->
                makeEqOp i.range [x; y] BinaryEqualStrict |> Some
            | Some x, [y; kind]
            | None, [x; y; kind] ->
                makeEqOp i.range [ccall i "String" "compare" [x; y; kind]; makeIntConst 0] BinaryEqualStrict |> Some
            | _ -> None
        | "contains" ->
            if (List.length i.args) > 1 then addWarning com i.fileName i.range "String.Contains: second argument is ignored"
            makeEqOp i.range [icall2 "indexOf" (i.callee.Value, [i.args.Head]); makeIntConst 0] BinaryGreaterOrEqual |> Some
        | "startsWith" ->
            match i.args with
            | [str] -> makeEqOp i.range [icall2 "indexOf" (i.callee.Value, [i.args.Head]); makeIntConst 0] BinaryEqualStrict |> Some
            | [str; comp] -> ccall i "String" "startsWith" (i.callee.Value::i.args) |> Some
            | _ -> None
        | "substring" -> icall i "substr" |> Some
        | "toUpper" -> icall i "toLocaleUpperCase" |> Some
        | "toUpperInvariant" -> icall i "toUpperCase" |> Some
        | "toLower" -> icall i "toLocaleLowerCase" |> Some
        | "toLowerInvariant" -> icall i "toLowerCase" |> Some
        | "chars" ->
            CoreLibCall("String", Some "getCharAtIndex", false, i.callee.Value::i.args)
            |> makeCall i.range i.returnType
            |> Some
        | "indexOf" | "lastIndexOf" ->
            match i.args with
            | [Type Fable.Char]
            | [Type Fable.String]
            | [Type Fable.Char; Type(Fable.Number Int32)]
            | [Type Fable.String; Type(Fable.Number Int32)] -> icall i i.methodName |> Some
            | _ -> "The only extra argument accepted for String.IndexOf/LastIndexOf is startIndex."
                   |> addErrorAndReturnNull com i.fileName i.range |> Some
        | "trim" | "trimStart" | "trimEnd" ->
            let side =
                match i.methodName with
                | "trimStart" -> "start"
                | "trimEnd" -> "end"
                | _ -> "both"
            CoreLibCall("String", Some "trim", false, i.callee.Value::(makeStrConst side)::i.args)
            |> makeCall i.range i.returnType |> Some
        | "toCharArray" ->
            InstanceCall(i.callee.Value, "split", [makeStrConst ""])
            |> makeCall i.range i.returnType |> Some
        | "iterate" | "iterateIndexed" | "forAll" | "exists" ->
            CoreLibCall("Seq", Some i.methodName, false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "map" | "mapIndexed" | "collect"  ->
            CoreLibCall("Seq", Some i.methodName, false, i.args)
            |> makeCall i.range Fable.Any
            |> List.singleton
            |> emit i "Array.from($0).join('')"
            |> Some
        | "concat" ->
            let args =
                if i.ownerFullName = "System.String"
                then (makeStrConst "")::i.args else i.args
            CoreLibCall("String", Some "join", false, args)
            |> makeCall i.range i.returnType |> Some
        | "split" ->
            match i.args with
            | [] -> InstanceCall(i.callee.Value, "split", [Fable.Value(Fable.StringConst " ")]) // Optimization
            // | [MaybeWrapped(Fable.Value(Fable.StringConst _) as separator)]
            // | [Fable.Value(Fable.ArrayConst(Fable.ArrayValues [separator],_))] ->
            //     InstanceCall(i.callee.Value, "split", [separator]) // Optimization
            | [arg1; Type(Fable.Enum _) as arg2] ->
                let arg1 =
                    match arg1.Type with
                    | Fable.Array _ -> arg1
                    | _ -> Fable.Value(Fable.ArrayConst(Fable.ArrayValues [arg1], Fable.String))
                let args = [arg1; Fable.Value Fable.Null; arg2]
                CoreLibCall("String", Some "split", false, i.callee.Value::args)
            | args -> CoreLibCall("String", Some "split", false, i.callee.Value::args)
            |> makeCall i.range Fable.String
            |> Some
        | "filter" ->
            ccall i "String" "filter" i.args
            |> Some
        | _ -> None

    let log com (i: Fable.ApplyInfo) =
        let v =
            match i.args with
            | [] -> Fable.Value Fable.Null
            | [v] -> v
            | Type Fable.String::_ ->
                CoreLibCall("String", Some "format", false, i.args)
                |> makeCall i.range Fable.String
            | _ -> i.args.Head
        GlobalCall("console", Some "log", false, [v])
        |> makeCall i.range i.returnType

    let bitConvert com (i: Fable.ApplyInfo) =
        let methodName =
            if i.methodName = "getBytes" then
                match i.args.Head.Type with
                | Fable.Boolean -> "getBytesBoolean"
                | Fable.Char -> "getBytesChar"
                | Fable.Number Int16 -> "getBytesInt16"
                | Fable.Number Int32 -> "getBytesInt32"
                | Fable.ExtendedNumber Int64 -> "getBytesInt64"
                | Fable.Number UInt16 -> "getBytesUInt16"
                | Fable.Number UInt32 -> "getBytesUInt32"
                | Fable.ExtendedNumber UInt64 -> "getBytesUInt64"
                | Fable.Number Float32 -> "getBytesSingle"
                | Fable.Number Float64 -> "getBytesDouble"
                | x -> failwithf "Unsupported type in BitConverter.GetBytes(): %A" x
            else i.methodName
        CoreLibCall("BitConverter", Some methodName, false, i.args)
        |> makeCall i.range i.returnType |> Some

    let parse (com: ICompiler) (i: Fable.ApplyInfo) isFloat =
        // TODO what about Single ?
        let numberModule =
            if isFloat
            then "Double"
            else "Int32"
        match i.methodName with
        | "isNaN" when isFloat ->
            match i.args with
            | [_someNumber] ->
                GlobalCall("Number", Some "isNaN", false, i.args)
                |> makeCall i.range (Fable.Number Float64)
                |> Some
            | _ -> None
        // TODO verify that the number is within the Int32/Double/Single range
        | "parse" | "tryParse" ->
            let hexConst = float System.Globalization.NumberStyles.HexNumber
            match i.methodName, i.args with
            | "parse", [str] ->
                CoreLibCall (numberModule, Some "parse", false,
                    [str; (if isFloat then makeNumConst 10.0 else makeIntConst 10)])
                |> makeCall i.range i.returnType |> Some
            | "parse", [str; Fable.Wrapped(Fable.Value(Fable.NumberConst(hexConst,_)), Fable.Enum _)] ->
                CoreLibCall (numberModule, Some "parse", false,
                    [str; (if isFloat then makeNumConst 16.0 else makeIntConst 16)])
                |> makeCall i.range i.returnType |> Some
            // System.Double.Parse(string, NumberStyle)
            | "parse", [_inputString; Fable.Wrapped(_numberStyleValue, Fable.Enum _)] ->
                (* Todo *)
                None
            // System.Double.Parse(string, IFormatProvider)
            // just ignore the second args (IFormatProvider) and compile
            // to System.Double.Parse(string)
            | "parse", [str; _ ] ->
                CoreLibCall (numberModule, Some "parse", false,
                    [str; (if isFloat then makeNumConst 10.0 else makeIntConst 10)])
                |> makeCall i.range i.returnType |> Some
            | "tryParse", [str; defValue] ->
                CoreLibCall (numberModule, Some "tryParse", false,
                    [str; (if isFloat then makeNumConst 10.0 else makeIntConst 10); defValue])
                |> makeCall i.range i.returnType |> Some
            | _ ->
                sprintf "%s.%s only accepts a single argument" i.ownerFullName i.methodName
                |> addErrorAndReturnNull com i.fileName i.range |> Some
        | "toString" ->
            match i.args with
            | [Type Fable.String as format] ->
                let format = emitNoInfo "'{0:' + $0 + '}'" [format]
                CoreLibCall ("String", Some "format", false, [format;i.callee.Value])
                |> makeCall i.range i.returnType |> Some
            | _ -> ccall i "Util" "toString" [i.callee.Value] |> Some
        | _ -> None

    let convert com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "toSByte" | "toByte"
        | "toInt16" | "toUInt16"
        | "toInt32" | "toUInt32"
        | "toInt64" | "toUInt64"
            -> toInt com i i.methodArgTypes.Head i.args |> Some
        | "toSingle" | "toDouble" | "toDecimal"
            -> toFloat com i i.methodArgTypes.Head i.args |> Some
        | "toChar" -> toChar com i i.methodArgTypes.Head i.args |> Some
        | "toString" -> toString com i i.methodArgTypes.Head i.args |> Some
        | "toBase64String" | "fromBase64String" ->
            if not(List.isSingle i.args) then
                sprintf "Convert.%s only accepts one single argument" (Naming.upperFirst i.methodName)
                |> addWarning com i.fileName i.range
            ccall i "String" i.methodName i.args |> Some
        | _ -> None

    let console com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "out" ->
            match i.methodKind with
            | Fable.Getter _ -> Fable.Null |> Fable.Value |> Some
            | _ -> None
        | "write" ->
            addWarning com i.fileName i.range "Write will behave as WriteLine"
            log com i |> Some
        | "writeLine" -> log com i |> Some
        | _ -> None

    let decimals com (i: Fable.ApplyInfo) =
        match i.methodName, i.args with
        | ".ctor", [Fable.Value (Fable.NumberConst (x, _))] ->
#if FABLE_COMPILER
            makeNumConst (float x) |> Some
#else
            makeDecConst (decimal(x)) |> Some
#endif
        | ".ctor", [Fable.Value(Fable.ArrayConst(Fable.ArrayValues arVals, _))] ->
            match arVals with
            | [ Fable.Value (Fable.NumberConst (low, Int32));
                Fable.Value (Fable.NumberConst (mid, Int32));
                Fable.Value (Fable.NumberConst (high, Int32));
                Fable.Value (Fable.NumberConst (scale, Int32)) ] ->
#if FABLE_COMPILER
                    let x = (float ((uint64 (uint32 mid)) <<< 32 ||| (uint64 (uint32 low))))
                            / System.Math.Pow(10.0, float ((int scale) >>> 16 &&& 0xFF))
                    makeNumConst (if scale < 0.0 then -x else x) |> Some
#else
                    makeDecConst (new decimal([| int low; int mid; int high; int scale |])) |> Some
#endif
            | _ -> None
        | (".ctor" | "makeDecimal"),
              [ Fable.Value (Fable.NumberConst (low, Int32));
                Fable.Value (Fable.NumberConst (mid, Int32));
                Fable.Value (Fable.NumberConst (high, Int32));
                Fable.Value (Fable.BoolConst isNegative);
                Fable.Value (Fable.NumberConst (scale, UInt8)) ] ->
#if FABLE_COMPILER
                    let x = (float ((uint64 (uint32 mid)) <<< 32 ||| (uint64 (uint32 low))))
                            / System.Math.Pow(10.0, float scale)
                    makeNumConst (if isNegative then -x else x) |> Some
#else
                    makeDecConst (new decimal(int low, int mid, int high, isNegative, byte scale)) |> Some
#endif
        | ".ctor", [Fable.Value (Fable.IdentValue _)] ->
            addWarning com i.fileName i.range "Decimals are implemented with floats."
            wrap i.returnType i.args.Head |> Some
        | ("parse" | "tryParse"), _ ->
            parse com i true
        | _,_ -> None

    let debug com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "write" ->
            addWarning com i.fileName i.range "Write will behave as WriteLine"
            log com i |> Some
        | "writeLine" -> log com i |> Some
        | "break" -> Fable.DebugBreak i.range |> Some
        | "assert" ->
            // emit i "if (!$0) { debugger; }" i.args |> Some
            let cond =
                Fable.Apply(Fable.Value(Fable.UnaryOp UnaryNot),
                    i.args, Fable.ApplyMeth, Fable.Boolean, i.range)
            Fable.IfThenElse(cond, Fable.DebugBreak i.range, Fable.Value Fable.Null, i.range)
            |> Some
        | _ -> None

    let regex com (i: Fable.ApplyInfo) =
        let propInt p callee = makeGet i.range i.returnType callee (makeIntConst p)
        let propStr p callee = makeGet i.range i.returnType callee (makeStrConst p)
        let isGroup =
            match i.callee with
            | Some (Type (EntFullName "System.Text.RegularExpressions.Group")) -> true
            | _ -> false
        match i.methodName with
        | ".ctor" ->
            // TODO: Use RegexConst if no options have been passed?
            CoreLibCall("RegExp", Some "create", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "options" ->
            CoreLibCall("RegExp", Some "options", false, [i.callee.Value])
            |> makeCall i.range i.returnType |> Some
        // Capture
        | "index" ->
            if not isGroup
            then propStr "index" i.callee.Value |> Some
            else "Accessing index of Regex groups is not supported"
                 |> addErrorAndReturnNull com i.fileName i.range |> Some
        | "value" ->
            if isGroup
            then
                let value = wrap i.returnType i.callee.Value
                // In JS Regex group values can be undefined, ensure they're empty strings #838
                makeLogOp i.range [value; makeStrConst ""] LogicalOr |> Some
            else propInt 0 i.callee.Value |> Some
        | "length" ->
            if isGroup
            then propStr "length" i.callee.Value |> Some
            else propInt 0 i.callee.Value |> propStr "length" |> Some
        // Group
        | "success" ->
            makeEqOp i.range [i.callee.Value; Fable.Value Fable.Null] BinaryUnequal |> Some
        // Match
        | "groups" -> wrap i.returnType i.callee.Value |> Some
        // MatchCollection & GroupCollection
        | "item" ->
            makeGet i.range i.returnType i.callee.Value i.args.Head |> Some
        | "count" ->
            propStr "length" i.callee.Value |> Some
        | _ -> None

    // Compile static strings to their constant values
    // reference: https://msdn.microsoft.com/en-us/visualfsharpdocs/conceptual/languageprimitives.errorstrings-module-%5bfsharp%5d
    let errorStrings com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "inputArrayEmptyString" -> makeStrConst "The input array was empty" |> Some
        | "inputSequenceEmptyString" -> makeStrConst "The input sequence was empty" |> Some
        | "inputMustBeNonNegativeString" -> makeStrConst "The input must be non-negative" |> Some
        | _ -> None

    let languagePrimitives com (i: Fable.ApplyInfo) =
        match i.methodName, (i.callee, i.args) with
        | "enumOfValue", OneArg (arg) -> arg |> Some
        | "genericHash", _ ->
            CoreLibCall("Util", Some "hash", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "genericComparison", _ ->
            CoreLibCall("Util", Some "compare", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "genericEquality", _ ->
            CoreLibCall("Util", Some "equals", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "physicalEquality", _ ->
            makeEqOp i.range i.args BinaryEqualStrict |> Some
        | _ -> None

    let intrinsicFunctions com (i: Fable.ApplyInfo) =
        match i.methodName, (i.callee, i.args) with
        | "checkThis", (None, [arg]) -> Some arg
        | "unboxFast", OneArg (arg) -> wrap i.returnType arg |> Some
        | "unboxGeneric", OneArg (arg) -> wrap i.returnType arg |> Some
        | "makeDecimal", (_,_) -> decimals com i
        | "getString", TwoArgs (ar, idx)
        | "getArray", TwoArgs (ar, idx) ->
            makeGet i.range i.returnType ar idx |> Some
        | "setArray", ThreeArgs (ar, idx, value) ->
            Fable.Set (ar, Some idx, value, i.range) |> Some
        | ("getArraySlice" | "getStringSlice"), ThreeArgs (ar, lower, upper) ->
            let upper =
                let t = Fable.Number Int32
                match upper with
                | Null _ -> makeGet None t ar (makeStrConst "length")
                | _ -> Fable.Apply(Fable.Value(Fable.BinaryOp BinaryPlus),
                                [upper; makeIntConst 1], Fable.ApplyMeth, t, None)
            InstanceCall (ar, "slice", [lower; upper])
            |> makeCall i.range i.returnType |> Some
        | "setArraySlice", (None, args) ->
            CoreLibCall("Array", Some "setSlice", false, args)
            |> makeCall i.range i.returnType |> Some
        | "typeTestGeneric", (None, [expr]) ->
            makeTypeTest com i.fileName i.range expr i.methodTypeArgs.Head |> Some
        | "createInstance", (None, _) ->
            let typRef, args = resolveTypeRef com i false i.methodTypeArgs.Head, []
            Fable.Apply (typRef, args, Fable.ApplyCons, i.returnType, i.range) |> Some
        | "rangeInt32", (None, args) ->
            CoreLibCall("Seq", Some "rangeStep", false, args)
            |> makeCall i.range i.returnType |> Some
        // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.powdouble-function-%5bfsharp%5d
        // Type: PowDouble : float -> int -> float
        // Usage: PowDouble x n
        | "powDouble", (None, _) ->
            GlobalCall ("Math", Some "pow", false, i.args)
            |> makeCall i.range i.returnType
            |> Some
        // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.rangechar-function-%5bfsharp%5d
        // Type: RangeChar : char -> char -> seq<char>
        // Usage: RangeChar start stop
        | "rangeChar", (None, _) ->
            CoreLibCall("Seq", Some "rangeChar", false, i.args)
            |> makeCall i.range i.returnType |> Some
        // reference: https://msdn.microsoft.com/visualfsharpdocs/conceptual/operatorintrinsics.rangedouble-function-%5bfsharp%5d
        // Type: RangeDouble: float -> float -> float -> seq<float>
        // Usage: RangeDouble start step stop
        | "rangeDouble", (None, _) ->
            CoreLibCall("Seq", Some "rangeStep", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | _ -> None

    let activator com (i: Fable.ApplyInfo) =
        match i.methodName, i.callee, i.args with
        | "createInstance", None, typRef::args ->
            Fable.Apply (typRef, args, Fable.ApplyCons, i.returnType, i.range) |> Some
        | _ -> None

    let funcs com (i: Fable.ApplyInfo) =
        match i.methodName, i.callee with
        | "adapt", _ -> wrap i.returnType i.args.Head |> Some
        | "invoke", Some callee ->
            Fable.Apply(callee, i.args, Fable.ApplyMeth, i.returnType, i.range) |> Some
        | _ -> None

    // See fable-core/Option.ts for more info on how
    // options behave in Fable runtime
    let options (com: ICompiler) (i: Fable.ApplyInfo) =
        let toArray r t arg =
            let ident = makeIdent (com.GetUniqueVar())
            let f =
                [Fable.Value (Fable.IdentValue ident)]
                |> makeArray Fable.Any
                |> makeLambdaExpr [ident]
            // Prevent functions being run twice, see #198
            ccall_ r t "Option" "defaultArg" [arg; makeArray Fable.Any []; f]
        let getCallee(i: Fable.ApplyInfo) =
            match i.callee with Some c -> c | None -> i.args.Head
        match i.methodName with
        | "none" ->
            Fable.Null |> Fable.Value |> Some
        | "value" | "getValue" ->
            ccall i "Option" "getValue" [getCallee i] |> Some
        | "toObj" | "toNullable" | "flatten" ->
            ccall i "Option" "getValue" [getCallee i; makeBoolConst true] |> Some
        | "ofObj" | "ofNullable" ->
            wrap i.returnType (getCallee i) |> Some
        | "isSome" | "isNone" ->
            let op =
                if i.methodName = "isSome"
                then BinaryUnequal
                else BinaryEqual
            let comp =
                ([getCallee i; Fable.Value Fable.Null], op)
                ||> makeEqOp i.range
            match i.returnType with
            | Fable.Boolean _ -> Some comp
            // Hack to fix instance member calls (e.g., myOpt.IsSome)
            // For some reason, F# compiler expects them to be applicable
            | _ -> makeLambdaExpr [] comp |> Some
        | "map" | "bind" ->
            let f, arg = i.args.Head, i.args.Tail.Head
            [arg; Fable.Value Fable.Null; f]
            |> ccall i "Option" "defaultArg" |> Some
        | "filter" ->
            ccall i "Option" "filter" i.args |> Some
        | "toArray" ->
            toArray i.range i.returnType i.args.Head |> Some
        | "foldBack" ->
            let opt = toArray None Fable.Any i.args.Tail.Head
            let args = i.args.Head::opt::i.args.Tail.Tail
            ccall i "Seq" "foldBack" args |> Some
        | "defaultValue" | "orElse" ->
            List.rev i.args
            |> ccall i "Option" "defaultArg" |> Some
        | "defaultWith" | "orElseWith" ->
            List.rev i.args
            |> ccall i "Option" "defaultArgWith" |> Some
        | "count" | "contains" | "exists"
        | "fold" | "forAll" | "iterate" | "toList" ->
            let args =
                let args = List.rev i.args
                let opt = toArray None Fable.Any args.Head
                List.rev (opt::args.Tail)
            ccall i "Seq" i.methodName args |> Some
        // | "map2" | "map3" -> failwith "TODO"
        | _ -> None

    let timeSpans com (i: Fable.ApplyInfo) =
        // let callee = match i.callee with Some c -> c | None -> i.args.Head
        match i.methodName with
        | ".ctor" ->
            CoreLibCall("TimeSpan", Some "create", false, i.args)
            |> makeCall i.range i.returnType |> Some
        | "fromMilliseconds" ->
            wrap i.returnType i.args.Head |> Some
        | "totalMilliseconds" ->
            wrap i.returnType i.callee.Value |> Some
        | _ -> None

    let systemEnv com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "newLine" -> Some (Fable.Value (Fable.StringConst "\n"))
        | _ -> None

    let dates com (i: Fable.ApplyInfo) =
        let getTime (e: Fable.Expr) =
            InstanceCall(e, "getTime", [])
            |> makeCall e.Range (Fable.Number Float64)
        let moduleName =
            if i.ownerFullName = "System.DateTime"
            then "Date" else "DateOffset"
        match i.methodName with
        | ".ctor" ->
            match i.methodArgTypes with
            | [] -> ccall i moduleName "minValue" [] |> Some
            | (Fable.ExtendedNumber Int64)::_ ->
                match i.args with
                | ticks::rest ->
                    let ms = ccall_ ticks.Range (Fable.Number Float64)
                                "Long" "ticksToUnixEpochMilliseconds" [ticks]
                    ccall i moduleName "default" (ms::rest) |> Some
                | _ -> None
            | (Fable.DeclaredType(e,[]))::_ when e.FullName = "System.DateTime" ->
                ccall i "DateOffset" "fromDate" i.args |> Some
            | _ ->
                let last = List.last i.args
                match i.args.Length, last.Type with
                | 7, Fable.Enum "System.DateTimeKind" ->
                    (List.take 6 i.args)@[makeIntConst 0; last]
                    |> ccall i "Date" "create" |> Some
                | _ -> ccall i moduleName "create" i.args |> Some
        | "toString" ->
            ccall i "Date" "toString" (i.callee.Value::i.args) |> Some
        | "kind" | "offset" ->
            makeGet i.range i.returnType i.callee.Value (makeStrConst i.methodName) |> Some
        // DateTimeOffset
        | "dateTime" | "localDateTime" | "utcDateTime" as m ->
            let ms = getTime i.callee.Value
            let kind =
                if m = "localDateTime" then System.DateTimeKind.Local
                elif m = "utcDateTime" then System.DateTimeKind.Utc
                else System.DateTimeKind.Unspecified
                |> int |> makeIntConst
            ccall i "Date" "default" [ms; kind] |> Some
        | "fromUnixTimeSeconds"
        | "fromUnixTimeMilliseconds" ->
            let value =
                List.head i.args |> chainCall "toNumber" [] (Fable.Number Float64)
            let value =
                if i.methodName = "fromUnixTimeSeconds"
                then makeBinOp i.range i.returnType [value; makeIntConst 1000] BinaryMultiply
                else value
            ccall i "DateOffset" "default" [value; makeIntConst 0] |> Some
        | "toUnixTimeSeconds"
        | "toUnixTimeMilliseconds" ->
            let ms = getTime i.callee.Value
            if i.methodName = "toUnixTimeSeconds"
            then [makeBinOp i.range i.returnType [ms; makeIntConst 1000] BinaryDivide]
            else [ms]
            |> ccall i "Long" "fromNumber" |> Some
        // Ticks methods are moved to Long.ts so we don't have to import it to Date.ts
        | "ticks" | "utcTicks" | "toBinary" ->
            let ms = getTime i.callee.Value
            let offset =
                if i.methodName = "utcTicks"
                then makeIntConst 0
                else ccall_ None (Fable.Number Float64) "Date" "offset" [i.callee.Value]
            ccall i "Long" "unixEpochMillisecondsToTicks" [ms; offset] |> Some
        | "addTicks" ->
            match i.callee, i.args with
            | Some c, [ticks] ->
                let ms =
                    chainCall "div" [makeIntConst 10000] (Fable.ExtendedNumber Int64) ticks
                    |> chainCall "toNumber" [] (Fable.Number Float64)
                ccall i moduleName "addMilliseconds" [c; ms] |> Some
            | _ -> None
        | _ -> None

    let keyValuePairs com (i: Fable.ApplyInfo) =
        let get (k: int) =
            makeGet i.range i.returnType i.callee.Value (makeIntConst k) |> Some
        match i.methodName with
        | ".ctor" -> Fable.Value(Fable.TupleConst i.args) |> Some
        | "key" -> get 0
        | "value" -> get 1
        | _ -> None

    let dictionaries (com: ICompiler) (i: Fable.ApplyInfo) =
        let makeComparer (e: Fable.Expr) =
            ccall_ e.Range e.Type "Comparer" "fromEqualityComparer" [e]
        let makeDic forceFSharpMap args =
            makeDictionaryOrHashSet i.range i.returnType "Map" forceFSharpMap i.calleeTypeArgs.Head args
        match i.methodName with
        | ".ctor" ->
            match i.methodArgTypes with
            | [] | [IDictionary] ->
                makeDic false i.args |> Some
            | [IDictionary; IEqualityComparer] ->
                makeDic true [i.args.Head; makeComparer i.args.Tail.Head] |> Some
            | [IEqualityComparer] ->
                makeDic true [Fable.Value Fable.Null; makeComparer i.args.Head] |> Some
            | [Fable.Number _] ->
                makeDic false [] |> Some
            | [Fable.Number _; IEqualityComparer] ->
                makeDic true [Fable.Value Fable.Null; makeComparer i.args.Tail.Head] |> Some
            | _ -> None
        | "isReadOnly" ->
            // TODO: Check for immutable maps with IDictionary interface
            Fable.BoolConst false |> Fable.Value |> Some
        | "count" ->
            makeGet i.range i.returnType i.callee.Value (makeStrConst "size") |> Some
        | "containsValue" ->
            CoreLibCall ("Map", Some "containsValue", false, [i.args.Head; i.callee.Value])
            |> makeCall i.range i.returnType |> Some
        | "item" -> icall i (if i.args.Length = 1 then "get" else "set") |> Some
        | "keys" -> icall i "keys" |> Some
        | "values" -> icall i "values" |> Some
        | "containsKey" -> icall i "has" |> Some
        | "clear" -> icall i "clear" |> Some
        | "add" -> icall i "set" |> Some
        | "remove" -> icall i "delete" |> Some
        | "tryGetValue" ->
            match i.callee, i.args with
            | Some dic, [key; defVal] ->
                CoreLibCall ("Map", Some "tryGetValue", false, [dic; key; defVal])
                |> makeCall i.range i.returnType |> Some
            | _ -> None
        | _ -> None

    let hashSets (com: ICompiler) (i: Fable.ApplyInfo) =
        let makeHashSet forceFSharp args =
            makeDictionaryOrHashSet i.range i.returnType "Set" forceFSharp i.calleeTypeArgs.Head args
        match i.methodName with
        | ".ctor" ->
            let makeComparer (e: Fable.Expr) =
                ccall_ e.Range e.Type "Comparer" "fromEqualityComparer" [e]
            match i.methodArgTypes with
            | [] | [IEnumerable] ->
                makeHashSet false i.args |> Some
            | [IEnumerable; IEqualityComparer] ->
                [i.args.Head; makeComparer i.args.Tail.Head]
                |> makeHashSet true |> Some
            | [IEqualityComparer] ->
                [Fable.Value Fable.Null; makeComparer i.args.Head]
                |> makeHashSet true |> Some
            | _ -> None
        | "count" ->
            makeGet i.range i.returnType i.callee.Value (makeStrConst "size") |> Some
        | "isReadOnly" ->
            Fable.BoolConst false |> Fable.Value |> Some
        | "clear" -> icall i "clear" |> Some
        | "contains" -> icall i "has" |> Some
        | "remove" -> icall i "delete" |> Some
        | "isProperSubsetOf" | "isProperSupersetOf"
        | "add" ->
            CoreLibCall ("Set", Some "addInPlace", false, [i.args.Head;i.callee.Value])
            |> makeCall i.range i.returnType |> Some
        | "unionWith" | "intersectWith" | "exceptWith"
        | "isSubsetOf" | "isSupersetOf" | "copyTo" ->
            let meth =
                let m = match i.methodName with "exceptWith" -> "differenceWith" | m -> m
                m.Replace("With", "InPlace")
            CoreLibCall ("Set", Some meth, false, i.callee.Value::i.args)
            |> makeCall i.range i.returnType |> Some
        // TODO
        // | "setEquals"
        // | "overlaps"
        // | "symmetricExceptWith"
        | _ -> None

    let mapAndSets com (i: Fable.ApplyInfo) =
        let instanceArgs () =
            match i.callee with
            | Some c -> c, i.args
            | None -> List.last i.args, List.take (i.args.Length-1) i.args
        let prop (prop: string) =
            let callee, _ = instanceArgs()
            makeGet i.range i.returnType callee (makeStrConst prop)
        let icall meth =
            let callee, args = instanceArgs()
            InstanceCall (callee, meth, args)
            |> makeCall i.range i.returnType
        let modName =
            if i.ownerFullName.Contains("Map")
            then "Map" else "Set"
        match i.methodName with
        // Instance and static shared methods
        | "count" -> prop "size" |> Some
        | "contains" | "containsKey" -> icall "has" |> Some
        | "add" | "remove" | "isEmpty"
        | "find" | "tryFind" // Map-only
        | "maximumElement" | "minimumElement"
        | "maxElement" | "minElement" -> // Set-only
            let args =
                match i.methodName with
                | "isEmpty" | "maximumElement" | "minimumElement"
                | "maxElement" | "minElement" -> staticArgs i.callee i.args
                | _ -> i.args @ (Option.toList i.callee)
            CoreLibCall(modName, Some i.methodName, false, args)
            |> makeCall i.range i.returnType |> Some
        // Map indexer
        | "item" -> icall "get" |> Some
        // Constructors
        | "empty" -> makeMapOrSetCons com i modName [] |> Some
        | ".ctor" -> makeMapOrSetCons com i modName i.args |> Some
        // Conversions
        | "toArray" -> toArray com i i.args.Head |> Some
        | "toList" -> toList com i i.args.Head |> Some
        | "toSeq" -> Some i.args.Head
        | "ofArray" -> makeMapOrSetCons com i modName i.args |> Some
        | "ofList" | "ofSeq" -> makeMapOrSetCons com i modName i.args |> Some
        // Static methods
        | "exists" | "fold" | "foldBack" | "forAll" | "iterate"
        | "filter" | "map" | "partition"
        | "findKey" | "tryFindKey" | "pick" | "tryPick" -> // Map-only
            CoreLibCall(modName, Some i.methodName, false, i.args)
            |> makeCall i.range i.returnType |> Some
        // Set only static methods
        | "singleton" ->
            [makeArray Fable.Any i.args]
            |> makeMapOrSetCons com i modName |> Some
        | _ -> None

    type CollectionKind =
        | Seq | List | Array

    // Functions which don't return a new collection of the same type
    let implementedSeqNonBuildFunctions =
        set [ "average"; "averageBy"; "compareWith"; "empty";
              "exactlyOne"; "exists"; "exists2"; "fold"; "fold2"; "foldBack"; "foldBack2";
              "forAll"; "forAll2"; "head"; "tryHead"; "item"; "tryItem";
              "iterate"; "iterateIndexed"; "iterate2"; "iterateIndexed2";
              "isEmpty"; "last"; "tryLast"; "length";
              "mapFold"; "mapFoldBack"; "max"; "maxBy"; "min"; "minBy";
              "reduce"; "reduceBack"; "sum"; "sumBy"; "tail"; "toList";
              "tryFind"; "find"; "tryFindIndex"; "findIndex"; "tryPick"; "pick";
              "tryFindBack"; "findBack"; "tryFindIndexBack"; "findIndexBack" ]

    // Functions that must return a collection of the same type
    let implementedSeqBuildFunctions =
        set [ "append"; "choose"; "collect"; "concat"; "countBy"; "distinct"; "distinctBy";
              "except"; "filter"; "where"; "groupBy"; "initialize";
              "map"; "mapIndexed"; "indexed"; "map2"; "mapIndexed2"; "map3";
              "ofArray"; "pairwise"; "permute"; "replicate"; "reverse";
              "scan"; "scanBack"; "singleton"; "skip"; "skipWhile";
              "take"; "takeWhile"; "sortWith"; "unfold"; "zip"; "zip3" ]

    /// Seq functions implemented in other modules to prevent cyclic dependencies
    let seqFunctionsImplementedOutside =
        [ "Map", [ "groupBy"; "countBy" ]
        ; "Set", [ "distinct"; "distinctBy" ] ] |> Map

    let implementedListFunctions =
        set [ "append"; "choose"; "collect"; "concat"; "filter"; "groupBy"; "where";
              "initialize"; "map"; "mapIndexed"; "indexed"; "ofArray"; "partition";
              "replicate"; "reverse"; "singleton"; "unzip"; "unzip3" ]

    let implementedArrayFunctions =
        set [ "chunkBySize"; "copyTo"; "partition"; "permute"; "sortInPlaceBy"; "unzip"; "unzip3" ]

    let nativeArrayFunctions =
        dict [ "exists" => "some"; "filter" => "filter";
               "find" => "find"; "findIndex" => "findIndex"; "forAll" => "every";
               "iterate" => "forEach";
               "reduce" => "reduce"; "reduceBack" => "reduceRight";
               "sortInPlace" => "sort"; "sortInPlaceWith" => "sort" ]

    let collectionsSecondPass (com: ICompiler) (i: Fable.ApplyInfo) kind =
        let prop (meth: string) callee =
            makeGet i.range i.returnType callee (makeStrConst meth)
        let icall meth (callee, args) =
            InstanceCall (callee, meth, args)
            |> makeCall i.range i.returnType
        let ccall modName meth args =
            CoreLibCall (modName, Some meth, false, args)
            |> makeCall i.range i.returnType
        let meth, c, args =
            i.methodName, i.callee, i.args
        match meth with
        // Deal with special cases first
        | "cast" -> Some i.args.Head // Seq only, erase
        | "cache" -> toArray com i args.Head |> Some // Seq only
        | "isEmpty" ->
            match kind with
            | Seq -> ccall "Seq" meth args
            | Array ->
                makeEqOp i.range [prop "length" args.Head; makeIntConst 0] BinaryEqualStrict
            | List ->
                let c, _ = instanceArgs c args
                makeEqOp i.range [prop "tail" c; Fable.Value Fable.Null] BinaryEqual
            |> Some
        | "head" | "tail" | "length" | "count" ->
            let meth = if meth = "count" then "length" else meth
            match kind with
            | Seq ->
                // A static 'length' method causes problems in JavaScript -- https://github.com/Microsoft/TypeScript/issues/442
                let seqMeth = if meth = "length" then "count" else meth
                ccall "Seq" seqMeth (staticArgs c args)
            | List -> let c, _ = instanceArgs c args in prop meth c
            | Array ->
                let c, _ = instanceArgs c args
                if meth = "head" then makeGet i.range i.returnType c (makeIntConst 0)
                elif meth = "tail" then icall "slice" (c, [makeIntConst 1])
                else prop "length" c
            |> Some
        | "item" ->
            match i.callee, kind with
            | Some callee, Array ->
                if i.args.Length = 1
                then makeGet i.range i.returnType callee i.args.Head
                else Fable.Set (i.callee.Value, Some i.args.Head, i.args.Tail.Head, i.range)
            | _, Seq -> ccall "Seq" meth args
            | _, Array -> makeGet i.range i.returnType args.Tail.Head args.Head
            | _, List -> match i.callee with Some x -> i.args@[x] | None -> i.args
                         |> ccall "Seq" meth
            |> Some
        | "sort" | "sortDescending" | "sortBy" | "sortByDescending" ->
            let proyector, args =
                if meth = "sortBy" || meth = "sortByDescending"
                then Some args.Head, args.Tail
                else None, args
            let compareFn =
                let fnArgs = [makeIdent (com.GetUniqueVar()); makeIdent (com.GetUniqueVar())]
                let identValue x =
                    let x = Fable.Value(Fable.IdentValue x)
                    match proyector with
                    | Some proyector -> Fable.Apply(proyector, [x], Fable.ApplyMeth, Fable.Any, None)
                    | None -> x
                let comparison =
                    let comparison = compare com None (List.map identValue fnArgs) None
                    if meth = "sortDescending" || meth = "sortByDescending"
                    then makeUnOp None (Fable.Number Int32) [comparison] UnaryMinus
                    else comparison
                makeLambdaExpr fnArgs comparison
            match c, kind with
            // This is for calls to instance `Sort` member on ResizeArrays
            | Some c, _ ->
                match args with
                | [] -> icall "sort" (c, [compareFn]) |> Some
                | [Type (Fable.Function _)] -> icall "sort" (c, args) |> Some
                | _ -> None
            | None, Seq -> ccall "Seq" "sortWith" (compareFn::args) |> Some
            | None, List -> ccall "Seq" "sortWith" (compareFn::args) |> toList com i |> Some
            | None, Array -> ccall "Seq" "sortWith" (compareFn::args) |> toArray com i |> Some
        | "splitAt" ->
            match kind with
            | Seq -> None
            | List -> ccall "List" meth args |> Some
            | Array -> ccall "Array" meth args |> Some
        // Constructors ('cons' only applies to List)
        | "empty" | "cons" ->
            match kind with
            | Seq -> ccall "Seq" meth args
            | Array ->
                match i.returnType with
                | Fable.Array typ ->
                    Fable.ArrayConst (Fable.ArrayAlloc (makeIntConst 0), typ) |> Fable.Value
                | _ -> "Expecting array type but got " + i.returnType.FullName
                       |> attachRange i.range |> failwith
            | List -> CoreLibCall ("List", None, true, args)
                      |> makeCall i.range i.returnType
            |> Some
        | "zeroCreate" ->
            match genArg i.returnType with
            | Fable.Number _ as t ->
                Fable.ArrayConst(Fable.ArrayAlloc i.args.Head, t)
                |> Fable.Value |> Some
            | Fable.Boolean -> emit i "new Array($0).fill(false)" i.args |> Some
            // If we don't fill the array with null values some operations
            // may behave unexpectedly, like Array.prototype.reduce
            | t -> emit i "new Array($0).fill(null)" i.args |> Some
        | "create" ->
            ccall "Seq" "replicate" args
            |> toArray com i |> Some
        // ResizeArray only
        | ".ctor" ->
            let makeJsArray arVals =
                // Use Any to prevent creation of typed arrays
                let ar = Fable.Value(Fable.ArrayConst(Fable.ArrayValues arVals, Fable.Any))
                Fable.Wrapped(ar, i.returnType) |> Some
            match i.args with
            | [] -> makeJsArray []
            | _ ->
                match i.args.Head with
                | Type(Fable.Number Int32) -> makeJsArray []
                | Fable.Value(Fable.ArrayConst(Fable.ArrayValues arVals, _)) -> makeJsArray arVals
                | _ -> emit i "Array.from($0)" i.args |> Some
        | "find" when Option.isSome c ->
            let defaultValue = defaultof i.calleeTypeArgs.Head
            ccall "Option" "getValue" [
                ccall "Seq" "tryFind" [args.Head;c.Value;defaultValue]
                Fable.Expr.Value (Fable.ValueKind.BoolConst true) ]
            |> Some
        | "findAll" when Option.isSome c ->
            ccall "Seq" "filter" [args.Head;c.Value] |> toArray com i |> Some
        | "findLast" when Option.isSome c ->
            let defaultValue = defaultof i.calleeTypeArgs.Head
            ccall "Option" "getValue" [
                ccall "Seq" "tryFindBack"
                    [args.Head;c.Value;defaultValue];
                Fable.Expr.Value (Fable.ValueKind.BoolConst true) ]
            |> Some
        | "add" ->
            icall "push" (c.Value, args) |> Some
        | "addRange" ->
            ccall "Array" "addRangeInPlace" [args.Head; c.Value] |> Some
        | "clear" ->
            // ICollection.Clear method can be called on IDictionary
            // too so we need to make a runtime check (see #1120)
            ccall "Util" "clear" [c.Value] |> Some
        | "contains" ->
            match c, args with
            | Some c, args ->
                emit i "$0.indexOf($1) > -1" (c::args) |> Some
            | None, [item;xs] ->
                let f =
                    wrapInLambda [makeIdent (com.GetUniqueVar())] (fun exprs ->
                        CoreLibCall("Util", Some "equals", false, item::exprs)
                        |> makeCall None Fable.Boolean)
                ccall "Seq" "exists" [f;xs] |> Some
            | _ -> None
        | "indexOf" ->
            icall "indexOf" (c.Value, args) |> Some
        | "insert" ->
            icall "splice" (c.Value, [args.Head; makeIntConst 0; args.Tail.Head]) |> Some
        | "remove" ->
            ccall "Array" "removeInPlace" [args.Head; c.Value] |> Some
        | "removeRange" ->
            icall "splice" (c.Value, args) |> Some
        | "removeAt" ->
            icall "splice" (c.Value, [args.Head; makeIntConst 1]) |> Some
        | "reverse" when kind = Array ->
            match i.returnType with
            | Fable.Array _ ->
                // Arrays need to be copied before sorted in place.
                emit i "$0.slice().reverse()" i.args |> Some
            | _ ->
                // ResizeArray should be sorted in place without copying.
                icall "reverse" (instanceArgs c i.args) |> Some
        // Conversions
        | "toSeq" | "ofSeq" ->
            match kind with
            | Seq -> "Unexpected method called on seq: " + meth
                     |> attachRange i.range |> failwith
            | List -> ccall "Seq" (if meth = "toSeq" then "ofList" else "toList") args
            | Array ->
                if meth = "toSeq"
                then ccall "Seq" "ofArray" args
                else toArray com i args.Head
            |> Some
        | "toArray" ->
            match c with
            | Some c -> toArray com i c
            | None -> toArray com i i.args.Head
            |> Some
        | "ofList" ->
            match kind with
            | List -> "Unexpected method called on list: " + meth
                      |> attachRange i.range |> failwith
            | Seq -> ccall "Seq" "ofList" args
            | Array -> toArray com i i.args.Head
            |> Some
        | "sum" | "sumBy" ->
            match i.returnType with
            | Fable.Boolean | Fable.Char | Fable.String | Fable.Number _ | Fable.Enum _ ->
                ccall "Seq" meth args |> Some
            | t ->
                let zero = getZero t
                let fargs = [makeTypedIdent (com.GetUniqueVar()) t; makeTypedIdent (com.GetUniqueVar()) t]
                let addFn = wrapInLambda fargs (fun args ->
                    applyOp None Fable.Any args "op_Addition")
                if meth = "sum"
                then addFn, args.Head
                else emitNoInfo "((f,add)=>(x,y)=>add(x,f(y)))($0,$1)" [args.Head;addFn], args.Tail.Head
                |> fun (f, xs) -> ccall "Seq" "fold" [f; zero; xs] |> Some
        | "min" | "minBy" | "max" | "maxBy" ->
            let reduce macro macroArgs xs =
                ccall "Seq" "reduce" [emitNoInfo macro macroArgs; xs] |> Some
            match meth, i.methodTypeArgs with
            // Optimization for numeric types
            | "min", [Fable.Number _] ->
                // Note: if we use just Math.min it fails with typed arrays
                reduce "(x,y) => Math.min(x,y)" [] args.Head
            | "max", [Fable.Number _] ->
                reduce "(x,y) => Math.max(x,y)" [] args.Head
            | "minBy", [_;Fable.Number _] ->
                reduce "(f=>(x,y)=>f(x)<f(y)?x:y)($0)" [args.Head] args.Tail.Head
            | "maxBy", [_;Fable.Number _] ->
                reduce "(f=>(x,y)=>f(x)>f(y)?x:y)($0)" [args.Head] args.Tail.Head
            | _ -> ccall "Seq" meth args |> Some
        // Default to Seq implementation in core lib
        | Patterns.SetContains implementedSeqNonBuildFunctions meth ->
            let args =
                // mapFold & mapFoldBack must be transformed, but only the first member of the tuple result, see #842
                match meth, kind with
                | ("mapFold"|"mapFoldBack"), List -> args@[makeCoreRef "List" "ofArray"]
                | _ -> args
            ccall "Seq" meth args |> Some
        | Patterns.SetContains implementedSeqBuildFunctions meth ->
            let mod_ =
                seqFunctionsImplementedOutside
                |> Map.tryFindKey (fun _ v -> List.contains meth v)
                |> defaultArg <| "Seq"
            match kind with
            | Seq -> ccall mod_ meth args
            | List -> ccall mod_ meth args |> toList com i
            | Array -> ccall mod_ meth args |> toArray com i
            |> Some
        | _ -> None

    let collectionsFirstPass (com: ICompiler) (i: Fable.ApplyInfo) kind =
        let icall meth (callee, args) =
            InstanceCall (callee, meth, args)
            |> makeCall i.range i.returnType
            |> Some
        match kind with
        | List ->
            let listMeth meth args =
                CoreLibCall ("List", Some meth, false, args)
                |> makeCall i.range i.returnType |> Some
            match i.methodName with
            | "getSlice" ->
                listMeth "slice" (i.args@[i.callee.Value])
            | "truncate" ->
                match i.args with
                | [arg1; arg2] ->
                    let arg1 = makeBinOp None (Fable.Number Int32) [arg1; makeIntConst 1] BinaryMinus
                    listMeth "slice" [makeIntConst 0; arg1; arg2]
                | _ -> None
            | Patterns.SetContains implementedListFunctions meth ->
                listMeth meth i.args
            | _ ->
                // match instance methods
                match i.callee with
                | Some instance ->
                    match i.methodName with
                    | "equals" -> icall "Equals" (instance, i.args)
                    | "compareTo" -> icall "CompareTo" (instance, i.args)
                    | "getHashCode" -> (* TODO *) None
                    | _ -> None
                | _ -> None

        | Array ->
            match i.methodName with
            | "get" ->
                match i.callee, i.args with
                | TwoArgs (ar, idx) ->
                    makeGet i.range i.returnType ar idx |> Some
                | _ -> None
            | "set" ->
                match i.callee, i.args with
                | ThreeArgs (ar, idx, value) ->
                    Fable.Set (ar, Some idx, value, i.range) |> Some
                | _ -> None
            | "take" -> icall "slice" (i.args.Tail.Head, [makeIntConst 0; i.args.Head])
            | "skip" -> icall "slice" (i.args.Tail.Head, [i.args.Head])
            | "copy" -> icall "slice" (i.args.Head, [])
            | "getSubArray" | "fill" ->
                ccall i "Array" i.methodName i.args |> Some
            | "truncate" ->
                // Array.truncate count array
                emit i "$1.slice(0, $0)" i.args |> Some
            // JS native Array.map, which accepts a 3-argument function,
            // conflicts with dynamic CurriedLambda. Also, allocating the array
            // in advance seems to be more performant
            | "map" | "mapIndexed"  ->
                let arrayCons =
                    match i.returnType with
                    | Fable.Array(Fable.Number numberKind) when com.Options.typedArrays ->
                        getTypedArrayName com numberKind
                    | _ -> "Array"
                ccall i "Array" i.methodName (i.args @ [makeIdentExpr arrayCons]) |> Some
            | "indexed" ->
                ccall i "Array" i.methodName i.args |> Some
            | "append" ->
                match i.methodTypeArgs with
                | [Fable.Any] | [Number _] -> None
                | _ -> icall "concat" (i.args.Head, i.args.Tail)
            | Patterns.SetContains implementedArrayFunctions meth ->
                CoreLibCall ("Array", Some meth, false, i.args)
                |> makeCall i.range i.returnType |> Some
            | Patterns.DicContains nativeArrayFunctions meth ->
                let revArgs = List.rev i.args
                icall meth (revArgs.Head, (List.rev revArgs.Tail))
            | _ -> None
        | _ -> None
        |> function None -> collectionsSecondPass com i kind | someExpr -> someExpr

    let exceptions com (i: Fable.ApplyInfo) =
        match i.methodName, i.callee with
        // TODO: Check argument number and types?
        | ".ctor", _ ->
            Fable.Apply(makeIdentExpr "Error", i.args, Fable.ApplyCons, i.returnType, i.range) |> Some
        | "message", Some e -> getProp i.range i.returnType e "message" |> Some
        | "stackTrace", Some e -> getProp i.range i.returnType e "stack" |> Some
        | _ -> None

    let cancels com (i: Fable.ApplyInfo) =
        match i.methodName with
        | ".ctor" -> ccall i "Async" "createCancellationToken" i.args |> Some
        | "token" -> i.callee
        | "cancel" | "cancelAfter" | "isCancellationRequested" ->
            match i.callee with Some c -> c::i.args | None -> i.args
            |> ccall i "Async" i.methodName |> Some
        // TODO: Add check so CancellationTokenSource cannot be cancelled after disposed?
        | "dispose" -> Fable.Null |> Fable.Value |> Some
        | _ -> None

    let objects com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "getHashCode" -> ccall i "Util" "hash" [i.callee.Value] |> Some
        | ".ctor" -> Fable.ObjExpr ([], [], None, i.range) |> Some
        | "referenceEquals" -> makeEqOp i.range i.args BinaryEqualStrict |> Some
        | "toString" -> ccall i "Util" "toString" [i.callee.Value] |> Some
        | "equals" -> staticArgs i.callee i.args |> equals true com i
        | "getType" ->
            match i.callee.Value.Type with
            | Fable.Any | Fable.GenericParam _ ->
                sprintf "%s %s"
                    "Cannot resolve .GetType() at compile time."
                    "The type created at runtime won't contain generic information."
                |> addWarning com i.fileName i.range
                CoreLibCall("Reflection", Some "getType", false, [i.callee.Value])
                |> makeCall i.range i.returnType |> Some
            | t ->
                let genInfo = {makeGeneric=true; genericAvailability=false}
                makeTypeRef com genInfo t |> Some
        | _ -> None

    let types com (info: Fable.ApplyInfo) =
        let str x = Fable.Value(Fable.StringConst x)
        let common com (info: Fable.ApplyInfo) =
            match info.methodName with
            | "getTypeInfo" -> info.callee
            | "genericTypeArguments" -> ccall info "Reflection" "getGenericArguments" [info.callee.Value] |> Some
            | _ -> None
        match info.callee with
        | Some(Fable.Value(Fable.TypeRef(ent,_))) ->
            match info.methodName with
            | "namespace" -> str ent.Namespace |> Some
            | "fullName" -> str ent.FullName |> Some
            | "name" -> str ent.Name |> Some
            | "isGenericType" -> ent.GenericParameters.Length > 0 |> makeBoolConst |> Some
            | "getGenericTypeDefinition" -> makeTypeRefFrom com ent |> Some
            | "makeGenericType" ->
                if not <| List.sameLength ent.GenericParameters info.args
                then
                    "Arguments have different length than generic parameters"
                    |> addErrorAndReturnNull com info.fileName info.range |> Some
                else
                    let genArgs2 =
                        List.zip ent.GenericParameters info.args
                        |> makeJsObject None
                    CoreLibCall("Util", Some "makeGeneric", false, [info.callee.Value; genArgs2])
                    |> makeCall None Fable.MetaType |> Some
            | _ -> common com info
        | _ ->
            let getTypeFullName args =
                args |> Option.map (fun args ->
                CoreLibCall("Reflection", Some "getTypeFullName", false, args)
                |> makeCall info.range info.returnType)
            match info.methodName with
            | "fullName" -> Some [info.callee.Value] |> getTypeFullName
            | "name" -> Some [info.callee.Value; str "name"] |> getTypeFullName
            | "namespace" -> Some [info.callee.Value; str "namespace"] |> getTypeFullName
            | "isGenericType" ->
                CoreLibCall("Util", Some "isGeneric", false, [info.callee.Value])
                |> makeCall info.range info.returnType |> Some
            | "getGenericTypeDefinition" ->
                CoreLibCall("Util", Some "getDefinition", false, [info.callee.Value])
                |> makeCall info.range info.returnType |> Some
            | "makeGenericType" ->
                "MakeGenericType won't work if type is not known at compile-time"
                |> addErrorAndReturnNull com info.fileName info.range |> Some
            | _ -> common com info

    let unchecked com (info: Fable.ApplyInfo) =
        match info.methodName with
        | "defaultOf" ->
            defaultof info.methodTypeArgs.Head |> Some
        | "hash" ->
            CoreLibCall("Util", Some "hash", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | "equals" ->
            CoreLibCall("Util", Some "equals", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | "compare" ->
            CoreLibCall("Util", Some "compare", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | _ -> None

    let random com (info: Fable.ApplyInfo) =
        match info.methodName with
        | ".ctor" ->
            let o = Fable.ObjExpr ([], [], None, info.range)
            Fable.Wrapped (o, info.returnType) |> Some
        | "next" ->
            let min, max =
                match info.args with
                | [] -> makeIntConst 0, makeIntConst System.Int32.MaxValue
                | [max] -> makeIntConst 0, max
                | [min; max] -> min, max
                | _ -> failwith "Unexpected arg count for Random.Next"
            ccall info "Util" "randomNext" [min; max] |> Some
        | "nextDouble" ->
            GlobalCall ("Math", Some "random", false, [])
            |> makeCall info.range info.returnType
            |> Some
        | _ -> None

    let enumerable com (info: Fable.ApplyInfo) =
        match info.callee, info.methodName with
        | Some callee, "getEnumerator" ->
            ccall info "Seq" "getEnumerator" [callee] |> Some
        | _ -> None

    let mailbox com (info: Fable.ApplyInfo) =
        match info.callee with
        | None ->
            match info.methodName with
            | ".ctor" -> CoreLibCall("MailboxProcessor", None, true, info.args) |> Some
            | "start" -> CoreLibCall("MailboxProcessor", Some "start", false, info.args) |> Some
            | _ -> None
            |> Option.map (makeCall info.range info.returnType)
        | Some callee ->
            match info.methodName with
            // `reply` belongs to AsyncReplyChannel
            | "start" | "receive" | "postAndAsyncReply" | "post" | "reply" ->
                InstanceCall(callee, info.methodName, info.args)
                |> makeCall info.range info.returnType |> Some
            | _ -> None

    let guids com (info: Fable.ApplyInfo) =
        match info.methodName with
        | "newGuid" ->
            CoreLibCall("String", Some "newGuid", false, [])
            |> makeCall info.range info.returnType |> Some
        | "parse" ->
            ccall info "String" "validateGuid" info.args |> Some
        | "tryParse" ->
            ccall info "String" "validateGuid" [info.args.Head; makeBoolConst true] |> Some
        | "toByteArray" ->
            ccall info "String" "guidToArray" [info.callee.Value] |> Some
        | ".ctor" ->
            match info.args with
            | [] -> Fable.Wrapped(makeStrConst "00000000-0000-0000-0000-000000000000", info.returnType) |> Some
            | [Type (Fable.Array _)] ->
                ccall info "String" "arrayToGuid" info.args |> Some
            | [Type Fable.String as arg] ->
                ccall info "String" "validateGuid" info.args |> Some
            | _ -> None
        | _ -> None

    let uris com (info: Fable.ApplyInfo) =
        match info.methodName with
        | "unescapeDataString" ->
            CoreLibCall("Util", Some "unescapeDataString", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | "escapeDataString" ->
            CoreLibCall("Util", Some "escapeDataString", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | "escapeUriString" ->
            CoreLibCall("Util", Some "escapeUriString", false, info.args)
            |> makeCall info.range info.returnType |> Some
        | _ -> None

    let laziness com (info: Fable.ApplyInfo) =
        let coreCall meth isCons args =
            CoreLibCall("Lazy", meth, isCons, args)
            |> makeCall info.range info.returnType
        match info.methodName with
        | ".ctor" | "create" -> coreCall None true info.args |> Some
        | "createFromValue" -> coreCall (Some info.methodName) false info.args |> Some
        | "force" | "value" | "isValueCreated" ->
            let callee, _ = instanceArgs info.callee info.args
            match info.methodName with
            | "force" -> "value" | another -> another
            |> getProp info.range info.returnType callee |> Some
        | _ -> None

    let controlExtensions com (info: Fable.ApplyInfo) =
        match info.methodName with
        | "addToObservable" -> Some "add"
        | "subscribeToObservable" -> Some "subscribe"
        | _ -> None
        |> Option.map (fun meth ->
            let args = staticArgs info.callee info.args |> List.rev
            CoreLibCall("Observable", Some meth, false, args)
            |> makeCall info.range info.returnType)

    let asyncs com (info: Fable.ApplyInfo) =
        let asyncMeth meth args =
            CoreLibCall("Async", Some meth, false, args)
            |> makeCall info.range info.returnType |> Some
        match info.methodName with
        | "start" ->
            // Just add warning, the replacement will actually happen in coreLibPass
            "Async.Start will behave as StartImmediate"
            |> addWarning com info.fileName info.range
            None
        | "cancellationToken" ->
            // Make sure cancellationToken is called as a function and not a getter
            asyncMeth "cancellationToken" []
        | "catch" ->
            // `catch` cannot be used as a function name in JS
            asyncMeth "catchAsync" info.args
        | _ -> None

    let enums (com: ICompiler) (appInfo: Fable.ApplyInfo) =
        match appInfo.callee, appInfo.methodName, appInfo.args with
        | Some callee, "hasFlag", [arg] ->
            // x.HasFlags(y) => (int x) &&& (int y) <> 0
            makeBinOp appInfo.range (Fable.Number Int32) [callee; arg] BinaryAndBitwise
            |> fun bitwise -> makeEqOp appInfo.range [bitwise; makeIntConst 0] BinaryUnequal
            |> Some
        | _ -> None

    // Initial support, making at least InvariantCulture compile-able
    // to be used System.Double.Parse and System.Single.Parse
    // see https://github.com/fable-compiler/Fable/pull/1197#issuecomment-348034660
    let globalization com (i: Fable.ApplyInfo) =
        match i.methodName with
        | "invariantCulture" ->
            // System.Globalization namespace is not supported by Fable. The value InvariantCulture will be compiled to an empty object literal
            makeJsObject i.range [] |> Some
        | _ -> None

    let bigint (com: ICompiler) (i: Fable.ApplyInfo) =
        match i.callee, i.methodName with
        | Some callee, meth -> icall i meth |> Some
        | None, ".ctor" ->
            match i.args with
            | [Type (Fable.ExtendedNumber (Int64|UInt64))] -> ccall i "BigInt" "fromInt64" i.args
            | [_] -> ccall i "BigInt" "fromInt32" i.args
            | _ ->
                CoreLibCall("BigInt", None, true, i.args)
                |> makeCall i.range i.returnType
            |> Some
        | None, ("zero"|"one"|"two") ->
            makeCoreRef "BigInt" i.methodName |> Some
        | None, ("fromZero"|"fromOne") ->
            let fi = if i.methodName = "fromZero" then "zero" else "one"
            makeCoreRef "BigInt" fi |> Some
        | None, "fromString" ->
            ccall i "BigInt" "parse" i.args |> Some
        | None, meth -> ccall i "BigInt" meth i.args |> Some

    let fsharpType (com: ICompiler) (i: Fable.ApplyInfo) methName =
        let hasInterface ifc (typRef: Fable.Expr) =
            let proto = ccall_ typRef.Range Fable.Any "Reflection" "getPrototypeOfType" [typRef]
            ccall i "Util" "hasInterface" [proto; Fable.StringConst ifc |> Fable.Value]
        match methName with
        | "getRecordFields"
        | "getExceptionFields" ->
            ccall i "Reflection" "getProperties" i.args |> Some
        | "getUnionCases" ->
            ccall i "Reflection" "getUnionCases" i.args |> Some
        | "getTupleElements" ->
            ccall i "Reflection" "getTupleElements" i.args |> Some
        | "getFunctionElements" ->
            ccall i "Reflection" "getFunctionElements" i.args |> Some
        | "isUnion" ->
            hasInterface "FSharpUnion" i.args.Head |> Some
        | "isRecord" ->
            hasInterface "FSharpRecord" i.args.Head |> Some
        | "isExceptionRepresentation" ->
            hasInterface "FSharpException" i.args.Head |> Some
        | "isTuple" ->
            ccall i "Reflection" "isTupleType" i.args |> Some
        | "isFunction" ->
            ccall i "Reflection" "isFunctionType" i.args |> Some
        | _ -> None

    let fsharpValue (com: ICompiler) (i: Fable.ApplyInfo) methName =
        match methName with
        | "getUnionFields" ->
            ccall i "Reflection" "getUnionFields" i.args |> Some
        | "getRecordFields"
        | "getExceptionFields" ->
            ccall i "Reflection" "getPropertyValues" i.args |> Some
        | "getTupleFields" -> // TODO: Check if it's an array first?
            Some i.args.Head
        | "getTupleField" ->
            makeGet i.range i.returnType i.args.Head i.args.Tail.Head |> Some
        | "getRecordField" ->
            match i.args with
            | [record; propInfo] ->
                let prop = makeGet propInfo.Range Fable.String propInfo (Fable.StringConst "name" |> Fable.Value)
                makeGet i.range i.returnType record prop |> Some
            | _ -> None
        | "makeUnion" ->
            ccall i "Reflection" "makeUnion" i.args |> Some
        | "makeRecord" ->
            match i.args with
            | [typ; vals] ->
                let typ = ccall_ typ.Range Fable.MetaType "Util" "getDefinition" [typ]
                let spread = Fable.Spread vals |> Fable.Value
                Fable.Apply(typ, [spread], Fable.ApplyCons, i.returnType, i.range) |> Some
            | _ -> None
        | "makeTuple" ->
            Some i.args.Head
        | _ -> None

    let tryReplace com (info: Fable.ApplyInfo) =
        match info.ownerFullName with
        | Naming.StartsWith fableCore _ -> fableCoreLib com info
        | Naming.EndsWith "Exception" _ -> exceptions com info
        | "System.Object" -> objects com info
        | "System.Timers.ElapsedEventArgs" -> info.callee // only signalTime is available here
        | "System.Char" -> chars com info
        | "System.Enum" -> enums com info
        | "System.String"
        | "Microsoft.FSharp.Core.StringModule" -> strings com info
        | "Microsoft.FSharp.Core.PrintfModule"
        | "Microsoft.FSharp.Core.PrintfFormat" -> fsFormat com info
        | "System.BitConverter" -> bitConvert com info
        | "System.Int32" -> parse com info false
        | "System.Single"
        | "System.Double" -> parse com info true
        | "System.Convert" -> convert com info
        | "System.Console" -> console com info
        | "System.Decimal" -> decimals com info
        | "System.Diagnostics.Debug"
        | "System.Diagnostics.Debugger" -> debug com info
        | "System.DateTime"
        | "System.DateTimeOffset" -> dates com info
        | "System.TimeSpan" -> timeSpans com info
        | "System.Environment" -> systemEnv com info
        | "System.Globalization.CultureInfo" -> globalization com info
        | "System.Action" | "System.Func"
        | "Microsoft.FSharp.Core.FSharpFunc"
        | "Microsoft.FSharp.Core.OptimizedClosures.FSharpFunc" -> funcs com info
        | "System.Random" -> random com info
        | "Microsoft.FSharp.Core.FSharpOption"
        | "Microsoft.FSharp.Core.OptionModule" -> options com info
        | "System.Threading.CancellationToken"
        | "System.Threading.CancellationTokenSource" -> cancels com info
        | "System.Math"
        | "Microsoft.FSharp.Core.Operators"
        | "Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicOperators"
        | "Microsoft.FSharp.Core.ExtraTopLevelOperators" -> operators com info
        | "Microsoft.FSharp.Core.FSharpRef" -> references com info
        | "System.Activator" -> activator com info
        | "Microsoft.FSharp.Core.LanguagePrimitives.ErrorStrings" -> errorStrings com info
        | "Microsoft.FSharp.Core.LanguagePrimitives" -> languagePrimitives com info
        | "Microsoft.FSharp.Core.LanguagePrimitives.IntrinsicFunctions"
        | "Microsoft.FSharp.Core.Operators.OperatorIntrinsics" -> intrinsicFunctions com info
        | "System.Text.RegularExpressions.Capture"
        | "System.Text.RegularExpressions.Match"
        | "System.Text.RegularExpressions.Group"
        | "System.Text.RegularExpressions.MatchCollection"
        | "System.Text.RegularExpressions.GroupCollection"
        | "System.Text.RegularExpressions.Regex" -> regex com info
        | "System.Collections.Generic.IEnumerable"
        | "System.Collections.IEnumerable" -> enumerable com info
        | "System.Collections.Generic.Dictionary"
        | "System.Collections.Generic.IDictionary" -> dictionaries com info
        | "System.Collections.Generic.HashSet"
        | "System.Collections.Generic.ISet" -> hashSets com info
        | "System.Collections.Generic.KeyValuePair" -> keyValuePairs com info
        | "System.Collections.Generic.Dictionary.KeyCollection"
        | "System.Collections.Generic.Dictionary.ValueCollection"
        | "System.Collections.Generic.ICollection" -> collectionsSecondPass com info Seq
        | "System.Array"
        | "System.Collections.Generic.List"
        | "System.Collections.Generic.IList" -> collectionsSecondPass com info Array
        | "Microsoft.FSharp.Collections.ArrayModule" -> collectionsFirstPass com info Array
        | "Microsoft.FSharp.Collections.FSharpList"
        | "Microsoft.FSharp.Collections.ListModule" -> collectionsFirstPass com info List
        | "Microsoft.FSharp.Collections.SeqModule" -> collectionsSecondPass com info Seq
        | "Microsoft.FSharp.Collections.FSharpMap"
        | "Microsoft.FSharp.Collections.MapModule"
        | "Microsoft.FSharp.Collections.FSharpSet"
        | "Microsoft.FSharp.Collections.SetModule" -> mapAndSets com info
        | "System.Type" -> types com info
        | "Microsoft.FSharp.Core.Operators.Unchecked" -> unchecked com info
        | "Microsoft.FSharp.Control.FSharpMailboxProcessor"
        | "Microsoft.FSharp.Control.FSharpAsyncReplyChannel" -> mailbox com info
        | "Microsoft.FSharp.Control.FSharpAsync" -> asyncs com info
        | "System.Guid" -> guids com info
        | "System.Uri" -> uris com info
        | "System.Lazy" | "Microsoft.FSharp.Control.Lazy"
        | "Microsoft.FSharp.Control.LazyExtensions" -> laziness com info
        | "Microsoft.FSharp.Control.CommonExtensions" -> controlExtensions com info
        | "System.Numerics.BigInteger"
        | "Microsoft.FSharp.Core.NumericLiterals.NumericLiteralI" -> bigint com info
        | "Microsoft.FSharp.Reflection.FSharpType" -> fsharpType com info info.methodName
        | "Microsoft.FSharp.Reflection.FSharpValue" -> fsharpValue com info info.methodName
        | "Microsoft.FSharp.Reflection.FSharpReflectionExtensions" ->
            // In netcore F# Reflection methods become extensions
            // with names like `FSharpType.GetExceptionFields.Static`
            let isFSharpType = info.methodName.StartsWith("fSharpType")
            let methName = info.methodName |> Naming.extensionMethodName |> Naming.lowerFirst
            if isFSharpType
            then fsharpType com info methName
            else fsharpValue com info methName
        | "Microsoft.FSharp.Reflection.UnionCaseInfo"
        | "System.Reflection.PropertyInfo"
        | "System.Reflection.MemberInfo" ->
            match info.callee, info.methodName with
            | _, "getFields" -> icall info "getUnionFields" |> Some
            | Some c, "name" -> ccall info "Reflection" "getName" [c] |> Some
            | Some c, ("tag" | "propertyType") ->
                let prop =
                    if info.methodName = "tag" then "index" else info.methodName
                    |> Fable.StringConst |> Fable.Value
                makeGet info.range info.returnType c prop |> Some
            | _ -> None
        | _ -> None

module CoreLibPass =
    open Util

    /// Module methods in the core lib can be bound Static or Both (instance and static).
    /// If they're bound only statically all methods will be called statically: if there's an
    /// instance, it'll be passed as the first argument and constructors will change to `create`.
    type MapKind = Static | Both

    // Attention! If a type is added here, it will likely need
    // to be added to `tryReplaceEntity` below too.
    let mappings =
        dict [
            system + "DateTime" => ("Date", Static)
            system + "DateTimeOffset" => ("DateOffset", Static)
            system + "TimeSpan" => ("TimeSpan", Static)
            system + "Timers.Timer" => ("Timer", Both)
            fsharp + "Control.FSharpAsync" => ("Async", Static)
            fsharp + "Control.FSharpAsyncBuilder" => ("AsyncBuilder", Both)
            fsharp + "Control.ObservableModule" => ("Observable", Static)
            fsharp + "Core.CompilerServices.RuntimeHelpers" => ("Seq", Static)
            system + "String" => ("String", Static)
            fsharp + "Core.StringModule" => ("String", Static)
            system + "Text.RegularExpressions.Regex" => ("RegExp", Static)
            fsharp + "Collections.SeqModule" => ("Seq", Static)
            fsharp + "Collections.FSharpSet" => ("Set", Static)
            fsharp + "Collections.SetModule" => ("Set", Static)
            fsharp + "Core.FSharpChoice" => ("Choice", Both)
            fsharp + "Core.FSharpResult" => ("Result", Both)
            fsharp + "Core.ResultModule" => ("Result", Static)
            fsharp + "Control.FSharpEvent" => ("Event", Both)
            fsharp + "Control.EventModule" => ("Event", Static)
        ]

open Util

let private coreLibPass com (info: Fable.ApplyInfo) =
    match info.ownerFullName with
    | Patterns.DicContains CoreLibPass.mappings (modName, kind) ->
        match kind with
        | CoreLibPass.Both ->
            match info.methodName, info.methodKind, info.callee with
            | ".ctor", _, None | _, Fable.Constructor, None ->
                CoreLibCall(modName, None, true, info.args)
                |> makeCall info.range info.returnType |> Some
            | _, Fable.Getter _, Some callee ->
                let prop = Naming.upperFirst info.methodName |> makeStrConst
                Fable.Apply(callee, [prop], Fable.ApplyGet, info.returnType, info.range) |> Some
            | _, Fable.Setter _, Some callee ->
                let prop = Naming.upperFirst info.methodName |> makeStrConst
                Fable.Set(callee, Some prop, info.args.Head, info.range) |> Some
            | _, _, Some callee ->
                InstanceCall (callee, Naming.upperFirst info.methodName, info.args)
                |> makeCall info.range info.returnType |> Some
            | _, _, None ->
                CoreLibCall(modName, Some info.methodName, false, staticArgs info.callee info.args)
                |> makeCall info.range info.returnType |> Some
        | CoreLibPass.Static ->
            let meth =
                if info.methodName = ".ctor" then "create" else info.methodName
            CoreLibCall(modName, Some meth, false, staticArgs info.callee info.args)
            |> makeCall info.range info.returnType |> Some
    | _ -> None

let tryReplace (com: ICompiler) (info: Fable.ApplyInfo) =
    let info =
        info.methodName |> Naming.removeGetSetPrefix |> Naming.lowerFirst
        |> fun methName -> { info with methodName = methName }
    match AstPass.tryReplace com info with
    | Some _ as res -> res
    | None -> coreLibPass com info

// TODO: We'll probably have to merge this with CoreLibPass.mappings
// Especially if we start making more types from the BCL compatible
let tryReplaceEntity (com: ICompiler) (ent: Fable.Entity) (genArgs: (string*Fable.Expr) list) =
    let makeGeneric genArgs expr =
        match genArgs with
        | [] -> expr
        | genArgs ->
            let genArgs = makeJsObject None genArgs
            CoreLibCall("Util", Some "makeGeneric", false, [expr; genArgs])
            |> makeCall None Fable.Any
    match ent.FullName with
    | "System.Guid" -> Fable.StringConst "string" |> Fable.Value |> Some
    | "System.TimeSpan" -> Fable.StringConst "number" |> Fable.Value |> Some
    | "System.DateTime" -> makeIdentExpr "Date" |> Some
    | "System.DateTimeOffset" as t -> makeNonDeclaredTypeRef (Fable.NonDeclInterface t) |> Some
    | "System.Timers.Timer" -> makeDefaultCoreRef "Timer" |> Some
    | "System.Text.RegularExpressions.Regex" -> makeIdentExpr "RegExp" |> Some
    | "System.Collections.Generic.Dictionary" ->
        makeIdentExpr "Map" |> makeGeneric genArgs |> Some
    | "System.Collections.Generic.HashSet" ->
        makeIdentExpr "Set" |> makeGeneric genArgs |> Some
    | "System.Collections.Generic.KeyValuePair" ->
        match genArgs with
        | [] -> makeIdentExpr "Array" |> Some
        | genArgs ->
            genArgs |> List.map snd
            |> Fable.NonDeclTuple
            |> makeNonDeclaredTypeRef |> Some
    | KeyValue "Microsoft.FSharp.Core.FSharpChoice" "Choice" name
    | KeyValue "Microsoft.FSharp.Core.FSharpResult" "Result" name
    | KeyValue "Microsoft.FSharp.Control.FSharpAsync" "Async" name
    | KeyValue "Microsoft.FSharp.Collections.FSharpSet" "Set" name
    | KeyValue "Microsoft.FSharp.Collections.FSharpMap" "Map" name
    | KeyValue "Microsoft.FSharp.Collections.FSharpList" "List" name ->
        makeDefaultCoreRef name |> makeGeneric genArgs |> Some
    | Naming.EndsWith "Exception" _ ->
        makeIdentExpr "Error" |> Some
    | "Fable.Core.JsInterop.JsConstructor"
    | Naming.StartsWith "Fable.Core.JsInterop.JsFunc" _ ->
        Fable.StringConst "function" |> Fable.Value |> Some
    // Catch-all for unknown references to System and FSharp.Core classes
    | Naming.StartsWith "System." _
    | Naming.StartsWith "Fable.Core." _
    | Naming.StartsWith "Microsoft.FSharp." _ ->
        makeNonDeclaredTypeRef Fable.NonDeclAny |> Some
    | _ -> None

let checkLiteral com fileName range (value: obj) (typ: Fable.Type) =
    match typ with
    | Fable.Enum("System.Text.RegularExpressions.RegexOptions") ->
        match value with
        | :? int as i when i = 0x0001 // "i" - RegexOptions.IgnoreCase
                        || i = 0x0002 // "m" - RegexOptions.Multiline
                        || i = 0x0100 // "e" - RegexOptions.ECMAScript
                        -> ()
        | _ -> addWarning com fileName range "Multiline and IgnoreCase are the only RegexOptions available"
    | _ -> ()
