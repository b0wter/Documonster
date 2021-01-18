open System
open System.IO
open System.Threading.Tasks
open b0wter.DocuMonster.BsaSearch.Core
open b0wter.DocuMonster
open b0wter.DocuMonster
open FSharp.Control.Tasks.V2
open Argu
open b0wter.DocuMonster.SharedEntities.Utilities

let bucketName = "documonster"
let indexDocumentId = "index-document"
let indexAttachmentName = "index-attachment"

let doubleNewLine = Environment.NewLine + Environment.NewLine

/// The IndexDocument is the document the actual binary index is attached to.
/// It does not contain any information. The `_id` is contant so that the document can be easily
/// checked for and retrieved.
type IndexDocument =
    {
        _rev: string
    }
    member this._id = indexDocumentId
    member this._type = "indexDocument"

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
    
let private store couchDb filename document =
    task {
        return! CouchDb.Core.storeFileWith couchDb document filename |> Async.StartAsTask
    }
    
let private serializeIndex (index: BsaIndex) : byte[] =
    let memoryStream = new MemoryStream()
    do index.ToStream(memoryStream)
    memoryStream.ToArray()
    
let private retrieveOrCreateIndex couchDb indexId attachmentName : Task<Result<BsaIndex, string>> =
    task {
        match! CouchDb.Core.attachmentIdExists couchDb indexId attachmentName |> Async.StartAsTask with
        | Ok true ->
            let deserialize = (fun (bytes: byte[]) -> new MemoryStream(bytes)) >> BsaIndex.FromStream
            let! result = b0wter.DocuMonster.CouchDb.Core.retrieveAttachment couchDb indexId attachmentName
            return result |> Result.map deserialize
        | Ok false ->
            return BsaIndex.Empty() |> Ok
        | Error e -> return Error e
    }
        
let private saveIndexToCouchDb couchDb documentId bytes =
    task {
        //b0wter.DocuMonster.CouchDb.
        return failwith "rekt"
    }
    
let private index (index: BsaIndex) couchDb filename document =
    task {
        do index.IndexDocument document
        let serializedIndex = index |> serializeIndex
        return 0
    }
            
let private annotateAndStore (results: ParseResults<Args>) (couchDb: (b0wter.CouchDb.Lib.DbProperties.T * string)) =
    (*
        1.  Check if old index exists (true -> 2a, false -> 2b) | Input: documentId, attachmentName        | Output: bool
        2a. Retrieve the old index from the database.           | Input: documentId, attachmentName        | Output: Result<Index>
        2b. Create empty index                                  | --- see above ---
        3.  Annotate the file                                   | Input: filename, name                    | Output: Result<Document>
        4.  Store annotation in db                              | Input: Document                          | Output: Result<bool>
        5.  Index the file                                      | Input: Document, Index                   | Output: Index
        6.  Store index in db                                   | Input: Index, documentId, attachmentName | Output: Result<bool>
        7.  Store file in db                                    | Input: filename, documentId, attachmentN | Output: Result<bool>
    *)
    task {
        //
        // --- Retrieve the command line arguments.
        //
        let annotationParameter = results.GetResult(<@ Annotate @>)
        let filename = annotationParameter.PostProcessResult(<@ Filename @>, parseFilename)
        let name = annotationParameter.TryGetResult(<@ Name @>)
        
        //
        // --- Annotate the file using the Google Vision API.
        // (steps 1, 2a and 2b)
        //
        let! indexRetrievalResult = retrieveOrCreateIndex couchDb indexDocumentId indexAttachmentName
        match indexRetrievalResult with
        | Ok index ->
            //
            // --- Store the file as a binary attachment for the annotation in CouchDb.
            // (step 3)
            //
            match! annotate filename name with
            | Ok document ->
                //
                // --- Store the annotation in the database.
                // (step 4)
                //
                match! document |> store couchDb filename with
                | Ok _ ->
                    //
                    // --- Index the file and store the index in the database  
                    match!
                    return 0
                | Error e ->
                    return 1
            | Error e ->
                return 0
                
            (*
            let! storeResult = annotateResult |> (Result.bindT (store couchDb filename))
            
            return  match storeResult with
                    | Ok _ ->
                        printfn "success"
                        0
                    | Error e ->
                        printfn "%s" e
                        1
                        *)
        | Error e ->
            printfn "Could not retrieve the old index or create a new one because: %s" e
            return 1
        
        //let! annotateResult = index |> Result.bind (fun _ -> annotate filename name)
        
        //let documentFormatter (d: b0wter.DocuMonster.SharedEntities.Document.Document) =
        //    (d.Pages |> List.fold (fun acc next -> acc + doubleNewLine + next.Text) String.Empty).TrimStart()
        
        
        //
        // --- Store the annotation results in the database.
        //
        //let! storeResult = annotateResult |> (Result.bindT (store couchDb filename))
        
        //do b0wter.DocuMonster.BsaSearch.Core.hello () |> ignore
        (*
        let index = b0wter.DocuMonster.BsaSearch.Core.BsaIndex.Empty ()
        
        
        match storeResult with
        | Ok _ ->
            printfn "File was annotated successfully and stored in CouchDb."
            return 0
        | Error e ->
            printfn "There was an error trying to annotate and store the file:"
            printfn "%s" e
            return 1
        *)
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
            return! annotateAndStore results couchDb
        | _ ->
            printfn "You need to supply either the 'list' or the 'annotate' argument not both."
            printfn "%s" (parser.PrintUsage())
            return 1
                
    } |> Async.AwaitTask |> Async.RunSynchronously
        
        

