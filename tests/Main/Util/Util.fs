module Fable.Tests.Util

open System

module Testing =
    #if FABLE_COMPILER
    type Assert = Fable.Core.Testing.Assert
    type TestAttribute = Fable.Core.Testing.TestAttribute
    #else
    type Assert = Xunit.Assert
    type TestAttribute = Xunit.FactAttribute
    #endif
    type TestFixtureAttribute = Fable.Core.Testing.TestFixtureAttribute


open Testing

let equal (expected: 'T) (actual: 'T): unit =
    #if FABLE_COMPILER
    Assert.AreEqual(expected, actual)
    #else
    Assert.Equal<'T>(expected, actual)
    #endif
