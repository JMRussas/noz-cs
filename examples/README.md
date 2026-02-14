# NoZ Examples

Example projects demonstrating the NoZ engine.

## Examples

- **hello-world** - Minimal project with a window, colored panel, and text label.

## Building & Running

Each example is a standalone .NET solution. To run an example:

```
cd examples/hello-world
dotnet run --project platform/desktop/HelloWorld.Desktop.csproj
```

## Assets

All examples share the `assets/` and `library/` folders in this directory. The compiled assets in `library/` are pre-built and checked into source control.

To rebuild assets after modifying `assets/`, open this directory in the NoZ editor:

```
dotnet run --project ../editor/NoZ.Editor.csproj -- --project ./examples
```
