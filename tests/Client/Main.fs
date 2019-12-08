// $begin{copyright}
//
// This file is part of Bolero
//
// Copyright (c) 2018 IntelliFactory and contributors
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

module Bolero.Test.Client.Main

open Microsoft.AspNetCore.Components.Routing
open Microsoft.JSInterop
open Elmish
open Bolero
open Bolero.Html

type Page =
    | [<EndPoint "/">] Form
    | [<EndPoint "/collection">] Collection
    | [<EndPoint "/collection-item/{key}">] Item of key: int * model: PageModel<int>
    | [<EndPoint "/lazy">] Lazy

type Item =
    {
        K: int
        V: string
    }

type LazyModel =
    {
        value: int
        guid: System.Guid
    }
with
    static member Default = { LazyModel.value = 0; guid = System.Guid.NewGuid(); }

type Model =
    {
        input: string
        submitted: option<string>
        addKey: int
        revOrder: bool
        items: Map<int, string>
        remoteResult: option<string>
        radioItem: option<int>
        page: Page
        nonLazyValue: int
        lazyModel: LazyModel
        lazyModels: LazyModel list
    }

type Message =
    | SetInput of text: string
    | Submit
    | RemoveItem of key: int
    | SetAddKey of key: int
    | SetKeyOf of key: int
    | AddKey
    | SetRevOrder of rev: bool
    | SetRadioItem of int
    | SetPage of Page
    | IncNonLazyVal
    | IncLazyVal
    | InsertLazyModelAt of int
    | ShuffleLazy

let initModel _ =
    {
        input = ""
        submitted = None
        addKey = 4
        revOrder = false
        items = Map [
            0, "it's 0"
            1, "it's 1"
            2, "it's 2"
            3, "it's 3"
        ]
        radioItem = None
        page = Form
        remoteResult = None
        nonLazyValue = 0
        lazyModel = LazyModel.Default
        lazyModels = [for _ in [1..5] -> LazyModel.Default ]
    }

let defaultPageModel = function
    | Form | Collection | Lazy -> ()
    | Item (_, m) -> Router.definePageModel m 10
let router = Router.inferWithModel SetPage (fun m -> m.page) defaultPageModel

type MyRemoting =
    {
        greet: string -> Async<string>
    }

// shuffle an array (in-place)
let shuffle a =
    let rand = new System.Random()
    let swap (a: _[]) x y =
        let tmp = a.[x]
        a.[x] <- a.[y]
        a.[y] <- tmp
    Array.iteri (fun i _ -> swap a i (rand.Next(i, Array.length a))) a
    a

let update message model =
    match message with
    | SetInput text -> { model with input = text }, []
    | Submit -> { model with submitted = Some model.input }, []
    | RemoveItem k -> { model with items = Map.filter (fun k' _ -> k' <> k) model.items }, []
    | SetAddKey i -> { model with addKey = i }, []
    | AddKey -> { model with items = Map.add model.addKey (sprintf "it's %i" model.addKey) model.items }, []
    | SetKeyOf k ->
        match Map.tryFind k model.items with
        | None -> model, []
        | Some item ->
            let items = model.items |> Map.remove k |> Map.add model.addKey item
            { model with items = items }, []
    | SetRevOrder rev -> { model with revOrder = rev }, []
    | SetRadioItem i -> { model with radioItem = Some i }, []
    | SetPage p -> { model with page = p }, []
    | IncNonLazyVal -> { model with nonLazyValue = model.nonLazyValue + 1 }, []
    | IncLazyVal -> { model with lazyModel = { model.lazyModel with LazyModel.value = model.lazyModel.value + 1 } }, []
    | InsertLazyModelAt i -> let (first,last) = model.lazyModels |> List.splitAt i in { model with lazyModels = first @ [LazyModel.Default] @ last }, []
    | ShuffleLazy -> { model with lazyModels = model.lazyModels |> List.toArray |> shuffle |> List.ofArray }, []

// ondblclick's handler uses UIMouseEventArgs properties to check that we do generate specific UI*EventArgs.
// ondblclick isn't handled in the "super" case to check that we correctly generate no-op when an event hole is unfilled.
// onclick and onkeypress point to the same event to check that different UI*EventArgs are merged as UIEventArgs. -->
type SecretPw = Template<"""<div>
                                You typed the ${Kind} <i>pass<span>word</span></i>&excl;
                                <!-- Testing a comment -->
                                <button onclick="${Clear}" onkeypress="${Clear}" ondblclick="${DblClick}">Clear</button>
                                <input value="(default value)" bind="${Input}" /> <- You typed: ${Input}
                                <input type="number" bind="${Value}" />
                            </div>""">

let btnRef = ElementReferenceBinder()

let viewForm (js: IJSRuntime) model dispatch =
    div [] [
        input [attr.value model.input; on.change (fun e -> dispatch (SetInput (unbox e.Value)))]
        input [
            attr.bindRef btnRef
            attr.``type`` "submit"
            on.click (fun _ ->
                js.InvokeAsync("console.log", btnRef.Ref) |> ignore
                dispatch Submit
            )
            attr.style (if model.input = "" then "color:gray;" else null)
        ]
        div [] [textf "selected radio item: %A" model.radioItem]
        forEach {1..10} <| fun ix ->
            input [
                attr.``type`` "radio"
                attr.name "my-radio-item"
                bind.change (string ix) (fun _ -> dispatch (SetRadioItem ix))
            ]
        div [] [text (defaultArg model.submitted "")]
        (match model.submitted with
        | Some s ->
            concat [
                cond (s.Contains "secret") <| function
                    | true ->
                        SecretPw()
                            .Kind(b [] [text "secret"])
                            .Clear(fun _ -> dispatch (SetInput ""))
                            .DblClick(fun e -> dispatch (SetInput (sprintf "(%f, %f)" e.ClientX e.ClientY)))
                            .Input(model.input, fun s -> dispatch (SetInput s))
                            .Value(model.addKey, fun k -> dispatch (SetAddKey k))
                            .Elt()
                    | false -> empty

                cond (s.Contains "super") <| function
                    | true ->
                        SecretPw()
                            .Kind("super secret")
                            .Clear(fun _ -> dispatch (SetInput ""))
                            .Elt()
                    | false -> empty
            ]
        | None -> empty)
    ]

type CollectionTemplate = Template<"collection.html">

type ViewItem() =
    inherit ElmishComponent<int * string, Message>()

    override _.View ((k, v)) dispatch =
        CollectionTemplate.Item()
            .Value(v)
            .SetKey(fun _ -> dispatch (SetKeyOf k))
            .Remove(fun _ -> dispatch (RemoveItem k))
            .Url(router.Link (Item (k, Router.noModel)))
            .Elt()

let viewCollection model dispatch =
    let items =
        if model.revOrder then
            Seq.rev model.items
        else
            model.items :> _
    CollectionTemplate()
        .AddKeyValue(model.addKey, fun i -> dispatch (SetAddKey i))
        .AddKey(fun _ -> dispatch AddKey)
        .RevOrder(model.revOrder, fun rev -> dispatch (SetRevOrder rev))
        .Items(forEach items <| fun (KeyValue(k, v)) ->
            ecomp<ViewItem,_,_> [attr.key k] (k, v) dispatch)
        .Elt()

type ViewItemPage() =
    inherit ElmishComponent<int * string * int, Message>()

    override _.View ((k, v, m)) dispatch =
        concat [
            p [] [text ("Viewing page for item #" + string k)]
            p [] [text ("Text is: " + v)]
            p [] [a [router.HRef Collection] [text "Back to collection"]]
            p [] [
                text "Model: "
                button [on.click (fun _ -> dispatch (SetPage (Item (k, { Model = m - 1 }))))] [text "-"]
                textf "%i" m
                button [on.click (fun _ -> dispatch (SetPage (Item (k, { Model = m + 1 }))))] [text "+"]
            ]
        ]

let viewLazy model dispatch =
    div [] [
        div [] [
            pre [] [
                text """
    let viewLazy model dispatch =
        div [] [
            p [] [button [on.click (fun _ -> dispatch IncNonLazyVal)] [text "Increase non-lazy value"]]
            p [] [button [on.click (fun _ -> dispatch IncLazyVal)] [text "Increase lazy value"]]
            p [] [text (sprintf "Non-lazy value: %i, re-render random number check: %i" model.nonLazyValue (System.Random().Next()))]
            p [] [lazyComp (fun m -> text (sprintf "Lazy value: %i, re-render random number check: %i" m.value (System.Random().Next()))) model.lazyModel]
        ]
                """
            ]
            p [] [button [on.click (fun _ -> dispatch IncNonLazyVal)] [text "Increase non-lazy value"]]
            p [] [button [on.click (fun _ -> dispatch IncLazyVal)] [text "Increase lazy value"]]
            p [] [text (sprintf "Non-lazy value: %i, re-render random number check: %i" model.nonLazyValue (System.Random().Next()))]
            p [] [lazyComp (fun m -> text (sprintf "Lazy value: %i, re-render random number check: %i" m.value (System.Random().Next()))) model.lazyModel]
        ]
        div [] [
            button [on.click (fun _ -> dispatch ShuffleLazy)] [text "Shuffle"]
            forEach (model.lazyModels |> List.indexed) (fun (n, model') ->
                p [attr.key model'.guid] [
                    lazyComp (fun m -> text (sprintf "Row number %i, lazy value: %i, re-render random number check: %i" n m.value (System.Random().Next()))) model'
                    forEach [("before",n); ("after",n+1)] (fun (t,n') ->
                        button [on.click (fun _ -> dispatch (InsertLazyModelAt n'))] [text (sprintf "Insert %s" t)]
                    )
                ]
            )
        ]
    ]

let view js model dispatch =
    concat [
        RawHtml """
            <div style="color:gray">The links below should have blue background based on the current page.</div>
            <style>.active { background: lightblue; }</style>
        """
        p [] [
            navLink NavLinkMatch.All [router.HRef Form] [text "Form"]
            text " "
            navLink NavLinkMatch.Prefix [router.HRef Collection] [text "Collection"]
            text " "
            navLink NavLinkMatch.Prefix [router.HRef Lazy] [text "Lazy"]
        ]
        cond model.page <| function
            | Form -> viewForm js model dispatch
            | Collection -> viewCollection model dispatch
            | Item (k, m) -> ecomp<ViewItemPage,_,_> [] (k, model.items.[k], m.Model) dispatch
            | Lazy -> viewLazy model dispatch
    ]

type MyApp() =
    inherit ProgramComponent<Model, Message>()

    override this.Program =
        Program.mkProgram (fun _ -> initModel(), []) update (view this.JSRuntime)
        //|> Program.withConsoleTrace
        |> Program.withErrorHandler (fun (msg, exn) -> printfn "%s: %A" msg exn)
        |> Program.withRouter router
