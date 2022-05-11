FROM ubuntu:20.04 as env-build

ENV DEBIAN_FRONTEND=noninteractive
ENV SNAPX_DOCKER_WORKING_DIR /build/snapx

ARG DOTNET_60_SDK_VERSION=6.0.300
ARG DOTNET_70_SDK_VERSION=7.0.100-preview.4.22252.9
ARG DOTNET_RID=linux-x64


# amd64
RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake make gcc g++ lsb-core curl wget gcc-aarch64-linux-gnu:amd64 g++-aarch64-linux-gnu:amd64

RUN \
	apt-get update && \
	apt-get install -y apt-transport-https:amd64 ca-certificates:amd64 && \
	wget --no-check-certificate https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
 	dpkg -i packages-microsoft-prod.deb && \
	apt-get update && \
	rm packages-microsoft-prod.deb
	       
RUN \
  wget https://dotnetcli.blob.core.windows.net/dotnet/Sdk/${DOTNET_70_SDK_VERSION}/dotnet-sdk-${DOTNET_70_SDK_VERSION}-${DOTNET_RID}.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-${DOTNET_70_SDK_VERSION}-${DOTNET_RID}.tar.gz -C /root/dotnet && \
  rm dotnet-sdk-${DOTNET_70_SDK_VERSION}-${DOTNET_RID}.tar.gz

RUN \
  wget https://dotnetcli.blob.core.windows.net/dotnet/Sdk/${DOTNET_60_SDK_VERSION}/dotnet-sdk-${DOTNET_60_SDK_VERSION}-${DOTNET_RID}.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-${DOTNET_60_SDK_VERSION}-${DOTNET_RID}.tar.gz -C /root/dotnet && \
  rm dotnet-sdk-${DOTNET_60_SDK_VERSION}-${DOTNET_RID}.tar.gz

RUN \
  /root/dotnet/dotnet tool update powershell -g

FROM env-build as env-run
ENV DOTNET_ROOT="/root/dotnet"
ENV PATH="/root/dotnet:/root/.dotnet/tools:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["sh", "-c", "(cd $SNAPX_DOCKER_WORKING_DIR && pwsh ./build.ps1 $BUILD_PS_PARAMETERS)"]
