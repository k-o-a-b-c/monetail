FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Monetail.Api/Monetail.Api.csproj src/Monetail.Api/
RUN dotnet restore src/Monetail.Api/Monetail.Api.csproj
COPY src/Monetail.Api/ src/Monetail.Api/
RUN dotnet publish src/Monetail.Api/Monetail.Api.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
ENV ASPNETCORE_HTTP_PORTS=8080
ENV TZ=Etc/UTC
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Monetail.Api.dll"]
