# 📖 About Snapx

[![Gitter](https://badges.gitter.im/fintermobilityas-snapx/community.svg)](https://gitter.im/fintermobilityas-snapx/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge) ![License](https://img.shields.io/github/license/fintermobilityas/snapx.svg)
<br>
[![NuGet](https://img.shields.io/nuget/v/snapx.svg)](https://www.nuget.org/packages/snapx) [![downloads](https://img.shields.io/nuget/dt/snapx)](https://www.nuget.org/packages/snapx) ![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/snapx) ![Size](https://img.shields.io/github/repo-size/fintermobilityas/snapx.svg)

| Build server   | Platforms                                | Build status                                                                                                 |
| -------------- | ---------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| Github Actions | linux-x64, linux-arm64, win-x86, win-x64 | Branch: develop ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=develop) |
| Github Actions | linux-x64, linux-arm64, win-x86, win-x64 | Branch: master ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=master)   |

**snapx** is a powerful xplat .NET application with built-in support for delta updates, release channels (test, staging, production) and automatic deployment using GitHub Actions. Updates can delivered via NuGet or network share (UNC).

## Sponsors

[![Finter As](https://static.wixstatic.com/media/c40fc8_f72c1de227614b2cbd30085f7c31abe7~mv2.png/v1/crop/x_0,y_34,w_1020,h_470/fill/w_162,h_71,al_c,q_85,usm_0.66_1.00_0.01,enc_auto/Finter%20AS%20Logo.png)](https://finter.no)

[![Hosted By: Cloudsmith](https://img.shields.io/badge/OSS%20hosting%20by-cloudsmith-blue?logo=cloudsmith&style=for-the-badge)](https://cloudsmith.com)

Package repository hosting is graciously provided by  [Cloudsmith](https://cloudsmith.com).
Cloudsmith is the only fully hosted, cloud-native, universal package management solution, that
enables your organization to create, store and share packages in any format, to any place, with total
confidence.

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
- GitVersion `dotnet tool update gitversion.tool -g`
- Powershell v7 `dotnet tool update powershell -g`
- .NET SDK v6.0
- .NET SDK v7.0
- .NET SDK v8.0

**Windows**:

- Docker Desktop >= v4.0.1
- GitVersion `dotnet tool update gitversion.tool -g`
- Powershell v7 `dotnet tool update powershell -g`
- .NET SDK v6.0
- .NET SDK v7.0
- .NET SDK v8.0

- Visual Studio 2022 Community Edition with C++ workload installed

#### Bootstrap snapx

Before you can open `src\Snapx.sln` in Visual Studio you must bootstrap dependencies.
Run `init.ps1` and all dependencies will be built in `Debug` and `Release` mode.

## .NET frameworks supported

- .NET 6.0 LTS
- .NET 7.0
- .NET 8.0

## Platforms supported

- Windows x86/x64

  - Windows 7 SP1
  - Windows Vista SP 2
  - Windows 8.1
  - Windows Server 2008 R2
  - Windows Server 2012 R2
  - Windows Server 2016 R2
  - Windows Server 2019 R2
  - Windows Server 2022 R2
 
- Ubuntu Desktop x64

  - 18.04
  - 20.04
  - 22.04
  - 24.04

- Ubuntu Server x64

  - 18.04
  - 20.04
  - 22.04
  - 24.04

- Ubuntu Desktop arm64

  - 18.04
  - 20.04
  - 22.04
  - 24.04

- Ubuntu Server arm64
  - 18.04
  - 20.04
  - 22.04
  - 24.04
  
- Raspberry Pi OS arm64

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [Code of Conduct](https://github.com/fintermobilityas/snapx/blob/develop/CODE_OF_CONDUCT.md).

## License

Snapx is under the MIT license. See the [LICENSE](LICENSE.md) for more information.
