FROM mcr.microsoft.com/dotnet/sdk:5.0.100-rc.1-focal as env-build

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
      gcc-aarch64-linux-gnu:amd64 g++-8-aarch64-linux-gnu:amd64 && \ 
      rm -rf /var/lib/apt/lists/*

# x64
RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake:amd64 make:amd64 gcc-8:amd64 g++-8:amd64 uuid-dev:amd64 lsb-core:amd64 curl:amd64 && \ 
    rm -rf /var/lib/apt/lists/*

RUN \ 
	update-alternatives --install /usr/bin/gcc gcc /usr/bin/gcc-8 800 --slave /usr/bin/g++ g++ /usr/bin/g++-8

FROM env-build as env-run
RUN mkdir /root/.dotnet/tools
ENV PATH="/root/.dotnet/tools:${PATH}"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
CMD ["sh", "-c", "(cd $SNAPX_DOCKER_WORKING_DIR && pwsh ./build.ps1 $BUILD_PS_PARAMETERS)"]
