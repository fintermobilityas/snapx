# 📖 About Snapx 

![dependabot](https://api.dependabot.com/badges/status?host=github&repo=fintermobilityas/snapx) [![Gitter](https://badges.gitter.im/fintermobilityas-snapx/community.svg)](https://gitter.im/fintermobilityas-snapx/community?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge) ![License](https://img.shields.io/github/license/fintermobilityas/snapx.svg)
<br>
[![NuGet](https://img.shields.io/nuget/v/snapx.svg)](https://www.nuget.org/packages/snapx) [![downloads](https://img.shields.io/nuget/dt/snapx)](https://www.nuget.org/packages/snapx) ![Nuget (with prereleases)](https://img.shields.io/nuget/vpre/snapx) ![Size](https://img.shields.io/github/repo-size/fintermobilityas/snapx.svg) 

| Build server | Platforms | Build status |
|--------------|----------|--------------|
| Github Actions | linux-latest, windows-latest | Branch: develop ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=develop) |
| Github Actions | linux-latest, windows-latest | Branch: master ![snapx](https://github.com/fintermobilityas/snapx/workflows/snapx/badge.svg?branch=master) |

**snapx** is a powerful xplat .NET application with built-in support for delta updates, release channels (test, staging, production) and automatic deployment using GitHub Actions. 

## 🚀 Getting Started Guide

Checkout our sample application, [snapx demoapp](https://github.com/fintermobilityas/snapx.demoapp). It features an xplat application (Windows and Ubuntu) that supports automatic release deployment using GitHub Actions.

### Local development requirements

#### Build requirements

**Linux**

- Docker >= 19.03.8
- .NET Core SDK v3.1 
- Powershell v7

**Windows**:
- Docker Desktop >= v2.3.0.3
- Powershell v7
- Visual Studio 2019 16.6 Community with C++ / .NET Core Sdk workload installed. 

#### Bootstrap snapx 

Before you can open `src\Snapx.sln` in Visual Studio you must bootstrap dependencies.
Run `init.ps1` and all dependencies will be built in `Debug` and `Release` mode.

## .NET frameworks supported

- .NET Core >= 3.1 (netcoreapp3.1)
- .NET Full Framework >= 4.7.2 (net472)

## Platforms supported

- Windows x64 [7.1 SP 1, 10]
- Ubuntu Desktop x64 [16.04, 20.04]
- Ubuntu Server x64 [16.04, 20.04]

## Using snapx

All available commands has usage examples if you append `snapx [command] --help` in your favourite shell. 

![snapx usage](https://github.com/fintermobilityas/snapx/blob/develop/docs/shell.png)

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [Code of Conduct](https://github.com/fintermobilityas/snapx/blob/develop/CODE_OF_CONDUCT.md). 

## Sponsors
<p align="center">
<a href="https://www.finterjobs.com" target="_blank"><img src="https://static.wixstatic.com/media/49c5ac_e5c089f7be224d6e92eb3f2f5edc3535~mv2.png/v1/crop/x_173,y_545,w_938,h_425/fill/w_189,h_87,al_c,q_85,usm_0.66_1.00_0.01/Finter%20Mobility%20AS%20gjennomsiktig%20bakgrun.webp"></a>
</p>

## License
Snapx is under the MIT license. See the [LICENSE](LICENSE.md) for more information.
