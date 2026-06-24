Imports System.Collections
Imports System.IO
Imports System.Resources
Imports dnlib.DotNet
Imports dnlib.DotNet.Writer
Imports Microsoft.Extensions.FileProviders

Module Program

    ' Pfad zur Affinity-Installation
    Private ReadOnly AssemblyDirectory As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wine/drive_c/Program Files/Affinity/Affinity")
    Private Const DllName As String = "Serif.Affinity.dll"

    Private Const NopTargetClass As String = "Serif.Affinity.Application"
    Private Const NopTargetMethod As String = "OnMainWindowLoaded"
    Private Const NopEndOffset As Integer = 63  ' letzter zu entfernender CIL-Bereich (Lizenzcheck + base-Aufruf)

    Private ReadOnly WineNetBase As String = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".wine/drive_c/windows/Microsoft.NET")

    Sub Main(args As String())
        Dim dllPath As String
        If args.Length > 0 AndAlso Not String.IsNullOrWhiteSpace(args(0)) Then
            dllPath = args(0)
        Else
            dllPath = Path.Combine(AssemblyDirectory, DllName)
        End If
        Dim backupPath = dllPath & ".bak"

        Console.WriteLine("=== Affinity Patcher ===")
        Console.WriteLine()

        ' Prüfen ob DLL leer/defekt ist, ggf. aus Backup wiederherstellen
        Dim fileInfo = New FileInfo(dllPath)
        If Not fileInfo.Exists Then
            WriteColoredLine($"Fehler: Assembly nicht gefunden: {dllPath}", ConsoleColor.Red)
            Return
        End If
        If fileInfo.Length = 0 Then
            WriteColoredLine("Assembly-Datei ist leer. Versuche Wiederherstellung aus Backup...", ConsoleColor.Yellow)
            If File.Exists(backupPath) Then
                File.Copy(backupPath, dllPath, overwrite:=True)
                WriteColoredLine("Aus Backup wiederhergestellt.", ConsoleColor.Green)
            Else
                WriteColoredLine("Kein Backup vorhanden. Abbruch.", ConsoleColor.Red)
                Return
            End If
        End If

        ' Schreibzugriff prüfen
        If Not CheckWriteAccess(AssemblyDirectory) Then Return

        ' Backup anlegen
        If Not File.Exists(backupPath) Then
            File.Copy(dllPath, backupPath)
            Console.WriteLine($"Backup erstellt: {backupPath}")
        Else
            Console.WriteLine($"Backup bereits vorhanden: {backupPath}")
        End If
        
        Dim dllBytes = File.ReadAllBytes(dllPath)
        Using module_ As ModuleDefMD = ModuleDefMD.Load(
            dllBytes,
            New ModuleCreationOptions(ModuleDef.CreateModuleContext()))

            Console.WriteLine()
            Console.WriteLine("Schritt 1: NOP-Patch auf OnMainWindowLoaded anwenden...")
            ApplyNopPatch(module_)

            Console.WriteLine()
            Console.WriteLine("Schritt 2: Parallele Font-Enumeration deaktivieren...")
            ApplyFontEnumerationPatch(module_)

            Console.WriteLine()
            Console.WriteLine("Schritt 3: Monochrome Icons durch farbige v2-Icons ersetzen...")
            Dim tempFile = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName())
            MergeResources(module_, tempFile)
            ReplaceResources(module_, tempFile)
            File.Delete(tempFile)

            SaveDll(module_, dllPath)
        End Using

        Console.WriteLine()
        WriteColoredLine("Fertig.", ConsoleColor.Green)
    End Sub

    Function CheckWriteAccess(directory As String) As Boolean
        Try
            Dim tempFile = Path.Combine(directory, Path.GetRandomFileName())
            Using File.Create(tempFile, 1, FileOptions.DeleteOnClose)
            End Using
            WriteColoredLine($"Schreibzugriff auf ""{directory}"" bestätigt.", ConsoleColor.Green)
            Return True
        Catch ex As UnauthorizedAccessException
            WriteColoredLine($"Kein Schreibzugriff auf ""{directory}"".", ConsoleColor.Red)
        Catch ex As IOException
            WriteColoredLine($"Schreiben nach ""{directory}"" fehlgeschlagen.", ConsoleColor.Red)
        End Try
        Return False
    End Function

    ' ── Colorize Icons ─────────────────────────────────────────────────────────


    Sub MergeResources(module_ As ModuleDefMD, resourcesFile As String)
        File.Delete(resourcesFile)
        Dim disposables As New Disposables()

        Dim v2Reader = GetV2ResourceReader(disposables)
        Dim v3Reader = GetV3ResourceReader(module_, disposables)
        Dim writer As New ResourceWriter(resourcesFile)

        For Each entry As DictionaryEntry In v3Reader
            Dim key = If(entry.Key?.ToString(), "")

            If Not key.EndsWith(".png") OrElse
               (Not key.StartsWith("resources/icons/tools/") AndAlso
                Not key.StartsWith("resources/icons/colourpicker.imageset") AndAlso
                Not key.StartsWith("resources/icons/formatdropper.imageset")) Then
                writer.AddResource(key, entry.Value)
                Continue For
            End If

            Try
                Dim v2Resource = FindV2Resource(key, v2Reader)
                writer.AddResource(key, If(v2Resource, entry.Value))
                WriteColoredLine($"v2-Resource eingebunden: ""{key}"".", ConsoleColor.Green)
            Catch ex As Exception
                WriteColoredLine($"Fehler bei v2-Resource ""{key}"": {ex.Message}", ConsoleColor.Red)
                writer.AddResource(key, entry.Value)
            End Try
        Next

        writer.Dispose()
        v2Reader.Dispose()
        v3Reader.Dispose()
        disposables.Dispose()
    End Sub

    Function FindV2Resource(key As String, v2Reader As ResourceReader) As Object
        Dim v2Key = GetV2ResourceKey(key)
        For Each entry As DictionaryEntry In v2Reader
            If entry.Key?.ToString() = v2Key Then Return entry.Value
        Next
        WriteColoredLine($"v2-Resource nicht gefunden: ""{key}"".", ConsoleColor.Yellow)
        Return Nothing
    End Function

    Function GetV2ResourceKey(v3Key As String) As String
        Select Case v3Key
            Case "resources/icons/tools/brushtool.imageset/paint%20brush%20tool_2.png"
                Return "resources/icons/tools/brushtool.imageset/paint%20brush%20tool.png"
            Case "resources/icons/tools/brushtool.imageset/paint%20brush%20tool@2x_2.png"
                Return "resources/icons/tools/brushtool.imageset/paint%20brush%20tool@2x.png"
            Case "resources/icons/tools/objectselectiontool.imageset/object%20selection%20tool.png"
                Return "resources/icons/tools/objectselectiontool.imageset/object_selection_tool.png"
            Case "resources/icons/tools/objectselectiontool.imageset/object%20selection%20tool@2x.png"
                Return "resources/icons/tools/objectselectiontool.imageset/object_selection_tool@2x.png"
            Case "resources/icons/tools/measuretool.imageset/measure%20tool.png"
                Return "resources/icons/tools/measuretool.imageset/measuretool.png"
            Case "resources/icons/tools/measuretool.imageset/measure%20tool@2x.png"
                Return "resources/icons/tools/measuretool.imageset/measuretool@2x.png"
            Case "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool%20mono.png"
                Return "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool.png"
            Case "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool%20mono@2x.png"
                Return "resources/icons/tools/strokewidthtool.imageset/line%20width%20tool@2x.png"
            Case "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20tool.png"
                Return "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20brush%20tool.png"
            Case "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20tool@2x.png"
                Return "resources/icons/tools/inpaintingbrushtool.imageset/inpainting%20brush%20tool@2x.png"
            Case Else
                Return v3Key
        End Select
    End Function

    Function GetV2ResourceReader(disposables As Disposables) As ResourceReader
        Dim provider = New ManifestEmbeddedFileProvider(GetType(Program).Assembly)
        Dim stream = provider.GetFileInfo("Serif.Affinity.v2.g.resources").CreateReadStream()
        disposables.Add(stream)
        Return New ResourceReader(stream)
    End Function

    Function GetV3ResourceReader(module_ As ModuleDefMD, disposables As Disposables) As ResourceReader
        Dim reader = module_.Resources.FindEmbeddedResource("Serif.Affinity.g.resources").CreateReader()
        Dim stream = reader.AsStream()
        disposables.Add(stream)
        Return New ResourceReader(stream)
    End Function

    Sub ReplaceResources(module_ As ModuleDefMD, resourcesFile As String)
        Dim resourceIndex = module_.Resources.IndexOf("Serif.Affinity.g.resources")
        Using fs As New FileStream(resourcesFile, FileMode.Open)
            Dim buffer(CInt(fs.Length) - 1) As Byte
            fs.ReadExactly(buffer, 0, buffer.Length)
            Dim newResource As New dnlib.DotNet.EmbeddedResource(
                "Serif.Affinity.g.resources",
                buffer,
                dnlib.DotNet.ManifestResourceAttributes.Public)
            module_.Resources(resourceIndex) = newResource
        End Using
    End Sub

    Sub SaveDll(module_ As ModuleDefMD, path As String)
        File.Delete(path)
        If module_.IsILOnly Then
            module_.Write(path)
        Else
            module_.NativeWrite(path, New NativeModuleWriterOptions(module_, False))
        End If
        Console.WriteLine($"DLL gespeichert: ""{path}""")
    End Sub

    ' ── NOP-Patch ──────────────────────────────────────────────────────────────

    Sub ApplyNopPatch(module_ As ModuleDefMD)
        Dim appType = module_.Types.FirstOrDefault(
            Function(t) t.FullName.Contains(NopTargetClass))
        Dim method = appType?.FindMethod(NopTargetMethod)

        If method Is Nothing Then
            WriteColoredLine($"Fehler: Methode {NopTargetClass}.{NopTargetMethod} nicht gefunden.", ConsoleColor.Red)
            Return
        End If

        Dim instructions = method.Body.Instructions
        Dim nopCount = 0
        For Each ins In instructions
            If ins.Offset > NopEndOffset Then Continue For
            ins.OpCode = dnlib.DotNet.Emit.OpCodes.Nop
            ins.Operand = Nothing
            nopCount += 1
        Next
        Console.WriteLine($"{nopCount} Instruktionen durch NOP ersetzt.")
        WriteColoredLine($"ERFOLG: NOP-Patch auf {NopTargetMethod} angewendet.", ConsoleColor.Green)
    End Sub

    ' ── Font-Enumeration-Patch ─────────────────────────────────────────────────

    Sub ApplyFontEnumerationPatch(module_ As ModuleDefMD)
        Dim appType = module_.Types.FirstOrDefault(
            Function(t) t.FullName = "Serif.Affinity.Application")
        Dim prop = appType?.Properties.FirstOrDefault(
            Function(p) p.Name = "ParallelFontEnumerationDisabled")
        Dim getter = prop?.GetMethod

        If getter Is Nothing Then
            WriteColoredLine("Fehler: ParallelFontEnumerationDisabled getter nicht gefunden.", ConsoleColor.Red)
            Return
        End If

        Dim instructions = getter.Body.Instructions
        instructions.Clear()
        instructions.Add(dnlib.DotNet.Emit.Instruction.Create(dnlib.DotNet.Emit.OpCodes.Ldc_I4_1))
        instructions.Add(dnlib.DotNet.Emit.Instruction.Create(dnlib.DotNet.Emit.OpCodes.Ret))
        WriteColoredLine("ERFOLG: ParallelFontEnumerationDisabled gibt jetzt immer true zurück.", ConsoleColor.Green)
    End Sub

    ' ── Hilfsfunktionen ────────────────────────────────────────────────────────

    Sub WriteColoredLine(message As String, color As ConsoleColor)
        Dim prev = Console.ForegroundColor
        Console.ForegroundColor = color
        Console.WriteLine(message)
        Console.ForegroundColor = prev
    End Sub

End Module

Friend NotInheritable Class Disposables
    Implements IDisposable

    Private ReadOnly _items As New List(Of IDisposable)()

    Sub Add(item As IDisposable)
        _items.Add(item)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        For Each item In _items
            item.Dispose()
        Next
    End Sub

End Class
