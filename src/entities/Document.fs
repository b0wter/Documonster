namespace b0wter.DocuMonster.SharedEntities
open System

module Document =
    type Point = {
        X: float
        Y: float
    }

    type BoundingBox = {
        NormalizedVertices: Point list
    }

    type Symbol = {
        BoundingBox: BoundingBox
        Text: string
        Confidence: float
    }

    type Word = {
        BoundingBox: BoundingBox
        Symbols: Symbol list
        Confidence: float
    }
    
    let word (w: Word) : string =
        w.Symbols |> List.fold (fun accumulator next -> accumulator + next.Text) String.Empty  

    type Paragraph = {
        BoundingBox: BoundingBox
        Words: Word list
    }
    
    let paragraph (p: Paragraph) =
        p.Words |> List.fold (fun accumulator next -> accumulator + " " + (next |> word)) String.Empty

    type Block = {
        BoundingBox: BoundingBox
        Paragraphs: Paragraph list
    }
    
    let block (b: Block) =
        b.Paragraphs |> List.fold (fun accumulator next -> accumulator + Environment.NewLine + (next |> paragraph))
                            String.Empty
    
    type Page = {
        Width: int
        Height: int
        Blocks: Block list
        Text: string
    }
    
    let page (p: Page) =
        p.Text
        //p.Blocks |> List.fold (fun accumulator next -> accumulator + Environment.NewLine + Environment.NewLine +
        //                                               (next |> block)) String.Empty

    type Document = {
        Pages: Page list
        Filename: string
        Name: string option
    }
    
    let document (d: Document) =
        d.Pages |> List.fold (fun accumulator next -> accumulator + Environment.NewLine + Environment.NewLine +
                                                       (next |> page)) String.Empty
    