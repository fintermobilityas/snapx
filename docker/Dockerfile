FROM mcr.microsoft.com/powershell:6.1.3-ubuntu-18.04 as env-build

ENV DEBIAN_FRONTEND=noninteractive

ENV SNAPX_DOCKER_WORKING_DIR /build/snapx

RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake make gcc g++ mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686 \
    uuid-dev

CMD ["sh", "-c", "$SNAPX_DOCKER_WORKING_DIR/docker/entry.sh"]