$editorProject = Join-Path $PSScriptRoot "editor/program/NoZ.Editor.Program.csproj"
dotnet run --project $editorProject -- --project . --import @args
