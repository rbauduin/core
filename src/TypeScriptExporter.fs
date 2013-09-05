﻿// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2013 IntelliFactory
//
// GNU Affero General Public License Usage
// WebSharper is free software: you can redistribute it and/or modify it under
// the terms of the GNU Affero General Public License, version 3, as published
// by the Free Software Foundation.
//
// WebSharper is distributed in the hope that it will be useful, but WITHOUT
// ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or
// FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License
// for more details at <http://www.gnu.org/licenses/>.
//
// If you are unsure which license is appropriate for your use, please contact
// IntelliFactory at http://intellifactory.com/contact.
//
// $end{copyright}

namespace IntelliFactory.WebSharper

module internal TypeScriptExporter =
    open System
    open System.Collections.Generic
    open System.IO
    open System.Text.RegularExpressions
    open IntelliFactory.WebSharper.Compiler
    module CM = Metadata
    module T = TypeScriptGenerator
    module P = IntelliFactory.JavaScript.Packager
    module Re = Reflector
    module V = Validator

    type Context =
        {
            Builder : T.AddressBuilder
            CM : Metadata.T
            GenericDeclaration : option<T.Declaration>
            GenericSignature : option<T.Signature>
            GetContractImplementation : Context -> TypeReference -> T.Contract
            TupledFunctions : Dictionary<int,T.Declaration * T.Interface>
        }

        static member Create(cm, impl) =
            {
                Builder = T.AddressBuilder.Create()
                CM = cm
                GenericDeclaration = None
                GenericSignature = None
                GetContractImplementation = impl
                TupledFunctions = Dictionary()
            }

    let emptyDefs = T.Definitions.Merge []
    let pat = Regex(@"[^\w$]")

    let cleanName (s: string) =
        pat.Replace(s, "_")

    let rec convertAddress ctx (addr: P.Address) =
        match addr with
        | P.Global x -> ctx.Builder.Root(cleanName x)
        | P.Local (x, y) ->
            let x = convertAddress ctx x
            ctx.Builder.Nested(x, cleanName y)

    let getContract ctx tR =
        ctx.GetContractImplementation ctx tR

    let makeGenerics prefix count =
        [ for i in 1 .. count -> prefix + string i ]

    let getTupledFuncDeclaration ctx (args: list<T.Contract>) (ret: T.Contract) =
        let arity = args.Length
        match ctx.TupledFunctions.TryGetValue(arity) with
        | true, (decl, _) -> T.Contract.Named(decl, args @ [ret])
        | _ ->
            let addr = ctx.Builder.Root("IntelliFactory").["WebSharper"].["F" + string arity]
            let decl = T.Declaration.Create(addr, makeGenerics "T" (arity + 1))
            let mutable s1 = T.Signature.Create().WithReturn(decl.[arity])
            let mutable s2 = T.Signature.Create().WithReturn(decl.[arity])
            for i in 1 .. arity do
                s1 <- s1.WithArgument("a" + string i, decl.[i - 1])
            let t =
                T.Interface.Create [
                    for n in 0 .. arity - 1 ->
                        T.Member.NumericProperty(n, decl.[n])
                ]
            s2 <- s2.WithArgument("tuple", T.Contract.Anonymous t)
            let def =
                T.Interface.Create [
                    T.Member.Call s1
                    T.Member.Call s2
                ]
            ctx.TupledFunctions.Add(arity, (decl, def))
            T.Contract.Named(decl, args @ [ret])

    let emitTupledFunctionDefinitions ctx =
        seq {
            for (decl, def) in ctx.TupledFunctions.Values ->
                T.Definitions.Define(decl, def)
        }

    type FType =
        {
            FArgs : list<TypeReference>
            FRet : TypeReference
        }

    let (|UnitType|_|) (tR: TypeReference) =
        if tR.Namespace = "Microsoft.FSharp.Core" && tR.Name = "Unit"
            then Some () else None

    let (|TupleType|_|) (tR: TypeReference) =
        match tR.Namespace, tR.Name with
        | "System", n when n.StartsWith("Tuple") ->
            let ps =
                match tR.Shape with
                | TypeShape.GenericInstanceType args -> args
                | _ -> []
            Some ps
        | _ -> None

    let (|FuncType|_|) ts (tR: TypeReference) =
        match tR.Namespace, tR.Name, ts with
        | "Microsoft.FSharp.Core", "FSharpFunc`2", [d; r] ->
            let fType args r = { FArgs = args; FRet = r }
            match d with
            | UnitType -> fType [] r
            | TupleType ts -> fType ts r
            | _ -> fType [d] r
            |> Some
        | _ -> None

    let getFuncContract ctx fT =
        match fT.FArgs with
        | [] ->
            let call =
                T.Signature.Create().WithReturn(getContract ctx fT.FRet)
                |> T.Member.Call
            T.Interface.Create [call]
            |> T.Contract.Anonymous
        | [x] ->
            let call =
                T.Signature.Create()
                    .WithArgument("x", getContract ctx x)
                    .WithReturn(getContract ctx fT.FRet)
                |> T.Member.Call
            T.Interface.Create [call]
            |> T.Contract.Anonymous
        | _ ->
            let args = List.map (getContract ctx) fT.FArgs
            let ret = getContract ctx fT.FRet
            getTupledFuncDeclaration ctx args ret

    let getNamedContract ctx (tR: TypeReference) ts =
        match ctx.CM.DataType(Adapter.AdaptTypeDefinition(tR)) with
        | Some (CM.DataTypeKind.Class addr)
        | Some (CM.DataTypeKind.Exception addr)
        | Some (CM.DataTypeKind.Record (addr, _)) ->
            let addr = convertAddress ctx addr
            let decl = T.Declaration.Create(addr, makeGenerics "T" tR.GenericArity)
            T.Contract.Named(decl, ts)
        | Some (CM.DataTypeKind.Object fields) -> T.Contract.Any
        | None -> T.Contract.Any

    let rec getContractImpl ctx (tR: TypeReference) =
        let inline getContract ctx tR = getContractImpl ctx tR
        match tR.Shape with
        | TypeShape.ArrayType (1, r) ->
            getContractImpl ctx r
            |> T.Contract.Array
        | TypeShape.ArrayType _ ->
            T.Contract.Any
        | TypeShape.GenericInstanceType ts ->
            match tR with
            | FuncType ts fT ->
                getFuncContract ctx fT
            | _ ->
                getNamedContract ctx tR [for t in ts -> getContract ctx t]
        | TypeShape.GenericParameter (OwnerMethod _, pos) ->
            match ctx.GenericSignature with
            | None -> T.Contract.Any
            | Some m -> T.Contract.Generic(m, pos)
        | TypeShape.GenericParameter (OwnerType _, pos) ->
            match ctx.GenericDeclaration with
            | None -> T.Contract.Any
            | Some d -> T.Contract.Generic(d, pos)
        | TypeShape.OtherType ->
            if tR.IsPrimitive then
                if tR.Name = "Boolean" then
                    T.Contract.Boolean
                else
                    T.Contract.Number
            else
                match tR.FullName with
                | "System.String" -> T.Contract.String
                | "System.Void"
                | "Microsoft.FSharp.Core.Unit" -> T.Contract.Void
                | _ -> getNamedContract ctx tR []

    let getSignature ctx (m: MethodReference) =
        let mutable s = T.Signature.Create(makeGenerics "M" m.GenericArity)
        let ctx = { ctx with GenericSignature = Some s }
        let mutable i = 0
        for p in m.Parameters do
            let name =
                match p.Name with
                | "" | null -> "x" + string i
                | n -> n
            s <- s.WithArgument(name, getContract ctx p.ParameterType)
            i <- i + 1
        match m.ReturnType with
        | None -> s
        | Some rT -> s.WithReturn(getContract ctx rT)

    let convMethod ctx isInterfaceMethod (m: V.Method) =
        let isExported =
            isInterfaceMethod ||
            match m.Kind with
            | V.JavaScriptMethod _ -> true
            | V.MacroMethod (_, x) -> x.Body.IsSome
            | _ ->
                false
        if isExported then
            Some (getSignature ctx m.Definition)
        else
            None

    let exportStaticMethod ctx (m: V.Method) =
        let addr = convertAddress ctx m.Name
        match convMethod ctx false m with
        | None -> []
        | Some s ->
            let c =
                [T.Member.Call s]
                |> T.Interface.Create
                |> T.Contract.Anonymous
            [T.Definitions.Var(addr, c)]

    let exportStaticMethods ctx (t: V.Type) =
        seq {
            for m in t.Methods do
                match m.Scope with
                | MemberScope.Static ->
                    yield! exportStaticMethod ctx m
                | MemberScope.Instance -> ()
        }

    let exportStaticProperties ctx (t: V.Type) =
        seq {
            for p in t.Properties do
                match p.Scope with
                | MemberScope.Static ->
                    match p.Kind with
                    | V.BasicProperty (m1, m2) ->
                        let p m =
                            match m with
                            | None -> []
                            | Some m -> exportStaticMethod ctx m
                        yield! p m1
                        yield! p m2
                    | V.JavaScriptModuleProperty _ ->
                        let addr = convertAddress ctx p.Slot.Address.Address
                        let s =
                            T.Signature.Create().WithReturn(getContract ctx p.PropertyType)
                        let con =
                            [ T.Member.Call s ]
                            |> T.Interface.Create
                        yield T.Definitions.Var(addr, T.Contract.Anonymous con)
                    | _ -> ()
                | _ -> ()
        }

    let exportNamedContract ctx (t: V.Type) isIF (recordProps: list<V.RecordProperty>) =
        let addr = convertAddress ctx t.Name
        let decl = T.Declaration.Create(addr, makeGenerics "T" t.ReflectorType.Definition.GenericArity)
        let ctx = { ctx with GenericDeclaration = Some decl }
        let emitMethod m =
            match convMethod ctx isIF m with
            | None -> []
            | Some s -> [T.Member.Method(m.Name.LocalName, s)]
        let emitMethodOpt m =
            match m with
            | None -> []
            | Some m -> emitMethod m
        let iF =
            [
                for m in t.Methods do
                    match m.Scope with
                    | MemberScope.Instance ->
                        if not (m.Definition.IsGetter || m.Definition.IsSetter) then
                            yield! emitMethod m
                    | MemberScope.Static -> ()
                for p in t.Properties do
                    match p.Scope with
                    | MemberScope.Instance ->
                        match p.Kind with
                        | V.PropertyKind.BasicProperty (gM, sM) ->
                            yield! emitMethodOpt gM
                            yield! emitMethodOpt sM
                        | _ -> ()
                    | _ -> ()
                for p in recordProps do
                    yield T.Member.Property(p.JavaScriptName, getContract ctx p.PropertyType)
            ]
            |> T.Interface.Create
        T.Definitions.Define(decl, iF)

    let rec exportType ctx (t: V.Type) =
        seq {
            yield! exportStaticMethods ctx t
            yield! exportStaticProperties ctx t
            match t.Kind with
            | V.TypeKind.Class (slot, baseType, ctorList, nestedTypes) ->
                match t.Status with
                | V.Status.Compiled ->
                    for t in nestedTypes do
                        yield! exportType ctx t
                    yield exportNamedContract ctx t false []
                | V.Status.Ignored -> ()
            | V.TypeKind.Exception ->
                yield exportNamedContract ctx t false []
            | V.TypeKind.Interface ->
                yield exportNamedContract ctx t true []
            | V.TypeKind.Module nestedTypes ->
                for t in nestedTypes do
                    yield! exportType ctx t
            | V.TypeKind.Record props ->
                yield exportNamedContract ctx t false props
            | V.TypeKind.Resource _ -> ()
            | V.TypeKind.Union _ ->
                yield exportNamedContract ctx t false []
        }

    let ExportDeclarations (cm: CM.T) (v: V.Assembly) =
        let ctx = Context.Create(cm, getContractImpl)
        let defs =
            seq {
                for t in v.Types do
                    yield! exportType ctx t
                yield! emitTupledFunctionDefinitions ctx
            }
            |> T.Definitions.Merge
        let text =
            use w = new StringWriter()
            defs.Write(w)
            w.ToString()
        text


