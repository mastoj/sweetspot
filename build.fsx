#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target 
nuget Fake.Tools.Git //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fake.Tools.Git
open System.IO


// Properties
let buildDir = "./.build/"
let publishDir = sprintf "%s/publish/" buildDir

// Helper functions
let runTool cmd args workingDir =
    let arguments = args |> String.split ' ' |> Arguments.OfArgs
    Command.RawCommand (cmd, arguments)
    |> CreateProcess.fromCommand
    |> CreateProcess.withWorkingDirectory workingDir
    |> CreateProcess.ensureExitCode
    |> Proc.run
    |> ignore

let buildDocker dockerFile tag =
    let args = sprintf "build -t %s -f %s ." tag dockerFile
    runTool "docker" args __SOURCE_DIRECTORY__

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    Trace.log " --- Cleaning stuff --- "
    Shell.cleanDir buildDir
)

Target.create "BuildApp" (fun _ ->
    let buildMode = Environment.environVarOrDefault "buildMode" "Release"
    let setParams (defaults:MSBuildParams) =
            { defaults with
                Verbosity = Some(MSBuildVerbosity.Normal)
                Targets = ["Publish"]
                Properties =
                    [
                        "Optimize", "True"
                        "DebugSymbols", "True"
                        "Configuration", buildMode
                        "PublishDir", publishDir 
                    ]
            }
    MSBuild.build setParams "./sweetspot.sln"
)

let getDockerTag app =
    let gitHash = Information.getCurrentHash()
    sprintf "mainacr70d6dafa.azurecr.io/%s:%s" app gitHash

let dockerMap = [
    "sweetspot.web", "Dockerfile.Web"
    "sweetspot.csharpworker", "Dockerfile.CSharpWorker"
]

Target.create "DockerBuild" (fun _ ->
    dockerMap
    |> List.iter(fun (appName, dockerFile) ->
        let dockerTag = getDockerTag appName
        let dockerFilePath = sprintf "./dockerfiles/%s" dockerFile
        buildDocker dockerFilePath dockerTag
    )
)

Target.create "Publish" (fun _ ->
    Trace.log " --- Publishing app --- "
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
    ==> "BuildApp"
    ==> "Publish"

// *** Start Build ***
Target.runOrDefault "Publish"
