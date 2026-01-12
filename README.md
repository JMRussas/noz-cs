# Noz Game Engine

A 2d game engine written in C#

## Requirements

- .NET 10 SDK

```
winget install Microsoft.DotNet.SDK.10
```

## Project Structure

- `src/` - Noz engine (class library)
- `editor/` - Noz editor (windowed application)

## Building

Build the editor:
```
dotnet build editor/editor.csproj
```

Build the engine only:
```
dotnet build src/noz.csproj
```

## Running

```
dotnet run --project editor/editor.csproj
```
