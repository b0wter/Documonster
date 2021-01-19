open System.IO
open FsToolkit.ErrorHandling
open FSharp.Control.Tasks.V2
open b0wter.DocuMonster.Library.Core
open Argu
open b0wter.FSharp

let parseFilename (f: string) =
    if not <| File.Exists(f) then failwith (sprintf "The given file '%s' does not exist." f) 
    else if not <| f.EndsWith(".pdf") then failwith "You may only supply pdf files."
    else f

type AnnotateArgs =
    | [<MainCommand; First; CliPrefix(CliPrefix.None)>] Filename of string
    | [<AltCommandLine("-n")>] Name of string 
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | Filename _ -> "File to annotate"
            | Name _ -> "Name of the document in the database."
            
type Args =
    | [<CliPrefix(CliPrefix.None)>] List of string option
    | [<CliPrefix(CliPrefix.None)>] Annotate of ParseResults<AnnotateArgs>
    interface IArgParserTemplate with
        member s.Usage =
            match s with
            | List _ -> "List previously annotated files."
            | Annotate _ -> "Annotate a pdf file."
            
    
[<EntryPoint>]
let main argv =
    task {
        let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> None)
        let parser = ArgumentParser.Create<Args>(errorHandler = errorHandler)
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let! couchDbConnection = b0wter.DocuMonster.CouchDb.Core.initFromEnvironment ()
        
        let annotationParameter = results.GetResult(<@ Annotate @>)
        let filename = annotationParameter.PostProcessResult(<@ Filename @>, parseFilename)
        let name = annotationParameter.TryGetResult(<@ Name @>)
        
        match results.GetAllResults(), couchDbConnection with
        | [], _ ->
            printfn "You need to specify a command."
            printfn "%s" (parser.PrintUsage())
            return 1
        | [ List filter ], Ok couchDb ->
            // Missing :(
            // Not implemented.
            return 1
        | _, Error e ->
            printfn "Could not initialize CouchDb Connection because: %s" e
            return 1
        | [ Annotate _ ], Ok couchDb ->
            match! annotateAndStore filename name couchDb with
            | Ok _ ->
                return 0
            | Error e ->
                printfn "%s" e
                return 1
        | _ ->
            printfn "You need to supply either the 'list' or the 'annotate' argument not both."
            printfn "%s" (parser.PrintUsage())
            return 1
                
    } |> Async.AwaitTask |> Async.RunSynchronously
        
