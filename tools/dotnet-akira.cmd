@echo off
rem Atalho "dotnet akira": o dotnet procura dotnet-<nome> no PATH quando o comando nao existe.
rem Precisa da pasta tools no PATH. Argumentos extras vao para o app (ex.: dotnet akira --hidden).
dotnet run --project "%~dp0..\src\AkiraCore.App" -- %*
