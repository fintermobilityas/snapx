#!/bin/bash
dotnet build -c Debug src/Snapx/Snapx.csproj
dotnet src/Snapx/bin/Debug/net6.0/snapx.dll $*
