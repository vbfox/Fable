module Fable.Tests.Main

// This is necessary to make webpack collect all test files

#if FABLE_COMPILER
open Fable.Core.JsInterop
importSideEffects "./ApplicativeTests.fs"
importSideEffects "./SudokuTest.fs"
#endif
