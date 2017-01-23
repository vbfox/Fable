// Use this template to make quick tests when adding new features to Fable.
// You must run a full build at least once (from repo root directory,
// type `sh build.sh` on OSX/Linux or just `build` on Windows). Then:
// - When making changes to Fable.Compiler run `build QuickFableCompilerTest`
// - When making changes to fable-core run `build QuickFableCoreTest`

// Please don't add this file to your commits

#r "../../build/fable-core/Fable.Core.dll"
open System
open Fable.Core
open Fable.Core.JsInterop

type TestAttribute() =
    inherit System.Attribute()

let equal expected actual =
    let areEqual = expected = actual
    printfn "%A = %A > %b" expected actual areEqual
    if not areEqual then
        failwithf "Expected %A but got %A" expected actual

// Write here the code you want to test,
// you can later put the code in a unit test.

let rec private factorialAux num i f =
    let res =
        if i = num then f
        else factorialAux num (i+1I) (f + (i+1I))
    // printf "."
    res

let factorial num =
    factorialAux num 0I 1I

factorial 1000000I
|> printfn "%O"

let rec sum v m s =
    if v >= m
    then s
    else sum (v + 1L) m (s + v)

[<Test>]
let ``Tailcall optimization works with arguments consumed after being passed``() =
    sum 0L 10000L 0L |> equal 49995000L


let rec parseNum tokens acc = function
  | x::xs when x >= '0' && x <= '9' ->
      parseNum tokens (x::acc) xs
  | xs -> parseTokens ((List.rev acc)::tokens) xs

and parseTokens tokens = function
  | x::xs when x >= '0' && x <= '9' ->
      parseNum tokens [x] xs
  | x::xs -> parseTokens tokens xs
  | [] -> List.rev tokens

// Example:
// Seq.except [2] [1; 3; 2] |> Seq.last |> equal 3
// Seq.except [2] [2; 4; 6] |> Seq.head |> equal 4
