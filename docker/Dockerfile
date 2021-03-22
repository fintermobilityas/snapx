FROM ubuntu:20.04 as env-build

ENV DEBIAN_FRONTEND=noninteractive

ENV SNAPX_DOCKER_WORKING_DIR /build/snapx

# arm64
RUN \
  cp /etc/apt/sources.list /etc/apt/sources.list.bak && \
  dpkg --add-architecture arm64 && \
  rm /etc/apt/sources.list && \
  apt-get clean && \
  apt-get autoclean && \
  rm -rf /var/lib/apt/lists/* && \
  echo "deb [arch=arm64] http://ports.ubuntu.com/ubuntu-ports focal main restricted universe multiverse" >> /etc/apt/sources.list && \
  echo "deb [arch=arm64] http://ports.ubuntu.com/ubuntu-ports focal-updates main restricted universe multiverse" >> /etc/apt/sources.list && \
  echo "deb [arch=arm64] http://ports.ubuntu.com/ubuntu-ports focal-backports main restricted universe multiverse" >> /etc/apt/sources.list && \
  echo "deb [arch=arm64] http://ports.ubuntu.com/ubuntu-ports focal-security main restricted universe multiverse" >> /etc/apt/sources.list && \
  apt-get update && \
  apt-get install -y --no-install-recommends \ 
   	 uuid-dev:arm64 

RUN \   
   dpkg --force-architecture --remove-architecture arm64 && \
   cp /etc/apt/sources.list.bak /etc/apt/sources.list && \
   apt-get clean && \
   apt-get autoclean && \
   rm -rf /var/lib/apt/lists/* && \
   apt-get update && \
   apt-get install -y --no-install-recommends \
      gcc-aarch64-linux-gnu:amd64 g++-aarch64-linux-gnu:amd64 && \ 
      rm -rf /var/lib/apt/lists/*

# x64
RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake:amd64 make:amd64 gcc:amd64 g++:amd64 uuid-dev:amd64 lsb-core:amd64 curl:amd64 wget:amd64 && \ 
    rm -rf /var/lib/apt/lists/*

RUN \
	apt-get update && \
	apt-get install -y apt-transport-https:amd64 ca-certificates:amd64 && \
	wget --no-check-certificate https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && \
 	dpkg -i packages-microsoft-prod.deb && \
	apt-get update

RUN \
  wget https://download.visualstudio.microsoft.com/download/pr/73a9cb2a-1acd-4d20-b864-d12797ca3d40/075dbe1dc3bba4aa85ca420167b861b6/dotnet-sdk-5.0.201-linux-x64.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-5.0.201-linux-x64.tar.gz -C /root/dotnet 

RUN \
  wget https://download.visualstudio.microsoft.com/download/pr/25c7e38e-0a6a-4d66-ac4e-b550a44b8a98/49128be84b903799259e7bebe8e9d969/dotnet-sdk-6.0.100-preview.2.21155.3-linux-x64.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-6.0.100-preview.2.21155.3-linux-x64.tar.gz -C /root/dotnet 

RUN \
  /root/dotnet/dotnet tool update powershell -g

FROM env-build as env-run
ENV DOTNET_ROOT="/root/dotnet"
ENV PATH="/root/dotnet:/root/.dotnet/tools:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["sh", "-c", "(cd $SNAPX_DOCKER_WORKING_DIR && pwsh ./build.ps1 $BUILD_PS_PARAMETERS)"]
