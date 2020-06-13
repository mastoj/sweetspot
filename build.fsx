#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.Cli
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

let buildDocker dockerFile tag contextPath =
    let args = sprintf "build -t %s -f %s %s" tag dockerFile contextPath
    runTool "docker" args __SOURCE_DIRECTORY__

let runPulumi path gitSha =
    let args = sprintf "up -y -c GIT_SHA=%s" gitSha
    runTool "pulumi" args path

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

Target.create "Publish" (fun _ ->
    Trace.log " --- Publishing app --- "
    let gitSha = Information.getCurrentHash()
    runPulumi "./src/infrastructure/Sweetspot.Infrastructure.Publish" gitSha
)

Target.create "Deploy" (fun _ ->
    let gitSha = Information.getCurrentHash()
    runPulumi "./src/infrastructure/Sweetspot.Infrastructure.Application" gitSha
)

open Fake.Core.TargetOperators

// *** Define Dependencies ***
"Clean"
    ==> "BuildApp"
    ==> "Publish"

// *** Start Build ***
Target.runOrDefault "Publish"
