$editorProject = Join-Path $PSScriptRoot "editor/NoZ.Editor.csproj"
dotnet run --project $editorProject -- --project . --import @args
