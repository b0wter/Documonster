namespace b0wter.DocuMonster.Google

module Annotation =

    open Google.Cloud.Vision.V1
    open b0wter.DocuMonster.Google.Utilities
    open FSharp.Control.Tasks

    type AnnotationParameter = {
        Source: BucketUri.T
        TargetPrefix: BucketUri.T
        MimeType: MimeType.T
    }

    type AnnotationCollection
        = Single of AnnotationParameter
        | Many of AnnotationParameter list

    let collectionAsList = function Single s -> [ s ] | Many m -> m

    let createAnnotationParameter source targetPrefix mimeType =
        { Source = source; TargetPrefix = targetPrefix; MimeType = mimeType }

    let tryCreateAnnotationParameter (source: string) (targetPrefix: string) (mimeType: MimeType.T) =
        match source |> BucketUri.create, targetPrefix |> BucketUri.create with
        | Ok s, Ok p ->
            Ok (createAnnotationParameter s p mimeType)
        | Error _, _ ->
            Error "The given source is not a valid Google Clouc Bucket uri."
        | _, Error _ ->
            Error "The given target prefix is not a valid Google Clouc Bucket uri."

    let createClient () =
        try
            Ok (ImageAnnotatorClient.Create())
        with
        | ex -> Error (ex.ToString())

    type AClient private (client: ImageAnnotatorClient) =
        member this.Client = client
        static member Create() =
            match createClient () with
            | Ok client ->
                Ok (AClient(client))
            | Error e ->
                Error (sprintf "The Google Image Annotator Client could not be properly initialized. Internal error: %s" e)

    let annotateAsync (client: AClient) (annotations: AnnotationCollection) = //(source: BucketUri.T) (targetPrefix: BucketUri.T) (mimeType: MimeType.T) =
        // Documentation:
        // https://cloud.google.com/vision/docs/pdf#vision_text_detection_pdf_gcs-csharp
        let transformParameter (parameter: AnnotationParameter) =
            let mimeType = parameter.MimeType |> MimeType.value
            let sourceUri = parameter.Source |> BucketUri.value
            let inputConfig = InputConfig(MimeType = mimeType, GcsSource = GcsSource(Uri = sourceUri))

            let targetUri = parameter.TargetPrefix |> BucketUri.value
            let outputConfig = OutputConfig(GcsDestination = GcsDestination(Uri = targetUri))

            let request = AsyncAnnotateFileRequest(InputConfig = inputConfig, OutputConfig = outputConfig)
            do request.Features.Add(Feature(Type = Feature.Types.Type.DocumentTextDetection))
            request

        task {
            let parameters = match annotations with | Single s -> [ s ] | Many m -> m
            let requests = parameters |> List.map transformParameter            

            let! operation = client.Client.AsyncBatchAnnotateFilesAsync(requests)
            let! pollStatus = operation.PollUntilCompletedAsync()

            if pollStatus.IsCompleted && not <| pollStatus.IsFaulted then
                return Ok pollStatus.Result
            else
                return Error (operation.RpcMessage.ResultCase.ToString() + " - " + operation.RpcMessage.ResultCase.ToString())
        } 