﻿// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2016 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}

// Checks RPC signatures for encoder support
module WebSharper.Compiler.Verifier

open WebSharper.Core.AST

open System.Collections.Generic
module J = WebSharper.Core.Json

let (|AsyncOrTask|_|) (t: Type) =
    match t with
    | ConcreteType { Generics = [ x ]; Entity = td } ->
        if td = Definitions.Async || td = Definitions.Task1 then
            Some (Some x)
        elif td = Definitions.Task then
            Some None
        else None        
    | _ ->
        None

type Status =
    | Correct
    | Incorrect of string
    | CriticallyIncorrect of string

[<Sealed>]
type State(jP: J.Provider) =

    let memoize (f: Type -> bool) : Type -> bool =
        let cache = Dictionary()
        fun x ->
            match cache.TryGetValue(x) with
            | true, x -> x
            | _ ->
                cache.[x] <- true
                let v = f x
                cache.[x] <- v
                v

    let canEncodeToJson =
        memoize <| fun t ->
            let t = Reflection.LoadType t
            try
                let enc = jP.GetEncoder(t)
                true
// TODO: check that no other kinds of errors can happen
//            with WebSharper.Core.Json.NoEncoderException _ ->
            with _ ->
                false

    let canDecodeFromJson =
        memoize <| fun t ->
            let t = Reflection.LoadType t
            try
                let enc = jP.GetDecoder(t)
                true
//            with WebSharper.Core.Json.NoDecoderException _ ->
            with _ ->
                false
   
    let getRemoteContractError (td: TypeDefinition) (m: Method) : Status =
        let m = m.Value
        if (Reflection.LoadTypeDefinition td).IsGenericTypeDefinition then
            CriticallyIncorrect "Static remote methods must be defined on non-generic types"
        elif m.Generics > 0 then
            CriticallyIncorrect "Remote methods must not be generic"
        else
            let p =
                m.Parameters
                |> Seq.tryFind (fun p ->
                    not (canDecodeFromJson p))
            match p with
            | Some p ->
                let msg = "Cannot decode a type from JSON: "
                CriticallyIncorrect (msg + p.AssemblyQualifiedName)
            | None ->
                match m.ReturnType with
                | VoidType -> Correct
                | AsyncOrTask t ->
                    match t with
                    | Some t ->
                        if canEncodeToJson t
                            then Correct
                            else CriticallyIncorrect "Cannot encode the return type to JSON"
                    | _ -> Correct
                | t ->
                    if canEncodeToJson t
                        then "Synchronous remote methods are strongly advised against, consider returning an Async<'T>"
                        else "Cannot encode the return type to JSON"
                    |> Incorrect

    let getWebControlError (t: TypeDefinition) : Status =
        if not (canEncodeToJson (ConcreteType (NonGeneric t))) then
            CriticallyIncorrect ("Cannot encode the Web.Control type to JSON: " + t.Value.AssemblyQualifiedName)
        else
            Correct

    member this.VerifyRemoteMethod(t: TypeDefinition, m: Method) =
        getRemoteContractError t m

    member this.VerifyWebControl(t: TypeDefinition) =
        getWebControlError t
