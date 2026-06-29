FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app
# Install dependencies for SteamCMD
RUN apt-get update && apt-get install -y lib32gcc-s1 curl tar && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TerrariaModArchive.Scraper/TerrariaModArchive.Scraper.csproj", "TerrariaModArchive.Scraper/"]
COPY ["TerrariaModArchive.Core/TerrariaModArchive.Core.csproj", "TerrariaModArchive.Core/"]
RUN dotnet restore "TerrariaModArchive.Scraper/TerrariaModArchive.Scraper.csproj"
COPY . .
WORKDIR "/src/TerrariaModArchive.Scraper"
RUN dotnet build "TerrariaModArchive.Scraper.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TerrariaModArchive.Scraper.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TerrariaModArchive.Scraper.dll"]
