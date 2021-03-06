﻿namespace b0wter.DocuMonster.Google

module Core =

    open FSharp.Control.Tasks.V2
    open System.Threading.Tasks
    open b0wter.DocuMonster.SharedEntities
    open b0wter.DocuMonster.Google.Utilities
    open b0wter.DocuMonster.Google

    type DeleteFailure = {
        Annotation: Document.Document
        Reason: string
    }

    type AnnotationResult
        = Success of Document.Document
        | BucketUriInvalid of string
        | UploadFailed of string
        | AnnotationFailed of string
        | DeletionFailed of DeleteFailure
        | EnumeratingBucketContentsFailed of string
        | RetrieveFailed of string
        | ParsingFailed of string

    type FileDefinition = {
        LocalFileName: string
        MimeType: MimeType.T
    }
    
    let private fileDefinitionFilename f =
        System.IO.Path.GetFileName(f.LocalFileName)

    let private upload (client: Storage.SClient) (bucketName: string) (file: FileDefinition) =
        let buildInputPath (f: FileDefinition) : Storage.Destination =
            sprintf "input/%s" (f |> fileDefinitionFilename) |> Storage.Destination.FullPath

        task {
            let input = file |> buildInputPath
            let! result = Storage.uploadLocalFileWith client bucketName input file.MimeType file.LocalFileName
            return result
        }

    let private annotate (client: Annotation.AClient) (annotation: Annotation.AnnotationParameter) =
        task {
            let! result = Annotation.annotateAsync client (Annotation.AnnotationCollection.Single annotation)
            return result
        }

    let private download (client: Storage.SClient) bucketName (annotation: Annotation.AnnotationParameter) =
        Storage.retrieveTextFile client bucketName (annotation.TargetPrefix |> BucketUri.value)

    let uploadAndAnnotate (sclient: Storage.SClient) (aclient: Annotation.AClient) (bucketName: string) (file: FileDefinition) (name: string option) : Task<AnnotationResult> =
        task {
            // Create all the necessary parameters.
            let rawAnnotationPrefix = sprintf "gs://%s/output/%s" bucketName (file.LocalFileName |> System.IO.Path.GetFileNameWithoutExtension)
            let annotationPrefix = rawAnnotationPrefix |> BucketUri.create
            let listPrefix = "output/" + (file.LocalFileName |> System.IO.Path.GetFileNameWithoutExtension)
            let source = sprintf "gs://%s/input/%s" bucketName (file |> fileDefinitionFilename) |> BucketUri.create
            
            let! uploadResult = file |> upload sclient bucketName
            
            match uploadResult, annotationPrefix, source with
            | Ok _, Ok annotationPrefix, Ok source ->
                let annotationParameters = Annotation.createAnnotationParameter source annotationPrefix file.MimeType
                match! annotate aclient annotationParameters with
                | Ok _ ->
                    match! Storage.list sclient bucketName (Some listPrefix) with
                    | Ok listResult ->
                        let resultFilename = (listResult |> List.head).Name
                        match! Storage.retrieveTextFile sclient bucketName resultFilename with
                        | Ok result ->
                            match result |> (Newtonsoft.Json.JsonConvert.DeserializeObject<Response.Responses> >> Document.fromResponse) with
                            | Ok document ->
                                let document = { document with Filename = file.LocalFileName; Name = name }
                                match! Storage.delete sclient bucketName (resultFilename|> Storage.Destination.FullPath) with
                                | Ok _ ->
                                    return Success document
                                | Error e ->
                                    return DeletionFailed { Annotation = document; Reason = e }
                            | Error e ->
                                return ParsingFailed e
                        | Error e ->
                            return RetrieveFailed e
                    | Error e ->
                        return EnumeratingBucketContentsFailed e
                | Error e ->
                    return AnnotationFailed e
            | Error e, _, _ ->
                return UploadFailed e
            | _, Error e, _ ->
                return BucketUriInvalid (sprintf "Could not create annotation prefix because: %s" e)
            | _, _, Error e ->
                return BucketUriInvalid (sprintf "Could not create annotation source bucket uri because: %s" e)
        }
        
    let asResult (result: AnnotationResult) =
        match result with
        | Success s -> Ok s
        | BucketUriInvalid e -> Error ("BucketUriInvalid: " + e)
        | UploadFailed e -> Error ("UploadFailed: " + e)
        | AnnotationFailed e -> Error ("AnnotationFailed: " + e)
        | RetrieveFailed e -> Error ("RetrieveFailed: " + e)
        | EnumeratingBucketContentsFailed e -> Error ("EnumeratingBucketContentsFailed: " + e)
        | ParsingFailed e -> Error ("ParsingFailed: " + e)
        | DeletionFailed failure -> Ok failure.Annotation

    let uploadAndAnnotateAsResult (sclient: Storage.SClient) (aclient: Annotation.AClient) (bucketName: string) (file: FileDefinition) (name: string option) : Task<Result<Document.Document, string>> =
        task {
            let! annotationResult = uploadAndAnnotate sclient aclient bucketName file name
            return (annotationResult |> asResult)
        }
        