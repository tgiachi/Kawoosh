FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/Kawoosh.Server/Kawoosh.Server.csproj", "src/Kawoosh.Server/"]
RUN dotnet restore "src/Kawoosh.Server/Kawoosh.Server.csproj"
COPY . .
WORKDIR "/src/src/Kawoosh.Server"
RUN dotnet build "./Kawoosh.Server.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Kawoosh.Server.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Kawoosh.Server.dll"]
