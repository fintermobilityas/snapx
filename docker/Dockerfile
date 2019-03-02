FROM mcr.microsoft.com/powershell:latest as env-build

RUN \
  apt-get update && \
  apt-get install -y --no-install-recommends \
    cmake make gcc g++ mingw-w64 g++-mingw-w64-x86-64 g++-mingw-w64-i686 \
    uuid-dev

CMD ["/build/snapx/docker/entry.sh"]
