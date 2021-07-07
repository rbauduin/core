// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2018 IntelliFactory
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

namespace WebSharper.Compiler

open System
open System.Collections.Generic
open System.Collections.Concurrent
open System.IO
open System.Reflection
open System.Runtime.Loader

[<AutoOpen>]
module Implemetnation =

    let isCompatible (ref: AssemblyName) (def: AssemblyName) =
        ref.Name = def.Name && (ref.Version = null || def.Version = null || ref.Version = def.Version)

    let tryFindAssembly (dom: AppDomain) (name: AssemblyName) =
        dom.GetAssemblies()
        |> Seq.tryFind (fun a ->
            a.GetName()
            |> isCompatible name)

    let loadInto (baseDir: string) (dom: AppDomain) (path: string) =
        File.ReadAllBytes path
        |> dom.Load

    type AssemblyResolution =
        {
            Cache : ConcurrentDictionary<string, option<Assembly>>
            ResolvePath : AssemblyName -> option<string>
        }

        member r.ResolveAssembly(bD: string, dom: AppDomain, loadContext: option<AssemblyLoadContext>, asmNameOrPath: string) =
            let resolve (x: string) =
                if x.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase) || x.EndsWith(".exe", StringComparison.InvariantCultureIgnoreCase) then
                    let p = Path.GetFullPath x
                    let asm =
                        match loadContext with
                        | Some alc ->
                                printfn "LoadFromAssemblyPath: %s" p
                                alc.LoadFromAssemblyPath(p)
                        | None ->
                                printfn "AppDomain.Load: %s" p
                                loadInto bD dom p
                    for ref in asm.GetReferencedAssemblies() do
                        printfn "Assembly load reference : %s -> %s" x ref.FullName
                        try r.ResolveAssembly(bD, dom, loadContext, ref.FullName) |> ignore
                        with _ -> ()
                    Some asm
                else
                    let name = AssemblyName(x)
                    match tryFindAssembly dom name with
                    | None ->
                        match r.ResolvePath name with
                        | None -> None
                        | Some p -> 
                            let asm =
                                match loadContext with
                                | Some alc ->
                                     printfn "LoadFromAssemblyPath: %s" p
                                     alc.LoadFromAssemblyPath(p)
                                | None ->
                                     printfn "AppDomain.Load: %s" p
                                     loadInto bD dom p
                            for ref in asm.GetReferencedAssemblies() do
                                printfn "Assembly load reference : %s -> %s" x ref.FullName
                                try r.ResolveAssembly(bD, dom, loadContext, ref.FullName) |> ignore
                                with _ -> ()
                            Some asm
                    | r -> r

            r.Cache.GetOrAdd(asmNameOrPath, valueFactory = Func<_,_>(resolve))

    let combine a b =
        {
            Cache = ConcurrentDictionary()
            ResolvePath = fun name ->
                match a.ResolvePath name with
                | None -> b.ResolvePath name
                | r -> r
        }

    let first xs =
        xs
        |> Seq.tryFind (fun x -> true)

    let isMatchingFile name path =
        let f = FileInfo path
        if f.Exists then
            let n = AssemblyName.GetAssemblyName f.FullName
            isCompatible n name
        else false

    let searchPaths (paths: seq<string>) =
        let paths =
            paths
            |> Seq.map Path.GetFullPath
            |> Seq.toArray
        {
            Cache = ConcurrentDictionary()
            ResolvePath = fun name ->
                seq {
                    for path in paths do
                        for ext in [".dll"; ".exe"] do
                            if String.Equals(Path.GetFileName(path), name.Name + ext, StringComparison.OrdinalIgnoreCase) then
                                if isMatchingFile name path then
                                    yield path
                }
                |> first
        }

    let searchDirs (dirs: seq<string>) =
        let dirs =
            dirs
            |> Seq.map Path.GetFullPath
            |> Seq.toArray
        {
            Cache = ConcurrentDictionary()
            ResolvePath = fun name ->
                seq {
                    for dir in dirs do
                        for ext in [".dll"; ".exe"] do
                            let p = Path.Combine(dir, name.Name + ext)
                            if isMatchingFile name p then
                                yield p
                }
                |> first
        }

    //let memoize f =
    //    let cache = ConcurrentDictionary()
    //    fun x -> cache.GetOrAdd(x, valueFactory = Func<_,_>(fun _ -> f x))

    //let memoizeResolution (r: AssemblyResolution) =
    //    let key (n: AssemblyName) = (n.Name, string n.Version)
    //    { ResolvePath = memoize key r.ResolvePath }

    let zero =
        { Cache = ConcurrentDictionary(); ResolvePath = fun name -> None }

    let inline ( ++ ) a b = combine a b

type MyAssemblyLoadContext(baseDir: string, dom: AppDomain, reso: AssemblyResolution) =
    inherit AssemblyLoadContext()

    override this.Load(asmName) =
        match reso.ResolveAssembly(baseDir, dom, Some (this :> AssemblyLoadContext), asmName.FullName) with
        | None -> null
        | Some r -> r

/// An utility for resolving assemblies from non-standard contexts.
[<Sealed>]
type AssemblyResolver(baseDir: string, dom: AppDomain, reso: AssemblyResolution) =

    let loadContext =
        //// hack to create a .NET 5 AssemblyLoadContext if we are on .NET 5
        //let ctor = typeof<AssemblyLoadContext>.GetConstructor([| typeof<string>; typeof<bool> |])
        //if isNull ctor then
        //    None
        //else
        //    Some (ctor.Invoke([| null; true |]) :?> AssemblyLoadContext)
        MyAssemblyLoadContext(baseDir, dom, reso) :> AssemblyLoadContext
        |> Some

    let unload() =
        let meth = typeof<AssemblyLoadContext>.GetMethod("Unload")
        meth.Invoke(loadContext, [||]) |> ignore

    //let resolve (x: obj) (a: ResolveEventArgs) =
    //    let name = AssemblyName(a.Name)
    //    match reso.ResolveAssembly(baseDir, dom, loadContext, name) with
    //    | None -> null
    //    | Some r -> r

    //let handler = ResolveEventHandler(resolve)

    member r.Install() =
        //dom.add_AssemblyResolve(handler)
        let resolve x =
            match reso.ResolveAssembly(baseDir, dom, loadContext, x) with
            | None -> null
            | Some r -> r
        WebSharper.Core.Reflection.OverrideAssemblyResolve <- 
            Some resolve

    member r.Remove() =
        WebSharper.Core.Reflection.OverrideAssemblyResolve <- None
        //dom.remove_AssemblyResolve(handler)
        //unload()

    member r.Wrap(action: unit -> 'T) =
        try
            r.Install()
            action ()
        finally
            r.Remove()

    member r.SearchDirectories ds = AssemblyResolver(baseDir, dom, reso ++ searchDirs ds)
    member r.SearchPaths ps = AssemblyResolver(baseDir, dom, reso ++ searchPaths ps)
    member r.ResolvePath name = reso.ResolvePath name
    member r.WithBaseDirectory bD = AssemblyResolver(Path.GetFullPath bD, dom, reso)

    static member Create(?domain) =
        let dom = defaultArg domain AppDomain.CurrentDomain
        AssemblyResolver(dom.BaseDirectory, dom, zero)
