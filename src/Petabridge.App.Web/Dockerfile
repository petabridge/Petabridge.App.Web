FROM mcr.microsoft.com/dotnet/sdk:5.0 AS base
WORKDIR /app

# should be a comma-delimited list
ENV CLUSTER_SEEDS "[]"
ENV CLUSTER_IP ""
ENV CLUSTER_PORT "4055"

# 9110 - Petabridge.Cmd
# 4053 - Akka.Cluster
# 80 - HTTP
EXPOSE 9110 4055 80

# Install Petabridge.Cmd client so it can be invoked remotely via
# Docker or K8s 'exec` commands
RUN dotnet tool install --global pbm 

# RUN pbm help

COPY ./bin/Release/net5/publish/ /app

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS app
WORKDIR /app

COPY --from=base /app /app

# copy .NET Core global tool
COPY --from=base /root/.dotnet /root/.dotnet/

# Needed because https://stackoverflow.com/questions/51977474/install-dotnet-core-tool-dockerfile
ENV PATH="${PATH}:/root/.dotnet/tools"

CMD ["dotnet", "Petabridge.App.Web.dll"]