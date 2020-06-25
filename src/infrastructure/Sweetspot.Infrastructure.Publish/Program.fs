module Program

open Pulumi
open Pulumi.FSharp
open Pulumi.Docker
open LibGit2Sharp

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

let getSha() =
    let repoPath = Repository.Discover(System.Environment.CurrentDirectory)
    use repo = new Repository(repoPath)
    let latestCommit = repo.Head.Tip
    latestCommit.Sha.Substring(0,6)

let lastPart (delimeter: string) (value: string) =
    let parts = value.Split(delimeter)
    parts.[parts.Length - 1]

let toLower (str: string) = str.ToLower();

let publishImages stack (paths: string list) =
    let imageRegistry = stack |> getImageRegistry
    let sha = getSha()

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
