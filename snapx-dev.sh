#!/bin/bash
dotnet build -c Debug src/Snapx/Snapx.csproj
dotnet src/Snapx/bin/Debug/netcoreapp3.1/snapx.dll $*
