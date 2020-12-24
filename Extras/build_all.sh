#!/bin/bash

echo "Building Win-x64"
dotnet publish LuaExpose.csproj -r win-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
echo "Building OSX-x64"
dotnet publish LuaExpose.csproj -r osx-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
echo "Building Linux-x64"
dotnet publish LuaExpose.csproj -r linux-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
echo "Building ubuntu.18.04-x64"
dotnet publish LuaExpose.csproj -r ubuntu.18.04-x64 -c Release /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true