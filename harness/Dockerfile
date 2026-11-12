# L'image des sessions du duel (protocole §3) : versions épinglées, aucun fichier de l'hôte, un HOME vide.
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update && apt-get install -y --no-install-recommends nodejs npm ca-certificates && rm -rf /var/lib/apt/lists/* \
 && npm install -g @anthropic-ai/claude-code@2.1.318 && npm cache clean --force
RUN useradd --create-home --shell /bin/bash agent && mkdir /travail && chown agent /travail
USER agent
ENV HOME=/home/agent DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DISABLE_AUTOUPDATER=1
WORKDIR /travail
