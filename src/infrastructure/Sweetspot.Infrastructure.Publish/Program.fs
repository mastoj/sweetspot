module Program

open Pulumi
open Pulumi.FSharp
open Helpers

let infra () =
    let stack = getCoreStackRef()
    [
        "../../app/Sweetspot.Web"
        "../../app/Sweetspot.CSharpWorker"
    ]
    |> publishImages stack
    |> ignore
    dict []

[<EntryPoint>]
let main _ =
    Deployment.run infra
