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

Build and run the editor:

```
dotnet run --project noz/editor/platform/desktop/NoZ.Editor.Desktop.csproj -- --project .
```

or on windows

```
noz/editor.ps1
```


## Creating a New Game Project

### Quick Start

```bash
# 1. Create and navigate to your project directory
mkdir MyGame
cd MyGame

# 2. Initialize git repository
git init

# 3. Add the NOZ engine as a submodule
git submodule add https://github.com/nozgames/noz-cs noz
git submodule update --init --recursive

# 4. Build the NOZ editor from the submodule
dotnet build noz/editor

# 5. Initialize the game project structure
dotnet run --project noz/editor/platform/desktop/NoZ.Editor.Desktop.csproj -- init

# 6. Restore NuGet packages and build the project
dotnet restore
dotnet build

# 7. Run the desktop version
dotnet run --project platform/desktop

```
