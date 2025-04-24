# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 根据将生成或运行容器的主机的操作系统，可能需要更改 FROM 语句中指定的映像。
# 有关详细信息，请参阅 https://aka.ms/containercompat

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/runtime:8.0-nanoserver-1809 AS base
WORKDIR /app


# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-1809 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["checker.csproj", "."]
RUN dotnet restore "./checker.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./checker.csproj" -c %BUILD_CONFIGURATION% -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./checker.csproj" -c %BUILD_CONFIGURATION% -o /app/publish /p:UseAppHost=false

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "checker.dll"]