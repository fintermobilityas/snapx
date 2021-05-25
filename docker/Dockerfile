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
  wget https://download.visualstudio.microsoft.com/download/pr/98563846-f949-4dc7-81a0-77016735bf08/56d5882a2046382fccb7db032f7d2a02/dotnet-sdk-5.0.300-linux-x64.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-5.0.300-linux-x64.tar.gz -C /root/dotnet && \
  rm dotnet-sdk-5.0.300-linux-x64.tar.gz 
  
RUN \
  wget https://download.visualstudio.microsoft.com/download/pr/f5c00d7a-e1c8-494a-a88b-9ed1dc62249e/8438122af4fd928900a0ebf129c6c1d6/dotnet-sdk-6.0.100-preview.4.21255.9-linux-x64.tar.gz && \
  mkdir -p /root/dotnet && tar zxf dotnet-sdk-6.0.100-preview.4.21255.9-linux-x64.tar.gz -C /root/dotnet && \
  rm dotnet-sdk-6.0.100-preview.4.21255.9-linux-x64.tar.gz

RUN \
  /root/dotnet/dotnet tool update powershell -g

FROM env-build as env-run
ENV DOTNET_ROOT="/root/dotnet"
ENV PATH="/root/dotnet:/root/.dotnet/tools:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["sh", "-c", "(cd $SNAPX_DOCKER_WORKING_DIR && pwsh ./build.ps1 $BUILD_PS_PARAMETERS)"]
