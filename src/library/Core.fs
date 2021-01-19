namespace b0wter.DocuMonster.Library


module Core =
    
    open System.IO
    open System.Threading.Tasks
    open FsToolkit.ErrorHandling
    open Newtonsoft.Json
    open b0wter.DocuMonster.BsaSearch.Core
    open b0wter.DocuMonster
    open FSharp.Control.Tasks.V2
    open b0wter.FSharp
    
    let bucketName = "documonster"
    let indexDocumentId = "index-document"
    let indexAttachmentName = "index-attachment"
    let indexDocumentType = "indexDocument"
    
    /// The IndexDocument is the document the actual binary index is attached to.
    /// It does not contain any information. The `_id` is contant so that the document can be easily
    /// checked for and retrieved.
    [<CLIMutable>]
    type IndexDocument =
        {
            _rev: string option
            [<JsonIgnore>]
            index: BsaIndex
        }
        member this._id = indexDocumentId
        member this._type = indexDocumentType
        
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
                return { _rev = None; index = BsaIndex.Empty () }
        }
            
    let annotateAndStore filename name (couchDb: (b0wter.CouchDb.Lib.DbProperties.DbProperties * string)) : Task<Result<unit, string>> =
        taskResult {
            
            let! indexDocument = retrieveOrCreateIndex couchDb indexDocumentId indexAttachmentName
            let! document = annotate filename name
            do indexDocument.index.IndexDocument document // In-place operation required because consuming a C# library
            do! storeAnnotationAndFile couchDb document filename
            do! storeIndex couchDb indexAttachmentName indexDocument
            return ()
        }
