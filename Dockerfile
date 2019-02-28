FROM ubuntu:18.04 as builder
  
ENV DEBIAN_FRONTEND noninteractive
ENV DOTNET_SKIP_FIRST_TIME_EXPERIENCE 1
ENV DOTNET_CLI_TELEMETRY_OPTOUT 1
ENV NUGET_XMLDOC_MODE "skip"
ENV SNAPX_DOCKER_BUILD 1

RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    wget sudo apt-utils apt-transport-https lsb-release ca-certificates cmake \
    build-essential pkg-config software-properties-common gcc g++ \
    mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686 uuid-dev upx-ucl

RUN \
  wget -q https://packages.microsoft.com/config/ubuntu/18.04/packages-microsoft-prod.deb
RUN \
  dpkg -i packages-microsoft-prod.deb 
RUN \
  apt-get update && apt-get install -y --no-install-recommends powershell
RUN \
  add-apt-repository universe
RUN \
  apt-get update && apt-get install -y dotnet-sdk-2.2

FROM builder as runner
WORKDIR /build/snapx

COPY src .
COPY tools .
COPY cmake .
COPY nuget.config .
COPY Directory.Build.props .
COPY Directory.Build.targets .
COPY Version.props .
COPY global.json .
COPY bootstrap.ps1 .
COPY build.ps1 .

RUN \
  /usr/bin/pwsh -f build.ps1

CMD ["snapx"]