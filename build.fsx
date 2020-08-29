#r "paket:
nuget Fantomas.Extras
nuget Fake.IO.FileSystem
nuget Fake.DotNet.Cli
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open Fantomas.Extras.FakeHelpers
open Fantomas.FormatConfig

let fantomasConfig = { FormatConfig.Default with MaxLineLength = 140 }

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

let buildDocker dockerFile tag contextPath =
    let args = sprintf "build -t %s -f %s %s" tag dockerFile contextPath
    runTool "docker" args __SOURCE_DIRECTORY__

let runPulumi args path =
    runTool "pulumi" args path

let runPulumiUp = runPulumi (sprintf "up -y")

let runPulumiSelectStack stack = runPulumi (sprintf "stack select %s" stack)

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    Trace.log " --- Cleaning stuff --- "
    Shell.cleanDir buildDir
    !! "./src/**/**/.build"
    |> Seq.iter Shell.cleanDir
)

Target.create "BuildApp" (fun _ ->
    let buildMode = Environment.environVarOrDefault "buildMode" "Release"
    let setParams (defaults:DotNet.BuildOptions) =
            { defaults with
                MSBuildParams = {
                    defaults.MSBuildParams with
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
            }
    DotNet.build setParams "./sweetspot.sln"
)

Target.create "PublishDocker" (fun _ ->
    Trace.log " --- Publishing app --- "
    runPulumiSelectStack "dev" "./src/infrastructure/Sweetspot.Infrastructure.Publish"
    runPulumiUp "./src/infrastructure/Sweetspot.Infrastructure.Publish"
)

Target.create "Deploy" (fun _ ->
    Trace.log " --- Depliyng to dev --- "
    runPulumiSelectStack "dev" "./src/infrastructure/Sweetspot.Infrastructure.Application"
    runPulumiUp "./src/infrastructure/Sweetspot.Infrastructure.Application"
)

Target.create "CheckCodeFormat" (fun _ ->
    !!"**/**/**/*.fs"
    |> checkCode
    |> Async.RunSynchronously
    |> printfn "Format check result: %A")

Target.create "Format" (fun _ ->
    !!"**/**/**/*.fs"
    |> formatCode
    |> Async.RunSynchronously
    |> printfn "Formatted files: %A")

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
    ==> "BuildApp"
    ==> "PublishDocker"

// *** Start Build ***
Target.runOrDefault "PublishDocker"
