#!/bin/bash
dotnet build -c Debug src/Snapx/Snapx.csproj
dotnet src/Snapx/bin/Debug/netcoreapp2.2/snapx.dll $*
