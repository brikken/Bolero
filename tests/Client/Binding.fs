module Bolero.Test.Client.Binding

open Elmish
open Bolero
open Bolero.Html

type Name = { first: string; last: string; }
type Model = { name: Name; }
type Message = | SetName of Name | ResetName
let init = { name = { first = ""; last = ""; }; }
let update model msg =
    match msg with
    | SetName name ->
        { model with name = name; },
        Cmd.none
    | ResetName ->
        { model with name = { first = ""; last = ""; }; },
        Cmd.none
let view model dispatch =
    concat [
        p [] [input [
            attr.placeholder "First name"
            bind.input model.name.first (fun first -> { model.name with first = first; } |> SetName |> dispatch)
        ]]
        p [] [input [
            attr.placeholder "Last name"
            bind.input model.name.last (fun last -> { model.name with last = last; } |> SetName |> dispatch)
        ]]
        p [] [text (sprintf "Entered name is %s %s" model.name.first model.name.last)]
        p [] [button [on.click (fun _ -> ResetName |> dispatch)] [text "Reset name"]]
    ]
