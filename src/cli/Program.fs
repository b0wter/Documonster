open System
open System.IO
open System.Threading.Tasks
open FsToolkit.ErrorHandling
open FsToolkit.ErrorHandling.Operator.TaskResult
open Newtonsoft.Json
open b0wter.DocuMonster.BsaSearch.Core
open b0wter.DocuMonster
open FSharp.Control.Tasks.V2
open Argu
open b0wter.DocuMonster.SharedEntities.Utilities
open b0wter.FSharp

let bucketName = "documonster"
let indexDocumentId = "index-document"
let indexAttachmentName = "index-attachment"

let doubleNewLine = Environment.NewLine + Environment.NewLine

/// The IndexDocument is the document the actual binary index is attached to.
/// It does not contain any information. The `_id` is contant so that the document can be easily
/// checked for and retrieved.
[<CLIMutable>]
type IndexDocument =
    {
        _rev: string option
        [<JsonIgnore>]
        index: BsaIndex
        _id: string
        _type: string
    }
    //member this._id = indexDocumentId
    //member this._type = "indexDocument"

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
            
let private annotate filename name =
    task {
        match Google.Storage.SClient.Create (), Google.Annotation.AClient.Create () with
        | Ok sClient, Ok aClient ->
            let! document = Google.Core.uploadAndAnnotateAsResult sClient
                                aClient bucketName
                                { Google.Core.FileDefinition.LocalFileName = filename
                                  Google.Core.FileDefinition.MimeType = Google.Utilities.MimeType.PDF }
                                name
            match document with
            | Ok r ->
                return Ok r
            | Error e ->
                return Error e
        | Error e, _ ->
            return Error (sprintf "The Google Storage client could not be initialized because: %s" e)
        | _, Error e ->
            return Error (sprintf "The Google Image Annotation client could not be initialized because: %s" e)
    }
    
    
let private storeAnnotationAndFile couchDb document filename : Task<Result<unit, string>> =
    b0wter.DocuMonster.CouchDb.Core.storeDocumentAndFileWith couchDb document filename |> Async.StartAsTask
    
let private serializeIndex (index: BsaIndex) : byte[] =
    let memoryStream = new MemoryStream()
    do index.ToStream(memoryStream)
    memoryStream.ToArray()
    
let private storeIndex couchDb indexAttachmentName (indexDocument: IndexDocument) : Task<Result<unit, string>> =
    task {
        let serializedIndex = indexDocument.index |> serializeIndex
        return! b0wter.DocuMonster.CouchDb.Core.storeAttachment couchDb indexDocument._id indexDocument._rev indexAttachmentName serializedIndex
    }
    
let private retrieveOrCreateIndex couchDb indexId attachmentName : Task<Result<IndexDocument, string>> =
    taskResult {
        let! attachmentIdExists = CouchDb.Core.attachmentIdExists couchDb indexId attachmentName |> Async.StartAsTask
        if attachmentIdExists then
            let! document = b0wter.DocuMonster.CouchDb.Core.retrieveById<IndexDocument> couchDb indexId
            let! attachment = b0wter.DocuMonster.CouchDb.Core.retrieveAttachment couchDb indexId attachmentName
            let deserialized = new MemoryStream(attachment) |> BsaIndex.FromStream
            return { document with index = deserialized }
        else
            return { _rev = None; index = BsaIndex.Empty (); _id = indexId; _type = "indexDocument" }
    }
        
let private annotateAndStore (results: ParseResults<Args>) (couchDb: (b0wter.CouchDb.Lib.DbProperties.DbProperties * string)) : Task<Result<unit, string>> =
    taskResult {
        let annotationParameter = results.GetResult(<@ Annotate @>)
        let filename = annotationParameter.PostProcessResult(<@ Filename @>, parseFilename)
        let name = annotationParameter.TryGetResult(<@ Name @>)
        
        let! indexDocument = retrieveOrCreateIndex couchDb indexDocumentId indexAttachmentName
        let! document = annotate filename name
        do indexDocument.index.IndexDocument document // In-place operation required because consuming a C# library
        do! storeAnnotationAndFile couchDb document filename
        do! storeIndex couchDb indexAttachmentName indexDocument
        return ()
    }
    
[<EntryPoint>]
let main argv =
    task {
        let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> None)
        let parser = ArgumentParser.Create<Args>(errorHandler = errorHandler)
        let results = parser.ParseCommandLine(inputs = argv, raiseOnUsage = true)
        let! couchDbConnection = b0wter.DocuMonster.CouchDb.Core.initFromEnvironment ()
        
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
            match! annotateAndStore results couchDb with
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
        
        

