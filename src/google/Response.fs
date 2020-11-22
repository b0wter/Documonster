namespace b0wter.DocuMonster.Google

module Response =
    open b0wter.DocuMonster.SharedEntities.Document

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
