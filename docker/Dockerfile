FROM mcr.microsoft.com/dotnet/core/sdk:3.1.301-focal as env-build

ENV DEBIAN_FRONTEND=noninteractive

ENV SNAPX_DOCKER_WORKING_DIR /build/snapx

RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake make gcc-8 g++-8 mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686 \
    uuid-dev

RUN \ 
	update-alternatives --install /usr/bin/gcc gcc /usr/bin/gcc-8 800 --slave /usr/bin/g++ g++ /usr/bin/g++-8

FROM env-build as env-run
CMD ["sh", "-c", "$SNAPX_DOCKER_WORKING_DIR/docker/entry.sh"]
