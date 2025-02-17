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
module WatchTreeViewTests

open Swensen.Unquote
open Xunit

open Swensen.FsEye.WatchModel
open Swensen.FsEye.Forms

open Swensen.Utils
open System.Windows.Forms

[<Fact>]
let ``calling Watch with new name adds a node`` () =
    let tree = new WatchTreeView()
    tree.Watch("w1", 1)
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.Find("w1", false) <> null @>

[<Fact>]
let ``calling Watch two times with different names creates two nodes`` () =
    let tree = new WatchTreeView()
    tree.Watch("w1", 1)
    tree.Watch("w2", 2)
    
    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.Find("w1", false).Length = 1 @>
    test <@ tree.Nodes.Find("w2", false).Length = 1 @>

let asRoot (tn:TreeNode) = 
    match tn.Tag with
    | :? Watch as w -> 
        match w with
        | Root(info) -> info
        | _ -> failwith "Watch is not a Root"
    | _ -> failwith "TreeNode is not a Watch"

[<Fact>]
let ``calling Watch with an existing name and different value replaces previous watch node`` () =
    let tree = new WatchTreeView()
    tree.Watch("w1", 1)
    tree.Watch("w1", 2)
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.Find("w1", false).Length = 1 @>
    test <@ ((tree.Nodes.Find("w1", false).[0] |> asRoot).ValueInfo.Value :?> int) = 2 @>

[<Fact>]
let ``calling Watch with an existing name and same reference does nothing`` () =
    let tree = new WatchTreeView()
    let value = "hello"
    tree.Watch("w1", value)
    tree.Watch("w1", value)
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.Find("w1", false).Length = 1 @>
    test <@ ((tree.Nodes.Find("w1", false).[0] |> asRoot).ValueInfo.Value :?> string) =& value @>

[<Fact>]
let ``create empty Archive with explicit label`` () =
    let tree = new WatchTreeView()
    let label = "Archive with label"
    tree.Archive(label)
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.[0].Text = label @>

[<Fact>]
let ``create mutliple empty Archives with same explicit label`` () =
    let tree = new WatchTreeView()
    let label = "Archive with label"
    tree.Archive(label)
    tree.Archive(label)
    
    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.[0].Text = label @>
    test <@ tree.Nodes.[1].Text = label @>

[<Fact>]
let ``create empty archives with auto increment default labels`` () =
    let tree = new WatchTreeView()
    tree.Archive()
    tree.Archive()
    
    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.[0].Text = "Archive (0)" @>
    test <@ tree.Nodes.[1].Text = "Archive (1)" @>

[<Fact>]
let ``Archive two watches removes watches from root and puts them under new Archive node`` () =
    let tree = new WatchTreeView()
    let w1, w2 = "w1", "w2"
    tree.Watch(w1, 1)
    tree.Watch(w2, 2)
    let label = "Archive with label"
    tree.Archive(label)
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.[0].Nodes.Count = 2 @>
    test <@ (tree.Nodes.[0].Nodes.[0] |> asRoot).Name = "w1" @>
    test <@ (tree.Nodes.[0].Nodes.[1] |> asRoot).Name = "w2" @>


[<Fact>]
let ``Archive does not archive archives`` () =
    let tree = new WatchTreeView()
    let w1, w2, w3 = "w1", "w2", "w3"
    tree.Watch(w1, 1)
    tree.Watch(w2, 2)
    tree.Archive()
    tree.Watch(w3, 3)
    tree.Archive()

    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.[0].Text = "Archive (0)" @>
    test <@ tree.Nodes.[1].Text = "Archive (1)" @>

///watch, watch, archive, watch, archive, watch, watch
let mockForClear() =
    let tree = new WatchTreeView()
    let w1, w2, w3, w4, w5 = "w1", "w2", "w3", "w4", "w5"
    tree.Watch(w1, 1)
    tree.Watch(w2, 2)
    tree.Archive()
    tree.Watch(w3, 3)
    tree.Archive()
    tree.Watch(w4, 4)
    tree.Watch(w5, 5)
    tree

[<Fact>]
let ``ClearArchives clears Archives but not Watches`` () =
    let tree = mockForClear()
    tree.ClearArchives()

    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.[0].Tag :? Watch @>
    test <@ tree.Nodes.[1].Tag :? Watch @>

[<Fact>]
let ``ClearWatches clears Watches but not Archives`` () =
    let tree = mockForClear()
    tree.ClearWatches()

    test <@ tree.Nodes.Count = 2 @>
    test <@ tree.Nodes.[0].Text = "Archive (0)" @>
    test <@ tree.Nodes.[1].Text = "Archive (1)" @>

[<Fact>]
let ``ClearAll clears all watches and archives`` () =
    let tree = mockForClear()
    tree.ClearAll()

    test <@ tree.Nodes.Count = 0 @>

[<Fact>]
let ``ClearAll resets archive count for default archive label`` () =
    let tree = new WatchTreeView()
    tree.Archive()
    tree.Archive()
    tree.Archive()
    tree.ClearAll()
    tree.Archive()
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.[0].Text = "Archive (0)" @>

[<Fact>]
let ``ClearArchives resets archive count for default archive label`` () =
    let tree = new WatchTreeView()
    tree.Archive()
    tree.Archive()
    tree.Archive()
    tree.ClearArchives()
    tree.Archive()
    
    test <@ tree.Nodes.Count = 1 @>
    test <@ tree.Nodes.[0].Text = "Archive (0)" @>

let dummyNodeText = "dummy"

[<Fact>]
let ``new Watch initially adds dummy child node for lazy loading`` () =
    let tree = new WatchTreeView()
    tree.Watch("watch", 1)

    test <@ tree.Nodes.[0].Nodes.Count = 1 @>
    test <@ tree.Nodes.[0].Nodes.[0].Text = dummyNodeText @>

//open ImpromptuInterface.FSharp
open FSharp.Interop.Dynamic

[<Fact>]
let ``after expanded, dummy watch child replaced with real children`` () =
    let tree = new WatchTreeView()
    tree.Watch("watch", [1;2;3;4;5])

    tree?OnAfterSelect(new TreeViewEventArgs(tree.Nodes.[0]))
    tree?OnAfterExpand(new TreeViewEventArgs(tree.Nodes.[0]))
    
    test <@ tree.Nodes.[0].Nodes.Count >= 1 @>
    test <@ tree.Nodes.[0].Nodes.[0].Text <> dummyNodeText @>
    test <@ tree.Nodes.[0].Nodes |> Seq.cast<TreeNode> |> Seq.forall (fun tn -> tn.Tag :? Watch) @>

