FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props NuGet.config CarbonFootprint.slnx ./
COPY src/CarbonFootprint.Domain/CarbonFootprint.Domain.csproj src/CarbonFootprint.Domain/
COPY src/CarbonFootprint.Application/CarbonFootprint.Application.csproj src/CarbonFootprint.Application/
COPY src/CarbonFootprint.Infrastructure/CarbonFootprint.Infrastructure.csproj src/CarbonFootprint.Infrastructure/
COPY src/CarbonFootprint.Web/CarbonFootprint.Web.csproj src/CarbonFootprint.Web/
RUN dotnet restore src/CarbonFootprint.Web/CarbonFootprint.Web.csproj

COPY src/ src/
RUN dotnet publish src/CarbonFootprint.Web/CarbonFootprint.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
WORKDIR /app
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys \
    && chown $APP_UID:$APP_UID /keys
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV DataProtection__KeyPath=/keys
ENTRYPOINT ["dotnet", "CarbonFootprint.Web.dll"]
