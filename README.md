# 📖 About Snapx 

![dependabot](https://api.dependabot.com/badges/status?host=github&repo=fintermobilityas/snapx) [![Gitter](https://badges.gitter.im/fintermobilityas-snapx/community.svg)](https://gitter.im/fintermobilityas-snapx/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge) ![License](https://img.shields.io/github/license/fintermobilityas/snapx.svg)
<br>
[![NuGet](https://img.shields.io/nuget/v/snapx.svg)](https://www.nuget.org/packages/snapx) [![downloads](https://img.shields.io/nuget/dt/snapx)](https://www.nuget.org/packages/snapx) ![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/snapx) ![Size](https://img.shields.io/github/repo-size/fintermobilityas/snapx.svg) 

| Build server | Platforms | Build status |
|--------------|----------|--------------|
| Github Actions | linux-latest, windows-latest | Branch: develop ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=develop) |
| Github Actions | linux-latest, windows-latest | Branch: master ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=master) |

**snapx** is a powerful xplat .NET application with built-in support for delta updates, release channels (test, staging, production) and automatic deployment using GitHub Actions. Updates can delivered via NuGet or network share (UNC).

## 🚀 Getting Started Guide

Checkout our sample application, [snapx demoapp](https://github.com/fintermobilityas/snapx.demoapp). It features an xplat application (Windows and Ubuntu) that supports automatic release deployment using GitHub Actions.

### Showcase

#### What does the installer look like?

<img src="https://media.githubusercontent.com/media/fintermobilityas/snapx/develop/docs/snapxinstaller.gif" width="794" />

#### Available commands
![snapx usage](https://github.com/fintermobilityas/snapx/blob/develop/docs/shell.png)

### Local development requirements

#### Build requirements

**Linux**

- Docker >= 19.03.8

**Windows**:
- Docker Desktop >= v2.3.0.3
- GitVersion `dotnet tool update gitversion.tool -g`
- Powershell v7 `dotnet tool update powershell -g`
- .NET SDK v2.1 
- .NET SDK v3.1 
- .NET SDK v5.0-preview.6 

- Visual Studio 2019 16.7 Preview Community with C++ workload installed

#### Bootstrap snapx 

Before you can open `src\Snapx.sln` in Visual Studio you must bootstrap dependencies.
Run `init.ps1` and all dependencies will be built in `Debug` and `Release` mode.

## .NET frameworks supported

- .NET Core >= 2.1 
- .NET Full Framework >= 4.6.1 

## Platforms supported

- Windows x86/x64 
    - Windows 7 SP1
    - Windows Vista SP 2
    - Windows 8.1
    - Windows Server 2008 R2
    - Windows Server 2012 R2
    - Windows Server 2016 R2
    - Windows Server 2019 R2

- Ubuntu Desktop x64 
    - 16.04
    - 18.04
    - 20.04

- Ubuntu Server x64 
    - 16.04
    - 18.04
    - 20.04
    
## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [Code of Conduct](https://github.com/fintermobilityas/snapx/blob/develop/CODE_OF_CONDUCT.md). 

## Sponsors
<p align="center">
<a href="https://www.finterjobs.com" target="_blank"><img src="https://static.wixstatic.com/media/49c5ac_e5c089f7be224d6e92eb3f2f5edc3535~mv2.png/v1/crop/x_173,y_545,w_938,h_425/fill/w_189,h_87,al_c,q_85,usm_0.66_1.00_0.01/Finter%20Mobility%20AS%20gjennomsiktig%20bakgrun.webp"></a>
</p>

## License
Snapx is under the MIT license. See the [LICENSE](LICENSE.md) for more information.
