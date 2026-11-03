FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/App.Notifier/ src/App.Notifier/
RUN dotnet publish src/App.Notifier/App.Notifier.csproj -c Release -o /app -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble AS final
WORKDIR /app
COPY --from=build /app ./
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "App.Notifier.dll"]
