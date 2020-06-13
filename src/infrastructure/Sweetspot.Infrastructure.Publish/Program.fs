﻿module Program

open Pulumi
open Pulumi.FSharp
open Pulumi.Docker

let getCoreStackRef() =
    let env = "dev" // stackParts[stackParts.Length - 1];
    let stackRef = sprintf "mastoj/Sweetspot.core/%s" env
    StackReference(stackRef)

let getStackOutput key (stack: StackReference) =
    stack.RequireOutput(input key).Apply(fun v -> v.ToString())

let getImageRegistry stack =
    let loginServer = stack |> getStackOutput "registryLoginServer" 
    let adminUserName = stack |> getStackOutput "registryAdminUsername" 
    let adminPassword = stack |> getStackOutput "registryAdminPassword" 
    ImageRegistry(
        Password = io adminPassword,
        Server = io loginServer,
        Username = io adminUserName
    )

let getSha (config: Pulumi.Config) =
    config.Get("GIT_SHA")

let lastPart (delimeter: string) (value: string) =
    let parts = value.Split(delimeter)
    parts.[parts.Length - 1]

let toLower (str: string) = str.ToLower();

let publishImages (paths: string list) =
    let stack = getCoreStackRef()
    let imageRegistry = stack |> getImageRegistry
    let config = Pulumi.Config()
    let sha = getSha config

    paths
    |> List.map (fun path ->
        let imageName = path |> lastPart "/" |> toLower
        let fullImageName = imageRegistry.Server.Apply(fun acr -> sprintf "%s/%s:%s" acr imageName sha)
        Image(imageName,
            ImageArgs(
                Build = inputUnion1Of2 path,
                ImageName = io fullImageName,
                Registry = input imageRegistry
                )
            )
        )

let infra () =
    [
        "../../app/Sweetspot.Web"
        "../../app/Sweetspot.CSharpWorker"
    ]
    |> publishImages
    |> ignore
    dict []

[<EntryPoint>]
let main _ =
    Deployment.run infra