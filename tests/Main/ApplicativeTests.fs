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

    [<Test>]
    let ``Special functions are ok`` () =
        ignore (GetName id)
        ignore (GetName sqrt)
        ignore (GetName not)
        ignore (GetName abs)

    [<Emit("($1 == null)")>]
    let isJsNull (x: 'a): bool = failwith "JS only"

    [<Test>]
    let ``Erasing return still possible`` () =
        let getAnswer x = 42
        let erased = fun x -> getAnswer x |> ignore
        let result = erased 420
        equal true (unbox result = null)
#endif