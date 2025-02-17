﻿(*
Copyright 2011 Stephen Swensen

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*)
module Swensen.FsEye.WatchModel
open System
open System.Reflection
open Microsoft.FSharp.Reflection
open Swensen.Utils


///let l be the lazy:'a arg, returns Some(l.Value) if l.IsValueCreated, else returns None.
let (|CreatedValue|_|) (l:'a Lazy) =
    if l.IsValueCreated then Some(l.Value)
    else None

//how to add icons to tree view: http://msdn.microsoft.com/en-us/library/aa983725(v=vs.71).aspx

type Root = { 
    Text: string
    Children:seq<Watch>
    ValueInfo:ValueInfo
    Name: String
    ExpressionInfo:ExpressionInfo }

and Organizer = { 
    OrganizerKind: OrganizerKind
    Children:seq<Watch> }

and OrganizerKind = 
    | Rest
    | NonPublic

and EnumeratorElement = { 
    Text:string
    Children:seq<Watch>
    ValueInfo:ValueInfo 
    ExpressionInfo:ExpressionInfo }

and DataMember = { 
    LoadingText: string
    LazyMemberValue: Lazy<MemberValue>
    Image:ImageResource
    MemberInfo:MemberInfo 
    ExpressionInfo:ExpressionInfo }

and CallMember = { 
    InitialText: string
    LoadingText: string
    LazyMemberValue: Lazy<MemberValue>
    Image:ImageResource
    MemberInfo:MemberInfo 
    ExpressionInfo:ExpressionInfo }

and MemberValue = { 
    LoadedText: string
    Children:seq<Watch>
    ValueInfo: ValueInfo option }

and ValueInfo = { 
    Text: string
    Value: obj
    Type : Type }

and ExpressionInfo = { 
    Expression: string
    IsPublic: bool
    ExplicitInterfaceName: string option }

and Watch =
    | Root              of Root
    | DataMember        of DataMember
    | CallMember        of CallMember
    | Organizer         of Organizer
    | EnumeratorElement of EnumeratorElement
    ///Get the "default text" of this Watch (i.e. the text which is displayed when a watch node is first created which may or may not be updated later).
    member this.DefaultText =
        match this with
        | Root {Text=text} 
        | DataMember {LoadingText=text}
        | CallMember {InitialText=text} 
        | EnumeratorElement {Text=text}-> text
        | Organizer {OrganizerKind=Rest} -> "Rest"
        | Organizer {OrganizerKind=NonPublic} -> "Non-public"
    ///Get the children of this Watch. If the children are taken from a Lazy property,
    ///evaluation is forced.
    member this.Children =
        match this with
        | Root {Children=children} 
        | Organizer {Children=children} 
        | EnumeratorElement {Children=children} -> children
        | CallMember {LazyMemberValue=l} 
        | DataMember {LazyMemberValue=l} -> l.Value.Children
    ///Returns the value info for this watch if a) the watch is fully initialized (it is not forced), b) it is even supported
    member this.ValueInfo =
        match this with
        | Root {ValueInfo=vi} 
        | EnumeratorElement {ValueInfo=vi}-> Some(vi)
        | DataMember {LazyMemberValue=CreatedValue({ValueInfo=optionalVi})} 
        | CallMember {LazyMemberValue=CreatedValue({ValueInfo=optionalVi})} -> optionalVi
        | _ -> None
    member this.Image =
        match this with
        | DataMember {Image=image}       
        | CallMember {Image=image} -> image
        | _ -> ImageResource.Default
    member this.MemberInfo =
        match this with
        | DataMember { MemberInfo=mi } 
        | CallMember { MemberInfo=mi } -> Some(mi)
        | _ -> None
    member this.ExpressionInfo =
        match this with
        | Root {ExpressionInfo=ei}  
        | EnumeratorElement {ExpressionInfo=ei}
        | CallMember {ExpressionInfo=ei}
        | DataMember {ExpressionInfo=ei} -> Some(ei)
        | Organizer _ -> None

open System.Text.RegularExpressions
///Sprint the given value with the given Type. Precondition: Type cannot be null.
let private sprintValue (value:obj) (ty:Type) =
    if ty =& null then
        nullArg "ty cannot be null"

    //remove white space since e.g. sprint "%A" uses a lot of whitespace for deeply nested records and DUs.
    let cleanString str = Regex.Replace(str, @"[\t\r\n]", "", RegexOptions.Compiled)

    match value with
    | null when ty.IsGenericType && ty.GetGenericTypeDefinition() = typedefof<option<_>> -> "None"
    | null -> "null"
    | _ -> 
        if typeof<System.Type>.IsAssignableFrom(ty) then
            sprintf "typeof<%s>" (value :?> Type).Name //.FSharpName
        else
            sprintf "%A" value |> cleanString

///Create lazy seq of children s for a typical valued 
let rec createChildren ownerValue (ownerTy:Type) =
    if ownerValue =& null then Seq.empty
    else
        let getMembers bindingFlags =
            let allMembers = seq {
                ///yield all ownerTy members
                yield! ownerTy.GetMembers(bindingFlags)
            
                let mapMembers tys = 
                    tys 
                    |> Seq.map (fun (ty:Type) -> ty.GetMembers(bindingFlags))
                    |> Seq.concat
                //yield all ownerTy interface members
                yield! ownerTy.GetInterfaces() |> mapMembers
                //yield all ownerTy base type (recursive) members
                yield! 
                    ownerTy 
                    |> Seq.unfold(fun ty -> 
                        let bty = ty.BaseType
                        if bty = null then None else Some(bty, bty))
                    |> mapMembers }

            let isDebuggerBrowserNeverAttribute (x:obj) =
                match x with
                | :? System.Diagnostics.DebuggerBrowsableAttribute as db -> db.State = System.Diagnostics.DebuggerBrowsableState.Never
                | _ -> false

            let validMemberTypes =
                allMembers
                |> Seq.filter (fun mi ->
                    match mi with
                    | :? PropertyInfo as pi -> 
                        pi.CanRead && //issue 19
                        pi.GetIndexParameters() = Array.empty &&
                        pi.GetCustomAttributes(false) |> Array.exists isDebuggerBrowserNeverAttribute |> not
                    | :? MethodInfo as meth -> //a simple method taking no arguments and returning a value
                        meth.GetParameters() = Array.empty && 
                        meth.ReturnType <> typeof<System.Void> && 
                        meth.ReturnType <> typeof<unit> &&
                        meth.ContainsGenericParameters |> not &&
                        meth.Name.StartsWith("get_") |> not //F# does not mark properties as having a "Special Name", so need to filter by prefix
                    | :? FieldInfo as fi -> 
                        fi.GetCustomAttributes(false) |> Array.exists isDebuggerBrowserNeverAttribute |> not
                    | _ -> false)

            let nonRedundantMembers =
                validMemberTypes
                //|> Seq.distinctByResolve
                |> Seq.distinctByResolve
                    (fun mi -> mi.Name.ToLower())
                    (fun mi1 mi2 -> 
                        let ty1, ty2 = mi1.DeclaringType, mi2.DeclaringType
                        if ty1.IsAssignableFrom(ty2) then -1
                        elif ty2.IsAssignableFrom(ty1) then 1
                        else 0)

            let sortedMembers =
                nonRedundantMembers
                |> Seq.sortBy (fun mi -> mi.Name.ToLower())

            sortedMembers

        let createResultWatches (value:System.Collections.IEnumerator) = 
            let createChild index value =
                //Would like to be able to always get the type
                //but if is non-Custom IEnumerable, then can't
                let ty = if value =& null then typeof<obj> else value.GetType()
                let valueText = sprintValue value ty
                let expression = sprintf "[%i]" index
                //let text = sprintf "%s : %s = %s" expression ty.FSharpName valueText
                let text = sprintf "%s : %s = %s" expression ty.Name valueText
                let children = createChildren value ty
                EnumeratorElement { Text=text
                                    Children=children
                                    ValueInfo={Text=valueText; Value=value; Type=ty} 
                                    ExpressionInfo={Expression=expression; ExplicitInterfaceName=None; IsPublic=true}}
            
            //yield 100  chunks
            let rec calcRest pos (ie:System.Collections.IEnumerator) = seq {
                if ie.MoveNext() then
                    let nextResult = createChild pos ie.Current
                    if pos % 100 = 0 && pos <> 0 then
                        let rest = seq { yield nextResult; yield! calcRest (pos+1) ie }
                        yield Organizer { OrganizerKind=Rest
                                          Children=rest }
                    else
                        yield nextResult;
                        yield! calcRest (pos+1) ie }
            
            //must cache the enumerator is created outside of the seq
            seq { yield! calcRest 0 value } |> Seq.cache //should use "use" when getting enumerator?

        //if member is inherited from base type or explicit interface, fully qualify
        let getMemberName (mi:Reflection.MemberInfo) =
            if mi.ReflectedType <> ownerTy then
                //mi.ReflectedType.FSharpName + "." + mi.Name
                mi.ReflectedType.Name + "." + mi.Name
            else
                mi.Name

        let getMemberExpressionInfo (mi:Reflection.MemberInfo) =
            let explicitInterfaceName =
                if mi.ReflectedType.IsInterface then
                    //Some(mi.ReflectedType.FSharpName)
                    Some(mi.ReflectedType.Name)
                else
                    None

            let expression =
                if mi :? MethodInfo then
                    sprintf "%s()" mi.Name
                else
                    mi.Name
            
            let isPublic =
                match mi with
                | :? FieldInfo as fi -> fi.IsPublic
                | :? MethodInfo as mi -> mi.IsPublic
                | :? PropertyInfo as pi -> pi.GetGetMethod(true).IsPublic
                | _ -> false //shouldn't be possible: we should log this, but not fail hard.

            { Expression=expression; ExplicitInterfaceName=explicitInterfaceName; IsPublic=isPublic }

        let makeMemberValue (value:obj) valueTy pretext =
            if typeof<System.Collections.IEnumerator>.IsAssignableFrom(valueTy) then
                //{ MemberValue.LoadedText=pretext valueTy.FSharpName ""
                { MemberValue.LoadedText=pretext valueTy.Name ""
                  Children=(createResultWatches (value :?> System.Collections.IEnumerator))
                  ValueInfo=None }
            else
                let valueText = sprintValue value valueTy
                //{ LoadedText=pretext valueTy.FSharpName (" = " + valueText)
                { LoadedText=pretext valueTy.Name (" = " + valueText)
                  Children=(createChildren value valueTy)
                  ValueInfo=Some({Text=valueText; Value=value; Type=valueTy}) }

        let loadingText = " = Loading..."

        ///Create a Watch node from the given PropertyInfo. Precondition: PropertyInfo.CanRead is true.
        let getPropertyWatch (pi:PropertyInfo) =
            let pretext = sprintf "%s : %s%s" (getMemberName pi)
            let delayed = lazy(
                let value, valueTy =
                    try
                        let value = pi.GetValue(ownerValue, Array.empty)
                        value, if value <>& null then value.GetType() else pi.PropertyType //use the actual type if we can
                    with e ->
                        box e, e.GetType()
                makeMemberValue value valueTy pretext)

            let image =
                let meth = pi.GetGetMethod(true)
                if meth.IsPublic then
                    if meth.IsFinal then
                        ImageResource.SealedProperty
                    else
                        ImageResource.Property
                elif meth.IsFamily || meth.IsFamilyAndAssembly || meth.IsFamilyOrAssembly then
                    ImageResource.ProtectedProperty
                else //assume private
                    ImageResource.PrivateProperty

            //DataMember { LoadingText=(pretext pi.PropertyType.FSharpName loadingText)
            DataMember { LoadingText=(pretext pi.PropertyType.Name loadingText)
                         LazyMemberValue=delayed
                         Image=image
                         MemberInfo=pi 
                         ExpressionInfo= getMemberExpressionInfo pi }

        let getFieldWatch (fi:FieldInfo) =
            let pretext = sprintf "%s : %s%s" (getMemberName fi)
            let delayed = lazy(
                let value, valueTy = 
                    try 
                        let value = fi.GetValue(ownerValue)
                        value, if value <>& null then value.GetType() else fi.FieldType //use the actual type if we can
                    with e ->
                        box e, e.GetType()
                makeMemberValue value valueTy pretext)

            let image =
                if fi.IsPublic then
                    ImageResource.Field //I don't think there is a such a thing as "sealed" fields
                elif fi.IsFamily || fi.IsFamilyAndAssembly || fi.IsFamilyOrAssembly then
                    ImageResource.ProtectedField
                else //assume private
                    ImageResource.PrivateField

            //DataMember { LoadingText=pretext fi.FieldType.FSharpName loadingText
            DataMember { LoadingText=pretext fi.FieldType.Name loadingText
                         LazyMemberValue=delayed
                         Image=image
                         MemberInfo=fi 
                         ExpressionInfo= getMemberExpressionInfo fi }

        let getMethodWatch (mi:MethodInfo) =
            let pretext = sprintf "%s() : %s%s" (getMemberName mi)
            let delayed = lazy(
                let value, valueTy =
                    try
                        let value = mi.Invoke(ownerValue, Array.empty)
                        value, if value <>& null then value.GetType() else mi.ReturnType //use the actual type if we can
                    with e ->
                        box e, e.GetType()
                makeMemberValue value valueTy pretext)

            let image =
                if mi.IsPublic then
                    if mi.IsFinal then
                        ImageResource.SealedMethod
                    else
                        ImageResource.Method
                elif mi.IsFamily || mi.IsFamilyAndAssembly || mi.IsFamilyOrAssembly then
                    ImageResource.ProtectedMethod
                else //assume private
                    ImageResource.PrivateMethod

            CallMember { InitialText=pretext mi.ReturnType.Name ""
            //CallMember { InitialText=pretext mi.ReturnType.FSharpName ""
                         LoadingText=pretext mi.ReturnType.Name loadingText
                         //LoadingText=pretext mi.ReturnType.FSharpName loadingText
                         LazyMemberValue=delayed
                         Image=image
                         MemberInfo=mi 
                         ExpressionInfo= getMemberExpressionInfo mi}

        let getMemberWatches bindingFlags = seq {
            let members = getMembers bindingFlags
            for m in members do
                match m with
                | :? PropertyInfo as x -> yield getPropertyWatch x
                | :? FieldInfo as x -> yield getFieldWatch x
                | :? MethodInfo as x -> yield getMethodWatch x 
                | _ -> () }

        let publicBindingFlags = BindingFlags.Instance ||| BindingFlags.Public
        let nonPublicBindingFlags = BindingFlags.Instance ||| BindingFlags.NonPublic

        seq {
            let nonPublicMemberWatches = getMemberWatches nonPublicBindingFlags
            yield Organizer { OrganizerKind=NonPublic
                              Children=nonPublicMemberWatches }
            yield! getMemberWatches publicBindingFlags
        }
///Create a watch root. If value is not null, then value.GetType() is used as the watch Type instead of
///ty. Else if ty is not null ty is used. Else typeof<obj> is used.
let createRootWatch (name:string) (value:obj) (ty:Type) = 
    let ty =
        if value <> null then value.GetType()
        elif ty <> null then ty
        else typeof<obj>

    let valueText = sprintValue value ty
    //let text = sprintf "%s : %s = %s" name ty.FSharpName valueText
    let text = sprintf "%s : %s = %s" name ty.Name valueText
    let children = createChildren value ty
    Root { Text=text
           Children=children
           ValueInfo={Text=valueText; Value=value; Type=ty}
           Name=name 
           ExpressionInfo= {Expression=name; IsPublic=true; ExplicitInterfaceName=None}}