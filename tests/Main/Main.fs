﻿module Fable.Tests.Main

// This is necessary to make webpack collect all test files

#if FABLE_COMPILER
open Fable.Core.JsInterop
importSideEffects "./js/polyfill"
importSideEffects "./ApplicativeTests.fs"
(*
importSideEffects "./ArithmeticTests.fs"
importSideEffects "./ArrayTests.fs"
importSideEffects "./AsyncTests.fs"
importSideEffects "./CharTests.fs"
importSideEffects "./ComparisonTests.fs"
importSideEffects "./ConvertTests.fs"
importSideEffects "./DateTimeTests.fs"
importSideEffects "./DateTimeOffsetTests.fs"
importSideEffects "./DictionaryTests.fs"
importSideEffects "./EnumerableTests.fs"
importSideEffects "./EnumTests.fs"
importSideEffects "./EventTests.fs"
importSideEffects "./HashSetTests.fs"
importSideEffects "./ImportTests.fs"
importSideEffects "./JsInteropTests.fs"
importSideEffects "./JsonTests.fs"
importSideEffects "./ListTests.fs"
importSideEffects "./MapTests.fs"
importSideEffects "./MiscTests.fs"
importSideEffects "./ObservableTests.fs"
importSideEffects "./OptionTests.fs"
importSideEffects "./RecordTypeTests.fs"
importSideEffects "./ReflectionTests.fs"
importSideEffects "./RegexTests.fs"
importSideEffects "./ResizeArrayTests.fs"
importSideEffects "./ResultTests.fs"
importSideEffects "./SeqExpressionTests.fs"
importSideEffects "./SeqTests.fs"
importSideEffects "./SetTests.fs"
importSideEffects "./StringTests.fs"*)
importSideEffects "./SudokuTest.fs"(*
importSideEffects "./TailCallTests.fs"
importSideEffects "./TupleTypeTests.fs"
importSideEffects "./TypeTests.fs"
importSideEffects "./UnionTypeTests.fs"
importSideEffects "./ElmishParserTests.fs"*)
#endif
