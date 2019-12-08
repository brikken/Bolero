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

namespace Bolero

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Components
open Microsoft.AspNetCore.Components.Routing
open Microsoft.JSInterop
open Elmish
open Bolero.Render

type ModelParameter<'model> = 'model
type DispatchParameter<'msg> = Dispatch<'msg>
type ViewFunctionParameter<'model,'msg> = 'model -> Dispatch<'msg> -> Node
type EqualParameter<'model> = 'model -> 'model -> bool
[<RequireQualifiedAccess>]
type ComponentParameter<'model,'msg> =
    | Model of ModelParameter<'model>
    | Dispatch of DispatchParameter<'msg>
    | ViewFunction of ViewFunctionParameter<'model,'msg>
    | Equal of EqualParameter<'model>

/// A component built from `Html.Node`s.
[<AbstractClass>]
type Component() =
    inherit ComponentBase()

    let matchCache = Dictionary()

    override this.BuildRenderTree(builder) =
        base.BuildRenderTree(builder)
        this.Render()
        |> RenderNode this builder matchCache

    /// The rendered contents of the component.
    abstract Render : unit -> Node

[<AbstractClass>]
type Component<'model>() =
    inherit Component()

    /// Compare the old model with the new to decide whether this component
    /// needs to be re-rendered.
    abstract member ShouldRender : oldModel: 'model * newModel: 'model -> bool
    default this.ShouldRender(oldModel, newModel) =
        not <| obj.ReferenceEquals(oldModel, newModel)

/// A component that is part of an Elmish view.
[<AbstractClass>]
type ElmishComponent<'model, 'msg>() =
    inherit Component<'model>()

    member val internal OldModel = Unchecked.defaultof<ModelParameter<'model>> with get, set

    /// The current value of the Elmish model.
    /// Can be just a part of the full program's model.
    [<Parameter>]
    member val Model = Unchecked.defaultof<ModelParameter<'model>> with get, set

    /// The Elmish dispatch function.
    [<Parameter>]
    member val Dispatch = Unchecked.defaultof<DispatchParameter<'msg>> with get, set

    /// The Elmish view function.
    abstract View : 'model -> Dispatch<'msg> -> Node

    override this.ShouldRender() =
        base.ShouldRender(this.OldModel, this.Model)

    override this.Render() =
        this.OldModel <- this.Model
        this.View this.Model this.Dispatch

type LazyComponent<'model,'msg>() as self =
    inherit ElmishComponent<'model,'msg>()

    /// The view function
    [<Parameter>]
    member val ViewFunction = Unchecked.defaultof<ViewFunctionParameter<'model,'msg>> with get, set

    /// The optional custom equality check
    // Uses default equality from base class
    [<Parameter>]
    member val Equal : EqualParameter<'model> = (fun m1 m2 -> self.ShouldRender(m1,m2)) with get, set

    override this.View model dispatch = this.ViewFunction model dispatch

    override this.ShouldRender() = this.Equal this.OldModel this.Model

type IProgramComponent =
    abstract Services : System.IServiceProvider

type Program<'model, 'msg> = Program<ProgramComponent<'model, 'msg>, 'model, 'msg, Node>

/// A component that runs an Elmish program.
and [<AbstractClass>]
    ProgramComponent<'model, 'msg>() =
    inherit Component<'model>()

    let mutable oldModel = Unchecked.defaultof<'model>
    let mutable navigationInterceptionEnabled = false

    [<Inject>]
    member val NavigationManager = Unchecked.defaultof<NavigationManager> with get, set
    [<Inject>]
    member val Services = Unchecked.defaultof<System.IServiceProvider> with get, set
    [<Inject>]
    member val JSRuntime = Unchecked.defaultof<IJSRuntime> with get, set
    [<Inject>]
    member val NavigationInterception = Unchecked.defaultof<INavigationInterception> with get, set

    member val private View = Empty with get, set
    member val private Dispatch = ignore with get, set
    member val private Router = None : option<IRouter<'model, 'msg>> with get, set

    /// The Elmish program to run. Either this or AsyncProgram must be overridden.
    abstract Program : Program<'model, 'msg>
    default this.Program = Unchecked.defaultof<_>

    /// The Elmish program to run. Either this or Program must be overridden.
    abstract AsyncProgram : Async<Program<'model, 'msg>>
    default this.AsyncProgram = async { return this.Program }

    interface IProgramComponent with
        member this.Services = this.Services

    member private this.OnLocationChanged (_: obj) (e: LocationChangedEventArgs) =
        this.Router |> Option.iter (fun router ->
            let uri = this.NavigationManager.ToBaseRelativePath(e.Location)
            let route = router.SetRoute uri
            Option.iter this.Dispatch route)

    member internal this.GetCurrentUri() =
        let uri = this.NavigationManager.Uri
        this.NavigationManager.ToBaseRelativePath(uri)

    member internal this.SetState(program, model, dispatch) =
        if this.ShouldRender(oldModel, model) then
            this.ForceSetState(program, model, dispatch)

    member internal this.StateHasChanged() =
        base.StateHasChanged()

    member private this.ForceSetState(program, model, dispatch) =
        this.View <- program.view model dispatch
        oldModel <- model
        this.InvokeAsync(this.StateHasChanged) |> ignore
        this.Router |> Option.iter (fun router ->
            let newUri = router.GetRoute model
            let oldUri = this.GetCurrentUri()
            if newUri <> oldUri then
                try this.NavigationManager.NavigateTo(newUri)
                with _ -> () // fails if run in prerender
        )

    member this.Rerender() =
        this.ForceSetState(this.Program, oldModel, this.Dispatch)

    member internal this._OnInitializedAsync() =
        base.OnInitializedAsync()

    override this.OnInitializedAsync() =
        async {
            do! this._OnInitializedAsync() |> Async.AwaitTask
            let! program = this.AsyncProgram
            let setDispatch dispatch =
                this.Dispatch <- dispatch
            { program with
                setState = fun model dispatch ->
                    this.SetState(program, model, dispatch)
                init = fun arg ->
                    let model, cmd = program.init arg
                    model, setDispatch :: cmd
            }
            |> Program.runWith this
        }
        |> Async.StartAsTask :> _

    member internal this.InitRouter
        (
            r: IRouter<'model, 'msg>,
            program: Program<'model, 'msg>,
            initModel: 'model
        ) =
        this.Router <- Some r
        EventHandler<_> this.OnLocationChanged
        |> this.NavigationManager.LocationChanged.AddHandler
        match r.SetRoute (this.GetCurrentUri()) with
        | Some msg ->
            program.update msg initModel
        | None ->
            initModel, []

    override this.OnAfterRenderAsync(firstRender) =
        if this.Router.IsSome && not navigationInterceptionEnabled then
            navigationInterceptionEnabled <- true
            this.NavigationInterception.EnableNavigationInterceptionAsync()
        else
            Task.CompletedTask

    override this.Render() =
        this.View

    interface System.IDisposable with
        member this.Dispose() =
            EventHandler<_> this.OnLocationChanged
            |> this.NavigationManager.LocationChanged.RemoveHandler

type ElementReferenceBinder() =

    let mutable ref = Unchecked.defaultof<ElementReference>

    member this.Ref = ref

    member internal this.SetRef(r) = ref <- r
