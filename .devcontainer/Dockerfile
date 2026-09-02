FROM mcr.microsoft.com/devcontainers/dotnet:2-10.0-noble

# [Optional] Uncomment this section to install additional OS packages.
# RUN apt-get update && export DEBIAN_FRONTEND=noninteractive \
#     && apt-get -y install --no-install-recommends <your-package-list-here>

# [Optional] Uncomment this line to install global node packages.
# RUN su vscode -c "source /usr/local/share/nvm/nvm.sh && npm install -g <your-package-here>" 2>&1

USER vscode

RUN wget -O - https://github.com/rtk-ai/rtk/releases/latest/download/rtk-x86_64-unknown-linux-musl.tar.gz | tar -xzf - -C /tmp \
  && mkdir -p /home/vscode/.local/bin \
  && mv /tmp/rtk /home/vscode/.local/bin/rtk
