[<Util.Testing.TestFixture>]
module Fable.Tests.Applicative
open System
open System.Collections.Generic
open Util.Testing
open Fable.Tests.Util

#if FABLE_COMPILER
module FullApplication =
    open Fable.Core

    let private SomeFunction (s: string) = failwith "Never called"

    [<Emit("$0.name")>]
    let private GetName (f: obj) = failwith "JS only"

    [<Test>]
    let ``Functions passed as parameters don't generate intermediate annonymous functions`` () =
        let name = GetName SomeFunction
        equal "SomeFunction" name

#endif