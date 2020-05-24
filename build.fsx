#r "paket:
nuget Fake.IO.FileSystem
nuget Fake.DotNet.MSBuild
nuget Fake.Core.Target //"
#load "./.fake/build.fsx/intellisense.fsx"

open Fake.Core
open Fake.DotNet
open Fake.IO
open Fake.IO.Globbing.Operators
open System.IO


// Properties
let buildDir = "./.build/"

// *** Define Targets ***
Target.create "Clean" (fun _ ->
    Trace.log " --- Cleaning stuff --- "
    Shell.cleanDir buildDir
)

Target.create "BuildApp" (fun _ ->
    let buildProject projFile =
        let projectFileInfo = FileInfo(projFile)
        let parentDir = projectFileInfo.Directory
        let appName = parentDir.Name
        let buildDir = sprintf "%s%s/" buildDir appName

        MSBuild.runRelease id buildDir "Publish" [projFile]

    !! "src/app/**/*.fsproj"
        |> Seq.map buildProject
        |> Seq.concat
        |> Trace.logItems "AppBuild-Output: "
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
