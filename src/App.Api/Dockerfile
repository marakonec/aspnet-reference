FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/App.Shared/App.Shared.csproj src/App.Shared/packages.lock.json src/App.Shared/
COPY src/App.Api/App.Api.csproj src/App.Api/packages.lock.json src/App.Api/
RUN dotnet restore src/App.Api/App.Api.csproj

COPY src/App.Shared/ src/App.Shared/
COPY src/App.Api/ src/App.Api/
RUN dotnet publish src/App.Api/App.Api.csproj -c Release --no-restore \
    -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble AS final
WORKDIR /app
COPY --from=build /app ./

EXPOSE 8080

USER $APP_UID

ENTRYPOINT ["dotnet", "App.Api.dll"]
