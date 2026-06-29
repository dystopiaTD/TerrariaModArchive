FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["TerrariaModArchive.Api/TerrariaModArchive.Api.csproj", "TerrariaModArchive.Api/"]
COPY ["TerrariaModArchive.Core/TerrariaModArchive.Core.csproj", "TerrariaModArchive.Core/"]
RUN dotnet restore "TerrariaModArchive.Api/TerrariaModArchive.Api.csproj"
COPY . .
WORKDIR "/src/TerrariaModArchive.Api"
RUN dotnet build "TerrariaModArchive.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "TerrariaModArchive.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "TerrariaModArchive.Api.dll"]
