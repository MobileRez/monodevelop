﻿namespace MonoDevelop.FSharp

open FSharp.CompilerBinding
open ICSharpCode.NRefactory.TypeSystem
open Microsoft.FSharp.Compiler
open MonoDevelop.Core
open MonoDevelop.Ide
open MonoDevelop.Ide.Gui
open MonoDevelop.Ide.TypeSystem
open System 
open System.Diagnostics
open System.IO
open System.Text
open System.Threading

type FSharpParsedDocument(fileName) = 
    inherit DefaultParsedDocument(fileName)

// An instance of this type is created by MonoDevelop (as defined in the .xml for the AddIn) 
type FSharpParser() = 
    inherit TypeSystemParser()

    let languageService = MDLanguageService.Instance 
    /// Split a line so it fits to a line width
    let splitLine (sb : StringBuilder) (line : string) lineWidth = 
        let emit (s : string) = sb.Append(s) |> ignore
        
        let indent = 
            line
            |> Seq.takeWhile (fun c -> c = ' ')
            |> Seq.length
        
        let words = line.Split(' ')
        let mutable i = 0
        let mutable first = true
        for word in words do
            if first || i + word.Length < lineWidth then 
                emit word
                emit " "
                i <- i + word.Length + 1
                first <- false
            else 
                sb.AppendLine() |> ignore
                for i in 1..indent do
                    emit " "
                emit word
                emit " "
                i <- indent + word.Length + 1
                first <- true
        sb.AppendLine() |> ignore
    
    /// Wrap text so it fits to a line width
    let wrapText (text : String) lineWidth = 
        //dont wrap empty lines
        if text.Length = 0 then text
        else 
            let sb = StringBuilder()
            let lines = text.Split [| '\r'; '\n' |]
            for line in lines do
                if line.Length <= lineWidth then sb.AppendLine(line) |> ignore
                else splitLine sb line lineWidth
            sb.ToString()
    
    /// Format errors for the given line (if there are multiple, we collapse them into a single one)
    let formatError (error : FSharpErrorInfo) = 
        // Single error for this line
        let errorType = 
            if error.Severity = FSharpErrorSeverity.Error then ErrorType.Error
            else ErrorType.Warning
        Error(errorType, wrapText error.Message 80, Editor.DocumentRegion (error.StartLineAlternate, error.StartColumn + 1, error.EndLineAlternate, error.EndColumn + 1))

    let tryGetFilePath fileName (project: MonoDevelop.Projects.Project) = 
        // TriggerParse will work only for full paths
        if IO.Path.IsPathRooted(fileName) then Some(fileName)
        else 
            let workBench = IdeApp.Workbench
            if (workBench <> null) then
                
                let doc = workBench.ActiveDocument
                if doc <> null then 
                    let file = doc.FileName.FullPath.ToString()
                    if file = "" then None else Some file
                else None
            else 
                let filePaths = project.GetItemFiles(true)
                let res = filePaths |> Seq.find(fun t -> t.FileName = fileName)
                Some(res.FullPath.ToString())

    override x.Parse(parseOptions, cancellationToken) =
        let fileName = parseOptions.FileName
        let content = parseOptions.Content
        let proj = parseOptions.Project
        if fileName = null || not (MDLanguageService.SupportedFileName (fileName)) then null
        else

        Async.StartAsTask(
            cancellationToken = cancellationToken,
            computation = async {
            let shortFilename = Path.GetFileName fileName

            let doc = new FSharpParsedDocument(fileName, Flags = ParsedDocumentFlags.NonSerializable)
            //TODO Check if these are needed
            //doc.CreateRefactoringContext <- Func<_, _, _>(fun doc token -> FSharpRefactoringContext() :> _)
            //doc.CreateRefactoringContextWithEditor <- Func<_, _, _, _> (fun data resolver token -> FSharpRefactoringContext() :> _)
            LoggingService.LogInfo ("FSharpParser: [Thread {0}] Parse {1}, ", Thread.CurrentThread.ManagedThreadId, shortFilename)
                                   
            match tryGetFilePath fileName proj with
            | None -> ()
            | Some filePath -> 
                let projFile, files, args = MonoDevelop.getCheckerArgs (proj, filePath)

                let! results =
                    try
                        LoggingService.LogInfo ("FSharpParser: [Thread {0}] Running ParseAndCheckFileInProject for {1}", Thread.CurrentThread.ManagedThreadId, shortFilename)
                        languageService.ParseAndCheckFileInProject(projFile, filePath, content.Text, files, args, false)
                    with
                    | :? TimeoutException ->
                        doc.IsInvalid <- true
                        LoggingService.LogWarning ("FSharpParser: [Thread {0}] ParseAndCheckFileInProject timed out for {1}", Thread.CurrentThread.ManagedThreadId, shortFilename)
                        async.Return ParseAndCheckResults.Empty
                    | :? Tasks.TaskCanceledException ->
                        doc.IsInvalid <- true
                        LoggingService.LogWarning ("FSharpParser: [Thread {0}] ParseAndCheckFileInProject was cancelled for {1}", Thread.CurrentThread.ManagedThreadId, shortFilename)
                        async.Return ParseAndCheckResults.Empty
                    | ex -> doc.IsInvalid <- true
                            LoggingService.LogError("FSharpParser: [Thread {0}] Error processing ParseAndCheckFileResults for {1}", Thread.CurrentThread.ManagedThreadId, shortFilename, ex)
                            async.Return ParseAndCheckResults.Empty
                                                                                     
                results.GetErrors()
                |> Option.iter (Array.map formatError >> Array.iter doc.Add)

                //Set code folding regions, GetNavigationItems may throw in some situations
                try 
                    let regions = 
                        let processDecl (decl : SourceCodeServices.FSharpNavigationDeclarationItem) = 
                            let m = decl.Range
                            FoldingRegion(decl.Name, Editor.DocumentRegion(m.StartLine, m.StartColumn + 1, m.EndLine, m.EndColumn + 1))
                        seq {for toplevel in results.GetNavigationItems() do
                                yield processDecl toplevel.Declaration
                                for next in toplevel.Nested do
                                    yield processDecl next }
                    regions |> Seq.iter doc.Add
                with ex -> LoggingService.LogWarning ("FSharpParser: Couldn't update navigation items.", ex)
                //also store the AST of active results if applicable 
                //Is there any reason not to store the AST? The navigation extension depends on it
                doc.Ast <- results
            doc.LastWriteTimeUtc <- try File.GetLastWriteTimeUtc(fileName) with _ -> DateTime.UtcNow
            return doc :> _})

