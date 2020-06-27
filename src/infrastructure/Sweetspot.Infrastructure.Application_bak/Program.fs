module Program

open Pulumi.FSharp
open Pulumi.Azure.Core
open Pulumi.Azure.Storage

let infra () =
    // Create an Azure Resource Group
    let resourceGroup = ResourceGroup "resourceGroup"


    // Export the connection string for the storage account
    dict [("connectionString", resourceGroup.Name :> obj)]

[<EntryPoint>]
let main _ =
  Deployment.run infra
