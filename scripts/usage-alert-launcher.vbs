' AIDexWidget usage alert launcher: start the PowerShell worker fully hidden.
'
' Same reason as the old prefetch launcher: wscript with window style 0
' creates the process hidden from birth, so no console flash on every run.
' The threshold comes from the scheduled task action as the first argument.
' This file intentionally contains ASCII only.
Option Explicit
Dim sh, fso, scriptDir, ps1, threshold
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = fso.BuildPath(scriptDir, "usage-alert.ps1")
If WScript.Arguments.Count >= 1 Then
    threshold = WScript.Arguments(0)
Else
    threshold = "10"
End If
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """ -Threshold " & threshold, 0, False
