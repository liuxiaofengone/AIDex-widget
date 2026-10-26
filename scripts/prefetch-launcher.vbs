' CodexBarWidget prefetch launcher: start the PowerShell worker fully hidden.
'
' Why not run powershell.exe directly from the scheduled task? With
' -WindowStyle Hidden, conhost still creates a console window first and
' PowerShell hides it afterwards, so the window flashes on every run on
' some systems. Launching through wscript with window style 0 creates the
' process hidden from birth, so there is no flash at all.
'
' This file intentionally contains ASCII only, so no encoding handling is
' needed on any Windows Script Host version.
Option Explicit
Dim sh, fso, scriptDir, ps1
Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
ps1 = fso.BuildPath(scriptDir, "prefetch-usage.ps1")
sh.Run "powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & ps1 & """", 0, False
