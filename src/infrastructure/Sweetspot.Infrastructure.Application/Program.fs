// Learn more about F# at http://fsharp.org

open KubernetesHelpers
open Pulumi.Azure.Core
open Pulumi.FSharp
open System

let infra () =
    let stack = getCoreStackRef() |> ignore
    let resourceGroup = ResourceGroup("Deleteme")
    [] |> dict
    // let infrastructureOutput = deployAppInfrastructure stack
    // let appsOutput = deployApps stack
    // [
    //     infrastructureOutput
    //     appsOutput
    // ]
    // |> List.concat
    // |> dict


[<EntryPoint>]
let main _ =
    Deployment.run infra