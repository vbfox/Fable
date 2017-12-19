[<Util.Testing.TestFixture>]
module Fable.Tests.Applicative
open System
open System.Collections.Generic
open Util.Testing
open Fable.Tests.Util

#if FABLE_COMPILER
module FullApplication =
    open Fable.Core

    let private SomeFunction (s: string): unit = failwith "Never called"

    [<Emit("$0.name")>]
    let private GetName (f: obj) = failwith "JS only"

    [<Test>]
    let ``Functions passed as parameters don't generate intermediate annonymous functions`` () =
        let name = GetName SomeFunction
        equal "SomeFunction" name

    type WithMembers() =
        member this.SomeMember() = failwith "Never called"

    [<Test>]
    let ``Members passed as parameters don't generate intermediate annonymous functions`` () =
        let inst = WithMembers()
        let name = GetName inst.SomeMember
        equal "SomeFunction" name

#endif