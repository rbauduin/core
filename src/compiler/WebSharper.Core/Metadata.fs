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

// WebSharper metadata contains all translated code in AST form and all information
// about needed for translating dependent libraries.
module WebSharper.Core.Metadata

open System.Collections.Generic
open System.Runtime.InteropServices
open WebSharper.Core.AST
open WebSharper.Constants

type RemotingKind =
    | RemoteAsync
    | RemoteTask
    | RemoteSend
    | RemoteSync

type MethodHandle =
    {
        Assembly : string
        Path : string
        SignatureHash : int
    }
    member this.Pack() =
        this.Assembly + ":" + this.Path + ":" + string this.SignatureHash

    static member Unpack(s: string) =
        try
            let p = s.Split(':')
            { Assembly = p.[0]; Path = p.[1]; SignatureHash = int p.[2] }
        with _ ->
            failwith "Failed to deserialize method handle"

[<RequireQualifiedAccess>]
type ParameterObject =
    | Null
    | Bool   of bool
    | SByte  of sbyte
    | Int16  of int16
    | Int32  of int32
    | Int64  of int64
    | Byte   of byte
    | UInt16 of uint16
    | UInt32 of uint32
    | UInt64 of uint64
    | Single of single
    | Double of double
    | Char   of char
    | String of string
    | Type   of Type 
    | Array  of ParameterObject[]

    static member OfObj(o: obj) =
        match o with
        | null -> Null
        | :? bool   as v -> Bool   v
        | :? sbyte  as v -> SByte  v
        | :? int16  as v -> Int16  v
        | :? int32  as v -> Int32  v
        | :? int64  as v -> Int64  v
        | :? byte   as v -> Byte   v
        | :? uint16 as v -> UInt16 v
        | :? uint32 as v -> UInt32 v
        | :? uint64 as v -> UInt64 v
        | :? single as v -> Single v
        | :? double as v -> Double v
        | :? char   as v -> Char   v
        | :? string as v -> String v
        | :? Type   as v -> Type v
        | :? System.Type as v -> Type (Reflection.ReadType v)
        | :? (obj[]) as a -> a |> Array.map ParameterObject.OfObj |> Array
        | _ -> failwithf "Invalid type for macro/generator parameter object: %s" (o.GetType().FullName)

    static member ToObj(o: ParameterObject) =
        match o with
        | Null   -> null
        | Bool   x -> box x 
        | SByte  x -> box x 
        | Int16  x -> box x 
        | Int32  x -> box x 
        | Int64  x -> box x 
        | Byte   x -> box x 
        | UInt16 x -> box x 
        | UInt32 x -> box x 
        | UInt64 x -> box x 
        | Single x -> box x 
        | Double x -> box x 
        | Char   x -> box x 
        | String x -> box x 
        | Type   x -> box x
        | Array  a -> box (a |> Array.map ParameterObject.ToObj)

type CompiledMember =
    | Instance of name:string * kind: ClassMethodKind
    | Static of name:string * kind: ClassMethodKind
    | Func of name:string
    | GlobalFunc of address: Address
    | New
    | NewIndexed of index:int
    | Inline of isCompiled:bool * assertReturnType:bool
    | Macro of macroType:TypeDefinition * parameters:option<ParameterObject> * fallback:option<CompiledMember> 
    | Remote of kind:RemotingKind * handle:MethodHandle * remotingProvider:option<TypeDefinition * option<ParameterObject>>

type CompiledField =
    | InstanceField of name:string
    | OptionalField of name:string
    | StaticField of name:string
    | IndexedField of index:int

type Optimizations =
    {
        IsPure : bool
        FuncArgs : option<list<FuncArgOptimization>>
        Warn : option<string>
    }

    static member None =
        {
            IsPure = false
            FuncArgs = None
            Warn = None
        }
    
    member this.IsNone =
        not this.IsPure && Option.isNone this.FuncArgs && Option.isNone this.Warn

    member this.Purity = if this.IsPure then Pure else NonPure

type GenericParam = 
    {
        Type : option<TSType>
        Constraints : list<Type>
    }

    static member None =
        {
            Type = None
            Constraints = []
        }

type ClassInfo =
    {
        BaseClass : option<Concrete<TypeDefinition>>
        Implements : list<Concrete<TypeDefinition>>
        Generics : list<GenericParam>
        Constructors : IDictionary<Constructor, CompiledMember * Optimizations * Expression>
        Fields : IDictionary<string, CompiledField * bool * Type>
        StaticConstructor : option<Statement>
        Methods : IDictionary<Method, CompiledMember * Optimizations * list<GenericParam> * Expression>
        QuotedArgMethods : IDictionary<Method, int[]>
        Implementations : IDictionary<TypeDefinition * Method, CompiledMember * Expression>
        HasWSPrototype : bool // do we need to output a class
        IsStub : bool // is the class just a declaration
        Macros : list<TypeDefinition * option<ParameterObject>>
        Type : option<TSType>
    }

    static member None =
        {
            BaseClass = None
            Implements = []
            Generics = []
            Constructors = dict []
            Fields = dict []
            StaticConstructor = None
            Methods = dict []
            QuotedArgMethods = dict []
            Implementations = dict []
            HasWSPrototype = false
            IsStub = true
            Macros = []
            Type = None
        }
        
type IClassInfo =
    abstract member Address : Address
    abstract member BaseClass : option<Concrete<TypeDefinition>>
    abstract member Implements : list<Concrete<TypeDefinition>>
    abstract member Constructors : IDictionary<Constructor, CompiledMember>
    /// value: field info, is readonly
    abstract member Fields : IDictionary<string, CompiledField * bool * Type>
    abstract member HasStaticConstructor : bool
    abstract member Methods : IDictionary<Method, CompiledMember>
    abstract member Implementations : IDictionary<TypeDefinition * Method, CompiledMember>
    abstract member HasWSPrototype : bool
    abstract member Macros : list<TypeDefinition * option<ParameterObject>>

type InterfaceInfo =
    {
        Address : Address
        Extends : list<Concrete<TypeDefinition>>
        Methods : IDictionary<Method, string * ClassMethodKind * list<GenericParam>>
        Generics : list<GenericParam>
        Type : option<TSType>
    }

type DelegateInfo =
    {
        DelegateArgs : list<Type * option<Literal>>
        ReturnType : Type
    }

type UnionCaseFieldInfo =
    {
        Name : string
        UnionFieldType : Type
        DateTimeFormat : option<string>    
    }  

type FSharpUnionCaseKind =
    | NormalFSharpUnionCase of list<UnionCaseFieldInfo> 
    | ConstantFSharpUnionCase of Literal 
    | SingletonFSharpUnionCase
    
    member this.IsConstant =
        match this with
        | ConstantFSharpUnionCase _ -> true
        | _ -> false

type FSharpUnionCaseInfo =
    {
        Name : string
        JsonName : option<string>
        Kind : FSharpUnionCaseKind
        StaticIs : bool
    }

type FSharpUnionInfo =
    {
        Cases : list<FSharpUnionCaseInfo>
        NamedUnionCases : option<option<string>>
        HasNull : bool
    }

type FSharpRecordFieldInfo =
    {
        Name : string
        JSName : string
        RecordFieldType : Type
        DateTimeFormat : option<string>    
        Optional : bool
        IsMutable : bool
    }

type CustomTypeInfo =
    | DelegateInfo of DelegateInfo
    | FSharpRecordInfo of list<FSharpRecordFieldInfo>
    | FSharpUnionInfo of FSharpUnionInfo
    | FSharpUnionCaseInfo of FSharpUnionCaseInfo
    | NotCustomType
    | EnumInfo of TypeDefinition
    | StructInfo
    | FSharpAnonRecordInfo of list<string>

type Node =
    | MethodNode of TypeDefinition * Method
    | ConstructorNode of TypeDefinition * Constructor
    | ImplementationNode of typ: TypeDefinition * baseTyp: TypeDefinition * Method
    | AbstractMethodNode of TypeDefinition * Method
    | TypeNode of TypeDefinition
    | ResourceNode of TypeDefinition * option<ParameterObject>
    | AssemblyNode of string * hasJs:bool * isModule:bool
    | EntryPointNode 
    | ExtraBundleEntryPointNode of string * string

type GraphData =
    {
        Nodes : Node[]
        Edges : int[][]
        Overrides : (int * (int * int)[])[]
    }

    static member Empty =
        {
            Nodes = [||]
            Edges = [||]
            Overrides = [||]
        }

type MetadataEntry =
    | StringEntry of string
    | TypeEntry of Type
    | TypeDefinitionEntry of TypeDefinition
    | MethodEntry of Method
    | ConstructorEntry of Constructor
    | CompositeEntry of list<MetadataEntry>

type ExtraBundle =
    {
        AssemblyName : string
        BundleName : string
    }

    member this.FileName =
        this.AssemblyName + "." + this.BundleName + ".js"

    member this.MinifiedFileName =
        this.AssemblyName + "." + this.BundleName + ".min.js"

type Info =
    {
        SiteletDefinition: option<TypeDefinition>
        Dependencies : GraphData
        Interfaces : IDictionary<TypeDefinition, InterfaceInfo>
        Classes : IDictionary<TypeDefinition, Address * CustomTypeInfo * option<ClassInfo>>
        MacroEntries : IDictionary<MetadataEntry, list<MetadataEntry>>
        Quotations : IDictionary<SourcePos, TypeDefinition * Method * list<string>>
        ResourceHashes : IDictionary<string, int>
        ExtraBundles : Set<ExtraBundle>
    }

    static member Empty =
        {
            SiteletDefinition = None
            Dependencies = GraphData.Empty
            Interfaces = Map.empty
            Classes = Map.empty
            MacroEntries = Map.empty
            Quotations = Map.empty
            ResourceHashes = Map.empty
            ExtraBundles = Set.empty
        }

    static member UnionWithoutDependencies (metas: seq<Info>) = 
        let isStaticPart (c: ClassInfo) =
            Option.isNone c.BaseClass
            && Dict.isEmpty c.Constructors
            && Dict.isEmpty c.Fields
            && not c.HasWSPrototype
            && Dict.isEmpty c.Implementations
            && List.isEmpty c.Macros
            && Option.isNone c.StaticConstructor
            && c.Methods.Values |> Seq.forall (function | Instance _,_,_,_ -> false | _ -> true)

        let tryMergeClassInfo (a: ClassInfo) (b: ClassInfo) =
            let combine (left: 'a option) (right: 'a option) =
                match left with
                | Some _ -> left
                | None -> right
            if isStaticPart a || isStaticPart b then
                Some {
                    BaseClass = combine a.BaseClass b.BaseClass
                    Implements = Seq.distinct (Seq.append a.Implements b.Implements) |> List.ofSeq
                    Generics = a.Generics
                    Constructors = Dict.union [a.Constructors; b.Constructors]
                    Fields = Dict.union [a.Fields; b.Fields]
                    HasWSPrototype = a.HasWSPrototype || b.HasWSPrototype
                    Implementations = Dict.union [a.Implementations; b.Implementations]
                    Macros = List.concat [a.Macros; b.Macros]
                    Methods = Dict.union [a.Methods; b.Methods]
                    QuotedArgMethods = Dict.union [a.QuotedArgMethods; b.QuotedArgMethods]
                    IsStub = a.IsStub && b.IsStub
                    StaticConstructor = combine a.StaticConstructor b.StaticConstructor
                    Type = combine a.Type b.Type
                }
            else
                None

        let unionMerge (dicts:seq<IDictionary<TypeDefinition,Address*CustomTypeInfo*option<ClassInfo>>>) =
            let result = Dictionary() :> IDictionary<TypeDefinition,_>
            for dict in dicts do
                for cls in dict do
                    result.TryGetValue cls.Key
                    |> function
                        | false, _ -> result.Add cls
                        | true, (rAddr, rCt, rCl) ->
                            let (addr, ct, cl) = cls.Value
                            let newAddr = rAddr
                            let newCt =
                                match ct, rCt with
                                | NotCustomType, ct | ct, NotCustomType -> ct
                                | ct, rCt -> if ct = rCt then ct else failwithf "Different values found for the same key: %A" cls.Key
                            let newCls =
                                match cl, rCl with
                                | Some cl, Some rCl ->
                                    match tryMergeClassInfo rCl cl with
                                    | Some mergedInfo -> Some mergedInfo
                                    | None -> failwithf "Error merging class info on key: %A" cls.Key
                                | Some cls, None | None, Some cls -> Some cls
                                | None, None -> None
                            result.[cls.Key] <- (newAddr, newCt, newCls)
            result

        let metas = Array.ofSeq metas
        {
            SiteletDefinition = metas |> Seq.tryPick (fun m -> m.SiteletDefinition)
            Dependencies = GraphData.Empty
            Interfaces = Dict.union (metas |> Seq.map (fun m -> m.Interfaces))
            Classes = unionMerge (metas |> Seq.map (fun m -> m.Classes))
            MacroEntries = Dict.unionAppend (metas |> Seq.map (fun m -> m.MacroEntries))
            Quotations = 
                try
                    Dict.union (metas |> Seq.map (fun m -> m.Quotations))
                with Dict.UnionError key ->
                    let pos = key :?> SourcePos
                    failwithf "Quoted expression found at the same position in two files with the same name: %s %d:%d-%d:%d"
                        pos.FileName (fst pos.Start) (snd pos.Start) (fst pos.End) (snd pos.End)
            ResourceHashes = Dict.union (metas |> Seq.map (fun m -> m.ResourceHashes))
            ExtraBundles = Set.unionMany (metas |> Seq.map (fun m -> m.ExtraBundles))
        }

    member this.ClassInfo(td) =
        let _, _, c = this.Classes.[td]
        c.Value

    member this.ClassInfos =
        this.Classes
        |> Seq.choose (function
            | KeyValue(_, (_, _, Some cls)) -> Some cls
            | _ -> None
        )

    member this.MapClasses(f, ?fEp) =
        { this with
            Classes =
                this.Classes |> Dict.map (fun (addr, ct, ci as t) ->
                    match ci with
                    | None -> t
                    | Some ci -> addr, ct, Some (f ci)
                )
        }

    member this.DiscardExpressions() =
        this.MapClasses((fun ci ->
            { ci with
                Constructors = ci.Constructors |> Dict.map (fun (a, b, _) -> a, b, Undefined)
                StaticConstructor = ci.StaticConstructor |> Option.map (fun _ -> Empty)
                Methods = ci.Methods |> Dict.map (fun (a, b, c, _) -> a, b, c, Undefined)
                Implementations = ci.Implementations |> Dict.map (fun (a, _) -> a, Undefined)
            }), (fun _ -> Empty))

    member this.DiscardInlineExpressions() =
        let rec discardInline i e =
            match i with
            | Inline _ -> Undefined
            | Macro (_, _, Some f) -> discardInline f e
            | _ -> e
        this.MapClasses(fun ci ->
            { ci with
                Constructors = ci.Constructors |> Dict.map (fun (i, p, e) -> i, p, e |> discardInline i)
                Methods = ci.Methods |> Dict.map (fun (i, p, c, e) -> i, p, c, e |> discardInline i)
            })

    member this.DiscardNotInlineExpressions() =
        let rec discardNotInline i e =
            match i with
            | Inline _ -> e
            | Macro (_, _, Some f) -> discardNotInline f e
            | _ -> Undefined
        this.MapClasses((fun ci ->
            { ci with
                Constructors = ci.Constructors |> Dict.map (fun (i, p, e) -> i, p, e |> discardNotInline i)
                Methods = ci.Methods |> Dict.map (fun (i, p, c, e) -> i, p, c, e |> discardNotInline i)
            }), (fun _ -> Empty))

    member this.IsEmpty =
        this.Classes.Count = 0 &&
        this.Interfaces.Count = 0 &&
        this.MacroEntries.Count = 0 &&
        this.SiteletDefinition.IsNone

type MetadataOptions =
    | FullMetadata
    | DiscardExpressions
    | DiscardInlineExpressions
    | DiscardNotInlineExpressions

let ApplyMetadataOptions options (m: Info) =
    match options with
    | FullMetadata -> m
    | DiscardExpressions -> m.DiscardExpressions() 
    | DiscardInlineExpressions -> m.DiscardInlineExpressions()
    | DiscardNotInlineExpressions -> m.DiscardNotInlineExpressions()

module internal Utilities = 
 
    let ignoreMacro m =
        match m with
        | Macro (_, _, Some f) -> f
        | _ -> m

    type RemoteMethods = IDictionary<MethodHandle, TypeDefinition * Method>

    let getRemoteMethods meta =
        let remotes = Dictionary()
        for KeyValue(cDef, (_, _, c)) in meta.Classes do
            c |> Option.iter (fun c ->
            for KeyValue(mDef, (m, _, _, _)) in c.Methods do
                match ignoreMacro m with
                | Remote (_, handle, _) ->
                    remotes.Add(handle, (cDef, mDef))
                | _ -> ()
            )
        remotes :> RemoteMethods            

let UnionCaseConstructMethod (td: TypeDefinition) (uc: FSharpUnionCaseInfo) =
    Method {
        MethodName = 
            match uc.Kind with 
            | NormalFSharpUnionCase (_ :: _) -> "New" + uc.Name
            | _ -> "get_" + uc.Name
        Parameters =
            match uc.Kind with 
            | NormalFSharpUnionCase cs -> cs |> List.map (fun c -> c.UnionFieldType)
            | _ -> []
        ReturnType = DefaultGenericType td
        Generics = 0       
    }

let RecordFieldGetter (f: FSharpRecordFieldInfo) =
    Method {
        MethodName = "get_" + f.Name
        Parameters = []
        ReturnType = f.RecordFieldType
        Generics = 0       
    }

type ICompilation =
    abstract GetCustomTypeInfo : TypeDefinition -> CustomTypeInfo
    abstract GetInterfaceInfo : TypeDefinition -> option<InterfaceInfo>
    abstract GetClassInfo : TypeDefinition -> option<IClassInfo>
    abstract GetQuotation : SourcePos -> option<TypeDefinition * Method * list<string>>
    abstract GetTypeAttributes : TypeDefinition -> option<list<TypeDefinition * ParameterObject[]>>
    abstract GetFieldAttributes : TypeDefinition * string -> option<list<TypeDefinition * ParameterObject[]>>
    abstract GetMethodAttributes : TypeDefinition * Method -> option<list<TypeDefinition * ParameterObject[]>>
    abstract GetConstructorAttributes : TypeDefinition * Constructor -> option<list<TypeDefinition * ParameterObject[]>>
    abstract GetJavaScriptClasses : unit -> list<TypeDefinition>
    abstract GetTSTypeOf : Type * ?context: list<GenericParam> -> TSType
    abstract ParseJSInline : string * list<Expression> * [<OptionalArgument; DefaultParameterValue null>] position: SourcePos * [<OptionalArgument; DefaultParameterValue null>] dollarVars: string[] -> Expression
    abstract NewGenerated : string list * ?generics: int * ?args: list<Type> * ?returns: Type -> TypeDefinition * Method * Address
    abstract AddGeneratedCode : Method * Expression -> unit
    abstract AddGeneratedInline : Method * Expression -> unit
    abstract AssemblyName : string with get
    abstract GetMetadataEntries : MetadataEntry -> list<MetadataEntry>
    abstract AddMetadataEntry : MetadataEntry * MetadataEntry -> unit
    abstract AddError : option<SourcePos> * string -> unit 
    abstract AddWarning : option<SourcePos> * string -> unit 
    abstract AddBundle : name: string * entryPoint: Statement * [<OptionalArgument; DefaultParameterValue false>] includeJsExports: bool -> ExtraBundle
    abstract AddJSImport : export: option<string> * from: string -> Expression 
              
module IO =
    module B = Binary

    let MetadataEncoding =
        try
            let eP = B.EncodingProvider.Create()
            eP.DeriveEncoding typeof<Info>
        with B.NoEncodingException t ->
            failwithf "Failed to create binary encoder for type %s" t.FullName

    let CurrentVersion = "6.1 ts-output"

    let Decode (stream: System.IO.Stream) = MetadataEncoding.Decode(stream, CurrentVersion) :?> Info   
    let Encode stream (comp: Info) = MetadataEncoding.Encode(stream, comp, CurrentVersion)

    let LoadRuntimeMetadata(a: System.Reflection.Assembly) =
        if Array.exists ((=) EMBEDDED_RUNTIME_METADATA) (a.GetManifestResourceNames()) then
            use s = a.GetManifestResourceStream EMBEDDED_RUNTIME_METADATA
            try
                Some (Decode s)
            with e ->
                failwithf "Failed to load metadata for: %s. Error: %s" a.FullName e.Message
        else
            None
