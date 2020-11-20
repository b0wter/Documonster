namespace b0wter.DocuMonster.Google

module Response =

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

    type Paragraph = {
        BoundingBox: BoundingBox
        Words: Word list
    }

    type Block = {
        BoundingBox: BoundingBox
        Paragraphs: Paragraph list
    }

    type Page = {
        Width: int
        Height: int
        Blocks: Block list
    }

    type Document = {
        Pages: Page list
    }

    type FullTextAnnotation = {
        Pages: Page list
        Text: string
    }

    type Context = {
        Uri: string
        PageNumber: int
    }

    type Response = {
        FullTextAnnotation: FullTextAnnotation
        Context: Context
    }

    type Responses = {
        Responses: Response list
    }
