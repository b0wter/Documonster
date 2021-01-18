namespace b0wter.DocuMonster.Google

module Document =
    open Response
    open b0wter.DocuMonster.SharedEntities.Document

    let private pageFromResponse (response: Response.Response) =
        if response.FullTextAnnotation.Pages.Length <= 0 then
            Error "The response has a length of zero."
        else if response.FullTextAnnotation.Pages.Length > 1 then
            Error "The response includes multiple pages."
        else
            let p = response.FullTextAnnotation.Pages.[0]
            {
                Width = p.Width
                Height = p.Height
                Blocks = p.Blocks
                Text = response.FullTextAnnotation.Text
            } |> Ok

    let fromResponse (responses: Responses) =
        if responses.Responses.IsEmpty then
            Error "The response did not contain any Response-objects."
        else if responses.Responses |> List.map (fun r -> r.Context.Uri) |> List.distinct |> List.length > 1 then
            Error "The response included more than one filename. This is not yet supported."
        else
            let rec step (acc: Page list) (remaining: Response.Response list) =
                match remaining with
                | head :: tail ->
                    match head |> pageFromResponse with
                    | Ok page -> step (page :: acc) tail
                    | Error e -> Error e
                | [] ->
                    Ok (acc |> List.rev)
                    
            let filename = responses.Responses.Head.Context.Uri |> System.IO.Path.GetFileName

            let r = step [] responses.Responses
            r |> Result.map (fun r -> { Pages = r; Filename = filename; Name = None })
