#!/bin/bash

echo "Building Win-x64"
dotnet publish LuaExpose.csproj -r win-x64 -c Release /p:PublishSingleFile=true
echo "Building OSX-x64"
dotnet publish LuaExpose.csproj -r osx-x64 -c Release /p:PublishSingleFile=true
echo "Building Linux-x64"
dotnet publish LuaExpose.csproj -r linux-x64 -c Release /p:PublishSingleFile=true